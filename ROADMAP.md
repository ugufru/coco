# CoCo Renovation — Roadmap

*Reset 2026-06-16.* The previous roadmap had grown stale: its Phase 1 demo
audit (issues 421-444) is **done**, kernel **1.2 shipped** (the async sound
engine), and the bulk of what was filed as "open" turned out to be an
accumulated *wishlist* — ideas logged early and never pruned, which made the
backlog look far heavier and more committed than it was. This version keeps
only genuinely-live work, honestly prioritized, and parks the rest in an
explicit **Icebox** (see bottom) rather than leaving it masquerading as planned
work.

**Where things stand (2026-10-26):** a cross-developed Forth (kernel + `fc.py`)
with ROM and all-RAM build profiles; a complete 13-chapter tutorial; ~20 demos;
an interactive ITC-threading tutorial; FujiNet/clock live on real hardware; a
Claude Code terminal over the Becker port; a disk library that reads DECB files
without Disk BASIC. Since October the sound side has grown to every chip the
CoCo Zero carries: SN76489 (GameMaster) with a song player and VGM playback, the
AY-3-8910 (CoCo PSG and Symphony-12), and the Orchestra-90, shown side by side
in `src/sound-tour`. Every library is in `reference.html`. Released as **v1.2**.

For per-issue detail, see `issues.jsonl` (statuses: `open`, `in-progress`,
`done`, `icebox`, `closed`), browsable as `issues.html`. What to do next, in
order and with reasons, is `roadmap.jsonl` (shown at the top of `issues.html`):
this file is the shape of the work, that one is the queue.

---

## North star: on-device, self-hosting Forth (decided 2026-07-19)

*This section previously described the direction as an open fork. It is not
open. See #551, which is the epic.*

**The CoCo compiles on itself.** The `COCO_RENOVATION.md` vision, *program the
machine on the machine*: today everything is cross-compiled from a laptop via
`fc.py`, and the CoCo runs pre-compiled threads but cannot compile on itself.
Forth is natively self-hosting, so the path is an on-device interpreter and
compiler rather than a separate 6809 assembler.

**#551** carries the settled design: headerless ITC threads stay byte-identical
to cross-compiled images with names in a detached `.sym` sidecar; symbols are
RAM-resident during a session and persisted to the filesystem at session
boundaries; the live dictionary is the working artifact and source is stored per
word; the editor is a definition editor, not a file editor; an on-device postfix
6809 assembler is critical, and its indexed-addressing postbyte encoder is the
risk-carrying nugget. The first spike is that encoder, proved by diffing
on-device output against lwasm for the same instructions.

Sub-epics get filed as #551 starts. Track 3 below is its seed, and remains worth
building on its own terms.

---

## Track 1 — Publish & polish (highest leverage)

Goal: make what already exists excellent and reachable. This is the best
return on effort right now — the project is good but hard to *experience*.

- **409 (high)**: embed runnable **XRoar (WASM)** in the HTML docs (run demos
  in-browser while reading)
- **467 (high)**: audit + bulletproof the shipped demos, now including the
  jukebox and the sound tour
- **539**: bespoke documentation publishing pipeline (markdown to servable HTML)
- **577 (low)**: tutorial polish (example files out of sync with prose)
- **537 (low)**: FujiNet Time screenshot for the gallery
- **411 (low)**: bespoke tutorial artwork (owner: Paul)

