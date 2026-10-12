# CoCo Renovation — Roadmap

*Reset 2026-06-16.* The previous roadmap had grown stale: its Phase 1 demo
audit (issues 421-444) is **done**, kernel **1.2 shipped** (the async sound
engine), and the bulk of what was filed as "open" turned out to be an
accumulated *wishlist* — ideas logged early and never pruned, which made the
backlog look far heavier and more committed than it was. This version keeps
only genuinely-live work, honestly prioritized, and parks the rest in an
explicit **Icebox** (see bottom) rather than leaving it masquerading as planned
work.

**Where things stand:** a cross-developed Forth (kernel + `fc.py`) with ROM and
all-RAM build profiles; a complete 13-chapter tutorial; ~15 demos; trig + sound
libraries; an interactive ITC-threading tutorial; FujiNet/clock live on real
hardware. Released as **v1.2**.

For per-issue detail, see `issues.jsonl` (statuses: `open`, `in-progress`,
`done`, `icebox`, `closed`).

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

- **409 (high)** — embed runnable **XRoar (WASM)** in the HTML docs (run demos
  in-browser while reading)
- **467 (high)** — audit + bulletproof the shipped demos (the per-demo
  standardization sweep is done; this is the final polish pass)
- **407** — tutorial flow audit (re-read all 13 chapters against the current kernel)
- **411 (low)** — bespoke tutorial artwork (owner: Paul)

Done: **410** (static site hosted, #547), **408** (demo appendix), **413**
("Working with Claude" guide), **412** (DSK build & distribution docs).

---

## Track 2 — Real hardware

Goal: escape the emulator — the cartridge thesis from `COCO_RENOVATION.md`.
Important but not urgent; no committed timeline.

- **405** — ROM cartridge image
- **404** — serial / SD loader (bit-banged RS-232)

*(RP2350 co-processor parked — see Icebox; revisit after 404/405.)*

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

---

## Track 4 — Sound & libraries (opportunistic, post-1.2)

Resume when motivated; not on the critical path.

- **522 / 523** — `fc.py` shared-asm / macro support, then DRY the async-sound emit core
- **525** — DP-locate voice state (CPU win)
- **530** — sine / wavetable ring modulator (the full 2-op path; wants 522/523 first)

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

The site is now hosted (410 done, #547), so the remaining Track 1 leverage is
**409** — embed runnable XRoar (WASM) so the docs go from "click a link and
read" to "click a link and play." Highest-value item left here, and independent
of #551.

The north star is settled (#551, on-device self-hosting). Its first spike is the
postfix assembler's indexed-postbyte encoder, verified against lwasm. Track 3
(#27, #26, #25) is the seed and can start whenever. See also
`proposals/UMBILICAL_FORTH_PROPOSAL.md` (#552) for a cartridge-based host-to-
target link that would give that spike an automated verification loop on real
hardware.
