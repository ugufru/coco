#!/usr/bin/env python3
"""
fc.py — Forth cross-compiler for the CoCo Forth executor kernel.

Usage:
    python3 fc.py <source.fs> [--kernel build/kernel.map] [--output build/app.bin] [--base 0x2000]

Reads Forth source and emits a CoCo DECB binary containing compiled
threaded code, ready to be loaded at APP_BASE and executed by the kernel.

CFA addresses for kernel primitives are read from the lwasm .map file,
so the compiler stays in sync with the kernel automatically.

Supported Forth syntax:
    : NAME ... ;    colon definition
    CODE NAME ... ;CODE   inline assembly (native 6809 machine code)
    NUMBER          integer literal  (e.g. 72, 0x48)
    CHAR X          character literal (ASCII value of X)
    WORD            reference to a defined word or kernel primitive
    \               line comment
    ( ... )         block comment

Known limitations:
    - EXIT inside IF/THEN may miscompile — avoid using EXIT inside IF blocks
    - BEGIN/WHILE/REPEAT is not supported — only BEGIN/AGAIN and BEGIN/UNTIL
    - CODE block comments must be ASCII only — Unicode (arrows, em dashes)
      will cause lwasm assembly errors
    - Blank lines inside CODE blocks are stripped by preprocess_asm to work
      around an lwasm local label scoping quirk
"""

import argparse
import re
import struct
import subprocess
import sys
import tempfile
from pathlib import Path

APP_BASE = 0x2000   # default application load address


# ── Symbol table ──────────────────────────────────────────────────────────────

def load_symbols(map_file):
    """Parse an lwasm .map file into a dict of name → address."""
    symbols = {}
    with open(map_file) as f:
        for line in f:
            m = re.match(r'Symbol:\s+(\w+)\s+\S+\s+=\s+([0-9A-Fa-f]+)', line)
            if m:
                symbols[m.group(1)] = int(m.group(2), 16)
    return symbols


def kernel_words(symbols):
    """Return the subset of kernel symbols that Forth source can call by name."""
    names = {
        'emit': 'CFA_EMIT',
        'halt': 'CFA_HALT',
        'exit': 'CFA_EXIT',
        '+':    'CFA_ADD',
        '-':    'CFA_SUB',
        'cr':   'CFA_CR',
        'dup':  'CFA_DUP',
        'drop': 'CFA_DROP',
        'swap': 'CFA_SWAP',
        'over': 'CFA_OVER',
        '@':    'CFA_FETCH',
        '!':    'CFA_STORE',
        'i':    'CFA_I',
        '*':    'CFA_MUL',
        '/mod': 'CFA_DIVMOD',
        'key':  'CFA_KEY',
        '=':    'CFA_EQ',
        '<>':   'CFA_NEQ',
        '<':    'CFA_LT',
        '>':    'CFA_GT',
        '0=':   'CFA_ZEQU',
        'at':   'CFA_AT',
        'c!':   'CFA_CSTORE',
        'c@':   'CFA_CFETCH',
        'and':  'CFA_AND',
        'or':   'CFA_OR',
        'kbd-scan': 'CFA_KBD_SCAN',
        'key?':     'CFA_KEY_NB',
        'fill':     'CFA_FILL',
        'cmove':    'CFA_CMOVE',
        'lshift':   'CFA_LSHIFT',
        'rshift':   'CFA_RSHIFT',
        'negate':   'CFA_NEGATE',
        '?dup':     'CFA_QDUP',
        'type':     'CFA_TYPE',
        'count':    'CFA_COUNT',
        '+!':       'CFA_PLUS_STORE',
        '2drop':    'CFA_2DROP',
        '2dup':     'CFA_2DUP',
        'rot':      'CFA_ROT',
        'prox-scan': 'CFA_PROX_SCAN',
        '>r':       'CFA_TOR',
        'r>':       'CFA_FROMR',
        'r@':       'CFA_RAT',
        'min':      'CFA_MIN',
        'max':      'CFA_MAX',
        'abs':      'CFA_ABS',
        'mdist':    'CFA_MDIST',
        'unloop':   'CFA_UNLOOP',
        'pick':     'CFA_PICK',
        'invert':   'CFA_INVERT',
        'xor':      'CFA_XOR',
        'j':        'CFA_J',
        'u<':          'CFA_ULT',
        '0max':        'CFA_ZEROMAX',
        '2*':          'CFA_TWOSTAR',
        '2/':          'CFA_TWOSLASH',
        '0min':        'CFA_ZEROMIN',
        'within':      'CFA_WITHIN',
        'sprite-data': 'CFA_SPRITE_DATA',
        'font-data':   'CFA_FONT_DATA',
        'vsync':       'CFA_VSYNC',
        'wait-past-row': 'CFA_WAIT_PAST_ROW',
        'count-blanking': 'CFA_COUNT_BLANKING',
        'rg-pset':     'CFA_RG_PSET',
        'rg-line':     'CFA_RG_LINE',
        'spr-draw':    'CFA_SPR_DRAW',
        'spr-erase-box': 'CFA_SPR_ERASE_BOX',
        'rg-char':     'CFA_RG_CHAR',
        'beam-trace':  'CFA_BEAM_TRACE',
        'beam-draw-slice': 'CFA_BEAM_DRAW_SLICE',
        'beam-restore-slice': 'CFA_BEAM_RESTORE_SLICE',
        'beam-find-obstacle': 'CFA_BEAM_FIND_OBSTACLE',
        'beam-scrub-pos': 'CFA_BEAM_SCRUB_POS',
    }
    result = {}
    for forth_name, sym_name in names.items():
        if sym_name not in symbols:
            raise KeyError(f"Kernel symbol {sym_name!r} not found in map file")
        result[forth_name] = symbols[sym_name]
    return result


# ── Tokeniser ─────────────────────────────────────────────────────────────────

def tokenize(source, base_dir=None):
    """Strip comments, resolve INCLUDE directives, and split into tokens.

    Processes source line-by-line to capture CODE...;CODE blocks as raw
    assembly text.  CODE blocks emit sentinel tokens:
        __CODE__  name  <raw-asm-text>  __ENDCODE__

    INCLUDE <filename> splices the named file's tokens at the point of the
    directive.  The filename is resolved relative to base_dir (the directory
    of the including file).  Nested INCLUDEs are handled recursively.
    """
    result = []
    in_code_block = False
    code_name = None
    code_lines = []
    in_block_comment = False

    for line in source.split('\n'):
        # Handle block comments that span multiple lines
        if in_block_comment:
            close = line.find(')')
            if close < 0:
                continue  # entire line inside block comment
            line = line[close + 1:]
            in_block_comment = False

        if in_code_block:
            stripped = line.strip()
            if stripped.upper() == ';CODE' or stripped.upper().startswith(';CODE '):
                result.extend([code_tag, code_name, '\n'.join(code_lines), '__ENDCODE__'])
                in_code_block = False
                code_name = None
                code_lines = []
            elif stripped.upper() == ';KCODE' or stripped.upper().startswith(';KCODE '):
                result.extend([code_tag, code_name, '\n'.join(code_lines), '__ENDCODE__'])
                in_code_block = False
                code_name = None
                code_lines = []
            else:
                code_lines.append(line)
            continue

        # Strip line comment (everything after \)
        backslash = line.find('\\')
        if backslash >= 0:
            line = line[:backslash]

        # Strip block comments, handling multiple per line and unclosed
        while True:
            open_paren = line.find('(')
            if open_paren < 0:
                break
            close_paren = line.find(')', open_paren + 1)
            if close_paren < 0:
                line = line[:open_paren]
                in_block_comment = True
                break
            line = line[:open_paren] + ' ' + line[close_paren + 1:]

        tokens_on_line = line.split()
        if not tokens_on_line:
            continue

        # Check for CODE / KCODE block start
        if tokens_on_line[0].upper() in ('CODE', 'KCODE'):
            if len(tokens_on_line) < 2:
                raise SyntaxError(f"{tokens_on_line[0]} requires a word name")
            code_tag = '__CODE__' if tokens_on_line[0].upper() == 'CODE' else '__KCODE__'
            code_name = tokens_on_line[1]
            code_lines = []
            in_code_block = True
            continue

        # Process normal tokens, with special handling for string literals.
        # S" and ." need the raw text up to the closing " (preserving spaces).
        # We extract them from the original line rather than from split tokens.
        i = 0
        while i < len(tokens_on_line):
            tok = tokens_on_line[i]
            if tok.upper() == 'INCLUDE':
                i += 1
                filename = tokens_on_line[i]
                filepath = (Path(base_dir) / filename) if base_dir else Path(filename)
                included = tokenize(filepath.read_text(), base_dir=filepath.parent)
                result.extend(included)
            elif tok in ('S"', 's"', '."'):
                # Find this token in the line and extract text until closing "
                # Locate the S" or ." token followed by a space in the line
                marker = tok
                # Find position after the marker+space in the line
                idx = line.find(marker)
                start = idx + len(marker)
                # Skip the single space that Forth requires after S"/."
                if start < len(line) and line[start] == ' ':
                    start += 1
                # Find closing quote
                end = line.find('"', start)
                if end < 0:
                    raise SyntaxError(f'Unterminated {marker} string literal')
                string_text = line[start:end]
                # Emit as a single __SLIT__ or __DOTSLIT__ token + string content
                result.append(marker)
                result.append('__STR__' + string_text)
                # Skip all remaining split tokens that came from this string
                # by finding which tokens are past the closing quote
                rest_of_line = line[end + 1:]
                tokens_on_line = rest_of_line.split()
                i = 0
                continue
            else:
                result.append(tok)
            i += 1

    if in_code_block:
        raise SyntaxError(f"Unterminated CODE block: {code_name!r}")

    return result


