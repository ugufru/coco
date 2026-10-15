"""midi2sng.py - turn a Standard MIDI File into an SN76489 song stream.

usage: midi2sng.py in.mid out.sng [--speed X] [--transpose N]

No third-party modules: a small SMF reader is included. Notes from every
track (except the GM drum channel 10) are pooled and given to the
chip's three tone channels:

  * a new note takes the free channel whose previous note was nearest
    in pitch, so lines tend to stay on one channel;
  * with all three busy, the middle pitch of the four loses: the new note
    is dropped, or it replaces the inner one, so melody and bass survive.

Timing follows the file's tempo map, converted to 60 Hz frames, and
--speed scales it (1.1 = 10% faster). The stream format, the octave lift
below B2 and the note gap all come from sn76489.py.
"""

import argparse
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sn76489  # noqa: E402

FPS = 60
PEAKS = [2, 3, 2]   # note-on attenuation per channel; ch1 slightly back
MAX_SNG = 0x1E00    # the jukebox's load area ($5A00-$77FF)


def read_varlen(data, i):
    n = 0
    while True:
        b = data[i]
        i += 1
        n = (n << 7) | (b & 0x7F)
        if not b & 0x80:
            return n, i


def read_midi(path):
    """Return (division, tempo_events, notes).

    tempo_events: [(tick, usec_per_quarter)]
    notes: [(start_tick, end_tick, pitch)]"""
    data = open(path, "rb").read()
    if data[:4] != b"MThd":
        raise ValueError(f"{path}: not a MIDI file")
    hlen = struct.unpack(">I", data[4:8])[0]
    _fmt, ntrk, division = struct.unpack(">HHH", data[8:14])
    if division & 0x8000:
        raise ValueError(f"{path}: SMPTE time division not supported")
    i = 8 + hlen
    tempos, notes = [], []
    for _ in range(ntrk):
        if data[i:i + 4] != b"MTrk":
            raise ValueError(f"{path}: bad track chunk")
        tlen = struct.unpack(">I", data[i + 4:i + 8])[0]
        j, end = i + 8, i + 8 + tlen
        tick, status = 0, 0
        held = {}   # (channel, pitch) -> [start ticks]
        while j < end:
            delta, j = read_varlen(data, j)
            tick += delta
            b = data[j]
            if b & 0x80:
                status = b
                j += 1
            if status == 0xFF:
                kind = data[j]
                ln, j = read_varlen(data, j + 1)
                if kind == 0x51:
                    tempos.append((tick, int.from_bytes(data[j:j + 3], "big")))
                j += ln
                continue
            if status in (0xF0, 0xF7):
                ln, j = read_varlen(data, j)
                j += ln
                continue
            kind, ch = status & 0xF0, status & 0x0F
            if kind in (0xC0, 0xD0):
                j += 1
                continue
            a, v = data[j], data[j + 1]
            j += 2
            if ch == 9 or kind not in (0x80, 0x90):
                continue
            if kind == 0x90 and v > 0:
                held.setdefault((ch, a), []).append(tick)
            elif held.get((ch, a)):
                start = held[(ch, a)].pop(0)
                if tick > start:
                    notes.append((start, tick, a))
        i = end
    tempos.sort()
    if not tempos or tempos[0][0] > 0:
        tempos.insert(0, (0, 500000))
    return division, tempos, notes


def tick_to_frame(division, tempos, speed):
    """A function mapping a tick to a 60 Hz frame number."""
    marks = []   # (tick, seconds at that tick, usec per quarter)
    sec = 0.0
    for k, (t, us) in enumerate(tempos):
        if k:
            pt, ps, pus = marks[-1]
            sec = ps + (t - pt) * pus / division / 1e6
        marks.append((t, sec, us))

    def conv(tick):
        m = marks[0]
        for mk in marks:
            if mk[0] <= tick:
                m = mk
            else:
                break
        s = m[1] + (tick - m[0]) * m[2] / division / 1e6
        return round(s * FPS / speed)
    return conv


def allocate(notes):
    """notes: [(start, end, pitch)] in frames. Returns three voices of
    (start, length, pitch), each a single line with no overlaps."""
    voices = [[], [], []]
    busy = [0, 0, 0]          # frame each channel is free from
    last = [76, 64, 48]       # previous pitch: seed top, middle, bottom
    dropped = 0
    for start, end, pitch in sorted(notes, key=lambda n: (n[0], -n[2])):
        if end <= start:
            end = start + 1
        free = [c for c in range(3) if busy[c] <= start]
        if free:
            c = min(free, key=lambda c: abs(last[c] - pitch))
        else:
            # Every channel sounds. Of the four pitches keep the top and
            # the bottom: an inner new note is dropped; a new top (or
            # bottom) note cuts off the old top (or bottom) and takes its
            # channel, which keeps the outer lines on steady channels.
            current = sorted((voices[c][-1][2], c) for c in range(3))
            if current[0][0] < pitch < current[2][0]:
                dropped += 1
                continue
            c = current[2][1] if pitch >= current[2][0] else current[0][1]
            s, _, p = voices[c][-1]
            voices[c][-1] = (s, start - s, p)
            if voices[c][-1][1] <= 0:
                voices[c].pop()
            dropped += 1
        voices[c].append((start, end - start, pitch))
        busy[c] = end
        last[c] = pitch
    return voices, dropped


def convert(path, speed=1.0, transpose=0):
    division, tempos, raw = read_midi(path)
    conv = tick_to_frame(division, tempos, speed)
    t0 = min(s for s, _, _ in raw)
    notes = [(conv(s) - conv(t0), conv(e) - conv(t0), p + transpose)
             for s, e, p in raw]
    voices, dropped = allocate(notes)
    data = sn76489.stream(voices, 1, PEAKS)
    secs = max(s + l for v in voices for s, l, _ in v) / FPS
    return data, secs, len(raw), dropped


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("midi")
    ap.add_argument("out")
    ap.add_argument("--speed", type=float, default=1.0)
    ap.add_argument("--transpose", type=int, default=0)
    a = ap.parse_args()
    data, secs, n, dropped = convert(a.midi, a.speed, a.transpose)
    if len(data) > MAX_SNG:
        sys.exit(f"{a.out}: {len(data)} bytes, over the jukebox's {MAX_SNG}")
    os.makedirs(os.path.dirname(a.out) or ".", exist_ok=True)
    open(a.out, "wb").write(data)
    print(f"{a.out}: {len(data)} B, {secs:.0f} s, {n} notes, {dropped} dropped or cut")


if __name__ == "__main__":
    main()
