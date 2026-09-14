#!/usr/bin/env python3
"""
cocoterm_bridge.py: XRoar Becker port <-> headless Claude Code.

The CoCo side (src/terminal/terminal.fs) is a dumb terminal: it sends every
key as it is typed and prints whatever arrives.  This bridge owns the line:
it echoes, handles backspace, submits on CR, runs one `claude -p` turn per
line, and streams the reply back reflowed for a 32x16 VDG text screen.

XRoar's Becker port is a TCP *client* that connects once, when the cartridge
is created (xroar/src/becker.c), so start this bridge before XRoar.

Claude also gets peek and poke MCP tools (tools/cocoterm_mcp.py) that read
and write live CoCo memory through ESC commands on the same link (#564).

Usage:
    python3 tools/cocoterm_bridge.py [--cwd DIR] [--permission-mode plan]
    python3 tools/cocoterm_bridge.py --nc        # test with: nc 127.0.0.1 65504
    python3 tools/cocoterm_bridge.py --selftest

Issues: #557 (bridge core), #560 (reflow and sanitizer), #564 (memory tools).
"""

import argparse
import json
import os
import signal
import socket
import subprocess
import sys
import threading
import time
import unicodedata

CR, BS, FF, ETX = '\r', '\x08', '\x0c', '\x03'

SYSTEM_PROMPT = (
    "Your replies are shown on a Tandy Color Computer 2 text screen: "
    "32 columns by 16 rows, plain ASCII only. Keep answers short, use plain "
    "sentences and simple '-' lists, and avoid markdown, tables and code "
    "fences unless the user asks for code."
)

# Unicode punctuation Claude likes, mapped to something the VDG can draw.
PUNCT = {
    '\u2014': '-', '\u2013': '-', '\u2012': '-', '\u2212': '-', '\u2010': '-',
    '\u2018': "'", '\u2019': "'", '\u201a': "'", '\u2032': "'",
    '\u201c': '"', '\u201d': '"', '\u201e': '"', '\u2033': '"',
    '\u2026': '...', '\u2022': '-', '\u00b7': '-', '\u25cf': '-',
    '\u00a0': ' ', '\u2009': ' ', '\u200b': '',
    '\u2192': '->', '\u2190': '<-', '\u21d2': '=>', '\u2194': '<->',
    '\u2264': '<=', '\u2265': '>=', '\u2260': '!=', '\u00d7': 'x',
    '\u2713': 'v', '\u2714': 'v', '\u2717': 'x', '\u00b0': ' deg',
    '\t': ' ',
}


# -- Reflow (#560) -------------------------------------------------------------

class Reflow:
    """Streaming word-wrapper and ASCII sanitizer.

    feed() takes arbitrary chunks of model text and returns terminal output;
    the result does not depend on how the input was chunked.  Output uses CR
    for line breaks and only bytes $20-$7E otherwise.

    autowrap=True matches the CoCo console: writing the last column already
    moves the cursor to the next row, so no CR is sent for an exactly full
    line.  autowrap=False sends that CR (for testing on a normal terminal).
    """

    def __init__(self, cols=32, autowrap=True):
        self.cols = cols
        self.autowrap = autowrap
        self.col = 0
        self.fresh = True           # cursor is at the start of a line
        self.word = ''
        self.line_has_text = False  # for dropping markdown '#' headers
        self.out = []

    def feed(self, text):
        for ch in text:
            self._char(ch)
        return self._take()

    def flush(self):
        """Place any buffered word (end of a streamed block)."""
        self._place_word()
        return self._take()

    def newline(self):
        """Force the next output onto a fresh line."""
        self._place_word()
        if self.col:
            self._break()
        return self._take()

    def _take(self):
        s = ''.join(self.out)
        self.out = []
        return s

    def _char(self, ch):
        if ch > '~' and ch not in PUNCT:
            # Fold accents (e with acute -> e); anything unfoldable stays and becomes '?'.
            ch = ''.join(c for c in unicodedata.normalize('NFKD', ch)
                         if not unicodedata.combining(c)) or '?'
        ch = PUNCT.get(ch, ch)
        if len(ch) != 1:
            for c in ch:
                self._char(c)
            return
        if ch in '*`\r':
            return
        if ch == '\n':
            self._place_word()
            if self.col:
                self._break()
            elif not self.fresh:
                self.out.append(CR)
            self.fresh = False
            self.line_has_text = False
            return
        if ch == ' ':
            self._place_word()
            return
        if ch == '#' and not self.line_has_text:
            return
        if not ' ' <= ch <= '~':
            ch = '?'
        self.line_has_text = True
        self.word += ch

    def _put(self, s):
        self.out.append(s)
        self.col += len(s)
        self.fresh = False
        if self.col >= self.cols:
            self.col = 0
            self.fresh = True
            if not self.autowrap:
                self.out.append(CR)

    def _break(self):
        self.out.append(CR)
        self.col = 0
        self.fresh = True

    def _place_word(self):
        w, self.word = self.word, ''
        if not w:
            return
        if self.col and self.col + 1 + len(w) > self.cols:
            self._break()
        elif self.col:
            self._put(' ')
        while len(w) > self.cols - self.col:
            n = self.cols - self.col
            self._put(w[:n])
            w = w[n:]
        if w:
            self._put(w)