# ── Parser → IR ───────────────────────────────────────────────────────────────

def parse(tokens):
    """
    Walk the token stream and return:
        definitions      — OrderedDict of name → [items]
        variables        — list of variable names (in declaration order)
        main_thread      — [items]  (top-level calls, after all definitions)
        code_definitions — OrderedDict of name → raw asm text

    Each item is one of:
        ('lit',       int_value)
        ('word',      name_str)
        ('label',     name_str)   — 0 bytes; marks a position for DO back-reference
        ('do',)                   — 2 bytes; emits CFA_DO
        ('loop_back', name_str)   — 4 bytes; emits CFA_LOOP + signed offset to label
    """
    definitions      = {}   # preserves insertion order (Python 3.7+)
    code_definitions = {}   # name → raw asm text (app-space CODE words)
    kcode_definitions = {}  # name → raw asm text (kernel-space KCODE words)
    variables        = []   # variable names in declaration order
    main_thread      = []

    current_def = None
    current_items = None
    do_counter = 0
    do_stack = []
    if_counter = 0
    if_stack = []
    begin_counter = 0
    begin_stack = []
    i = 0

    while i < len(tokens):
        tok = tokens[i]

        if tok in ('__CODE__', '__KCODE__'):
            name = tokens[i + 1].lower()
            asm_text = tokens[i + 2]
            # i+3 is __ENDCODE__
            if name in definitions:
                raise SyntaxError(f"CODE/KCODE word {name!r} collides with colon definition")
            if name in code_definitions or name in kcode_definitions:
                raise SyntaxError(f"Duplicate CODE/KCODE word: {name!r}")
            if tok == '__KCODE__':
                kcode_definitions[name] = asm_text
            else:
                code_definitions[name] = asm_text
            i += 4
            continue

        elif tok == ':':
            i += 1
            name = tokens[i].lower()
            if name in code_definitions:
                raise SyntaxError(f"Colon definition {name!r} collides with CODE word")
            current_def = name
            current_items = []
            do_stack = []
            if_stack = []
            begin_stack = []

        elif tok == ';':
            if current_def is None:
                raise SyntaxError("';' without ':'")
            if do_stack:
                raise SyntaxError(f"Unclosed DO in definition: {current_def!r}")
            if if_stack:
                raise SyntaxError(f"Unclosed IF in definition: {current_def!r}")
            if begin_stack:
                raise SyntaxError(f"Unclosed BEGIN in definition: {current_def!r}")
            definitions[current_def] = current_items
            current_def = None
            current_items = None

        elif tok.upper() == 'VARIABLE':
            if current_def is not None:
                raise SyntaxError("VARIABLE inside a definition is not supported")
            i += 1
            var_name = tokens[i].lower()
            variables.append(var_name)

        elif tok.upper() == 'CONSTANT':
            if current_def is not None:
                raise SyntaxError("CONSTANT inside a definition is not supported")
            i += 1
            const_name = tokens[i].lower()
            if not main_thread or main_thread[-1][0] != 'lit':
                raise SyntaxError(f"CONSTANT {const_name!r} must follow a literal value")
            val = main_thread.pop()[1]
            definitions[const_name] = [('lit', val)]

        elif tok == 'S"' or tok == 's"':
            i += 1
            raw = tokens[i]
            if not raw.startswith('__STR__'):
                raise SyntaxError('S" string not properly tokenized')
            text = raw[7:]  # strip __STR__ prefix
            item = ('slit', text)
            (current_items if current_def else main_thread).append(item)

        elif tok == '."':
            i += 1
            raw = tokens[i]
            if not raw.startswith('__STR__'):
                raise SyntaxError('." string not properly tokenized')
            text = raw[7:]
            target = current_items if current_def else main_thread
            target.append(('slit', text))
            target.append(('word', 'type'))

        elif tok.upper() == 'CHAR':
            i += 1
            char_tok = tokens[i]
            item = ('lit', ord(char_tok[0]))
            (current_items if current_def else main_thread).append(item)

        elif tok.upper() == 'DO':
            target = current_items if current_def else main_thread
            label = f'__do_{do_counter}'
            do_counter += 1
            do_stack.append(label)
            target.append(('do',))
            target.append(('label', label))

        elif tok.upper() == 'LOOP':
            if not do_stack:
                raise SyntaxError("LOOP without DO")
            target = current_items if current_def else main_thread
            target.append(('loop_back', do_stack.pop()))

        elif tok.upper() == '+LOOP':
            if not do_stack:
                raise SyntaxError("+LOOP without DO")
            target = current_items if current_def else main_thread
            target.append(('plus_loop_back', do_stack.pop()))

        elif tok.upper() == 'BEGIN':
            target = current_items if current_def else main_thread
            label = f'__begin_{begin_counter}'
            begin_counter += 1
            begin_stack.append(label)
            target.append(('label', label))

        elif tok.upper() == 'AGAIN':
            if not begin_stack:
                raise SyntaxError("AGAIN without BEGIN")
            target = current_items if current_def else main_thread
            target.append(('again_back', begin_stack.pop()))

        elif tok.upper() == 'UNTIL':
            if not begin_stack:
                raise SyntaxError("UNTIL without BEGIN")
            target = current_items if current_def else main_thread
            target.append(('until_back', begin_stack.pop()))

        elif tok.upper() == 'IF':
            target = current_items if current_def else main_thread
            label = f'__if_{if_counter}'
            if_counter += 1
            if_stack.append(('if', label))
            target.append(('if_fwd', label))

        elif tok.upper() == 'ELSE':
            if not if_stack or if_stack[-1][0] != 'if':
                raise SyntaxError("ELSE without IF")
            _, if_label = if_stack.pop()
            target = current_items if current_def else main_thread
            else_label = f'__else_{if_counter}'
            if_counter += 1
            if_stack.append(('else', else_label))
            target.append(('else_fwd', else_label))
            target.append(('label', if_label))

        elif tok.upper() == 'THEN':
            if not if_stack or if_stack[-1][0] not in ('if', 'else'):
                raise SyntaxError("THEN without IF")
            _, label = if_stack.pop()
            target = current_items if current_def else main_thread
            target.append(('label', label))

        else:
            # Integer literal or word reference
            try:
                if tok.startswith('$'):
                    val = int(tok[1:], 16)  # $ prefix: CoCo/Forth hex notation
                else:
                    val = int(tok, 0)   # base-0 handles 0x… as hex
                item = ('lit', val)
            except ValueError:
                item = ('word', tok.lower())
            (current_items if current_def else main_thread).append(item)

        i += 1

    if current_def is not None:
        raise SyntaxError(f"Unterminated definition: {current_def!r}")

    return definitions, variables, main_thread, code_definitions, kcode_definitions


# ── CODE word assembly ────────────────────────────────────────────────────────

# NEXT sequence for the 6809 ITC kernel (inlined at end of CODE words)
NEXT_ASM = """\
        LDY     ,X++
        JMP     [,Y]"""


def preprocess_asm(name, asm_text):
    """Prepare a CODE word's assembly text for lwasm.

    - Prepend a global label so @-local labels scope correctly
    - Expand ;NEXT into the two-instruction NEXT sequence
    - Strip blank/comment-only lines (lwasm local label scoping quirk)
    """
    safe = re.sub(r'[^a-z0-9]', '_', name)
    label = f'__code_{safe}'
    lines = [label]
    for line in asm_text.split('\n'):
        stripped = line.strip()
        if not stripped or (stripped.startswith(';') and not stripped.startswith(';NEXT')):
            continue   # skip blank and comment-only lines
        if re.match(r'^\s*;NEXT\b', line):
            lines.append(NEXT_ASM)
        else:
            lines.append(line)
    lines.append(f'{label}__END')
    return label, '\n'.join(lines)