Done: **410** (static site hosted, #547), **408** (demo appendix), **413**
("Working with Claude" guide), **412** (DSK build & distribution docs), **407**
(tutorial flow audit), **637** (library reference complete).
Left from #637: **638 (low)**, settle the 63 places where a source comment and
`reference.html` describe a word differently.

---

## Track 2 — Real hardware

Goal: escape the emulator — the cartridge thesis from `COCO_RENOVATION.md`.
Important but not urgent; no committed timeline.

- **405** — ROM cartridge image
- **404** — serial / SD loader (bit-banged RS-232)

In practice the CoCo Zero (the RP2350 XRoar port in
`~/github/xroar-waveshare-rp2350-pizero`) is now where work meets hardware:
it boots our disks, autoruns them, and carries every sound chip. Everything
shipped so far has now run there: the last two checks, the sound tour's AY
sections (#635) and the jukebox keys (#605), passed on 2026-10-26.

*(RP2350 co-processor parked, see Icebox; revisit after 404/405.)*

---

## Track 3 — Interactive / on-device tooling

Small, useful introspection words that are worth having on their own, and that
seed the on-device experience #551 is heading for. Prerequisites named in #551.

- **27** — `WORDS` (word lister)
- **26** — `.S` stack viewer
- **25** — 6809 disassembler / `SEE`
- **22 (low)** — memory monitor / hex editor
- **28 (low)** — breakpoint trap
- **490 (low)** — `fc.py` vocabularies / word lists
- **592 (medium, bug)**: `fc.py` should warn when a definition shadows a
  kernel primitive or an earlier word. Its silent traps cost time in October
  (parenthesised text inside strings is stripped as a comment, now noted in
  CLAUDE.md).

---

## Track 4: Sound & libraries

Not on the critical path, but where most of October went. Done: SN76489 chip
words and song player (#579-#604), VGM playback (#598), the AY and Orchestra-90
libraries and the sound tour (#632-#634). The Bach library and the COCO.FM
jukebox it grew into moved to `~/github/coco-fm-bach` (#626); coco keeps the
Badinerie as its one song (#627).

- **629**: stream VGM from disk. A VGM is about 280 bytes a second, so only
  about 28 seconds fits in the jukebox today.
- **631 (on hold)**: `vgmstrip.py`, a VGM cut to its SN76489 part. Waiting on
  direction: a Mega Drive track's melody is on its YM2612, which no CoCo
  hardware has; arranging it onto the Symphony-12's AY chips is the likely way.
- **580**: YMF262 (OPL3) on the MEGA mini MPI. Not emulated by the CoCo Zero or
  XRoar, so it needs the hardware.
- **615 / 616 / 617 (low)**: song player: per-song envelope, vibrato, bass below
  B2 via periodic noise. **582 / 583 (low)**: Badinerie polish and visualiser.
- **596 / 606 (low)**: jukebox: a phantom song pick (unconfirmed), CoCo 3
  composite palettes.
- **522 / 523**: `fc.py` shared-asm / macro support, then DRY the async-sound emit core
- **525**: DP-locate voice state (CPU win)
- **530**: sine / wavetable ring modulator (the full 2-op path; wants 522/523 first)
- **587 / 593 (low)**: `lib/decb.fs` write support, and CoCo 3 support

---

## Track 5: The CoCo terminal

`src/terminal` talks to Claude Code over the Becker port (#24, done).

- **561 (low)**: absorb `lib/console.fs` into the kernel's EMIT/CR/TYPE
- **562 (low)**: key auto-repeat and rollover
- **558 (low)**: VT52-style cursor escapes
- **559 (low)**: a CoCo 3 40/80-column version
- **578 (low)**: confirm T1 has no SG6 on real hardware; port or retire `tools/sg6_*`

---

## Icebox (parked 2026-06-16 — reopen on demand)

Not rejected, just not committed: ideas with no concrete consumer or trigger.
Kept here so the open list reflects *real* commitments. Pull any back the moment
there's a reason to build it.

- **Demos (22):** Snake (13), Drawing (14), Life (16), Starfield (17),
  Clock/stopwatch (18, superseded), Maze (19), Text adventure (20),
  Sprite animation (21), Reaction game (23), Serial terminal (24),
  Joy to the World (38), Rocket (39), Hi-res font (40), Back to Bach (41),
  Electronic piano (42), Ready Aim Fire (44), Typing test (45),
  Music composer (46), Bouncing ball (47), Dice/Craps (48),
  Speed reading (49), Math quiz (50)
- **Libraries (4):** circle (33), flood fill (34), turtle/DRAW (35), PLAY music (37)
- **Tools (2):** memory compare (29), execution tracer (30)
- **Hardware spike:** RP2350 co-processor (406)
- **Research (3):** international CoCo community (459), DriveWire/toolchain
  authors (460), pre-XRoar emulator lineage (461)

---

## Cross-cutting commitments (unchanged)

- Every change gets a tracking issue in `issues.jsonl` BEFORE work starts.
- Every code change ships with verification (XRoar capture or hardware run).
- Demos work without optional peripherals (FujiNet, joystick) — graceful
  fallback or a clear message.
- `reference.html` and generated docs stay in sync with code, not as a follow-up.

---

## Suggested next move

The ranked queue is `roadmap.jsonl` (top of `issues.html`). The hardware
checks (#635, #605) passed, so it starts with **409**, embedding runnable
XRoar (WASM) so the docs go from "click a link and read" to "click a link and
play": the highest-value item in Track 1, and independent of #551.

The north star is settled (#551, on-device self-hosting). Its first spike is the
postfix assembler's indexed-postbyte encoder, verified against lwasm. Track 3
(#27, #26, #25) is the seed and can start whenever. See also
`proposals/UMBILICAL_FORTH_PROPOSAL.md` (#552) for a cartridge-based host-to-
target link that would give that spike an automated verification loop on real
hardware.
