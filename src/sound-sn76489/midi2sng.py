"""midi2sng.py - turn a Standard MIDI File into an SN76489 song stream.

usage: midi2sng.py in.mid out.sng [--speed X] [--transpose N]
                   [--title T] [--composer C] [--perf FILE] [--draft]

The title and composer for the v2 header (#600) come from TITLES.txt
next to the .mid, by file name stem, unless given on the command line.

No third-party modules: a small SMF reader is included. Notes from every
track (except the GM drum channel 10) are pooled and given to the
chip's three tone channels:

  * a new note takes the free channel whose previous note was nearest
    in pitch, so lines tend to stay on one channel;
  * with all three busy, the middle pitch of the four loses: the new note
    is dropped, or it replaces the inner one, so melody and bass survive.

Timing follows the file's tempo map, converted to 60 Hz frames, and
--speed scales it (1.1 = 10% faster). A performance file (NAME.perf
beside the .mid, see PERFORMANCE.md) adds tempo shaping, dynamics and
voicing. A song with no performance file is refused (#614) unless
--draft is given; a draft gets a gentle final ritardando and mf. The stream format, the octave lift
below B2 and the note gap all come from sn76489.py.
"""

import argparse
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sn76489  # noqa: E402

FPS = 60
PEAKS = [2, 3, 2]   # fallback note-on attenuation (notes carry their own)
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
    """Return (division, tempo_events, notes, timesig).

    tempo_events: [(tick, usec_per_quarter)]
    notes: [(start_tick, end_tick, pitch)]
    timesig: (numerator, denominator) of the first time signature"""
    data = open(path, "rb").read()
    if data[:4] != b"MThd":
        raise ValueError(f"{path}: not a MIDI file")
    hlen = struct.unpack(">I", data[4:8])[0]
    _fmt, ntrk, division = struct.unpack(">HHH", data[8:14])
    if division & 0x8000:
        raise ValueError(f"{path}: SMPTE time division not supported")
    i = 8 + hlen
    tempos, notes = [], []
    timesig = None
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
                elif kind == 0x58 and timesig is None:
                    timesig = (data[j], 2 ** data[j + 1])
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
    return division, tempos, notes, timesig or (4, 4)


# ── The performance layer (#609) ───────────────────────────────────────
#
# Mutopia's MIDI files are flat: one tempo, one velocity. A performance
# file, midi/NAME.perf, adds the interpretation. It is read here and
# baked into the stream: tempo shaping moves the waits, dynamics and
# voicing set each note's starting volume. The format is described in
# PERFORMANCE.md. Bars count from 1; "12.5" is halfway through bar 12.

LEVELS = {"ff": 0, "f": 1, "mf": 2, "mp": 3, "p": 4, "pp": 5}
UNITS = {"2.": 3.0, "2": 2.0, "4.": 1.5, "4": 1.0, "8.": 0.75, "8": 0.5,
         "16.": 0.375, "16": 0.25}   # note values, in quarter notes
VOICES = ("top", "mid", "bottom")


def bar_range(text):
    """'12' or '12-16' -> (12.0, 16.0), both bars whole."""
    a, _, b = text.partition("-")
    return float(a), float(b or a)