def assemble_code_words(code_defs, symbols, var_addrs=None):
    """Assemble all CODE words into machine code via lwasm.

    Returns an ordered dict of name → bytes (raw machine code for each word).

    var_addrs: optional dict of variable name → address.  When provided,
    each entry is emitted as  FVAR_<name>  EQU  $<addr>  so CODE words
    can reference Forth VARIABLEs by their data-cell address (CFA + 2).
    """
    if not code_defs:
        return {}

    # Build assembly source with kernel symbol EQUs
    asm_parts = ['        PRAGMA  6809', '        ORG     $0000', '']
    for sym_name, addr in sorted(symbols.items()):
        asm_parts.append(f'{sym_name:20s} EQU     ${addr:04X}')
    if var_addrs:
        asm_parts.append('')
        asm_parts.append('; Forth VARIABLE data-cell addresses')
        for vname, vaddr in sorted(var_addrs.items()):
            label = 'FVAR_' + vname.replace('-', '_')
            asm_parts.append(f'{label:20s} EQU     ${vaddr:04X}')
    asm_parts.append('')

    labels = {}  # name → (start_label, end_label)
    for name, asm_text in code_defs.items():
        start_label, processed = preprocess_asm(name, asm_text)
        end_label = f'{start_label}__END'
        labels[name] = (start_label, end_label)
        asm_parts.append(processed)
        asm_parts.append('')

    asm_source = '\n'.join(asm_parts)

    with tempfile.TemporaryDirectory() as tmpdir:
        asm_file = Path(tmpdir) / 'code_words.asm'
        bin_file = Path(tmpdir) / 'code_words.bin'
        map_file = Path(tmpdir) / 'code_words.map'
        asm_file.write_text(asm_source)

        result = subprocess.run(
            ['lwasm', '--format=raw', f'--output={bin_file}',
             f'--map={map_file}', str(asm_file)],
            capture_output=True, text=True
        )
        if result.returncode != 0:
            raise RuntimeError(
                f"lwasm failed assembling CODE words:\n{result.stderr}\n"
                f"--- assembly source ---\n{asm_source}")

        raw_bin = bin_file.read_bytes()
        map_text = map_file.read_text()

        # Parse map file to get label addresses
        map_syms = {}
        for line in map_text.split('\n'):
            m = re.match(r'Symbol:\s+(\w+)\s+\S+\s+=\s+([0-9A-Fa-f]+)', line)
            if m:
                map_syms[m.group(1)] = int(m.group(2), 16)

        code_bytes = {}
        for name, (start_label, end_label) in labels.items():
            if start_label not in map_syms or end_label not in map_syms:
                raise RuntimeError(f"Could not find labels for CODE word {name!r} in map")
            start = map_syms[start_label]
            end = map_syms[end_label]
            code_bytes[name] = raw_bin[start:end]

    return code_bytes


def assemble_kcode_words(kcode_defs, symbols, var_addrs, kern_end):
    """Assemble KCODE words into machine code targeted at kernel addresses.

    Each KCODE word is laid out as: CFA (2 bytes, FDB self+2) + machine code.
    Returns (kcode_bytes, kcode_cfas) where:
      - kcode_bytes: bytearray to append to kernel binary
      - kcode_cfas: dict of name → CFA address (in kernel address space)
    """
    if not kcode_defs:
        return bytearray(), {}

    # Build assembly source with EQUs for all symbols
    asm_parts = ['        PRAGMA  6809', f'        ORG     ${kern_end:04X}', '']
    for sym_name, addr in sorted(symbols.items()):
        asm_parts.append(f'{sym_name:20s} EQU     ${addr:04X}')
    if var_addrs:
        asm_parts.append('')
        asm_parts.append('; Forth VARIABLE data-cell addresses')
        for vname, vaddr in sorted(var_addrs.items()):
            label = 'FVAR_' + vname.replace('-', '_')
            asm_parts.append(f'{label:20s} EQU     ${vaddr:04X}')
    asm_parts.append('')

    # Emit each KCODE word with CFA + machine code
    cfa_labels = {}
    for name, asm_text in kcode_defs.items():
        safe = re.sub(r'[^a-z0-9]', '_', name)
        cfa_label = f'KCFA_{safe}'
        code_label = f'KCODE_{safe}'
        end_label = f'KCODE_{safe}__END'
        cfa_labels[name] = cfa_label

        asm_parts.append(f'{cfa_label}  FDB     {code_label}')
        asm_parts.append(f'{code_label}')

        # Process assembly: expand ;NEXT, skip blanks/comments
        for line in asm_text.split('\n'):
            stripped = line.strip()
            if not stripped or (stripped.startswith(';') and not stripped.startswith(';NEXT')):
                continue
            if re.match(r'^\s*;NEXT\b', line):
                asm_parts.append(NEXT_ASM)
            else:
                asm_parts.append(line)
        asm_parts.append(end_label)
        asm_parts.append('')

    asm_source = '\n'.join(asm_parts)

    with tempfile.TemporaryDirectory() as tmpdir:
        asm_file = Path(tmpdir) / 'kcode_words.asm'
        bin_file = Path(tmpdir) / 'kcode_words.bin'
        map_file = Path(tmpdir) / 'kcode_words.map'
        asm_file.write_text(asm_source)

        result = subprocess.run(
            ['lwasm', '--format=raw', f'--output={bin_file}',
             f'--map={map_file}', str(asm_file)],
            capture_output=True, text=True
        )
        if result.returncode != 0:
            raise RuntimeError(
                f"lwasm failed assembling KCODE words:\n{result.stderr}\n"
                f"--- assembly source ---\n{asm_source}")

        raw_bin = bin_file.read_bytes()
        map_text = map_file.read_text()

        # Parse map to get CFA addresses
        map_syms = {}
        for line in map_text.split('\n'):
            m = re.match(r'Symbol:\s+(\w+)\s+\S+\s+=\s+([0-9A-Fa-f]+)', line)
            if m:
                map_syms[m.group(1)] = int(m.group(2), 16)

        kcode_cfas = {}
        for name, cfa_label in cfa_labels.items():
            if cfa_label not in map_syms:
                raise RuntimeError(f"Could not find CFA label for KCODE word {name!r}")
            kcode_cfas[name] = map_syms[cfa_label]

    return bytearray(raw_bin), kcode_cfas


# ── Constant inlining ─────────────────────────────────────────────────────────

MAX_INLINE_REFS = 999  # inline all constants (each ref: 80cy→31cy, +2 bytes)

def inline_constants(definitions, main_thread):
    """Replace low-reference CONSTANT words with inline LIT values.

    A CONSTANT definition is [('lit', val)].  Each reference emits a CFA token
    (2 bytes, 80cy).  Inlining emits LIT+value (4 bytes, 31cy) and removes the
    definition (8 bytes).  Net size: saves space when refs <= 3, costs when >= 5.
    """
    # Identify constants: definitions whose body is exactly [('lit', val)]
    constants = {}
    for name, body in definitions.items():
        if len(body) == 1 and body[0][0] == 'lit':
            constants[name] = body[0][1]

    if not constants:
        return

    # Count references across all IR
    def count_refs(items):
        refs = {}
        for item in items:
            if item[0] == 'word' and item[1] in constants:
                refs[item[1]] = refs.get(item[1], 0) + 1
        return refs

    ref_counts = count_refs(main_thread)
    for name, body in definitions.items():
        if name not in constants:
            for cname, cnt in count_refs(body).items():
                ref_counts[cname] = ref_counts.get(cname, 0) + cnt

    # Determine which to inline
    to_inline = {name: constants[name] for name, cnt in ref_counts.items()
                 if cnt <= MAX_INLINE_REFS}
    # Also inline constants with zero references (removes dead definition)
    for name in constants:
        if name not in ref_counts:
            to_inline[name] = constants[name]

    if not to_inline:
        return

    # Replace references with inline LIT
    def rewrite(items):
        for i, item in enumerate(items):
            if item[0] == 'word' and item[1] in to_inline:
                items[i] = ('lit', to_inline[item[1]])

    rewrite(main_thread)
    for name, body in definitions.items():
        if name not in to_inline:
            rewrite(body)

    # Remove inlined definitions
    for name in to_inline:
        del definitions[name]


# ── Compiler ──────────────────────────────────────────────────────────────────

def item_size(item):
    """Bytes emitted for a single IR item."""
    kind = item[0]
    if kind == 'lit':        return 4    # CFA_LIT (2) + value (2)
    if kind == 'slit':                   # S" string literal
        slen = len(item[1])
        return 4 + slen + 8             # BRANCH(2) + offset(2) + chars + LIT(2)+addr(2) + LIT(2)+len(2)
    if kind == 'label':      return 0    # marker only, no bytes
    if kind == 'do':         return 2    # CFA_DO
    if kind == 'loop_back':  return 4    # CFA_LOOP (2) + offset cell (2)
    if kind == 'plus_loop_back': return 4  # CFA_PLUS_LOOP (2) + offset (2)
    if kind == 'if_fwd':     return 4    # CFA_0BRANCH (2) + forward offset cell (2)
    if kind == 'else_fwd':   return 4    # CFA_BRANCH (2) + forward offset cell (2)
    if kind == 'again_back': return 4    # CFA_BRANCH (2) + backward offset cell (2)
    if kind == 'until_back': return 4    # CFA_0BRANCH (2) + backward offset cell (2)
    return 2                             # word reference: CFA address