def selftest():
    sample = (
        "## Files in lib\n\nHere are the **main** files \u2014 each one is a "
        "`.fs` library:\n\n- console.fs \u2192 screen output\n- becker.fs: "
        "the \u201cBecker\u201d port\u2026\n\nA very long identifier: "
        "supercalifragilisticexpialidocious_and_then_some_more_text ok.\n"
        "Exactly thirty-two characters!!\nnext line \u00e9t\u00e9 \U0001F600 done"
    )
    cols = 32

    def run(chunk, autowrap):
        r = Reflow(cols, autowrap)
        parts = [r.feed(sample[i:i + chunk]) for i in range(0, len(sample), chunk)]
        return ''.join(parts) + r.flush()

    def screen(s):
        """Simulate the CoCo console: autowrap at cols, CR = next line."""
        lines, cur = [], ''
        for ch in s:
            if ch == CR:
                lines.append(cur)
                cur = ''
            else:
                cur += ch
                if len(cur) == cols:
                    lines.append(cur)
                    cur = ''
        return lines + ([cur] if cur else [])

    base = run(len(sample), True)
    for chunk in range(1, 12):
        assert run(chunk, True) == base, f"chunking changed output (chunk={chunk})"
        assert run(chunk, False) == run(len(sample), False), "chunking changed no-autowrap output"
    for ch in base:
        assert ch == CR or ' ' <= ch <= '~', f"bad byte {ord(ch):#x}"
    plain = run(len(sample), False)
    for seg in plain.split(CR):
        assert len(seg) <= cols, f"line too long: {seg!r}"
    plain_lines = plain.split(CR)
    if plain_lines and plain_lines[-1] == '':
        plain_lines.pop()
    assert screen(base) == plain_lines, "autowrap and CR-wrap screens differ"
    assert '#' not in base and '*' not in base and '`' not in base
    assert '->' in base and '"Becker"' in base and '...' in base
    assert 'ete ? done' in base, "accent folding / unknown glyph"
    print('\n'.join('|' + l.ljust(cols) + '|' for l in screen(base)))
    print('selftest ok')


# -- Paced output --------------------------------------------------------------

class Writer:
    """Single paced writer.  XRoar's Becker receive side drops bytes when its
    buffer is full, and a steady trickle also looks like a real terminal."""

    def __init__(self, cps, nc_mode):
        self.cps = cps
        self.nc_mode = nc_mode
        self.buf = bytearray()
        self.frames = bytearray()   # memory commands: sent first, never discarded
        self.conn = None
        self.cond = threading.Condition()
        threading.Thread(target=self._run, daemon=True).start()

    def attach(self, conn):
        with self.cond:
            self.conn = conn
            self.buf.clear()
            self.frames.clear()

    def detach(self):
        with self.cond:
            self.conn = None
            self.buf.clear()
            self.frames.clear()

    @property
    def connected(self):
        return self.conn is not None

    def write_frame(self, frame):
        with self.cond:
            self.frames += frame
            self.cond.notify()

    def discard(self):
        with self.cond:
            self.buf.clear()

    def write(self, s):
        if not s:
            return
        if self.nc_mode:
            s = (s.replace(CR, '\r\n').replace(BS, '\b \b')
                  .replace(FF, '\x1b[2J\x1b[H'))
        with self.cond:
            self.buf += s.encode('ascii', 'replace')
            self.cond.notify()

    def _run(self):
        tick = 0.02
        per_tick = max(1, int(self.cps * tick))
        while True:
            with self.cond:
                while not (self.buf or self.frames) or self.conn is None:
                    self.cond.wait()
                # A frame finishes before any text, so text never splits one.
                src = self.frames if self.frames else self.buf
                chunk = bytes(src[:per_tick])
                del src[:per_tick]
                conn = self.conn
            try:
                conn.sendall(chunk)
            except OSError:
                pass
            time.sleep(tick)


