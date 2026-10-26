"""tour.py - the sound tour's phrase, as each device's tables (#634).

sound-tour.fs splices these in with DATA[PY: one table per device, one
entry per note, every value big-endian as Forth's @ reads it.

The phrase is the opening of the Ode to Joy (Beethoven, Symphony No. 9,
public domain): two four-bar phrases, the first ending on the dominant,
the second on the tonic, over a bass of C3 and B2 and the chords I and V.
The bass is B2 rather than G2 because the SN76489's lowest note at 4 MHz
is about B2.

Timing comes from the CoCo 2's clock and each player's measured loop
(fc.py --cycles): snd-tone1 is 42 + 14p cycles a period, snd-saw 36 + 7p
cycles a sample, orch-play 79 cycles a sample.
"""

import struct

CLOCK = 894886          # CoCo 2 CPU, Hz
BEAT = 24               # frames a beat
GAP = 3                 # frames of silence at the end of a note
SAW_STEP = 64           # snd-saw step: 4 samples a period
ORCH_RATE = CLOCK / 79  # orch-play samples a second

C3, B2 = 48, 47
CHORDS = {'I': (52, 55, 60), 'V': (50, 55, 59)}   # E3 G3 C4 / D3 G3 B3

# (beats, melody, bass or None to keep, chord or None to keep)
E4, F4, G4, D4, C4 = 64, 65, 67, 62, 60
PHRASE = [
    (1, E4, C3, 'I'), (1, E4, None, None), (1, F4, None, None), (1, G4, None, None),
    (1, G4, B2, 'V'), (1, F4, None, None), (1, E4, None, None), (1, D4, None, None),
    (1, C4, C3, 'I'), (1, C4, None, None), (1, D4, None, None), (1, E4, None, None),
    (1.5, E4, B2, 'V'), (0.5, D4, None, None), (2, D4, None, None),
    (1, E4, C3, 'I'), (1, E4, None, None), (1, F4, None, None), (1, G4, None, None),
    (1, G4, B2, 'V'), (1, F4, None, None), (1, E4, None, None), (1, D4, None, None),
    (1, C4, C3, 'I'), (1, C4, None, None), (1, D4, None, None), (1, E4, None, None),
    (1.5, D4, B2, 'V'), (0.5, C4, None, None), (2, C4, C3, 'I'),
]


def hz(midi):
    return 440.0 * 2 ** ((midi - 69) / 12)


def frames(note):
    return round(note[0] * BEAT)


def on_secs(note):
    return (frames(note) - GAP) / 60


def words(values):
    return b''.join(struct.pack('>H', max(0, min(0xFFFF, round(v)))) for v in values)


def count():
    """The number of notes, as one byte."""
    return bytes([len(PHRASE)])


def frame_table():
    """Frames per note."""
    return bytes(frames(n) for n in PHRASE)


def one_bit():
    """snd-tone1: pitch, then periods to play."""
    out = []
    for n in PHRASE:
        f = hz(n[1])
        out += [(CLOCK / f - 42) / 14, f * on_secs(n)]
    return words(out)


def dac_saw():
    """snd-saw at SAW_STEP: pitch, then samples to play."""
    per = 256 // SAW_STEP
    out = []
    for n in PHRASE:
        f = hz(n[1])
        out += [(CLOCK / (per * f) - 36) / 7, per * f * on_secs(n)]
    return words(out)


def periods(divide):
    """Melody and bass (0 = keep) as clock dividers: divide / Hz."""
    out = []
    for n in PHRASE:
        out += [divide / hz(n[1]), divide / hz(n[2]) if n[2] else 0]
    return words(out)


def sn():
    return periods(4000000 / 32)            # SN76489 at 4 MHz


def psg():
    return periods(2000000 / 16)            # CoCo PSG at 2 MHz


def sym():
    """Symphony-12: melody, bass, then the chord's three notes (0 = keep)."""
    d = 14318180 / 16 / 16                  # chip clock 894886 Hz, / 16
    out = []
    for n in PHRASE:
        out += [d / hz(n[1]), d / hz(n[2]) if n[2] else 0]
        out += [d / hz(m) for m in CHORDS[n[3]]] if n[3] else [0, 0, 0]
    return words(out)


def orch():
    """orch-play: melody increment, bass increment, samples on, gap."""
    out, bass = [], None
    for n in PHRASE:
        bass = n[2] or bass
        out += [hz(n[1]) * 65536 / ORCH_RATE, hz(bass) * 65536 / ORCH_RATE,
                on_secs(n) * ORCH_RATE, GAP / 60 * ORCH_RATE]
    return words(out)