def compile_forth(definitions, variables, main_thread, code_definitions,
                  symbols, app_base, hole_start=None, hole_end=None,
                  kcode_cfas=None):
    """
    Two-pass compiler.

    Layout in the output binary:
        [app_base]          main thread
        [+ main_size]       colon definitions (DOCOL + body + EXIT each)
        [+ defs_size]       CODE definitions  (FDB self+2 + machine_code each)
        [+ code_size]       variable cells    (DOVAR + 2-byte data each)

    If hole_start/hole_end are set, the compiler skips over that address range
    (e.g. for VRAM).  No word will be placed inside the hole; the DECB output
    is split into two load records around it.

    Returns a bytearray to be loaded at app_base.
    """
    DOCOL    = symbols['DOCOL']
    DOVAR    = symbols['DOVAR']
    CFA_EXIT = symbols['CFA_EXIT']
    CFA_LIT  = symbols['CFA_LIT']
    CFA_DO       = symbols['CFA_DO']
    CFA_LOOP     = symbols['CFA_LOOP']
    CFA_PLUS_LOOP = symbols['CFA_PLUS_LOOP']
    CFA_0BRANCH  = symbols['CFA_0BRANCH']
    CFA_BRANCH   = symbols['CFA_BRANCH']
    kwords       = kernel_words(symbols)

    # Assemble CODE words to get their sizes.
    # Provide dummy variable addresses (all $0000) so FVAR_* symbols resolve.
    # Real addresses are computed in Pass 1 and CODE words are re-assembled.
    dummy_vars = {name: 0x4000 for name in variables} if variables else None
    code_bytes = assemble_code_words(code_definitions, symbols, dummy_vars)

    def skip_hole(addr):
        """If addr falls inside the reserved hole, jump past it."""
        if hole_start is not None and addr >= hole_start and addr < hole_end:
            return hole_end
        return addr

    # ── Pass 1: calculate addresses ───────────────────────────────────────────
    label_map = {}

    def scan(items, start):
        cursor = start
        for item in items:
            if item[0] == 'label':
                label_map[item[1]] = cursor
            cursor += item_size(item)
        return cursor

    cursor = scan(main_thread, app_base)

    def would_cross_hole(addr, size):
        """True if a block at addr..addr+size would overlap the hole."""
        if hole_start is None:
            return False
        return addr < hole_start and addr + size > hole_start

    word_cfa = {}   # name → address of the word's CFA cell in the output
    for name, items in definitions.items():
        cursor = skip_hole(cursor)
        def_size = 2 + sum(item_size(it) for it in items) + 2
        if would_cross_hole(cursor, def_size):
            cursor = hole_end
        word_cfa[name] = cursor
        cursor += 2                                      # DOCOL (the CFA cell)
        cursor = scan(items, cursor)
        cursor += 2                                      # CFA_EXIT

    # CODE definitions: CFA cell (2 bytes) + machine code
    for name in code_definitions:
        cursor = skip_hole(cursor)
        cw_size = 2 + len(code_bytes[name])
        if would_cross_hole(cursor, cw_size):
            cursor = hole_end
        word_cfa[name] = cursor
        cursor += cw_size

    var_cfa = {}    # name → address of the variable's CFA cell in the output
    for name in variables:
        cursor = skip_hole(cursor)
        if would_cross_hole(cursor, 4):
            cursor = hole_end
        var_cfa[name] = cursor
        cursor += 2                                      # DOVAR (the CFA cell)
        cursor += 2                                      # data cell (16-bit, init 0)

    # ── Re-assemble CODE words with variable addresses ───────────────────────
    # Now that var_cfa is known, re-assemble so CODE words can use FVAR_* EQUs.
    # Data cell = CFA + 2 (skip the DOVAR pointer to get the actual storage).
    if code_definitions and variables:
        var_addrs = {name: addr + 2 for name, addr in var_cfa.items()}
        code_bytes = assemble_code_words(code_definitions, symbols, var_addrs)

    # ── Pass 2: generate binary ───────────────────────────────────────────────

    buf = bytearray()

    def emit_word(val):
        buf.extend(struct.pack('>H', val & 0xFFFF))

    def pad_to(addr):
        """Pad buffer so next byte lands at addr (handles hole gaps)."""
        current = app_base + len(buf)
        if addr > current:
            buf.extend(bytes(addr - current))

    def resolve(item):
        kind = item[0]
        if kind == 'lit':
            emit_word(CFA_LIT)
            emit_word(item[1])
        elif kind == 'slit':
            text = item[1]
            slen = len(text)
            # BRANCH over the string data
            emit_word(CFA_BRANCH)
            offset_cell_addr = app_base + len(buf)
            # offset = bytes to skip: slen bytes of string data
            # target is the LIT word after the string data
            # from (offset_cell_addr + 2), skip slen bytes
            emit_word(slen)
            # String data (raw ASCII bytes)
            str_addr = app_base + len(buf)
            buf.extend(text.encode('ascii'))
            # LIT addr, LIT len
            emit_word(CFA_LIT)
            emit_word(str_addr)
            emit_word(CFA_LIT)
            emit_word(slen)
        elif kind == 'label':
            pass    # 0 bytes; position already recorded in label_map
        elif kind == 'do':
            emit_word(CFA_DO)
        elif kind == 'loop_back':
            emit_word(CFA_LOOP)
            offset_cell_addr = app_base + len(buf)
            emit_word(label_map[item[1]] - (offset_cell_addr + 2))
        elif kind == 'plus_loop_back':
            emit_word(CFA_PLUS_LOOP)
            offset_cell_addr = app_base + len(buf)
            emit_word(label_map[item[1]] - (offset_cell_addr + 2))
        elif kind == 'if_fwd':
            emit_word(CFA_0BRANCH)
            offset_cell_addr = app_base + len(buf)
            emit_word(label_map[item[1]] - (offset_cell_addr + 2))
        elif kind == 'else_fwd':
            emit_word(CFA_BRANCH)
            offset_cell_addr = app_base + len(buf)
            emit_word(label_map[item[1]] - (offset_cell_addr + 2))
        elif kind == 'again_back':
            emit_word(CFA_BRANCH)
            offset_cell_addr = app_base + len(buf)
            emit_word(label_map[item[1]] - (offset_cell_addr + 2))
        elif kind == 'until_back':
            emit_word(CFA_0BRANCH)
            offset_cell_addr = app_base + len(buf)
            emit_word(label_map[item[1]] - (offset_cell_addr + 2))
        else:
            name = item[1]
            if name in word_cfa:
                emit_word(word_cfa[name])
            elif name in var_cfa:
                emit_word(var_cfa[name])
            elif name in kwords:
                emit_word(kwords[name])
            elif kcode_cfas and name in kcode_cfas:
                emit_word(kcode_cfas[name])
            else:
                raise ValueError(f"Unknown word: {name!r}")

    # Main thread
    for item in main_thread:
        resolve(item)

    # Colon definitions
    for name, items in definitions.items():
        pad_to(word_cfa[name])
        emit_word(DOCOL)        # CFA cell
        for item in items:
            resolve(item)
        emit_word(CFA_EXIT)

    # CODE definitions
    for name in code_definitions:
        pad_to(word_cfa[name])
        emit_word(word_cfa[name] + 2)   # CFA cell points to machine code
        buf.extend(code_bytes[name])    # raw machine code

    # Variable definitions
    for name in variables:
        pad_to(var_cfa[name])
        emit_word(DOVAR)        # CFA cell
        emit_word(0)            # data cell, initialized to 0

    return buf


# ── DECB I/O ──────────────────────────────────────────────────────────────────

def read_decb(path):
    """
    Parse a DECB binary.  Returns (records, exec_addr) where records is a
    list of (load_address, bytes) tuples.

    lwasm DECB block format:
        Data block:  $00  length(2 BE)  addr(2 BE)  data[length]
        End  block:  $FF  $00  $00  exec_addr(2 BE)
    """
    data = Path(path).read_bytes()
    records = []
    exec_addr = 0
    i = 0
    while i < len(data):
        if data[i] == 0x00:
            length  = struct.unpack('>H', data[i + 1:i + 3])[0]
            addr    = struct.unpack('>H', data[i + 3:i + 5])[0]
            payload = data[i + 5:i + 5 + length]
            records.append((addr, payload))
            i += 5 + length
        elif data[i] == 0xFF:
            exec_addr = struct.unpack('>H', data[i + 3:i + 5])[0]
            break
        else:
            i += 1
    return records, exec_addr