# -- Memory access (#564) ------------------------------------------------------

ESC = 0x1B
MCP_SCRIPT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'cocoterm_mcp.py')


class Memory:
    """Peek and poke over the Becker link.

    Request frames go out through the Writer; terminal.fs answers with ESC
    followed by the bytes read (or a bare ESC after a write).  serve() hands
    every received byte to feed() first, so keys typed before the ESC still
    reach the line editor.
    """

    CHUNK = 64

    def __init__(self, writer):
        self.w = writer
        self.lock = threading.Lock()     # one request at a time
        self.state = threading.Lock()    # guards want/started/got
        self.want = None                 # bytes expected after ESC; None = idle
        self.started = False
        self.got = bytearray()
        self.done = threading.Event()

    def feed(self, b):
        with self.state:
            if self.want is None:
                return False
            if not self.started:
                if b != ESC:
                    return False
                self.started = True
            else:
                self.got.append(b)
            if len(self.got) >= self.want:
                self.want = None
                self.done.set()
            return True

    def _xfer(self, frame, want, timeout=5.0):
        if not self.w.connected:
            raise ConnectionError('no CoCo connected')
        with self.state:
            self.want, self.started, self.got = want, False, bytearray()
            self.done.clear()
        self.w.write_frame(frame)
        if not self.done.wait(timeout):
            with self.state:
                self.want = None
            raise TimeoutError('no reply from the CoCo')
        return bytes(self.got)

    def peek(self, addr, n):
        out = bytearray()
        with self.lock:
            while n > 0:
                k = min(n, self.CHUNK)
                out += self._xfer(bytes([ESC, ord('P'), addr >> 8, addr & 0xFF, k]), k)
                addr, n = addr + k, n - k
        return bytes(out)

    def poke(self, addr, data):
        with self.lock:
            for i in range(0, len(data), self.CHUNK):
                part = data[i:i + self.CHUNK]
                a = addr + i
                self._xfer(bytes([ESC, ord('W'), a >> 8, a & 0xFF, len(part)]) + part, 0)


def control_server(args, memory):
    """JSON lines on 127.0.0.1:<mem-port>, used by cocoterm_mcp.py."""
    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind(('127.0.0.1', args.mem_port))
    srv.listen(4)

    def handle(conn):
        with conn, conn.makefile('rwb') as f:
            for line in f:
                try:
                    req = json.loads(line)
                    addr = int(req['addr'])
                    if req['op'] == 'peek':
                        n = int(req.get('len', 16))
                        if not 1 <= n <= 1024:
                            raise ValueError('length must be 1-1024')
                        data = None
                    elif req['op'] == 'poke':
                        data = bytes.fromhex(req['data'])
                        n = len(data)
                        if not 1 <= n <= 1024:
                            raise ValueError('data must be 1-1024 bytes')
                    else:
                        raise ValueError(f"unknown op {req['op']!r}")
                    if not (0 <= addr and addr + n <= 0x10000):
                        raise ValueError('address range outside $0000-$FFFF')
                    if data is None:
                        rep = {'ok': True, 'data': memory.peek(addr, n).hex()}
                        log(f"peek ${addr:04X} +{n}")
                    else:
                        memory.poke(addr, data)
                        rep = {'ok': True}
                        log(f"poke ${addr:04X} +{n}")
                except Exception as e:
                    rep = {'ok': False, 'error': str(e)}
                f.write(json.dumps(rep).encode() + b'\n')
                f.flush()

    def loop():
        while True:
            conn, _ = srv.accept()
            threading.Thread(target=handle, args=(conn,), daemon=True).start()

    threading.Thread(target=loop, daemon=True).start()


