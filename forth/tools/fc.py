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
    NUMBER          integer literal  (e.g. 72, 0x48)
    CHAR X          character literal (ASCII value of X)
    WORD            reference to a defined word or kernel primitive
    \               line comment
    ( ... )         block comment
"""

import argparse
import re
import struct
import sys
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
    }
    result = {}
    for forth_name, sym_name in names.items():
        if sym_name not in symbols:
            raise KeyError(f"Kernel symbol {sym_name!r} not found in map file")
        result[forth_name] = symbols[sym_name]
    return result


# ── Tokeniser ─────────────────────────────────────────────────────────────────

def tokenize(source):
    """Strip comments and split into tokens."""
    source = re.sub(r'\\[^\n]*', '', source)          # line comments
    source = re.sub(r'\(.*?\)', '', source, flags=re.DOTALL)  # block comments
    return source.split()


# ── Parser → IR ───────────────────────────────────────────────────────────────

def parse(tokens):
    """
    Walk the token stream and return:
        definitions  — OrderedDict of name → [items]
        main_thread  — [items]  (top-level calls, after all definitions)

    Each item is one of:
        ('lit',  int_value)
        ('word', name_str)
    """
    definitions = {}   # preserves insertion order (Python 3.7+)
    main_thread = []

    current_def = None
    current_items = None
    i = 0

    while i < len(tokens):
        tok = tokens[i]

        if tok == ':':
            i += 1
            name = tokens[i].lower()
            current_def = name
            current_items = []

        elif tok == ';':
            if current_def is None:
                raise SyntaxError("';' without ':'")
            definitions[current_def] = current_items
            current_def = None
            current_items = None

        elif tok.upper() == 'CHAR':
            i += 1
            char_tok = tokens[i]
            item = ('lit', ord(char_tok[0]))
            (current_items if current_def else main_thread).append(item)

        else:
            # Integer literal or word reference
            try:
                val = int(tok, 0)   # base-0 handles 0x… as hex
                item = ('lit', val)
            except ValueError:
                item = ('word', tok.lower())
            (current_items if current_def else main_thread).append(item)

        i += 1

    if current_def is not None:
        raise SyntaxError(f"Unterminated definition: {current_def!r}")

    return definitions, main_thread


# ── Compiler ──────────────────────────────────────────────────────────────────

def item_size(item):
    """Bytes emitted for a single IR item."""
    kind, _ = item
    if kind == 'lit':
        return 4    # CFA_LIT (2) + value (2)
    else:
        return 2    # CFA address


def compile_forth(definitions, main_thread, symbols, app_base):
    """
    Two-pass compiler.

    Layout in the output binary:
        [app_base]              main thread
        [app_base + main_size]  word definitions (DOCOL … EXIT)

    Returns a bytearray to be loaded at app_base.
    """
    DOCOL    = symbols['DOCOL']
    CFA_EXIT = symbols['CFA_EXIT']
    CFA_LIT  = symbols['CFA_LIT']
    kwords   = kernel_words(symbols)

    # ── Pass 1: calculate addresses ───────────────────────────────────────────

    main_size = sum(item_size(it) for it in main_thread)

    word_cfa = {}   # name → address of the word's CFA cell in the output
    cursor = app_base + main_size
    for name, items in definitions.items():
        word_cfa[name] = cursor
        cursor += 2                                      # DOCOL (the CFA cell)
        cursor += sum(item_size(it) for it in items)
        cursor += 2                                      # CFA_EXIT

    # ── Pass 2: generate binary ───────────────────────────────────────────────

    buf = bytearray()

    def emit_word(val):
        buf.extend(struct.pack('>H', val & 0xFFFF))

    def resolve(item):
        kind, val = item
        if kind == 'lit':
            emit_word(CFA_LIT)
            emit_word(val)
        else:
            name = val
            if name in word_cfa:
                emit_word(word_cfa[name])
            elif name in kwords:
                emit_word(kwords[name])
            else:
                raise ValueError(f"Unknown word: {name!r}")

    # Main thread
    for item in main_thread:
        resolve(item)

    # Word definitions
    for name, items in definitions.items():
        emit_word(DOCOL)        # CFA cell
        for item in items:
            resolve(item)
        emit_word(CFA_EXIT)

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
    args = parser.parse_args()

    app_base = int(args.base, 16)
    out_file = args.output or str(Path(args.source).with_suffix('.bin'))

    symbols    = load_symbols(args.kernel)
    source     = Path(args.source).read_text()
    tokens     = tokenize(source)
    defs, main = parse(tokens)
    code       = compile_forth(defs, main, symbols, app_base)

    if args.kernel_bin:
        # Combine kernel + app into one DECB binary.
        # BASIC loads both blocks in a single CLOADM, then executes at START.
        kernel_records, exec_addr = read_decb(args.kernel_bin)
        app_records = [(app_base, bytes(code))]
        write_decb(kernel_records + app_records, exec_addr, out_file)
        print(f"combined → {out_file}  (kernel + {len(code)} byte app at ${app_base:04X}, exec ${exec_addr:04X})")
    else:
        write_decb([(app_base, bytes(code))], exec_addr=0x0000, out_file=out_file)
        print(f"compiled {len(code)} bytes → {out_file}  (load ${app_base:04X})")

    if defs:
        print(f"  words: {', '.join(defs)}")


if __name__ == '__main__':
    main()