def write_decb(records, exec_addr, out_file):
    """
    Write a DECB binary from a list of (load_addr, bytes) records.

    lwasm DECB block format:
        Data block:  $00  length(2 BE)  addr(2 BE)  data[length]
        End  block:  $FF  $00  $00  exec_addr(2 BE)
    """
    with open(out_file, 'wb') as f:
        for load_addr, payload in records:
            f.write(bytes([0x00]))
            f.write(struct.pack('>H', len(payload)))
            f.write(struct.pack('>H', load_addr))
            f.write(payload)
        f.write(bytes([0xFF, 0x00, 0x00]))
        f.write(struct.pack('>H', exec_addr))


# ── 6809 Cycle Counter ────────────────────────────────────────────────────────
#
# The 6809 is fully deterministic: no pipeline, no cache, no branch prediction.
# Every instruction has an exact cycle count determined by the opcode and
# addressing mode.  This module computes per-word cycle costs for both
# CODE words (6809 assembly) and Forth colon definitions (ITC threading).

# Opcode table: mnemonic → (inherent, immediate, direct, extended, indexed_base)
# None = mode unavailable for that opcode.
# Sources: Motorola MC6809E data sheet, Table A-1.
OPCODE_CYCLES = {
    # ── 8-bit loads/stores ──
    'LDA':  (None, 2, 4, 5, 4),  'LDB':  (None, 2, 4, 5, 4),
    'STA':  (None, None, 4, 5, 4), 'STB':  (None, None, 4, 5, 4),
    # ── 16-bit loads/stores ──
    'LDD':  (None, 3, 5, 6, 5),  'STD':  (None, None, 5, 6, 5),
    'LDX':  (None, 3, 5, 6, 5),  'STX':  (None, None, 5, 6, 5),
    'LDY':  (None, 4, 6, 7, 6),  'STY':  (None, None, 6, 7, 6),
    'LDU':  (None, 3, 5, 6, 5),  'STU':  (None, None, 5, 6, 5),
    'LDS':  (None, 4, 6, 7, 6),  'STS':  (None, None, 6, 7, 6),
    # ── 8-bit arithmetic ──
    'ADDA': (None, 2, 4, 5, 4),  'ADDB': (None, 2, 4, 5, 4),
    'ADCA': (None, 2, 4, 5, 4),  'ADCB': (None, 2, 4, 5, 4),
    'SUBA': (None, 2, 4, 5, 4),  'SUBB': (None, 2, 4, 5, 4),
    'SBCA': (None, 2, 4, 5, 4),  'SBCB': (None, 2, 4, 5, 4),
    'CMPA': (None, 2, 4, 5, 4),  'CMPB': (None, 2, 4, 5, 4),
    # ── 16-bit arithmetic ──
    'ADDD': (None, 4, 6, 7, 6),  'SUBD': (None, 4, 6, 7, 6),
    'CMPD': (None, 5, 7, 8, 7),
    'CMPX': (None, 4, 6, 7, 6),  'CMPY': (None, 5, 7, 8, 7),
    'CMPU': (None, 5, 7, 8, 7),  'CMPS': (None, 5, 7, 8, 7),
    # ── 8-bit logic ──
    'ANDA': (None, 2, 4, 5, 4),  'ANDB': (None, 2, 4, 5, 4),
    'ORA':  (None, 2, 4, 5, 4),  'ORB':  (None, 2, 4, 5, 4),
    'EORA': (None, 2, 4, 5, 4),  'EORB': (None, 2, 4, 5, 4),
    'BITA': (None, 2, 4, 5, 4),  'BITB': (None, 2, 4, 5, 4),
    # ── Inherent (register) ──
    'NEGA': (2,), 'NEGB': (2,), 'COMA': (2,), 'COMB': (2,),
    'INCA': (2,), 'INCB': (2,), 'DECA': (2,), 'DECB': (2,),
    'CLRA': (2,), 'CLRB': (2,), 'TSTA': (2,), 'TSTB': (2,),
    'ASLA': (2,), 'ASLB': (2,), 'ASRA': (2,), 'ASRB': (2,),
    'LSLA': (2,), 'LSLB': (2,), 'LSRA': (2,), 'LSRB': (2,),
    'ROLA': (2,), 'ROLB': (2,), 'RORA': (2,), 'RORB': (2,),
    'MUL':  (11,), 'ABX':  (3,), 'DAA':  (2,), 'NOP':  (2,),
    'SEX':  (2,),
    # ── Memory read-modify-write ──
    'NEG':  (None, None, 6, 7, 6), 'COM':  (None, None, 6, 7, 6),
    'INC':  (None, None, 6, 7, 6), 'DEC':  (None, None, 6, 7, 6),
    'CLR':  (None, None, 6, 7, 6), 'TST':  (None, None, 6, 7, 6),
    'ASL':  (None, None, 6, 7, 6), 'ASR':  (None, None, 6, 7, 6),
    'LSL':  (None, None, 6, 7, 6), 'LSR':  (None, None, 6, 7, 6),
    'ROL':  (None, None, 6, 7, 6), 'ROR':  (None, None, 6, 7, 6),
    # ── Transfers ──
    'TFR':  (6,), 'EXG':  (8,),
    # ── LEA ──
    'LEAX': (None, None, None, None, 4),
    'LEAY': (None, None, None, None, 4),
    'LEAU': (None, None, None, None, 4),
    'LEAS': (None, None, None, None, 4),
    # ── Jumps ──
    'JMP':  (None, None, 3, 4, 3),
    'JSR':  (None, None, 7, 8, 7),
    'RTS':  (5,),
    # ── CC ops ──
    'ANDCC': (3,), 'ORCC': (3,),
    'CWAI': (20,), 'SYNC': (2,),
    'SWI': (19,), 'SWI2': (20,), 'SWI3': (20,),
}

# Additional cycles for indexed addressing postbyte variants.
# The base indexed cycle count from OPCODE_CYCLES is added to these.
INDEXED_ADDER = {
    ',R':     0,   ',R+':    2,   ',R++':   3,
    ',-R':    2,   ',--R':   3,
    'n5,R':   1,   'n8,R':   1,   'n16,R':  4,
    'A,R':    1,   'B,R':    1,   'D,R':    4,
    'n8,PCR': 1,   'n16,PCR':5,
    # Indirect variants (extra level of indirection)
    '[,R]':     3,  '[n8,R]':   4,  '[n16,R]':  7,
    '[A,R]':    4,  '[B,R]':    4,  '[D,R]':    7,
    '[n8,PCR]': 4,  '[n16,PCR]':8,  '[n16]':    5,
}

# PSHS/PULS/PSHU/PULU: base 5 cycles + per-register cost.
_REG_CYCLES = {
    'CC': 1, 'A': 1, 'B': 1, 'DP': 1,
    'D': 2, 'X': 2, 'Y': 2, 'U': 2, 'S': 2, 'PC': 2,
}

# Short conditional branches: 3 cycles regardless of taken/not-taken.
_SHORT_BRANCHES = {
    'BEQ', 'BNE', 'BCS', 'BCC', 'BMI', 'BPL', 'BVS', 'BVC',
    'BHI', 'BLS', 'BGE', 'BLE', 'BGT', 'BLT', 'BLO', 'BHS',
}
# Long conditional branches: 5 not-taken, 6 taken.
_LONG_BRANCHES = {
    'LBEQ', 'LBNE', 'LBCS', 'LBCC', 'LBMI', 'LBPL', 'LBVS', 'LBVC',
    'LBHI', 'LBLS', 'LBGE', 'LBLE', 'LBGT', 'LBLT', 'LBLO', 'LBHS',
}


def _parse_stack_regs(operand):
    """Count cycles for PSHS/PULS register list."""
    total = 5  # base cost
    for reg in operand.upper().replace(' ', '').split(','):
        total += _REG_CYCLES.get(reg, 0)
    return total