# -- Claude turns (#557) -------------------------------------------------------

def tool_note(block):
    name = block.get('name', 'tool')
    inp = block.get('input') or {}
    arg = ''
    for key in ('command', 'file_path', 'path', 'pattern', 'url', 'query', 'description'):
        if isinstance(inp.get(key), str):
            arg = inp[key]
            break
    if key in ('file_path', 'path') and arg:
        arg = os.path.basename(arg.rstrip('/')) or arg
    note = f"* {name} {arg}".strip()
    return note[:60]


class Claude:
    def __init__(self, args, writer):
        self.args = args
        self.w = writer
        self.session_id = None
        self.proc = None
        self.cancelled = False
        self.lock = threading.Lock()

    @property
    def busy(self):
        return self.proc is not None

    def start(self, prompt):
        cmd = ['claude', '-p', prompt,
               '--output-format', 'stream-json', '--verbose',
               '--include-partial-messages',
               '--permission-mode', self.args.permission_mode,
               '--append-system-prompt', SYSTEM_PROMPT]
        if self.args.model:
            cmd += ['--model', self.args.model]
        allowed = [self.args.allowed_tools] if self.args.allowed_tools else []
        if not self.args.no_memory:
            mcp = {'mcpServers': {'coco': {
                'command': sys.executable,
                'args': [MCP_SCRIPT, '--port', str(self.args.mem_port)]}}}
            cmd += ['--mcp-config', json.dumps(mcp)]
            allowed += ['mcp__coco__peek', 'mcp__coco__poke']
        if allowed:
            cmd += ['--allowedTools', ','.join(allowed)]
        if self.session_id:
            cmd += ['--resume', self.session_id]
        self.cancelled = False
        env = dict(os.environ)
        if self.args.no_api_key:
            # An API key outranks the claude.ai login; drop it for this child.
            env.pop('ANTHROPIC_API_KEY', None)
        self.proc = subprocess.Popen(
            cmd, cwd=self.args.cwd, env=env, stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE, stderr=sys.stderr, text=True,
            start_new_session=True)
        threading.Thread(target=self._pump, args=(self.proc,), daemon=True).start()

    def cancel(self):
        with self.lock:
            proc, self.cancelled = self.proc, True
        if proc and proc.poll() is None:
            try:
                os.killpg(proc.pid, signal.SIGTERM)
            except ProcessLookupError:
                pass

    def _pump(self, proc):
        r = Reflow(self.args.cols, autowrap=not self.args.nc)
        wrote = False
        for line in proc.stdout:
            if self.cancelled:
                continue
            try:
                ev = json.loads(line)
            except json.JSONDecodeError:
                continue
            t = ev.get('type')
            if ev.get('session_id'):
                self.session_id = ev['session_id']
            if t == 'stream_event':
                e = ev.get('event', {})
                if e.get('type') == 'content_block_start' and \
                        e.get('content_block', {}).get('type') == 'text' and wrote:
                    self.w.write(r.newline())
                elif e.get('type') == 'content_block_delta' and \
                        e.get('delta', {}).get('type') == 'text_delta':
                    self.w.write(r.feed(e['delta'].get('text', '')))
                    wrote = True
                elif e.get('type') == 'content_block_stop':
                    self.w.write(r.flush())
            elif t == 'assistant':
                for b in ev.get('message', {}).get('content', []):
                    if b.get('type') == 'tool_use':
                        if b.get('name', '').startswith('mcp__coco__'):
                            # Memory tools draw on the screen; a note would
                            # scroll it (#565).
                            continue
                        self.w.write(r.newline())
                        self.w.write(r.feed(tool_note(b)))
                        self.w.write(r.newline())
                        wrote = True
            elif t == 'result':
                self.w.write(r.flush())
                if ev.get('is_error') or ev.get('subtype') != 'success':
                    # subtype can say 'success' even on an API error; the
                    # human-readable reason is in 'result' (or 'errors').
                    reason = ev.get('result') or ev.get('errors') or ev.get('subtype')
                    status = ev.get('api_error_status')
                    log(f"turn error: {reason!r} (api status {status}, "
                        f"{ev.get('terminal_reason')})")
                    self.w.write(r.newline() + r.feed(f"* error: {reason}"))
                    self.w.write(r.newline())
        proc.wait()
        with self.lock:
            self.proc = None
            cancelled = self.cancelled
        if not cancelled:
            self.w.write(r.newline() + CR + '> ')
        log(f"turn done (exit {proc.returncode}, session {self.session_id})")


