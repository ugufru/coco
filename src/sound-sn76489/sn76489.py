"""sn76489.py - turn a hand-written score into an SN76489 register stream.

The score is plain text, one string per voice. Tokens are separated by
spaces:

    b5:16      note B5 (scientific pitch, C4 = middle C), a sixteenth
    f#5 d5     sharps '#', flats 'b' (bb4 = B-flat 4); the duration is sticky
    a4:8.      dotted eighth
    r:4        rest, a quarter
    e5:8~      tie into the next note (which must be the same pitch)
    |          bar line, checked against the time signature

Durations are note values: 1 2 4 8 16 32, with an optional dot.

The stream the player (badinerie.fs) walks is a run of bytes:

    $00          end of song
    $01-$7F      wait this many frames (60 Hz) before reading on
    $E8          mark: remember this point as the start of a repeat
    $E9          repeat: go back to the mark once (so the section plays
                 twice). Both reuse noise-control bytes whose bit 3 the
                 chip ignores, so they never need to reach it.
    $80-$FF      otherwise an SN76489 latch byte. A tone-frequency latch
                 for channels 0-2 ($80-$8F, $A0-$AF, $C0-$CF) is followed
                 by its data byte (the top 6 bits of the divider).
                 A volume latch (1cc1vvvv) is NOT written straight to the
                 chip: the player records it as the channel's new level
                 and its envelope decays it from there.

Chip facts: 4 MHz clock (XRoar gmc.c, and the GameMaster), so a tone
divider is N = 4000000 / (32 * f) = 125000 / f, 10 bits. N = 1023 is
122 Hz, about B2; anything lower is lifted an octave with a warning.
"""

import re
import sys

CLOCK = 4_000_000
NOTE_RE = re.compile(r"^([a-gr])([#b]*)(\d?)(?::(\d+)(\.?))?(~?)$")
SEMITONE = {"c": 0, "d": 2, "e": 4, "f": 5, "g": 7, "a": 9, "b": 11}


def midi_of(letter, acc, octave):
    n = 12 * (octave + 1) + SEMITONE[letter]
    return n + acc.count("#") - acc.count("b")


def divider(midi):
    f = 440.0 * 2 ** ((midi - 69) / 12)
    return round(CLOCK / (32 * f))


def parse(text, bar_len, pickup=0, name="voice"):
    """Return [(start, length, midi or None)] in 32nd-note ticks.

    bar_len and pickup are in 32nds. Bar lines are checked so a missing
    or extra note shows up at the bar it happened in, not as a voice
    that drifts out of step with the others."""
    notes = []
    t = 0
    dur = 8
    octave = 4
    bar_start = 0
    bar_no = 0 if pickup else 1
    expect = pickup or bar_len
    tie = False
    for tok in text.split():
        if tok == "|":
            if t - bar_start != expect:
                raise ValueError(f"{name}: bar {bar_no} is {t - bar_start}/32, want {expect}/32")
            bar_start = t
            bar_no += 1
            expect = bar_len
            continue
        m = NOTE_RE.match(tok)
        if not m:
            raise ValueError(f"{name}: bad token {tok!r} in bar {bar_no}")
        letter, acc, octv, val, dot, tilde = m.groups()
        if octv:
            octave = int(octv)
        if val:
            dur = 32 // int(val)
            if dot:
                dur += dur // 2
        if letter == "r":
            notes.append((t, dur, None))
        else:
            midi = midi_of(letter, acc, octave)
            if tie:
                s, l, prev = notes[-1]
                if prev != midi:
                    raise ValueError(f"{name}: tie to a different pitch in bar {bar_no}")
                notes[-1] = (s, l + dur, midi)
            else:
                notes.append((t, dur, midi))
        tie = tilde == "~"
        t += dur
    # A closing partial bar is allowed: it pairs with the pickup.
    return notes


MARK, REPEAT = 0xE8, 0xE9


def stream(voices, frames_per_32nd, peaks, drums=(), gap=1, sections=None):
    """Build the byte stream.

    voices    list of up to three parse() results, channel 0..2
    peaks     note-on attenuation per channel (0 loudest, 15 off)
    drums     [(start_tick, noise_ctrl, attenuation[, gate])] on channel 3;
              gate, in frames, cuts the hit off that long after it starts,
              so it ticks instead of hissing through the envelope decay
    gap       frames of silence cut from the end of each note, so
              repeated notes re-strike instead of smearing together
    sections  [(start_tick, end_tick)] each played twice, back to back,
              covering the whole piece; None plays everything once
    """
    events = {}  # frame -> list of byte strings, ordered offs before ons

    def at(frame, prio, data):
        events.setdefault(frame, []).append((prio, data))

    lifted = 0
    for ch, notes in enumerate(voices):
        for start, length, midi in notes:
            if midi is None:
                continue
            n = divider(midi)
            if n > 1023:
                lifted += 1
            while n > 1023:
                midi += 12
                n = divider(midi)
            f0 = start * frames_per_32nd
            f1 = (start + length) * frames_per_32nd
            off = f1 - gap if f1 - f0 > gap + 1 else f1
            at(f0, 1, bytes([0x80 | ch << 5 | (n & 0x0F), n >> 4,
                             0x90 | ch << 5 | peaks[ch]]))
            at(off, 0, bytes([0x90 | ch << 5 | 0x0F]))

    if lifted:
        print(f"note: {lifted} notes below B2 lifted an octave", file=sys.stderr)

    last_ctrl = None
    for hit in sorted(drums):
        start, ctrl, att = hit[:3]
        data = b""
        if ctrl != last_ctrl:
            data += bytes([0xE0 | ctrl])
            last_ctrl = ctrl
        f0 = start * frames_per_32nd
        at(f0, 2, data + bytes([0xF0 | att]))
        if len(hit) > 3:
            at(f0 + hit[3], 0, bytes([0xFF]))

    out = bytearray()

    def wait(n):
        while n > 0:
            step = min(n, 127)
            out.append(step)
            n -= step

    if sections is None:
        end = max(events) + 1 if events else 0
        sections = [(0, end, False)]
    else:
        sections = [(s * frames_per_32nd, e * frames_per_32nd, True)
                    for s, e in sections]
    for f0, f1, twice in sections:
        if twice:
            out.append(MARK)
        now = f0
        for frame in sorted(f for f in events if f0 <= f < f1):
            wait(frame - now)
            for _, data in sorted(events[frame], key=lambda e: e[0]):
                out += data
            now = frame
        wait(f1 - now)
        if twice:
            out.append(REPEAT)
    wait(32)  # let the last notes ring out
    out.append(0)
    return bytes(out)