def _classify_indexed(operand):
    """Classify an indexed operand and return the INDEXED_ADDER key."""
    op = operand.strip()
    indirect = op.startswith('[') and op.endswith(']')
    if indirect:
        op = op[1:-1].strip()

    # ,R++ or ,R+
    if re.match(r'^,\s*[XYUS]\+\+$', op, re.I):
        key = ',R++'
    elif re.match(r'^,\s*[XYUS]\+$', op, re.I):
        key = ',R+'
    # ,--R or ,-R
    elif re.match(r'^,\s*--[XYUS]$', op, re.I):
        key = ',--R'
    elif re.match(r'^,\s*-[XYUS]$', op, re.I):
        key = ',-R'
    # A,R  B,R  D,R
    elif re.match(r'^[ABD]\s*,\s*[XYUS]$', op, re.I):
        reg = op[0].upper()
        key = f'{reg},R'
    # n,PCR
    elif re.match(r'^.+,\s*PCR$', op, re.I):
        # Assume 8-bit offset by default; 16-bit if large label
        key = 'n8,PCR'
    # ,R  (zero offset)
    elif re.match(r'^,\s*[XYUS]$', op, re.I):
        key = ',R'
    # n,R  (offset)
    elif re.match(r'^[^,]+,\s*[XYUS]$', op, re.I):
        offset_str = op.split(',')[0].strip()
        try:
            val = int(offset_str.replace('$', '0x'), 0)
            if -16 <= val <= 15:
                key = 'n5,R'
            elif -128 <= val <= 127:
                key = 'n8,R'
            else:
                key = 'n16,R'
        except ValueError:
            key = 'n16,R'  # named label → assume 16-bit
    # Extended indirect [n]
    elif indirect and ',' not in operand:
        return INDEXED_ADDER.get('[n16]', 5)
    else:
        key = ',R'  # fallback

    if indirect:
        key = f'[{key}]'
    return INDEXED_ADDER.get(key, 0)


def _classify_operand(mnemonic, operand):
    """Classify addressing mode → 'inherent'|'immediate'|'direct'|'extended'|'indexed'."""
    if not operand:
        return 'inherent', 0
    op = operand.strip()
    if op.startswith('#'):
        return 'immediate', 0
    if op.startswith('<'):
        return 'direct', 0
    if op.startswith('>'):
        return 'extended', 0
    if op.startswith('[') or ',' in op:
        return 'indexed', _classify_indexed(operand)
    # Bare number or label — heuristic: small hex values (< $100) are direct page
    # but in this kernel all variables use extended addressing, so default to extended.
    return 'extended', 0


def instruction_cycles(mnemonic, operand):
    """Return (min_cycles, max_cycles) for a single 6809 instruction.

    Returns (0, 0) for unrecognised instructions (FCB, FDB, ORG, etc.)."""
    mn = mnemonic.upper()

    # Stack push/pull — variable cycles based on register list
    if mn in ('PSHS', 'PULS', 'PSHU', 'PULU'):
        cy = _parse_stack_regs(operand)
        return (cy, cy)

    # Short conditional branch: always 3 cycles
    if mn in _SHORT_BRANCHES:
        return (3, 3)
    # Short unconditional: BRA=3, BSR=7
    if mn == 'BRA':
        return (3, 3)
    if mn == 'BRN':
        return (3, 3)
    if mn == 'BSR':
        return (7, 7)

    # Long conditional branch: 5 not-taken, 6 taken
    if mn in _LONG_BRANCHES:
        return (5, 6)
    if mn == 'LBRA':
        return (5, 5)
    if mn == 'LBSR':
        return (9, 9)

    entry = OPCODE_CYCLES.get(mn)
    if entry is None:
        return (0, 0)  # directive or unknown

    # Inherent-only (single-element tuple)
    if len(entry) == 1:
        return (entry[0], entry[0])

    # Full 5-tuple: (inherent, immediate, direct, extended, indexed_base)
    mode, idx_adder = _classify_operand(mn, operand)
    mode_idx = {'inherent': 0, 'immediate': 1, 'direct': 2,
                'extended': 3, 'indexed': 4}[mode]
    base_cy = entry[mode_idx]
    if base_cy is None:
        return (0, 0)  # invalid mode for this opcode
    cy = base_cy + idx_adder
    return (cy, cy)


# ── Assembly line parser ─────────────────────────────────────────────────────

def parse_asm_line(line):
    """Parse one assembly line → (mnemonic, operand) or None for labels/blanks."""
    # Strip comment
    s = line.split(';')[0].strip()
    if not s:
        return None
    # Skip labels on their own line (start with @ or letter at column 0, no space before)
    # Labels: start at column 0 (no leading whitespace)
    if not line[0:1].isspace() and not line[0:1] == '':
        # It's a label.  There may be an instruction after it on the same line.
        parts = s.split(None, 1)
        if len(parts) < 2:
            return None  # label only
        s = parts[1].strip()
        if not s:
            return None
    # Now parse mnemonic + operand
    parts = s.split(None, 1)
    mnemonic = parts[0]
    operand = parts[1].strip() if len(parts) > 1 else ''
    # Skip assembler directives
    if mnemonic.upper() in ('FCB', 'FDB', 'FCC', 'RMB', 'ORG', 'EQU',
                             'PRAGMA', 'SECTION', 'ENDSECT', 'INCLUDE',
                             'SET', 'SETDP', 'FILL'):
        return None
    return (mnemonic, operand)


# ── CODE word cycle analyzer ─────────────────────────────────────────────────

def analyze_code_block(asm_text):
    """Analyze a block of 6809 assembly, return (min_cy, max_cy, notes).

    Sums straight-line cycles.  Detects backward branches (loops) and
    forward branches (min/max paths) heuristically."""
    total_min = 0
    total_max = 0
    notes = []
    labels_seen = {}   # label → cumulative cycle count at that point
    line_num = 0

    for line in asm_text.split('\n'):
        parsed = parse_asm_line(line)

        # Track label positions (for backward branch detection)
        s = line.split(';')[0].strip()
        if s and not line[0:1].isspace():
            label = s.split()[0]
            labels_seen[label] = total_max

        if parsed is None:
            continue
        mnemonic, operand = parsed
        mn = mnemonic.upper()
        cmin, cmax = instruction_cycles(mn, operand)
        total_min += cmin
        total_max += cmax

        # Backward branch detection (loop)
        if mn in _SHORT_BRANCHES and operand.strip() in labels_seen:
            loop_start = labels_seen[operand.strip()]
            loop_body = total_max - loop_start
            notes.append(f"loop: ~{loop_body}cy/iter")

    return (total_min, total_max, notes)


def analyze_code_word(name, asm_text):
    """Analyze a user CODE word from fc.py source."""
    _, preprocessed = preprocess_asm(name, asm_text)
    return analyze_code_block(preprocessed)


# ── Kernel primitive analyzer ────────────────────────────────────────────────

def analyze_kernel_primitives(kernel_asm_path):
    """Parse kernel.asm and compute cycle costs for each CODE_xxx primitive.

    Returns dict mapping Forth name → (min_cy, max_cy)."""
    try:
        with open(kernel_asm_path) as f:
            lines = f.readlines()
    except FileNotFoundError:
        return {}

    # Extract CODE_xxx blocks: from label to next CODE_ label or DOCOL/DOVAR
    blocks = {}
    current_label = None
    current_lines = []
    boundaries = {'DOCOL', 'DOVAR'}
    capture_labels = {'DOCOL', 'DOVAR'}  # also capture these as blocks

    for line in lines:
        stripped = line.strip()
        # Check for CODE_xxx label at column 0
        if stripped and not line[0].isspace():
            word = stripped.split()[0]
            if word.startswith('CODE_') or word in boundaries:
                if current_label and (current_label.startswith('CODE_') or
                                       current_label in capture_labels):
                    blocks[current_label] = '\n'.join(current_lines)
                current_label = word
                current_lines = []
                continue
        if current_label:
            current_lines.append(line.rstrip())

    # Don't forget the last block
    if current_label and (current_label.startswith('CODE_') or
                           current_label in capture_labels):
        blocks[current_label] = '\n'.join(current_lines)

    # Analyze each block
    raw_costs = {}
    for label, asm in blocks.items():
        cmin, cmax, _ = analyze_code_block(asm)
        raw_costs[label] = (cmin, cmax)

    # Build reverse mapping: CFA_xxx → Forth name
    # Use the same names dict from kernel_words()
    cfa_to_forth = {
        'CODE_EMIT': 'emit', 'CODE_HALT': 'halt', 'CODE_EXIT': 'exit',
        'CODE_ADD': '+', 'CODE_SUB': '-', 'CODE_CR': 'cr',
        'CODE_DUP': 'dup', 'CODE_DROP': 'drop', 'CODE_SWAP': 'swap',
        'CODE_OVER': 'over', 'CODE_FETCH': '@', 'CODE_STORE': '!',
        'CODE_DO': 'do', 'CODE_LOOP': 'loop', 'CODE_I': 'i',
        'CODE_MUL': '*', 'CODE_DIVMOD': '/mod',
        'CODE_KBD_SCAN': 'kbd-scan', 'CODE_KEY': 'key',
        'CODE_KEY_NB': 'key?',
        'CODE_0BRANCH': '0branch', 'CODE_BRANCH': 'branch',
        'CODE_EQ': '=', 'CODE_NEQ': '<>', 'CODE_LT': '<', 'CODE_GT': '>',
        'CODE_ZEQU': '0=', 'CODE_AT': 'at',
        'CODE_CSTORE': 'c!', 'CODE_CFETCH': 'c@',
        'CODE_AND': 'and', 'CODE_OR': 'or',
        'CODE_FILL': 'fill', 'CODE_CMOVE': 'cmove',
        'CODE_LSHIFT': 'lshift', 'CODE_RSHIFT': 'rshift',
        'CODE_NEGATE': 'negate', 'CODE_QDUP': '?dup',
        'CODE_TYPE': 'type', 'CODE_COUNT': 'count',
        'CODE_PLUS_STORE': '+!', 'CODE_2DROP': '2drop', 'CODE_2DUP': '2dup',
        'CODE_ROT': 'rot', 'CODE_PROX_SCAN': 'prox-scan',
        'CODE_TOR': '>r', 'CODE_FROMR': 'r>', 'CODE_RAT': 'r@',
        'CODE_MIN': 'min', 'CODE_MAX': 'max', 'CODE_ABS': 'abs',
        'CODE_MDIST': 'mdist', 'CODE_UNLOOP': 'unloop',
        'CODE_PICK': 'pick', 'CODE_INVERT': 'invert', 'CODE_XOR': 'xor',
        'CODE_J': 'j', 'CODE_PLUS_LOOP': '+loop',
        'CODE_LIT': 'lit',
    }

    result = {}
    for code_label, costs in raw_costs.items():
        forth_name = cfa_to_forth.get(code_label, code_label)
        result[forth_name] = costs

    return result


