"""badinerie.py - J. S. Bach, Badinerie (Orchestral Suite No. 2, BWV 1067).

Hand transcription for the SN76489, three voices plus noise percussion:

    channel 0  flute (the melody)
    channel 1  violin II (the inner line)
    channel 2  basso continuo, lifted an octave where it falls below B2

Source: Bach-Gesellschaft Ausgabe, vol. 31 (public domain), IMSLP #01467,
pages 15-16. Transcribed 2026-10-14 (#579). Score format: see sn76489.py.
Every accidental is written out (key signature not applied), and E#
appears as e#.

Editorial choices:
  - Trills (A8, A10, B11, B14, B16) are played as plain notes.
  - B17's grace-note A5 is played as a long appoggiatura (half the G5).
  - B8 ends on C natural in the flute. The print has no natural sign, but
    the bass under it is a marked C natural with 4/2 figures (D7 over C),
    so C sharp would clash.
Bar labels below: A = first half, B = second half, numbered from the
first full bar.

Run as a script to write build/badinerie.snd, the byte stream that
badinerie.fs splices in with DATA[PY.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sn76489  # noqa: E402

BAR = 16             # 2/4 in thirty-seconds
PICKUP = 8           # each half starts on a quarter-note upbeat
FRAMES_PER_32ND = 4  # sixteenth = 8 frames, quarter = 112.5 bpm

# Each voice: upbeat, full bars, closing quarter; for both halves.
FLUTE = {
    "a_pick": "b5:8 d6:16 b5",
    "a": [
        "f#5:8 b5:16 f#5 d5:8 f#5:16 d5",          # A1
        "b4:4 f#4:16 b4 d5 b4",
        "c#5:16 b4 c#5 b4 a#4 c#5 e5 c#5",
        "d5:8 b4 b5 d6:16 b5",
        "f#5:8 b5:16 f#5 d5:8 f#5:16 d5",          # A5
        "b4:4 d5:8 d5",
        "d5:8 d5 b5 d5",
        "d5:8 c#5 f#5 f#5",
        "f#5:8 f#5 d6 f#5",
        "f#5:8 e#5 c#5:16 f#5 a5 f#5",             # A10
        "g#5:16 f#5 g#5 f#5 e#5 g#5 b5 g#5",
        "a5:16 g#5 a5 g#5 f#5 a5 f#5 e#5",
        "f#5:16 b5 f#5 e#5 f#5 c#6 f#5 e#5",
        "f#5:16 d6 f#5 e#5 f#5 d6 c#6 b5",
        "c#6:16 a5 g#5 f#5 a5:8 g#5",              # A15
    ],
    "a_end": "f#5:4",
    "b_pick": "f#5:8 a5:16 f#5",
    "b": [
        "c#5:8 f#5:16 c#5 a4:8 c#5:16 a4",         # B1
        "f#4:4 c5:8 b4",
        "e5:8 d#5:16 f#5 a5:8 g5:16 f#5",
        "g5:8 e5 g5 b5:16 g5",
        "e5:8 g5:16 e5 c#5:8 e5:16 c#5",           # B5
        "a4:4~ a4:16 d5 f#5 d5",
        "e5:16 d5 e5 d5 c#5 e5 g5 e5",
        "f#5:16 e5 f#5 e5 d5 f#5 d5 c5",
        "d5:16 g5 d5 c#5 d5 a5 d5 c#5",
        "d5:16 b5 d5 c#5 d5 b5 a5 g5",             # B10
        "a5:16 f#5 e5 d5 f#5:8 e5",
        "d5:4 f#5:8 f#5",
        "f#5:8 f#5 d6 f#5",
        "f#5:8 e5 e5 e5",
        "e5:8 e5 c#6 e5",                          # B15
        "e5:8 d5 b5 d6:16 b5",
        "a5:8 g5:8~ g5:8 b5:32 a5 g5 f#5",
        "e5:4~ e5:8 g5:32 f#5 e5 d5",
        "c5:16 e5 g5 e5 c5 b4 c5 b4",
        "a#4:8 f#4 g4 f#4",                        # B20
        "b4:8 a#4:16 c#5 e5:8 d5:16 c#5",
        "d5:8 b4:32 c#5 d5 e5 f#5:8 d5:16 f#5",
        "b5:8 f#5 e5:16 d5 c#5 d5",
    ],
    "b_end": "b4:4",
}

VIOLIN2 = {
    "a_pick": "f#5:8 r",
    "a": [
        "d5:8 r b4 r",                             # A1
        "f#4:4 f#4:8 b4",
        "b4:8 b4 a#4 a#4",
        "b4:8 f#4 f#5 r",
        "d5:8 r b4 r",                             # A5
        "f#4:4 f#4:8 b4",
        "e4:8 b4 g#4 b4",
        "a4:8 e4 a4 d5",
        "g#4:8 d5 b4 d5",
        "c#5:8 g#4 c#5 f#5",                       # A10
        "f#5:8 f#5 e#5 e#5",
        "f#5:8 a4 c#5 r",
        "b4:8 r a4 r",
        "g#4:8 r b4 d5",
        "c#5:8 b4 a4 c#5",                         # A15
    ],
    "a_end": "c#5:4",
    "b_pick": "c#5:8 r",
    "b": [
        "a4:8 r f#4 r",                            # B1
        "c#4:8 r f#4 r",
        "e4:8 r d#4 r",
        "e4:8 r e5 r",
        "e5:8 r c#5 r",                            # B5
        "a4:4 a4:8 d5",
        "d5:8 d5 c#5 c#5",
        "d5:8 f#4 a4 r",
        "g4:8 r f#4 r",
        "e4:8 r b4 e5",                            # B10
        "a4:8 g4 f#4 a4",
        "a4:4 a4:8 d5",
        "a4:8 d5 a4 d5",
        "d5:8 c#5 f#4 c#5",
        "f#4:8 c#5 a#4 c#5",                       # B15
        "c#5:8 b4 r:4",
        "r:8 e5:16 b4 g4:8 r",
        "r:8 b4:16 g4 e4:8 r",
        "r:8 e4 g4 c5",
        "a#4:4 c#5:8 r",                           # B20
        "b4:8 r a#4 r",
        "b4:4 r:8 b4:16 d5",
        "f#5:8 b4 b4 a#4",
    ],
    "b_end": "f#4:4",
}

BASS = {
    "a_pick": "b2:8 b2",
    "a": [
        "d3:8 b2 f#3 d3",                          # A1
        "b3:8 f#3 d3 b2",
        "e3:8 c#3 f#3 f#2",
        "b2:16 c#3 d3 c#3 b2:8 b2",
        "d3:8 b2 f#3 d3",                          # A5
        "b3:8 f#3 b3 d4:16 b3",
        "g#3:8 b3:16 g#3 e3:8 g#3:16 e3",
        "a3:8 c#4:16 a3 d4:8 f#4:16 d4",
        "b3:8 d4:16 b3 g#3:8 b3:16 g#3",
        "c#4:16 d4 c#4 b3 a3:8 f#3",               # A10
        "b3:8 g#3 c#4 c#3",
        "f#3:8 f#2 f#3 a3",
        "d3:8 b3 c#3 c#4",
        "b2:8 d4 b3 g#3",
        "a3:8 b3 c#4 c#3",                         # A15
    ],
    "a_end": "f#3:4",
    "b_pick": "f#2:8 f#2",
    "b": [
        "a2:8 f#2 c#3 a2",                         # B1
        "f#3:8 a3:16 f#3 d#3:8 f#3:16 d#3",
        "b2:8 b2 b2 b2",
        "e3:8 b3:16 g3 e3:8 g3:16 e3",
        "c#3:8 a2 e3 c#3",                         # B5
        "g3:8 e3 f#3 b3",
        "g3:8 e3 a3 a2",
        "d3:8 d2 c3 f#3",
        "b2:8 g3 a2 a3",
        "g2:8 b3 g3 e3",                           # B10
        "f#3:8 g3 a3 a2",
        "d3:8 a2:16 f#2 d2:8 d4:16 a3",
        "f#3:8 a3:16 f#3 d3:8 f#3:16 d3",
        "a3:8 e3:16 c#3 a#2:8 e4:16 c#4",
        "a#3:8 c#4:16 a#3 f#3:8 a#3:16 f#3",       # B15
        "b3:8 f#3 d3 b2",
        "e3:8 e3 e3 e2",
        "e3:8 e3 e3 e2",
        "e3:8 e3 e3 e2",
        "e3:8 e4:16 c#4 a#3:8 c#4:16 a#3",         # B20
        "f#3:8 f#3 f#3 f#3",
        "b3:8 d4:16 b3 f#3:8 b3:16 f#3",
        "d3:8 b2:16 d3 f#3:8 f#2",
    ],
    "b_end": "b2:4",
}


def unroll(v):
    """Each half once; the stream repeats them (see sections())."""
    bars = ([v["a_pick"]] + v["a"] + [v["a_end"] + " " + v["b_pick"]]
            + v["b"] + [v["b_end"]])
    return " | ".join(bars)


def sections():
    a = PICKUP + BAR * len(FLUTE["a"]) + (BAR - PICKUP)
    b = PICKUP + BAR * len(FLUTE["b"]) + (BAR - PICKUP)
    return [(0, a), (a, a + b)]


PEAKS = [1, 5, 3]    # note-on attenuation per channel (2 dB a step)

HAT, KICK = 4, 6     # noise control: white noise, rate N/512 and N/2048


def drums(total):
    """A light pulse: kick on beat 1, hats on the other eighths.

    Each hit is gated (last field, frames) so it is a short tick rather
    than a long hiss through the envelope decay (#590)."""
    hits = []
    for bar_start in range(PICKUP, total, BAR):
        hits.append((bar_start, KICK, 2, 3))
        for off in (4, 8, 12):
            hits.append((bar_start + off, HAT, 5 if off == 8 else 7, 2))
    return [h for h in hits if h[0] < total]


def build():
    voices = [sn76489.parse(unroll(v), BAR, PICKUP, name)
              for v, name in ((FLUTE, "flute"), (VIOLIN2, "violin2"), (BASS, "bass"))]
    total = sections()[-1][1]
    for v, name in zip(voices, ("flute", "violin2", "bass")):
        end = max(s + l for s, l, _ in v)
        if end != total:
            raise ValueError(f"{name} ends at {end}/32, want {total}/32")
    return sn76489.stream(voices, FRAMES_PER_32ND, PEAKS, drums(total),
                          sections=sections())


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "build/badinerie.snd"
    os.makedirs(os.path.dirname(out) or ".", exist_ok=True)
    data = build()
    with open(out, "wb") as f:
        f.write(data)
    print(f"{out}: {len(data)} bytes")