# -- Connection / line discipline ----------------------------------------------

def log(msg):
    print(f"[cocoterm] {msg}", file=sys.stderr, flush=True)


def banner(args):
    where = os.path.basename(os.path.abspath(args.cwd)) or '/'
    return (f"{CR}cocoterm <-> claude code{CR}"
            f"dir: {where[:26]}{CR}"
            f"mode: {args.permission_mode}{CR}{CR}> ")


def serve(conn, args, writer, claude, memory):
    writer.attach(conn)
    writer.write(banner(args))
    line = ''
    last = ''
    while True:
        try:
            data = conn.recv(256)
        except OSError:
            break
        if not data:
            break
        for b in data:
            if memory.feed(b):
                continue
            ch = chr(b)
            if ch == ETX:
                if claude.busy:
                    claude.cancel()
                    writer.discard()
                line = ''
                writer.write(f"^C{CR}> ")
            elif claude.busy:
                pass                      # ignore typing while Claude answers
            elif ch in '\r\n':
                if ch == '\n' and last == '\r':
                    pass                  # CRLF from a host terminal
                elif line.strip():
                    if not args.nc:
                        writer.write(CR)
                    log(f"prompt: {line!r}")
                    claude.start(line)
                    line = ''
                else:
                    line = ''
                    writer.write(f"{CR}> ")
            elif ch in (BS, '\x7f'):
                if line:
                    line = line[:-1]
                    if not args.nc:
                        writer.write(BS)
            elif ch == FF:
                line = ''
                writer.write(FF + '> ')
            elif ' ' <= ch <= '~' and len(line) < 400:
                line += ch
                if not args.nc:
                    writer.write(ch)
            last = ch
    if claude.busy:
        claude.cancel()
    writer.detach()


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n\n')[1])
    ap.add_argument('--host', default='127.0.0.1')
    ap.add_argument('--port', type=int, default=65504)
    ap.add_argument('--cwd', default=os.getcwd(), help='directory Claude works in')
    ap.add_argument('--permission-mode', default='plan')
    ap.add_argument('--allowed-tools', default='', help='passed to claude --allowedTools')
    ap.add_argument('--model', default='')
    ap.add_argument('--no-api-key', action='store_true',
                    help='unset ANTHROPIC_API_KEY for claude so it uses the claude.ai login')
    ap.add_argument('--cols', type=int, default=32)
    ap.add_argument('--cps', type=int, default=400, help='output characters per second')
    ap.add_argument('--nc', action='store_true',
                    help='host-terminal test mode: CRLF, no echo, no autowrap')
    ap.add_argument('--mem-port', type=int, default=65505,
                    help='local control port for the peek/poke MCP tools')
    ap.add_argument('--no-memory', action='store_true',
                    help='do not give Claude the peek/poke tools')
    ap.add_argument('--selftest', action='store_true')
    args = ap.parse_args()

    if args.selftest:
        selftest()
        return

    writer = Writer(args.cps, args.nc)
    claude = Claude(args, writer)
    memory = Memory(writer)
    if not args.no_memory:
        control_server(args, memory)
    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind((args.host, args.port))
    srv.listen(1)
    log(f"listening on {args.host}:{args.port}, cwd {args.cwd}; start XRoar now")
    try:
        while True:
            conn, addr = srv.accept()
            conn.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
            log(f"connected: {addr[0]}:{addr[1]}")
            serve(conn, args, writer, claude, memory)
            conn.close()
            log("disconnected; waiting for the next connection")
    except KeyboardInterrupt:
        claude.cancel()


if __name__ == '__main__':
    main()