# ── Forth word cycle analyzer ────────────────────────────────────────────────

def analyze_forth_words(definitions, kernel_costs, code_word_costs, variables=None):
    """Compute cycle costs for all user Forth (colon) definitions.

    Returns dict mapping word name → (min_cy, max_cy, notes)."""
    docol_cost = kernel_costs.get('DOCOL', (0, 0))
    exit_cost  = kernel_costs.get('exit', (0, 0))
    lit_cost   = kernel_costs.get('lit', (0, 0))
    branch_cost = kernel_costs.get('branch', (0, 0))
    zbranch_cost = kernel_costs.get('0branch', (0, 0))
    do_cost    = kernel_costs.get('do', (0, 0))
    loop_cost  = kernel_costs.get('loop', (0, 0))
    ploop_cost = kernel_costs.get('+loop', (0, 0))
    dovar_cost = kernel_costs.get('DOVAR', (0, 0))

    var_set = set(variables) if variables else set()

    user_costs = {}  # name → (min, max, notes)

    def lookup_word(name):
        """Get (min, max) for a word by name."""
        if name in user_costs:
            return (user_costs[name][0], user_costs[name][1])
        if name in var_set:
            return dovar_cost
        if name in kernel_costs:
            return kernel_costs[name]
        if name in code_word_costs:
            return (code_word_costs[name][0], code_word_costs[name][1])
        return (0, 0)  # unknown

    def analyze_one(name, visited):
        if name in user_costs:
            return user_costs[name]
        if name not in definitions:
            return (0, 0, [])
        if name in visited:
            return (0, 0, ['recursive'])
        visited = visited | {name}

        items = definitions[name]
        total_min = docol_cost[0]
        total_max = docol_cost[1]
        notes = []

        # For IF/ELSE/THEN and loop tracking
        i = 0
        while i < len(items):
            item = items[i]

            if item[0] == 'word':
                wname = item[1]
                if wname in definitions and wname not in user_costs:
                    analyze_one(wname, visited)
                wmin, wmax = lookup_word(wname)
                total_min += wmin
                total_max += wmax

            elif item[0] == 'lit':
                total_min += lit_cost[0]
                total_max += lit_cost[1]

            elif item[0] == 'slit':
                # String literal: BRANCH + string data + LIT addr + LIT len
                total_min += branch_cost[0] + lit_cost[0] * 2
                total_max += branch_cost[1] + lit_cost[1] * 2

            elif item[0] == 'do':
                total_min += do_cost[0]
                total_max += do_cost[1]

            elif item[0] == 'if_fwd':
                total_min += zbranch_cost[0]
                total_max += zbranch_cost[1]

            elif item[0] == 'else_fwd':
                total_min += branch_cost[0]
                total_max += branch_cost[1]

            elif item[0] == 'loop_back':
                # LOOP: min = exit loop, max = continue
                total_min += loop_cost[0]
                total_max += loop_cost[1]
                notes.append('has DO/LOOP')

            elif item[0] == 'ploop_back':
                total_min += ploop_cost[0]
                total_max += ploop_cost[1]
                notes.append('has DO/+LOOP')

            elif item[0] == 'again_back':
                total_min += branch_cost[0]
                total_max += branch_cost[1]
                notes.append('infinite loop (per-iter cost)')

            elif item[0] == 'until_back':
                total_min += zbranch_cost[0]
                total_max += zbranch_cost[1]
                notes.append('has BEGIN/UNTIL loop')

            elif item[0] == 'label':
                pass  # no cost

            i += 1

        # Add EXIT cost
        total_min += exit_cost[0]
        total_max += exit_cost[1]

        user_costs[name] = (total_min, total_max, notes)
        return user_costs[name]

    for name in definitions:
        analyze_one(name, set())

    return user_costs


# ── Cycle report ─────────────────────────────────────────────────────────────

def cycle_report(definitions, code_definitions, variables, kernel_map_path):
    """Print per-word cycle cost report."""
    # Derive kernel.asm path from kernel.map path
    kernel_asm_path = str(Path(kernel_map_path).parent.parent / 'kernel.asm')

    # Step 1: Analyze kernel primitives
    kernel_costs = analyze_kernel_primitives(kernel_asm_path)
    if not kernel_costs:
        print(f"\n  warning: could not read {kernel_asm_path}, skipping cycle report")
        return

    # Step 2: Analyze user CODE words
    code_word_costs = {}
    for name, asm_text in code_definitions.items():
        code_word_costs[name] = analyze_code_word(name, asm_text)

    # Step 3: Analyze user Forth words
    user_costs = analyze_forth_words(definitions, kernel_costs, code_word_costs,
                                      variables=variables)

    # Step 4: Print report
    docol = kernel_costs.get('DOCOL', (0, 0))
    exit_ = kernel_costs.get('exit', (0, 0))
    next_cy = instruction_cycles('LDY', ',X++')[0] + instruction_cycles('JMP', '[,Y]')[0]

    print(f"\n=== Cycle Estimates (6809 @ 0.895 MHz, 14917 cy/frame) ===")
    print(f"NEXT: {next_cy}cy  DOCOL: {docol[0]}cy  EXIT: {exit_[0]}cy")

    # Kernel primitives (compact)
    print(f"\nKernel Primitives:")
    kp_items = [(n, c) for n, c in sorted(kernel_costs.items())
                if n not in ('DOCOL', 'DOVAR')]
    row = []
    for name, (cmin, cmax) in kp_items:
        if cmin == cmax:
            row.append(f"  {name:<12s} {cmin:>4d}cy")
        else:
            row.append(f"  {name:<12s} {cmin}-{cmax}cy")
        if len(row) == 4:
            print(''.join(row))
            row = []
    if row:
        print(''.join(row))

    # CODE words
    if code_word_costs:
        print(f"\nCODE Words:")
        for name, (cmin, cmax, notes) in sorted(code_word_costs.items(),
                                                  key=lambda x: -x[1][1]):
            note_str = f"  ({', '.join(notes)})" if notes else ""
            if cmin == cmax:
                print(f"  {name:<24s} {cmin:>6d}cy{note_str}")
            else:
                print(f"  {name:<24s} {cmin:>5d}-{cmax}cy{note_str}")

    # Forth words (sorted by max cost, descending)
    if user_costs:
        print(f"\nForth Words (by max cost):")
        for name, (cmin, cmax, notes) in sorted(user_costs.items(),
                                                  key=lambda x: -x[1][1]):
            note_str = f"  ({', '.join(notes)})" if notes else ""
            if cmin == cmax:
                print(f"  {name:<24s} {cmin:>6d}cy{note_str}")
            else:
                print(f"  {name:<24s} {cmin:>5d}-{cmax}cy{note_str}")


