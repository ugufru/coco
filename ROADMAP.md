# CoCo Renovation — Roadmap

Forth kernel 1.0 shipped. Tutorial complete (13 chapters). FujiNet
integration and the analog clock demo are live on real hardware. This
roadmap organizes the 71 open issues in `issues.jsonl` into phases by
theme and impact. Phases are not strictly sequential — work can flow
in parallel where dependencies allow.

For per-issue detail, see `issues.jsonl` (each item below references
its issue ID).

---

## Phase 1 — Stabilize & Polish (now)

Goal: every shipped demo is exceptional and bullet-proof. No rough
edges in the showcase set before we add more surface area.

**Critical (blocks confidence in current shipset)**
- 433 — `sound` demo: stub references non-existent `SND_PLAY`; either remove or mark EXPERIMENTAL
- 440 — `fn-ready` spins forever when no FujiNet; add timeout
- 441 — `clock` graceful fallback when FujiNet absent (depends on 440)

**Standardization sweep across demos**
- 421 / 424 / 426 / 429 / 431 / 434 / 435 / 437 — add Build/Load doc header to 8 demos
- 423 / 428 / 438 — restore VDG text mode on exit (3 demos)
- 442 — add new demos to top-level `make dsk`

**Smaller polish**
- 422 / 427 / 430 / 436 / 439 — exit-path / doc consistency on 5 demos
- 425 — calculator: replace hardcoded delay with vsync-counted timing
- 432 — rg-test: KEY blocking edge case
- 443 — kernel.asm comment fix (`$8000` → `$E000`)
- 444 — per-demo Makefile template refactor

**Exit criterion**: all 24 audit issues (421-444) closed; every demo
launches cleanly from BASIC, behaves predictably without FujiNet
hardware, and leaves the screen in a sane state on exit.

---

## Phase 2 — Developer Toolchain Maturity

Goal: `Bare Naked Forth` is a credible development environment, not
just a runtime.

- 26 — Stack viewer (`.S`-style)
- 27 — Word lister
- 22 — Memory monitor / hex editor
- 25 — 6809 disassembler
- 28 — Breakpoint trap
- 29 — Memory compare
- 30 — Execution tracer

These tools graduate the project from "Forth that runs programs" to
"Forth you can build serious software in." Implement in the order
listed — each later tool benefits from the earlier ones.

---

## Phase 3 — Library Expansion

Goal: pad out the standard library so apps don't reinvent primitives.

- 33 — Circle drawing (paired with `clock` work)
- 34 — Flood fill (PAINT)
- 35 — Turtle graphics (DRAW)
- 415 — Promote rng/rnd to kernel primitives
- 36 — Sound / DAC access *(blocked by sound investigation)*
- 37 — PLAY music command *(blocked by 36)*

---

## Phase 4 — Documentation Excellence

Goal: the docs match the project's ambition. Marketing-grade, not
just developer notes.

- 407 — Tutorial flow audit (re-read all 13 chapters against current kernel)
- 408 — Demo appendix (featured page per demo in the tutorial)
- 411 — Replace tutorial illustrations with bespoke artwork (owner: Paul)
- 413 — "Working with Claude" guide — modern Bare Naked Forth workflow
- 412 — Document the DSK build & distribution workflow
- 410 — Host docs as a static website
- 409 — Embed runnable XRoar (WASM) inside the HTML docs

The XRoar-WASM embed is the biggest leap — readers run demos in their
browser as they read the chapters.

---

## Phase 5 — New Demos (selective)

Goal: a curated roster that spans every interesting CoCo capability.
Pick a small, intentional set rather than implementing every
suggestion.

**Highest showcase value:**
- 13 — Snake (classic, exercises VSYNC + keyboard + state)
- 16 — Conway's Game of Life (cellular automaton; visually striking)
- 17 — Starfield (3D-ish, shows beam tracking)
- 24 — Serial terminal (real I/O, pairs with FujiNet story)
- 19 — Maze generator + navigator (algorithmic)
- 14 — Drawing program (interactive, joystick + paint)
- 15 — Sound/music player *(blocked by sound)*
- 42 — Electronic piano *(blocked by sound)*

**Defer or drop:**
- 18 — Clock/stopwatch (superseded by `clock` demo)
- 20 — Text adventure (large effort, niche audience)
- 21 / 23 / 38–50 — many demo ideas; cherry-pick after Phase 1 stabilizes

---

## Phase 6 — Hardware Delivery (long-term)

Goal: the ROM cartridge product imagined in `COCO_RENOVATION.md`.

- 404 — Platform: serial loader (bit-banged RS-232)
- 405 — Platform: ROM cartridge image
- 406 — Platform: RP2350 co-processor

This is the eventual destination. Months of work. Earlier phases
should not block on these — but design decisions should keep them in
mind (e.g. binary layouts that work for cart delivery).

---

## Cross-cutting commitments

- Every new feature gets a tracking issue in `issues.jsonl` BEFORE work starts
- Every code change ships with verification (XRoar capture or hardware run)
- Demos must work without optional peripherals (FujiNet, joystick, etc.) — graceful fallback or clear error message
- Doc and reference (`docs/reference.html`) updated alongside code, not as a follow-up

---

## Suggested next moves

1. **Start Phase 1** — knock out 421-444 in batches. Doc-header sweep first (mechanical), then mode-restore sweep (mechanical), then 433/440/441 (real engineering).
2. **Pick one Phase 2 tool to prototype in parallel** — Stack viewer (26) is the smallest and most useful; ~1-day implementation.
3. **Start gathering content for Phase 4** in the background — tutorial audit notes can accumulate as we touch chapters.
