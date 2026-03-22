# Jovian Genome System — AI Diversity Strategy

## Design Metaphor

**Synthesizer**. Genome = oscillator patch, emotion = mod wheel, appearance = waveform, tick frequency = clock rate. Signal in → calculation → signal out.

## Architecture: Three Decoupled Layers

```
┌─────────────────────────────────────────────┐
│  AI LAYER (CODE word: jov-think)            │
│  genome + emotion + positions → intent      │
│  (dx, dy, fire-flag, flee-flag)             │
│  Runs on each Jovian's tick only            │
└──────────────┬──────────────────────────────┘
               │ writes intent to shared RAM
┌──────────────▼──────────────────────────────┐
│  PHYSICS LAYER (existing, simplified)       │
│  Apply dx/dy, bounds check, gravity,        │
│  collision — reads intent, writes position  │
└──────────────┬──────────────────────────────┘
               │ position + appearance bytes
┌──────────────▼──────────────────────────────┐
│  RENDER LAYER (fully independent)           │
│  Read position + appearance seed + emotion  │
│  → draw sprite, color tint                  │
│  Never reads genome. Never makes decisions. │
└─────────────────────────────────────────────┘
```

**AI never touches the screen. Rendering never reads genome. They communicate through shared position/state bytes only.**

Game loop:
```
for each jovian:
  increment tick
  if tick >= threshold:
    jov-think     \ CODE word: genome→intent
    apply-move    \ physics
  draw-jovian     \ render (every frame, independent)
```

## Core Principle: Tick Frequency IS Intelligence

Each Jovian gets its own tick threshold from `speed_modifier + pilot_skill`. This single number controls EVERYTHING:

- **Movement speed**: more ticks = more pixels per second
- **Reaction time**: more ticks = faster course correction
- **Detection responsiveness**: notices player sooner
- **Fire responsiveness**: fires sooner when cooldown expires
- **Apparent intelligence**: evaluates more often = better decisions

A slow dolt ticking every 8 frames is lumbering, oblivious, easy prey. A fast ace ticking every 2 frames is agile, responsive, dangerous. The smart ones literally *think more often*.

**Performance payoff**: The system naturally spends CPU where gameplay is hottest. A quadrant with 1 ace + 2 dullards costs less than the current 3 identical medium Jovians.

## Core Principle: CODE Words From the Outset

The `jov-think` CODE word is the heart of the system. Pure 6809 assembly:
- Read 4 genome bytes → extract trait values via shifts/masks
- Compute intent via lookup tables and arithmetic (no branching Forth)
- Write dx, dy, fire-flag to output bytes
- ~50-80 bytes of tight 6809, runs in microseconds

This **replaces** the current Forth words: `jov-targets-base?`, `move-one-jovian`, `jov-blocked?`, and parts of `tick-jovians`. Net code size may actually decrease.

Additional CODE word candidates:
- `jov-detect` — detection roll (pilot_skill + emotion vs distance)
- `jov-emotion-tick` — decay toward baseline, apply stimulus
- `gen-genome` — difficulty-biased random generation at spawn

## Data Structure: 4 bytes per Jovian (12 bytes total for 3)

### Byte 0-1: Behavior Genome (set at spawn, never changes)

| Bits | Axis | Spectrum |
|------|------|----------|
| 15-13 | Aggression | 0=peaceful/scared ... 7=berserker |
| 12-11 | Initiative | 0=waits ... 3=relentless |
| 10-8 | Pilot skill | 0=terrible ... 7=ace |
| 7-6 | Path style | 0=direct, 1=flanking, 2=orbiting, 3=patterned |
| 5-4 | Speed modifier | 0=slow, 1=normal, 2=fast, 3=rush |
| 3-2 | Handedness | 0=neutral, 1=left, 2=right, 3=strong bias |
| 1-0 | Regularity | 0=clockwork, 1=steady, 2=jittery, 3=chaotic |

**Difficulty bias**: `glevel` shifts the random distribution center. Level 1 → peaceful/slow/poor. Level 10 → aggressive/fast/ace. Always with variance.

### Byte 2: Appearance Seed

Drives algorithmic sprite generation. Own expression, not mapped to behavior. Emotion tints the output — a raging Jovian shifts toward red regardless of base appearance.

### Byte 3: Emotion (high nibble) + Origin (low nibble)

**Emotion** (bits 7-4): 0-15 scale
- 0-3 = fear/panic, 4-7 = uneasy/cautious, 8-11 = neutral/alert, 12-15 = angry/enraged
- Starts based on aggression genome
- Shifts in real time from game events
- Decays toward genome baseline over time