# ── Entry point ───────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description='Forth cross-compiler for the CoCo kernel')
    parser.add_argument('source',           help='Forth source file (.fs)')
    parser.add_argument('--kernel',         default='build/kernel.map',
                        help='lwasm map file (default: build/kernel.map)')
    parser.add_argument('--kernel-bin',     default=None,
                        help='kernel DECB binary to combine with app output')
    parser.add_argument('--output',         default=None,
                        help='output binary (default: <source>.bin)')
    parser.add_argument('--base',           default='0x2000',
                        help='application load address (default: 0x2000)')
    parser.add_argument('--hole',           default=None,
                        help='reserved address hole, e.g. 0x4000,6144 (start,size)')
    parser.add_argument('--cycles',        action='store_true',
                        help='print per-word 6809 cycle cost estimates')
    args = parser.parse_args()

    app_base = int(args.base, 16)
    out_file = args.output or str(Path(args.source).with_suffix('.bin'))

    hole_start = hole_end = None
    if args.hole:
        parts = args.hole.split(',')
        hole_start = int(parts[0], 0)
        hole_size  = int(parts[1], 0)
        hole_end   = hole_start + hole_size

    symbols              = load_symbols(args.kernel)
    src_path             = Path(args.source)
    source               = src_path.read_text()
    tokens               = tokenize(source, base_dir=src_path.parent)
    defs, variables, main, code_defs, kcode_defs = parse(tokens)

    # Inject kernel symbols as Forth CONSTANTs:
    #   VAR_*        → KVAR-*        (variable addresses)
    #   KERN_VERSION → KERN-VERSION  (version constant)
    for sym, addr in symbols.items():
        if sym.startswith('VAR_'):
            forth_name = 'kvar-' + sym[4:].lower().replace('_', '-')
        elif sym == 'KERN_VERSION':
            forth_name = 'kern-version'
        else:
            continue
        if forth_name not in defs:
            defs[forth_name] = [('lit', addr)]

    inline_constants(defs, main)

    # ── Assemble KCODE words into kernel address space ──
    kcode_bytes = bytearray()
    kcode_cfas = {}
    if kcode_defs:
        kern_end = symbols.get('KERN_END', 0xEDB0)
        # KCODE words reference FVAR_* (Forth variable addresses) via EQU labels
        # in the assembly.  Use dummy addresses for initial assembly — the real
        # addresses are computed by compile_forth in Pass 1, but KCODE words only
        # need valid placeholder addresses for lwasm to resolve the EQUs.
        # We'll re-assemble with real addresses after compile_forth runs.
        kcode_dummy_vars = {name: 0x4000 + i * 4 for i, name in enumerate(variables)}
        kcode_bytes, kcode_cfas = assemble_kcode_words(
            kcode_defs, symbols, kcode_dummy_vars, kern_end)

    code = compile_forth(defs, variables, main, code_defs, symbols, app_base,
                         hole_start=hole_start, hole_end=hole_end,
                         kcode_cfas=kcode_cfas)

    # Re-assemble KCODE with real variable addresses
    if kcode_defs and variables:
        # compute real FVAR addresses: variables are at end of app code,
        # each is DOVAR(2) + data(2) = 4 bytes.  The first variable's CFA
        # is at app_base + len(code) - len(variables)*4.
        var_base = app_base + len(code) - len(variables) * 4
        real_vars = {name: var_base + i * 4 + 2  # +2 for data cell (after DOVAR)
                     for i, name in enumerate(variables)}
        kcode_bytes, kcode_cfas_new = assemble_kcode_words(
            kcode_defs, symbols, real_vars, kern_end)
        # CFAs should be identical since kernel addresses don't change
        assert kcode_cfas == kcode_cfas_new, "KCODE CFA mismatch after re-assembly"

    if args.kernel_bin:
        # Combine kernel + app into one DECB binary.
        # BASIC loads both blocks in a single CLOADM, then executes bootstrap.
        kernel_records, exec_addr = read_decb(args.kernel_bin)

        # Remap kernel records at $E000+ to staging address.
        # The bootstrap code copies them to their final location at runtime.
        # Stage immediately after the bootstrap record to maximise headroom
        # before APP_BASE.  The bootstrap at $0E00 is small (~25 bytes);
        # packing the kernel right after it reclaims ~$0E19–$0FFF.
        boot_end = 0x0E00
        for addr, payload in kernel_records:
            if addr < 0xE000:
                boot_end = max(boot_end, addr + len(payload))
        KERNEL_STAGE_ADDR = boot_end
        staged_records = []
        stage_cursor = KERNEL_STAGE_ADDR
        for addr, payload in kernel_records:
            if addr >= 0xE000:
                staged_records.append((stage_cursor, payload))
                stage_cursor += len(payload)
            else:
                staged_records.append((addr, payload))
        kernel_records = staged_records

        # Patch bootstrap's LDX #$1000 to point at new staging address.
        # The bootstrap has: LDX #<stage_addr> (opcode 8E xx xx)
        OLD_STAGE = 0x1000
        if KERNEL_STAGE_ADDR != OLD_STAGE:
            old_hi = (OLD_STAGE >> 8) & 0xFF
            old_lo = OLD_STAGE & 0xFF
            new_hi = (KERNEL_STAGE_ADDR >> 8) & 0xFF
            new_lo = KERNEL_STAGE_ADDR & 0xFF
            for idx, (addr, payload) in enumerate(kernel_records):
                if addr < 0xE000:       # bootstrap record
                    payload = bytearray(payload)
                    for j in range(len(payload) - 2):
                        if (payload[j] == 0x8E and
                            payload[j+1] == old_hi and payload[j+2] == old_lo):
                            payload[j+1] = new_hi
                            payload[j+2] = new_lo
                            kernel_records[idx] = (addr, bytes(payload))
                            break

        # Append KCODE bytes to the staged kernel region
        if kcode_bytes:
            kernel_records.append((stage_cursor, bytes(kcode_bytes)))
            new_kern_end = symbols.get('KERN_END', 0xEDB0) + len(kcode_bytes)
            stage_cursor += len(kcode_bytes)

            # Patch bootstrap's CMPY #KERN_END operand to include KCODE
            # The bootstrap has: CMPY #KERN_END (opcode 10 8C xx xx)
            # Find and patch in the staged records
            old_end = symbols['KERN_END']
            old_end_hi = (old_end >> 8) & 0xFF
            old_end_lo = old_end & 0xFF
            new_end_hi = (new_kern_end >> 8) & 0xFF
            new_end_lo = new_kern_end & 0xFF
            for idx, (addr, payload) in enumerate(kernel_records):
                payload = bytearray(payload)
                # Search for CMPY immediate: 10 8C <old_hi> <old_lo>
                for j in range(len(payload) - 3):
                    if (payload[j] == 0x10 and payload[j+1] == 0x8C and
                        payload[j+2] == old_end_hi and payload[j+3] == old_end_lo):
                        payload[j+2] = new_end_hi
                        payload[j+3] = new_end_lo
                        kernel_records[idx] = (addr, bytes(payload))
                        break

        # Patch kernel's LDX #APP_BASE if --base differs from default.
        # START has: LDX #$2000 (opcode 8E 20 00).
        KERN_APP_BASE = symbols.get('APP_BASE', 0x2000)
        if app_base != KERN_APP_BASE:
            old_hi = (KERN_APP_BASE >> 8) & 0xFF
            old_lo = KERN_APP_BASE & 0xFF
            new_hi = (app_base >> 8) & 0xFF
            new_lo = app_base & 0xFF
            for idx, (addr, payload) in enumerate(kernel_records):
                payload = bytearray(payload)
                for j in range(len(payload) - 2):
                    if (payload[j] == 0x8E and
                        payload[j+1] == old_hi and payload[j+2] == old_lo):
                        payload[j+1] = new_hi
                        payload[j+2] = new_lo
                        kernel_records[idx] = (addr, bytes(payload))
                        break

        if stage_cursor > app_base:
            sys.exit(f"fc.py: staged kernel ends at ${stage_cursor:04X}, "
                     f"overlaps app base ${app_base:04X}")
        if hole_start is not None and hole_start > app_base:
            # Split app around hole: two DECB records, skip the hole
            hole_off = hole_start - app_base
            hole_sz  = hole_end - hole_start
            part1 = bytes(code[:hole_off])
            part2 = bytes(code[hole_off + hole_sz:])
            app_records = [(app_base, part1)]
            if part2:
                app_records.append((hole_end, part2))
            code_size = len(part1) + len(part2)
        else:
            app_records = [(app_base, bytes(code))]
            code_size = len(code)
        write_decb(kernel_records + app_records, exec_addr, out_file)
        hole_msg = f", hole ${hole_start:04X}-${hole_end-1:04X}" if hole_start else ""
        print(f"combined → {out_file}  (kernel + {code_size} byte app at ${app_base:04X}{hole_msg}, exec ${exec_addr:04X})")
    else:
        write_decb([(app_base, bytes(code))], exec_addr=0x0000, out_file=out_file)
        print(f"compiled {len(code)} bytes → {out_file}  (load ${app_base:04X})")

    if defs:
        print(f"  words: {', '.join(defs)}")
    if code_defs:
        print(f"  CODE:  {', '.join(code_defs)}")
    if kcode_defs:
        print(f"  KCODE: {', '.join(kcode_defs)} ({len(kcode_bytes)} bytes in kernel space)")
    if variables:
        print(f"  vars:  {', '.join(variables)}")

    if args.cycles:
        cycle_report(defs, code_defs, variables, args.kernel)


if __name__ == '__main__':
    main()