class Performance:
    def __init__(self):
        self.tempo = None            # usec per quarter; None keeps the MIDI's
        self.rit = None              # (bars, end factor); None = default
        self.broaden = []            # (from bar, to bar, factor)
        self.dynamics = []           # (bar, "dyn", level) or (a, "ramp", level, b)
        self.voice = {"top": 0, "mid": 1, "bottom": 0}
        self.lead = []               # (from bar, to bar, voice)
        self.accent = 0              # steps louder on each downbeat
        self.pickup = 0.0            # upbeat before bar 1, in quarter notes

    @classmethod
    def load(cls, path):
        perf = cls()
        if not path or not os.path.exists(path):
            return perf
        for n, line in enumerate(open(path), 1):
            words = line.split("#")[0].split()
            if not words:
                continue
            try:
                perf.parse(words)
            except (ValueError, KeyError, IndexError) as e:
                raise ValueError(f"{path}:{n}: {line.strip()!r}: {e}") from None
        return perf

    def parse(self, w):
        cmd = w[0]
        if cmd == "tempo":               # tempo 72 [4.]
            unit = UNITS[w[2]] if len(w) > 2 else 1.0
            self.tempo = 60e6 / float(w[1]) / unit
        elif cmd == "rit":               # rit BARS END  (rit 0 = none)
            self.rit = (float(w[1]), float(w[2]) if len(w) > 2 else 1.0)
        elif cmd == "broaden":           # broaden 12-13 0.9
            a, b = bar_range(w[1])
            self.broaden.append((a, b + 1, float(w[2])))
        elif cmd == "dyn":               # dyn 9 p
            self.dynamics.append((float(w[1]), "dyn", LEVELS[w[2]]))
        elif cmd == "ramp":              # ramp 20-24 f   (hairpin)
            a, b = bar_range(w[1])
            self.dynamics.append((a, "ramp", LEVELS[w[2]], b + 1))
        elif cmd == "voice":             # voice mid 1
            if w[1] not in VOICES:
                raise ValueError("voice is top, mid or bottom")
            self.voice[w[1]] = int(w[2])
        elif cmd == "lead":              # lead 1-4 top
            a, b = bar_range(w[1])
            if w[2] not in VOICES:
                raise ValueError("voice is top, mid or bottom")
            self.lead.append((a, b + 1, w[2]))
        elif cmd == "accent":            # accent 1
            self.accent = int(w[1])
        elif cmd == "pickup":            # pickup 8   (an eighth-note upbeat)
            self.pickup = UNITS[w[1]]
        else:
            raise ValueError(f"unknown command {cmd!r}")

    # Tempo: a factor per position, 1.0 as written, below 1 slower.
    def factor(self, pos, end_pos, beats_per_bar):
        f = 1.0
        for a, b, k in self.broaden:
            if a <= pos < b:
                f *= k
        bars, end = self.rit if self.rit else (2 if beats_per_bar >= 3 else 4, 0.75)
        if bars > 0 and pos > end_pos - bars:
            f *= 1 + (end - 1) * (pos - (end_pos - bars)) / bars
        return f

    def pos(self, tick, division, bar_ticks):
        """Bar position of a tick: bar 1 starts after the pickup."""
        return (tick - self.pickup * division) / bar_ticks + 1

    def timing(self, division, tempos, bar_ticks, end_tick, speed):
        """A function mapping a tick to a 60 Hz frame number."""
        step = max(1, division // 4)           # a sixteenth
        beats = bar_ticks / division
        end_pos = self.pos(end_tick, division, bar_ticks)
        cum = [0.0]
        rates = []
        k = 0
        for t in range(0, end_tick + step, step):
            while k + 1 < len(tempos) and tempos[k + 1][0] <= t:
                k += 1
            us = self.tempo or tempos[k][1]
            pos = self.pos(t + step / 2, division, bar_ticks)
            rate = us / division / 1e6 / self.factor(pos, end_pos, beats)
            rates.append(rate)
            cum.append(cum[-1] + rate * step)

        def conv(tick):
            i = min(tick // step, len(rates) - 1)
            sec = cum[i] + (tick - i * step) * rates[i]
            return round(sec * FPS / speed)
        return conv

    # Loudness: a level per position, from the dyn and ramp marks.
    def level(self, pos):
        marks = sorted(self.dynamics)
        v = LEVELS["mf"]
        if marks and marks[0][1] == "dyn" and marks[0][0] <= 1:
            v = marks[0][2]          # an opening mark covers any upbeat too
        for mark in marks:
            if mark[0] > pos:
                break
            if mark[1] == "dyn":
                v = mark[2]
            else:
                a, _, to, b = mark
                v = to if pos >= b else v + (to - v) * (pos - a) / (b - a)
        return v

    def attenuations(self, notes, division, bar_ticks):
        """The starting attenuation (0 loud to 15 off) of each note."""
        out = []
        for s, e, p in notes:
            pos = self.pos(s, division, bar_ticks)
            sounding = [q for t, u, q in notes if t <= s < u]
            if p >= max(sounding):
                rank = "top"
            elif p <= min(sounding):
                rank = "bottom"
            else:
                rank = "mid"
            att = self.level(pos) + self.voice[rank]
            for a, b, v in self.lead:
                if a <= pos < b and v == rank:
                    att -= 1
            if round(s - self.pickup * division) % bar_ticks == 0:
                att -= self.accent
            out.append(max(0, min(13, round(att))))
        return out


def allocate(notes):
    """notes: [(start, end, pitch, att)] in frames. Returns three voices
    of (start, length, pitch, att), each a single line with no overlaps."""
    voices = [[], [], []]
    busy = [0, 0, 0]          # frame each channel is free from
    last = [76, 64, 48]       # previous pitch: seed top, middle, bottom
    dropped = 0
    for start, end, pitch, att in sorted(notes, key=lambda n: (n[0], -n[2])):
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
            s, _, p, a = voices[c][-1]
            voices[c][-1] = (s, start - s, p, a)
            if voices[c][-1][1] <= 0:
                voices[c].pop()
            dropped += 1
        voices[c].append((start, end - start, pitch, att))
        busy[c] = end
        last[c] = pitch
    return voices, dropped


def convert(path, speed=1.0, transpose=0, perf_path=None):
    division, tempos, raw, (num, den) = read_midi(path)
    perf = Performance.load(perf_path)
    bar_ticks = division * 4 * num // den
    end_tick = max(e for _, e, _ in raw)
    conv = perf.timing(division, tempos, bar_ticks, end_tick, speed)
    atts = perf.attenuations(raw, division, bar_ticks)
    t0 = min(s for s, _, _ in raw)
    notes = [(conv(s) - conv(t0), conv(e) - conv(t0), p + transpose, a)
             for (s, e, p), a in zip(raw, atts)]
    voices, dropped = allocate(notes)
    data = sn76489.stream(voices, 1, PEAKS)
    secs = max(n[0] + n[1] for v in voices for n in v) / FPS
    return data, secs, len(raw), dropped


def lookup_title(path):
    """(title, composer) from TITLES.txt beside the file, else the stem."""
    stem = os.path.splitext(os.path.basename(path))[0]
    listing = os.path.join(os.path.dirname(path), "TITLES.txt")
    if os.path.exists(listing):
        for line in open(listing):
            if line.startswith("#") or "|" not in line:
                continue
            name, title, composer = (f.strip() for f in line.split("|"))
            if name == stem:
                return title, composer
    return stem, ""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("midi")
    ap.add_argument("out")
    ap.add_argument("--speed", type=float, default=1.0)
    ap.add_argument("--transpose", type=int, default=0)
    ap.add_argument("--title")
    ap.add_argument("--composer")
    ap.add_argument("--perf", help="performance file (default: NAME.perf beside the .mid)")
    ap.add_argument("--draft", action="store_true",
                    help="allow a song with no performance file (not for the library)")
    a = ap.parse_args()
    perf = a.perf or os.path.splitext(a.midi)[0] + ".perf"
    if not os.path.exists(perf) and not a.draft:
        sys.exit(f"{a.midi}: no performance file {perf}. Every song ships with a "
                 "researched performance (PERFORMANCE.md, #614); --draft builds "
                 "one without, for trying an import out.")
    data, secs, n, dropped = convert(a.midi, a.speed, a.transpose, perf)
    title, composer = lookup_title(a.midi)
    data = sn76489.header(data, a.title or title, a.composer or composer)
    if len(data) > MAX_SNG:
        sys.exit(f"{a.out}: {len(data)} bytes, over the jukebox's {MAX_SNG}")
    os.makedirs(os.path.dirname(a.out) or ".", exist_ok=True)
    open(a.out, "wb").write(data)
    print(f"{a.out}: {len(data)} B, {secs:.0f} s, {n} notes, {dropped} dropped or cut")


if __name__ == "__main__":
    main()
