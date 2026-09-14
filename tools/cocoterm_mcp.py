#!/usr/bin/env python3
"""
cocoterm_mcp.py: MCP tools that read and write live CoCo memory.

Claude Code starts this over stdio (cocoterm_bridge.py passes it with
--mcp-config).  Each tool call becomes one JSON line on the bridge's control
port; the bridge runs it over the Becker link against src/terminal/terminal.fs.

Usage:
    python3 tools/cocoterm_mcp.py [--port 65505]

Issue: #564.
"""

import argparse
import json
import socket
import sys

MAP_NOTE = (
    "ROM-mode memory map: $0400-$05FF text screen, $2000-$2EAF kernel, "
    "$3000+ terminal program, data stack below $7E00, return stack below "
    "$8000, $8000-$DFFF BASIC ROMs, $FF00+ I/O. "
)

TOOLS = [
    {
        'name': 'peek',
        'description': (
            "Read bytes from the memory of the CoCo 2 this conversation is "
            "running on (live, over the Becker port). Returns a hex dump. "
            + MAP_NOTE +
            "Avoid $FF40-$FF4F: reading the Becker port breaks the link. "
            "Some other I/O reads have side effects."
        ),
        'inputSchema': {
            'type': 'object',
            'properties': {
                'addr': {'type': ['integer', 'string'],
                         'description': 'start address: 1024, "$0400" or "0x400"'},
                'length': {'type': 'integer',
                           'description': 'bytes to read, 1-1024 (default 16)'},
            },
            'required': ['addr'],
        },
    },
    {
        'name': 'poke',
        'description': (
            "Write bytes into the memory of the CoCo 2 this conversation is "
            "running on (live, over the Becker port). "
            + MAP_NOTE +
            "Screen bytes use T1 lowercase codes: ASCII $60-$7E -> AND $1F, "
            "$40-$5F unchanged, $20-$3F -> OR $40, space is $60. Writing the "
            "kernel, program or stacks can crash the terminal."
        ),
        'inputSchema': {
            'type': 'object',
            'properties': {
                'addr': {'type': ['integer', 'string'],
                         'description': 'start address: 1024, "$0400" or "0x400"'},
                'data': {'type': 'string',
                         'description': 'hex bytes, e.g. "48 49" or "4849"'},
            },
            'required': ['addr', 'data'],
        },
    },
]


def parse_addr(v):
    if isinstance(v, str):
        s = v.strip().lower()
        return int(s[1:], 16) if s.startswith('$') else int(s, 0)
    return int(v)


def dump(addr, data):
    lines = []
    for i in range(0, len(data), 16):
        row = data[i:i + 16]
        hexes = ' '.join(f'{b:02X}' for b in row)
        text = ''.join(chr(b) if 32 <= b < 127 else '.' for b in row)
        lines.append(f'${addr + i:04X}: {hexes:<47}  {text}')
    return '\n'.join(lines)


def bridge(port, req):
    with socket.create_connection(('127.0.0.1', port), timeout=60) as s:
        s.sendall(json.dumps(req).encode() + b'\n')
        line = s.makefile('rb').readline()
    if not line:
        raise RuntimeError('bridge closed the connection')
    rep = json.loads(line)
    if not rep.get('ok'):
        raise RuntimeError(rep.get('error', 'unknown error'))
    return rep


def call_tool(port, name, a):
    addr = parse_addr(a.get('addr'))
    if name == 'peek':
        n = int(a.get('length', 16))
        rep = bridge(port, {'op': 'peek', 'addr': addr, 'len': n})
        return dump(addr, bytes.fromhex(rep['data']))
    if name == 'poke':
        data = bytes.fromhex(str(a.get('data', '')).replace('$', ''))
        bridge(port, {'op': 'poke', 'addr': addr, 'data': data.hex()})
        return f'wrote {len(data)} bytes at ${addr:04X}'
    raise ValueError(f'unknown tool {name}')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--port', type=int, default=65505)
    args = ap.parse_args()

    for line in sys.stdin:
        try:
            msg = json.loads(line)
        except json.JSONDecodeError:
            continue
        mid = msg.get('id')
        method = msg.get('method')
        params = msg.get('params') or {}
        error = None
        if method == 'initialize':
            result = {'protocolVersion': params.get('protocolVersion', '2025-06-18'),
                      'capabilities': {'tools': {}},
                      'serverInfo': {'name': 'coco', 'version': '1.0'}}
        elif method == 'tools/list':
            result = {'tools': TOOLS}
        elif method == 'tools/call':
            try:
                text = call_tool(args.port, params.get('name'), params.get('arguments') or {})
                result = {'content': [{'type': 'text', 'text': text}]}
            except Exception as e:
                result = {'content': [{'type': 'text', 'text': f'error: {e}'}],
                          'isError': True}
        elif method == 'ping':
            result = {}
        elif mid is None:
            continue                      # notification
        else:
            error = {'code': -32601, 'message': f'unknown method {method}'}
        if mid is None:
            continue
        reply = {'jsonrpc': '2.0', 'id': mid}
        reply['error' if error else 'result'] = error or result
        sys.stdout.write(json.dumps(reply) + '\n')
        sys.stdout.flush()


if __name__ == '__main__':
    main()
