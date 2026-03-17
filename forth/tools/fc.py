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
                result.extend(['__CODE__', code_name, '\n'.join(code_lines), '__ENDCODE__'])
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

        # Check for CODE block start
        if tokens_on_line[0].upper() == 'CODE':
            if len(tokens_on_line) < 2:
                raise SyntaxError("CODE requires a word name")
            code_name = tokens_on_line[1]
            code_lines = []
            in_code_block = True
            continue

        # Process normal tokens
        i = 0
        while i < len(tokens_on_line):
            tok = tokens_on_line[i]
            if tok.upper() == 'INCLUDE':
                i += 1
                filename = tokens_on_line[i]
                filepath = (Path(base_dir) / filename) if base_dir else Path(filename)
                included = tokenize(filepath.read_text(), base_dir=filepath.parent)
                result.extend(included)
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
    code_definitions = {}   # name → raw asm text
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

        if tok == '__CODE__':
            name = tokens[i + 1].lower()
            asm_text = tokens[i + 2]
            # i+3 is __ENDCODE__
            if name in definitions:
                raise SyntaxError(f"CODE word {name!r} collides with colon definition")
            if name in code_definitions:
                raise SyntaxError(f"Duplicate CODE word: {name!r}")
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

    return definitions, variables, main_thread, code_definitions


# ── CODE word assembly ────────────────────────────────────────────────────────

# NEXT sequence for the 6809 ITC kernel (inlined at end of CODE words)
NEXT_ASM = """\
        LDY     ,X++
        JMP     [,Y]"""


def preprocess_asm(name, asm_text):
    """Prepare a CODE word's assembly text for lwasm.

    - Prepend a global label so @-local labels scope correctly
    - Expand ;NEXT into the two-instruction NEXT sequence
    """
    safe = re.sub(r'[^a-z0-9]', '_', name)
    label = f'__code_{safe}'
    lines = [label]
    for line in asm_text.split('\n'):
        if re.match(r'^\s*;NEXT\b', line):
            lines.append(NEXT_ASM)
        else:
            lines.append(line)
    lines.append(f'{label}__END')
    return label, '\n'.join(lines)


def assemble_code_words(code_defs, symbols):
    """Assemble all CODE words into machine code via lwasm.

    Returns an ordered dict of name → bytes (raw machine code for each word).
    """
    if not code_defs:
        return {}

    # Build assembly source with kernel symbol EQUs
    asm_parts = ['        PRAGMA  6809', '        ORG     $0000', '']
    for sym_name, addr in sorted(symbols.items()):
        asm_parts.append(f'{sym_name:20s} EQU     ${addr:04X}')
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


# ── Compiler ──────────────────────────────────────────────────────────────────

def item_size(item):
    """Bytes emitted for a single IR item."""
    kind = item[0]
    if kind == 'lit':        return 4    # CFA_LIT (2) + value (2)
    if kind == 'label':      return 0    # marker only, no bytes
    if kind == 'do':         return 2    # CFA_DO
    if kind == 'loop_back':  return 4    # CFA_LOOP (2) + offset cell (2)
    if kind == 'if_fwd':     return 4    # CFA_0BRANCH (2) + forward offset cell (2)
    if kind == 'else_fwd':   return 4    # CFA_BRANCH (2) + forward offset cell (2)
    if kind == 'again_back': return 4    # CFA_BRANCH (2) + backward offset cell (2)
    if kind == 'until_back': return 4    # CFA_0BRANCH (2) + backward offset cell (2)
    return 2                             # word reference: CFA address


def compile_forth(definitions, variables, main_thread, code_definitions,
                  symbols, app_base, hole_start=None, hole_end=None):
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
    CFA_0BRANCH  = symbols['CFA_0BRANCH']
    CFA_BRANCH   = symbols['CFA_BRANCH']
    kwords       = kernel_words(symbols)

    # Assemble CODE words to get their sizes
    code_bytes = assemble_code_words(code_definitions, symbols)

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
        elif kind == 'label':
            pass    # 0 bytes; position already recorded in label_map
        elif kind == 'do':
            emit_word(CFA_DO)
        elif kind == 'loop_back':
            emit_word(CFA_LOOP)
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
    defs, variables, main, code_defs = parse(tokens)
    code                 = compile_forth(defs, variables, main, code_defs, symbols, app_base,
                                         hole_start=hole_start, hole_end=hole_end)

    if args.kernel_bin:
        # Combine kernel + app into one DECB binary.
        # BASIC loads both blocks in a single CLOADM, then executes bootstrap.
        kernel_records, exec_addr = read_decb(args.kernel_bin)

        # Remap kernel records at $E000+ to staging address ($1000).
        # The bootstrap code copies them to their final location at runtime.
        KERNEL_STAGE_ADDR = 0x1000
        staged_records = []
        stage_cursor = KERNEL_STAGE_ADDR
        for addr, payload in kernel_records:
            if addr >= 0xE000:
                staged_records.append((stage_cursor, payload))
                stage_cursor += len(payload)
            else:
                staged_records.append((addr, payload))
        kernel_records = staged_records

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
    if variables:
        print(f"  vars:  {', '.join(variables)}")


if __name__ == '__main__':
    main()