**Origin** (bits 3-0): Sector hash from quadrant coordinates
- Regional character across the galaxy
- Jovians from same region share tendencies

## Detection & Awareness Model

Jovians are NOT omniscient. Questions to answer before they act:

| Question | Mechanism |
|----------|-----------|
| How soon do they see us? | Detection roll: `pilot_skill + emotion` vs Manhattan distance to player |
| Are they vigilant from last time? | Quadrant mood seeds starting emotion — high mood = high alert |
| Have they forgotten us? | Mood decays per stardate — long absence = low mood = unaware |
| Are they hungry/territorial? | Unvisited sectors drift aggressive — avoided quadrants grow dangerous |
| What primes them? | Quadrant mood + genome baseline + current stimuli |

**Detection flow on quadrant entry:**
1. Each Jovian rolls detection based on `pilot_skill + emotion_level` vs distance
2. Failed → idle/patrol until player approaches or fires
3. Firing always reveals player to all (sound stimulus)
4. High-mood quadrants → detection bonus, faster reaction

## Emotion as Modifier Matrix

Emotion is a single 0-15 value per Jovian. Baseline aggression from the genome sets the resting point each Jovian decays toward; stimuli create temporary spikes.

### Baseline Aggression

Derived from genome byte 0, bits 7-3 (aggression field, 0-7):
```
baseline = aggression * 2 + 8, clamped to 15
```
- Passive genome (aggression 0) → baseline 8 (neutral)
- Aggressive genome (aggression 7) → baseline 15 (max rage, clamped)

A genetically passive Jovian can rage temporarily but calms down. A genetically aggressive one is always simmering and barely needs provocation.

### Decay

Every 60 frames, each Jovian's emotion drifts 1 step toward their baseline. This means:
- A passive Jovian (baseline 8) stimulated to rage (15) takes ~7 decay cycles to calm down
- An aggressive Jovian (baseline 15) frightened to 0 takes ~15 decay cycles to recover
- Emotion never drifts past baseline — it settles exactly there

### Stimuli (implemented)

| Event | Shift | Code location |
|-------|-------|---------------|
| Player enters quadrant | +2 all | `expand-quadrant` |
| Fellow Jovian killed | +3 all | `jov-kill` |
| Player docks at base | +1 all | `do-dock` |
| Jovian detects player | +1 to that Jovian | `jov-detect-tick` |
| Jovian fires at player | +1 to that Jovian | `tick-jbeam` |
| Player hit by jbeam | +1 to firing Jovian | `apply-jbeam-hit` |
| Wounded to flee threshold | set to 0 (fear) | `jov-flee-check` |
| Time passes (decay) | Drift 1 step toward baseline | `jov-emotion-decay-all` |

### Stimuli (spec'd but not yet implemented)

| Event | Shift |
|-------|-------|
| Player fires weapon | ±2 (aggressive→rage, peaceful→fear) |
| Near black hole/star | +1 fear (survival instinct) |

### Modifier Outputs (implemented)

| Output | Fear effect | Rage effect |
|--------|-------------|-------------|
| Fire rate | Slower cooldown (140%) | Faster cooldown (65%) |
| Detection range | Shorter | Longer |
| Sprite color | Blue | Red |
| Sprite regen | On color band crossing | On color band crossing |
| Quadrant mood | Seeded from mood grid on entry | Saved to mood grid on exit |

### Modifier Outputs (spec'd but not yet implemented)

| Output | Fear effect | Rage effect |
|--------|-------------|-------------|
| Movement speed | Faster flee | Faster chase |
| Obstacle avoidance | Wider berth | Reckless/tight |
| Engagement range | Keep distance | Close in |
| Flee threshold | Flee sooner | Fight to death |

## Quadrant Mood Persistence

One mood byte per sector in the galaxy grid. 64 bytes total.

- **On exit**: Aggregate Jovian emotions → write mood byte
- **On entry**: Seed new Jovian emotions from mood byte
- **Between visits**: Decay by 1 per stardate toward neutral
- **Unvisited**: Drift slightly aggressive (territorial/hungry)
- **After killing all**: High mood persists — replacements arrive hostile
- **Storage**: 64 bytes at $7E00–$7E3F

## Algorithmic Sprite Generation

Sprites generated from appearance seed + emotion color tint:
- **Half-template mirror**: Generate left half, mirror right (space invader style)
- **Seed bits toggle pixels** in a grid template
- **Emotion → color**: Fear=blue/green, neutral=yellow/white, rage=red/orange
- **SG4 constraint**: 2×2 pixels + 1 color per character cell
- Regenerate only on emotion color threshold change, not per frame
- Separate issue — needs iterative aesthetic tuning with screen-capture feedback

