# Combat System Analysis: Original vs CoCo

A side-by-side comparison of the original Z80 Space Warp (1979) combat/damage
system against our CoCo Forth reimplementation, based on reverse engineering
of the TRS-80 Model I binary.

---

## Side-by-Side Comparison

### Damage Application

| Mechanic | Original (Z80) | CoCo (Forth) | Notes |
|----------|----------------|--------------|-------|
| Per-hit damage | Combat state -> sqrt -> difficulty scale -> random per system | Fixed 75 (JBEAM-DMG) | Simpler is better -- original's complexity was Z80 register tricks, not design intent |
| System damage distribution | All 5 systems each get independent random damage (0 to scaled x 2) | One random system takes full hit; overflow to second system | Consider hybrid: 2-3 systems per hit for "slow rust" attrition feel |
| Difficulty scaling | 1x-5x multiplier on base damage | Not yet scaling damage by level | Worth adding post-v1.0; small code cost |

### Deflectors / Shields

| Mechanic | Original | CoCo | Notes |
|----------|----------|------|-------|
| Model | Binary survival gate: allocation > 0 = survive, 0 = critical (instant death) | Strength pool (0-100%) that degrades under fire, 25 damage per hit | CoCo is better -- gradual degradation creates real resource management |
| Offense trade-off | Deflector % reduces maser power (100% defl = 33% maser) | No maser/shield trade-off | Worth adding -- forces offense/defense balance, one of the original's best ideas |
| System damage bypass | Deflectors do NOT reduce system damage -- all 5 systems always take hits | Shields up = no system damage at all | Biggest philosophical gap; consider bleedthrough (#306) to restore attrition pressure |
| Deflector system damage | If DEFLECTORS >= 100 damage, allocation forced to 0% on next hit -> death | Deflector health caps max shield level | Similar intent, CoCo execution is less punishing and more readable to the player |

### Energy

| Mechanic | Original | CoCo | Notes |
|----------|----------|------|-------|
| Energy depletion | Energy < 0 -> critical -> ship destroyed | Energy can reach 0 without death (all-systems-zero = death) | Keep CoCo model -- visible degradation before death is more satisfying |
| Energy cost of shields | Static allocation (set once, persists) | Upfront cost to raise (20 + level/5), no drain | Both valid; CoCo's burst cost makes raising shields a commitment |

### Repair

| Mechanic | Original | CoCo | Notes |
|----------|----------|------|-------|
| Undocked, damage < 128 | No repair at all -- permanent until docked | +5% to first damaged system every 32 frames (costs 1 energy) | CoCo is more forgiving; compromise: no field repair below 50% (#309) |
| Undocked, damage >= 128 | Slow repair: 1 pt/frame down to 128 (still inoperable) | N/A (health is 0-100%, not 0-255 counter) | Original's "self-repair but still broken" is clever but hard to map to our 0-100 scale |
| Docked | 4 pts/frame, full repair possible | +2%/frame per system, tiered energy recharge | Similar pacing; both make docking feel worthwhile |

### Death

| Mechanic | Original | CoCo | Notes |
|----------|----------|------|-------|
| Trigger | Energy depleted OR deflectors at 0% allocation | All 5 systems at 0% | Keep CoCo model -- ship visibly falls apart before dying |
| Instant death | Any hit with 0% deflector allocation | Star collision, black hole, self-destruct | Original's 0%-deflector trap is too punishing for real-time play; skip |

---

## Key Differences

### 1. Damage Distribution: Spread vs Focused

**Original**: Every hit damages ALL 5 systems independently with random amounts.
The ship slowly rusts -- even light hits gradually degrade everything.

**CoCo**: Each hit damages ONE system (overflow to a second). Systems stay at
100% until specifically targeted. Creates spiky outcomes (one system goes down
fast, others untouched).

A hybrid approach -- splitting damage across 2-3 random systems per hit --
would capture the original's attrition feel without the full 5-system spread.

### 2. Shields Don't Protect Systems in the Original

**Original**: Deflectors are purely a survival gate (stay alive vs die). Systems
ALWAYS take damage regardless of deflector setting.

**CoCo**: Shields absorb everything -- no system damage while shields hold.

This is the biggest philosophical difference. The original creates constant
repair pressure even with full deflectors. Our model means a well-shielded
player takes zero attrition until shields fail. A middle ground: shields could
reduce but not eliminate system damage (see deferred #306).

### 3. Field Repair vs Dock-Only

**Original**: No field repair for normal combat damage (< 128). You MUST dock.
This creates strong time pressure and navigation decisions.

**CoCo**: Slow field repair always available (1 energy = 5% to worst system).
More forgiving, which may suit the real-time arcade feel better.

Compromise: field repair only above 50% health, dock required below that
(see deferred #309).

### 4. Maser/Shield Trade-off

**Original**: `maser_power = 100 - (2 * deflector_alloc / 3)`. High deflectors
means weak masers. 100% deflectors = only 33% maser power.

**CoCo**: Masers and shields are independent. No trade-off.

The original's trade-off forces the player to balance offense and defense --
one of its best design decisions.

---

## Design Decisions

### Kept from CoCo (improvements over original)

- **Shield pool model** -- gradual degradation is more interesting than a
  binary gate. Players manage shield health as a resource.
- **All-systems-zero death** -- more satisfying than instant death from energy
  depletion. The ship degrades visibly before dying.
- **Real-time movement during combat** -- arrow-key dodging adds a physical
  skill layer the turn-based original couldn't have.

### Deferred Alignment Opportunities

These overlap with existing deferred issues:

1. Shield bleedthrough below 40% (#306) -- partial alignment with original's
   "shields don't protect systems" design
2. Ion engines at 0% disables movement (#307)
3. Non-linear repair: no passive repair below 25% (#309) -- partial alignment
   with original's dock-only repair
4. Scanner degradation at low health (#310)

### New Alignment Opportunities (not yet tracked)

1. **Spread damage across multiple systems per hit** -- 2-3 systems per hit
   would create the "slow rust" attrition feel
2. **Maser/shield power trade-off** -- high shields = weaker masers

### Intentionally Skipped

- Complex sqrt-based damage calculation -- fixed damage with difficulty scaling
  is cleaner
- Energy depletion = death -- all-systems-zero is a better mechanic
- "0% deflectors = instant death" trap -- too punishing for real-time play

---

## The Kirk Test: How TOS Creates Drama

The Starfleet Tactical Command Briefing (our TOS episode analysis) describes
combat as a system of interconnected constraints and trade-offs. Every mechanic
that creates dramatic tension in TOS maps to a specific gameplay question for
Space Warp. The goal: a player who's watched TOS should feel the same pressures
Kirk felt.

### The Zero-Sum Power Equation

TOS: "Every tactical decision you make in combat is, at its root, a power
allocation decision. Your ship generates a finite amount of power. Every system
aboard competes for a share." The weapons-shields-engines triangle means you
can maximize any two at the expense of the third.

**Space Warp today**: Energy is a single pool (0-100) that pays for shields,
warp, and repair. But there's no moment-to-moment tension because shields
don't drain energy once raised. The player sets shields to 100%, forgets about
them, and fights at full maser power.

**What Kirk would feel**: Nothing. No dilemma. The shields just sit there.

**What Kirk should feel**: "I need shields up to survive, but every point in
shields is a point I can't spend on weapons." The original Space Warp captured
this with the maser/deflector trade-off (100% deflectors = 33% maser power).
Our CoCo version has no such trade-off.

### Phasers Are the Jab; Torpedoes Are the Cross

TOS: "Sustained phaser fire forces the enemy to allocate shield power broadly.
When sensors indicate a section has degraded significantly, a torpedo aimed at
that section delivers concentrated force against the weakened point." Phasers
are unlimited but power-hungry. Torpedoes are devastating but finite: "Every
torpedo fired is one fewer available for the next engagement."

The Enterprise carries 100-200 torpedoes for an entire campaign of multiple
engagements. You can't torpedo your way through a war.

**Space Warp today**: 10 missiles, one-hit kill each. Masers do 3-30 damage
(need 4-7 hits to kill). Docking restocks missiles to 10. At level 1 (8
Jovians), you can nearly missile your way through the whole game. At level 5
(40 Jovians), you still get full restocks at every base visit.

**What Kirk would feel**: "Why am I bothering with these masers? I'll just
dock and reload missiles." No resource anxiety, no weapon selection drama.

**What Kirk should feel**: Kirk's escalating engagement pattern -- phasers at
standard (assessment), phasers at full (pressure), torpedoes when shields
weaken (exploitation), combined fire (killing blow). In our terms: soften with
masers, finish with a missile when health is low. Missiles should be precious
enough that wasting one on a full-health Jovian feels expensive.

Possible approaches:
- Fewer starting missiles (5 instead of 10)
- Partial restock at base (5, not full 10)
- Masers as primary weapon, missiles as finishers
- Missile supply that doesn't scale with difficulty (40 Jovians, same 10
  missiles -- forces maser reliance at higher levels)

### Shield Bleedthrough: The Slow Erosion

TOS: Below 40-50% shield strength, weapon energy starts bleeding through.
"Power system surges: energy enters ship's hull and EPS power grid. Minor
structural stress. System fluctuations." This is Stage 2 damage -- before
shields actually fail. The ship takes punishment even with shields nominally up.

**Space Warp today**: Shields absorb 100% of damage until they reach 0%.
Systems take nothing while shields hold. No bleedthrough at any level.

**What Kirk would feel**: Safe. Too safe. Shields at 10% feel the same as
shields at 90% -- both give total protection. There's no "she cannae take
much more" moment, no gradual deterioration.

**What Kirk should feel**: Shields weakening should feel increasingly dangerous
before they actually fail. The dread of watching shield percentage drop while
knowing each hit is now damaging systems too. This is already deferred as #306
(shield bleedthrough below 40%), but the TOS framing makes it clear why it
matters: it creates the dramatic middle act between "shields holding" and
"shields down."

### The Damage Cascade: Slow Rust, Then Collapse

TOS describes four damage stages: shield absorption, energy bleedthrough,
shield section failure, deep penetration. The critical insight is that damage
compounds: "Damage reduces power output -> reduced power weakens shields ->
weakened shields allow more damage through -> more damage further reduces
power output." This is the death spiral.

The original Space Warp captures this with its "all 5 systems take random
damage per hit" mechanic. Even light hits gradually degrade everything. The
ship rusts. Systems go inoperable one by one until you're fighting blind with
no engines and failing deflectors.

**Space Warp today**: One system takes all the damage per hit. Four systems
stay at 100% while one goes to 0%. No rust, no cascade, no compounding
degradation. The ship doesn't feel like it's falling apart -- one thing
breaks while everything else is fine.

**What Kirk would feel**: A single-point failure, not a ship in crisis.
Scotty wouldn't be triaging five damaged systems -- he'd be fixing one while
ignoring four pristine ones.

**What Kirk should feel**: Multiple systems degrading simultaneously. The
agonizing triage of "do I fix engines or scanners first?" The feeling that
every hit makes everything a little worse. Spreading damage across 2-3
systems per hit captures this without the original's full 5-system spread.

### Field Repair: Buying Time, Not Solving Problems

TOS: Scotty's field repairs are partial, temporary, and fragile. Power
rerouting takes 2-5 minutes and gives reduced output. Component bypasses
take 5-15 minutes and may fail under load. Real repair requires hours at a
starbase. "Each new hit may undo work already completed."

The original Space Warp mirrors this: no field repair for normal damage. You
MUST dock. This creates navigation pressure (where's the nearest base?) and
time pressure (stardates are ticking).

**Space Warp today**: Slow but steady field repair always works. 1 energy =
+5% to worst system, every 32 frames. Given enough time between fights, the
ship self-heals to 100% without ever visiting a base.

**What Kirk would feel**: Patient. Too patient. Just wait, and the ship fixes
itself. No urgency to find a starbase. No "I need ten minutes" from Scotty.

**What Kirk should feel**: Field repair that helps but doesn't solve the
problem. Partial recovery that keeps you fighting but leaves you degraded.
The strategic decision of "do I press on damaged, or burn stardates getting
to a base?" Already partially captured in deferred #309 (no passive repair
below 25%), but the TOS framing suggests a steeper curve: field repair only
above 50%, and slower than current rate.

### The Endurance Spiral: Campaign Attrition

TOS: "Sustained combat operations without resupply are strategically
unsustainable. You can win individual battles while slowly losing the ability
to fight the next one." The Enterprise can handle 3-5 significant engagements
before torpedoes, dilithium, components, and crew fatigue degrade capability.

**Space Warp today**: Full missile restock at every base. Full system repair
at every base. Full energy at every base. Each base visit completely resets
the ship. There's no campaign attrition -- every fight starts fresh after
docking.

**What Kirk would feel**: Invincible between fights. The base is a complete
reset button.

**What Kirk should feel**: Each engagement should leave a mark that isn't
fully erased by docking. The missile supply question is the clearest lever:
if bases restock only 5 missiles instead of 10, you're slowly depleting
across the campaign. Combined with damage spread, even a base visit doesn't
fully erase the accumulated wear.

### Knowing You're Losing the Ship

TOS: "When Scotty goes quiet or stops offering solutions, the dread is real."
The manual lists objective indicators (multiple shields below 20%, reactor at
reduced output, weapons minimal) and subjective ones (Scotty's reports shorter,
tactical options narrowing to one).

The all-systems-zero death condition in our CoCo version is actually good for
this. The player watches systems fail one by one. The damage report shows the
ship dying. Ion engines go, then scanners, then masers -- and you know what's
coming. This is better than the original's sudden death from energy depletion.

But it only works if damage is spread across systems (so multiple things are
failing) and if bleedthrough exists (so shields weakening makes everything
worse). Without those mechanics, the death is sudden: one system zeroes out,
then another, then boom. With spread damage and bleedthrough, the player
watches the whole ship degrade and has time to feel the dread.

---

## Summary: What Creates TOS Drama in Space Warp

| TOS Dramatic Element | Mechanic That Creates It | Current Status |
|---------------------|--------------------------|----------------|
| Power dilemma ("I can give you one or the other") | Shield/maser trade-off | Missing -- no trade-off |
| Weapon selection ("phasers are the jab") | Missiles scarce enough to force maser use | Weak -- missiles too plentiful |
| Shield erosion ("shields weakening, Captain") | Bleedthrough below 40% | Deferred (#306) |
| Slow rust ("she cannae take much more") | Damage spread across multiple systems | Missing -- single-system hits |
| Repair triage ("I need ten minutes") | Limited field repair, base dependency | Partially there; too forgiving (#309) |
| Campaign attrition ("resupply priority") | Partial missile restock, cumulative wear | Missing -- base fully resets |
| Watching the ship die | All-systems-zero death with spread damage | Death condition good; needs spread damage to shine |
| The death spiral | Compounding failures from cascading damage | Needs bleedthrough + spread damage |

---

---

## Design Vision: v1.0 Combat Overhaul

A synthesis of the original Z80 mechanics, TOS tactical doctrine, and new
ideas. Organized by system, with overlapping concepts unified.

### Guiding Principles

- **Arcade first** -- v1.0 should feel like Galaga in a Trek skin. Fast,
  readable, physical. Complex systems must express through simple inputs.
- **TOS dramatic arc** -- early hits bounce off shields. Mid-fight sees
  systems degrading. Late fight is desperate. The player should feel the
  ship falling apart.
- **Skill gradient** -- Jovian genome should matter in combat, not just
  movement. A dolt misses; an ace is terrifying.
- **Resource anxiety** -- missiles are precious. Energy is finite. Bases
  are irreplaceable. Every expenditure should feel like a decision.

### 1. Weapons Redesign: Masers and Missiles

**Current problem**: Missiles are one-hit kills with infinite restocking.
Masers do 3-30 damage (4-7 hits to kill). No reason to use masers.

**Design**: Masers are the primary weapon (the jab). Missiles are the
finisher (the cross). Neither is a guaranteed kill alone.

#### Masers (Command 5)

- **Range-dependent damage**: Devastating at close range, weak at distance.
  Beam fires from sprite center. Damage scales by proximity to target
  center -- a direct hit at point-blank is far more damaging than a graze
  at max range.
- **Suggested curve**: 50 damage at melee (< 15px), 30 at mid-range
  (15-60px), 10 at long range (> 60px). Exact values TBD via playtesting.
- **Direct hit bonus**: Distance from beam pixel to target sprite center
  determines damage multiplier. Center hit = full damage. Edge hit = half.
  This rewards precise angle entry.
- **Spacebar quick-fire**: Fire masers in the direction of the held arrow
  key. No angle entry needed. Lower damage than aimed fire (fixed mid-range
  equivalent) but enables dogfighting. XBlast-style combat layer.

#### Missiles (Command 6)

- **No longer one-hit kill**: Significant damage (60-80) but not instant
  death. Two missiles kill a full-health Jovian. One missile finishes a
  maser-softened target.
- **Wider blast radius**: Less accuracy-dependent than masers. Proximity
  detonation within ~6px (vs current 4px). Rewards getting close to the
  right angle without requiring pixel precision.
- **Two independent missiles in flight**: Player can fire a second missile
  while the first is still flying, each aimed independently. Enables
  multi-target engagement (fire left, fire right, dogfight with masers).
  Costs 1 missile each from supply. Requires duplicated missile state
  (16 extra bytes) and doubled per-frame collision/draw cycles -- may not
  fit in the 14,930cy frame budget. Evaluate after other changes land.
- **Smart Jovians evade**: High pilot-skill Jovians have a chance to dodge
  missiles. Evasion roll = `pilot_skill * 12`% chance. An ace (skill 7)
  evades ~84% of missiles. A dolt (skill 0) never evades. Forces the
  player to soften aces with masers first.
- **Finite base supply**: Each base starts with 5-8 missiles. Docking
  restocks from the base's pool (not infinite). Once a base's supply is
  exhausted, it can still repair and recharge but not rearm. This makes
  base destruction hurt more (those missiles are gone forever) and creates
  campaign attrition.

#### Friendly Fire

- Endever weapons damage starbases. A missed maser that hits the base
  sprite damages or destroys it. "Don't shoot toward the base" becomes
  a real tactical constraint, especially during base defense.

### 2. Hit Accuracy and the "Direct Hit" Concept

**Current state**: All hits are deterministic geometry. If a beam pixel
touches a bounding box, it's a hit at full damage. No accuracy gradient.

**Design**: Introduce accuracy as a spectrum, not a binary.

#### Beam Accuracy (Jovian Fire)

- **Aim scatter from pilot skill**: Before tracing the beam, offset the
  target point by `(7 - pilot_skill)` random pixels in x and y. A dolt
  (skill 0) scatters up to 7px; an ace (skill 7) fires dead center.
- **Moving target penalty**: If both the Jovian and the Endever moved this
  frame, add additional scatter. Simulates difficulty of hitting a moving
  target from a moving platform. Amount = 2-3px additional offset.
- **Result**: At long range, a dolt's scattered beam often misses entirely.
  At close range, even a scattered beam is likely to hit something. Aces
  are dangerous at any range.

#### Beam Damage Gradient

- **Distance from sprite center**: When a beam hits a bounding box, measure
  the pixel distance from the hit point to the sprite center. Closer to
  center = more damage. This applies to both Jovian and player fire.
- **Center hit = "DIRECT HIT"**: Could trigger a brief status message
  and bonus damage. Hits at the sprite edge do reduced damage.
- **Practical effect**: Combined with aim scatter, a dolt's beam that does
  hit will usually be a glancing blow (edge of bbox). An ace's beam hits
  center more often, doing full damage.

### 3. Shield and Damage Model

**Design goal**: Shields hold early, erode mid-fight, bleed through late.
The TOS dramatic arc.

#### Shield Behavior

- **100-40%**: Full absorption. No system damage. "Shields holding."
- **Below 40%**: Bleedthrough begins (#306). A fraction of each hit
  bypasses shields and damages systems. Fraction = `(40 - shields) / 40`,
  so at 20% shields, half the damage bleeds through. At 0%, all damage
  hits systems.
- **Shield level displayed as TOS-style percentage**: Status panel shows
  shield % prominently. This IS the "how am I doing?" number that TOS
  fans expect.

#### System Damage

- **Spread across all 5 systems**: Each hit distributes random damage
  across 2-3 systems (not all 5, not just 1). Creates the "slow rust"
  feel where the whole ship degrades.
- **Field repair caps at 50-60%**: Passive repair cannot restore a system
  above 50%. Full repair requires docking. This means combat damage
  accumulates across the campaign -- you're never quite at 100% between
  bases.
- **Some damage is permanent**: Each hit adds a small amount (1-2%) to a
  permanent damage floor that even docking can't repair. Over a long game,
  the ship gradually wears out. Keeps late-game tension high.

#### Power Source

The Endever runs on a **fusion reactor** (not matter/antimatter -- that's
Trek IP, and this is Space Warp's universe). "Fusion core" or simply
"reactor" works. The reactor is implicit in the energy system we already
have -- it's what regenerates energy passively.

A **direct hit to the reactor** (represented as: a direct-center hit that
deals more than 100% to a system, or a specific "reactor critical" event
when all systems are below some threshold) could be an additional death
condition. But this may be better as flavor text than a separate mechanic
-- the all-systems-zero death already captures "ship destroyed." A
"REACTOR CRITICAL" message when the last system fails adds drama without
new code.

### 4. SOS and Base Defense

**Current state**: Off-screen bases have a random 1-in-8 chance of
destruction per stardate tick. No player agency. In-quadrant base attack
is excellent (3-second timer, player can intercept beams).

**Design**: Timer-based system for off-screen bases with escalating alerts.

#### Off-Screen Base Attack

When a base quadrant has Jovians, a **destruction timer** starts (in
stardate ticks, not frames -- this is off-screen).

- **Timer duration**: Varies by Jovian count and difficulty. More Jovians
  = shorter timer. Surprise attacks (first tick a Jovian enters) start
  with a shorter timer.
- **SOS escalation sequence** (shown in status panel or brief flash):
  1. "JOVIANS IN 3,6" -- science officer detects threat
  2. "SOS BASE 3,6" -- base under attack
  3. "BASE 3,6 CRITICAL" -- base about to fall
  4. (silence) -- base destroyed
- **Unpredictability**: Timer has some random variance. The player knows
  the base is threatened but not exactly when it falls. Creates urgency
  without being perfectly predictable.
- **Arrival resets timer**: Warping to the quadrant and engaging Jovians
  (killing at least one, or being present) resets the timer. The
  in-quadrant mechanic (tick-base-attack) takes over.

#### Base Resources

Each base tracks a missile supply (1 byte per quadrant in hi-mem, 64
bytes total -- most are zero for no-base quadrants). Docking draws from
this pool. When a base is destroyed, its remaining missiles are lost.

### 5. Crew

- **Crew count**: Starts at 100 (or a round number appropriate for a
  smaller ship than the Enterprise). Decremented by hits that penetrate
  shields (bleedthrough and unshielded hits). Direct hits cost more crew
  than glancing blows.
- **Crew replenished at bases**: Docking restores some crew (not infinite
  -- bases have finite personnel too, or maybe this one is infinite for
  simplicity).
- **Score = crew survival**: End-game score based on percentage of crew
  surviving. Incentivizes shield management and base visits over pure
  aggression.
- **Morale**: Could affect passive repair rate or something subtle, but
  for v1.0 crew count alone may be enough. Morale as a stretch goal.

### 6. Status Reports (Micro-Dialogue)

Limited screen space (10 chars per row in the status panel) but brief
reports add enormous TOS flavor:

- "SHIELDS 80%" -- after taking a hit
- "DIRECT HIT" -- center-of-sprite beam hit
- "EVASION" -- Jovian dodges missile
- "SOS 3,6" -- base under attack
- "REACTR LOW" -- energy critical
- "SYS DAMAGE" -- bleedthrough is damaging systems

These rotate through a single status line, replacing each other. No
complex dialogue system needed -- just a word that writes a string to
a fixed status row with auto-clear after N frames.

### 7. Ship Sprite and Movement

- **Endever sprite redesign**: The current triangle works for v1.0 but
  a more distinctive 7x5 sprite (small saucer + nacelle suggestion)
  would read better. Evaluate after gameplay is locked.
- **Directional sprites**: 4 or 8 sprite variants based on last movement
  direction. Adds visual life. Each variant is 7x5 = 5 bytes, so 4
  variants = 20 bytes, 8 variants = 40 bytes. Feasible in hi-mem.
- **Quick-fire direction**: Spacebar fires masers in the direction of the
  currently held arrow key. No direction held = no fire (prevents the
  "spam 0+enter" problem). This makes the arrow keys dual-purpose:
  movement AND weapon aiming.

### 8. Black Holes

Black holes are instant death in both the original ("SWALLOWED BY A BLACK
HOLE!") and our version. Making them wormholes would be interesting
(random quadrant teleport) but changes the galaxy navigation dynamic
significantly. **Keep as instant death for v1.0** -- it's canonical to the
original and creates real map-reading tension.

### 9. Technical: CODE Words in Kernel Space

Currently all CODE words compile into the app region ($2000-$7FF7, 9 bytes
headroom). Moving performance-critical CODE words to kernel space
($E86A-$FEFF, ~5.7K free) would reclaim app bytes.

This would require fc.py changes:
- A directive like `KCODE` (or `CODE-HI`) that targets the kernel region
- The cross-compiler would need to track a second origin counter
- Call sites would use the kernel address (above $E000) instead of app
  address
- Feasibility: moderate. The kernel already has DOVAR and primitive data
  in that region. Adding more CODE words there is architecturally sound.

Worth investigating after v1.0 gameplay is locked, when byte pressure
becomes the primary constraint.

---

## Priority Summary for v1.0

What to implement vs defer, based on gameplay impact and byte cost:

### Must Have (core dramatic arc)

| Feature | Why | Byte Est |
|---------|-----|----------|
| Maser range damage | Makes masers the primary weapon | ~40-60 |
| Missile damage nerf (60-80, not 100) | Stops missile spam | ~10 |
| Shield bleedthrough below 40% | Creates TOS damage arc | ~30-40 |
| Damage spread (2-3 systems per hit) | "Slow rust" attrition | ~20-30 |
| Aim scatter by pilot skill | Skill gradient for Jovians | ~20-30 |
| SOS timer (replace random 1-in-8) | Player agency for base defense | ~40-60 |
| Finite base missile supply | Campaign resource anxiety | ~70-80 |

### Should Have (significant flavor)

| Feature | Why | Byte Est |
|---------|-----|----------|
| Direct hit bonus (center distance) | Rewards precise aiming | ~40-60 |
| Field repair cap at 50-60% | Campaign attrition | ~10-15 |
| Smart Jovian missile evasion | Genome matters in combat | ~30-40 |
| Status line micro-reports | TOS flavor, player feedback | ~60-80 |
| Spacebar quick-fire | Dogfighting layer | ~40-60 |

### Nice to Have (post-v1.0 or if bytes allow)

| Feature | Why | Byte Est |
|---------|-----|----------|
| Crew count + score | TOS immersion, endgame metric | ~60-80 |
| Directional Endever sprites | Visual polish | ~40-60 |
| Two independent missiles in flight | Multi-target engagement | ~50-60 + cycles |
| Permanent damage accumulation | Late-game tension | ~15-20 |
| SOS escalation messages | Dramatic flair | ~40-60 |
| Endever sprite redesign | Visual identity | ~20 |
| Moving target scatter bonus | Physics feel | ~15-20 |
| CODE words in kernel space | Byte recovery | fc.py work |

### Overlap Simplifications

Several ideas collapse into single mechanics:

1. **"Direct hit" + range damage + center distance** = one system:
   measure distance from hit pixel to target center, scale damage by
   that distance AND by range. One calculation, three effects.

2. **Shield bleedthrough + damage spread** = one flow: `take-damage`
   checks shield level, computes bleedthrough fraction, distributes
   bleed damage across 2-3 random systems. Single code path.

3. **SOS timer + finite base supply + base destruction** = one system:
   the timer drives everything. When it expires, `destroy-base` runs
   (which already works). The missile supply is just a byte lookup
   during docking.

4. **Aim scatter + moving target penalty** = one offset calculation
   before beam trace. Add `(7 - pilot_skill + movement_bonus)` random
   offset to target coords.

5. **Missile evasion + missile damage nerf** = complementary nerfs that
   both push the player toward masers. Could implement just one if bytes
   are tight (evasion alone may be enough).

---

## Original Z80 Implementation Details

For the full reverse-engineered Z80 disassembly analysis including memory
layout, timing model, and code addresses, see the reference analysis in
`refs/`.