## Debug Display

During development, on-screen overlay showing:
- Per-Jovian: genome byte values, current emotion, tick rate, detection state
- Quadrant mood byte
- Toggle on/off with a key (doesn't ship in final game)
- Essential for tuning the aesthetic algorithms iteratively

## RAM Budget

| Item | Bytes | Address |
|------|-------|---------|
| Genome data (3 × 4 bytes) | 12 | $77C5–$77D0 |
| Intent output (3 × 3 bytes: dx, dy, flags) | 9 | $77D1–$77D9 |
| Quadrant mood grid (8 × 8) | 64 | $7E00–$7E3F |
| Sprite generation workspace | 12 | $77DA–$77E5 |
| **Total new RAM** | **97** | |

## Code Budget

| Item | Estimate | Notes |
|------|----------|-------|
| `jov-think` CODE word | 60-80 bytes | Replaces ~150 bytes of Forth AI |
| `gen-genome` CODE word | 30-40 bytes | Difficulty-biased random |
| `jov-detect` CODE word | 20-30 bytes | Distance + skill check |
| `jov-emotion-tick` CODE word | 30-40 bytes | Decay + stimulus |
| Emotion event hooks (Forth) | 40-60 bytes | Small words at trigger points |
| Sprite generator | 150-200 bytes | Separate issue |
| Mood grid save/restore | 30-40 bytes | On sector transition |
| **Total new code** | **~400-500 bytes** | |
| **Removed Forth AI code** | **~-200 bytes** | Net savings from replacement |
| **Net code change** | **~200-300 bytes** | |

~200-300 bytes net new code. 97 bytes new RAM. Well within ~2,860 byte headroom.

## Performance Budget

- Per-frame: 3 tick counter increments + threshold checks (trivial)
- AI runs ONLY on that Jovian's tick — dullards skip most frames
- CODE words: microseconds per execution vs milliseconds for Forth equivalent
- Emotion: piggybacks on AI tick, no separate overhead
- Sprite regen: only on emotion threshold change
- **Net effect: FASTER than current system, especially at high difficulty**

## Implementation Phases

### Phase 1: Architecture — replace current AI with data-driven loop
- Allocate genome data (12 bytes) + mood grid (64 bytes) + intent output (9 bytes: 3×{dx,dy,flags})
- Write `jov-think` CODE word (genome→intent calculation)
- Write `gen-genome` CODE word (difficulty-biased random generation)
- Replace `move-one-jovian` / `jov-targets-base?` / `jov-blocked?` with genome-driven equivalents
- **Goal**: Same visible behavior as today, but data-driven. Verify with screen-capture.

### Phase 2: Tick frequency variation
- Per-Jovian tick thresholds from speed_modifier + pilot_skill
- Verify smooth animation at all tick rates
- Performance measurement: confirm equal or better than current system

### Phase 3: Emotion system
- `jov-emotion-tick` CODE word (decay + stimulus)
- Event hooks at each trigger point in game loop
- Emotion modifier applied inside `jov-think`
- Verify behavioral shifts with screen-capture

### Phase 4: Detection & awareness
- `jov-detect` CODE word
- Idle/patrol behavior for unaware Jovians
- Firing reveals player
- Quadrant mood seeds detection state

### Phase 5: Quadrant mood persistence
- Mood grid save/restore on sector transitions
- Stardate-based decay
- Territorial drift for unvisited sectors

### Phase 6: Algorithmic sprites
- Sprite generator from appearance seed
- Emotion color tinting (red=rage, blue=fear)
- Iterative tuning with screen-capture feedback loops
- Debug overlay for genome/emotion state

### Phase 7: Regional character + polish
- Origin hash, regional genome bias
- Final aesthetic tuning
- Remove debug overlay (or gate behind key)

## Key Files to Modify

- `src/spacewarp/spacewarp.fs` — replace AI words, add genome spawn, emotion hooks
- `forth/kernel/kernel.asm` — new CODE words (jov-think, gen-genome, jov-detect, jov-emotion-tick)

## Verification Strategy

- **Screen-capture MCP** at every phase for visual regression and aesthetic tuning
- **Debug overlay** (toggled by key) showing genome values, emotion, tick rate per Jovian
- **Iterative feedback loops**: implement → capture → assess → tune → repeat
- Phase 1 must produce identical visible behavior before proceeding
- Each subsequent phase adds one dimension of variation, verified visually
