# SPACE WARP — User Guide

## Your Mission

The Jovian fleet is invading United Planet System space. As commander of the
starship Endever, your mission is to destroy every Jovian warship before they
wipe out all UP bases. The galaxy is vast, time is short, and the Jovians
don't wait for you to make up your mind.

Everything happens in real time. While you're reading the scanner, a Jovian
is firing at you. While you're typing a maser angle, a base on the far side
of the galaxy is under attack. Speed and decisiveness win. Hesitation kills.

## Starting the Game

When the game loads, you are asked to select a difficulty level from 1 to 10:

| Level | Jovian fleet size | Recommended for |
|-------|------------------|----------------|
| 1-2 | 8-16 ships | First-time players |
| 3-5 | 24-40 ships | Experienced commanders |
| 6-8 | 48-72 ships | Veterans only |
| 9-10 | 80-89 ships | Nearly impossible |

After selecting your level, the mission briefing appears:

```
YOUR MISSION:

  DESTROY THE FLEET OF 40 JOVIAN SHIPS BEFORE THEY DESTROY
  THE 8 UNITED PLANET SYSTEM BASES

                                          GOOD LUCK!
```

Press any key to begin.

## The Tactical Display

The main screen shows two areas:

**Upper area — Tactical View.** This is your window into the current quadrant.
You see the Endever, any Jovian ships, the starbase (if present), and stars
scattered across space. Weapon fire appears as visible lines streaking across
the display.

**Lower area — Status Panel.** Continuously updated readouts:

```
STARDATE 3   QUADRANT 2 4   DEFLECTORS 100%
ENERGY  68%  MISSILES  5    CONDITION GREEN
                            COMMAND?
```

- **STARDATE** — elapsed time. One stardate equals roughly one minute.
- **QUADRANT** — your position in the 8x8 galaxy (column, row).
- **ENERGY** — ship's power reserves. Movement, masers, and damage deplete it.
  Reaches 0% and you're dead.
- **DEFLECTORS** — current shield strength setting.
- **MISSILES** — triton missiles remaining.
- **CONDITION** — GREEN (no enemies), YELLOW (Jovians in galaxy), RED (Jovians in this quadrant), DOCKED, or SOS (base under threat).
- **SOS-BASE x,y** — appears when a base in another quadrant is under attack,
  showing its coordinates. You have 3 stardates to warp there. Arrive and kill
  the Jovians to save the base and reset the timer.
- **COMMAND?** — waiting for your next command.

### What You See in the Tactical View

| Symbol | Color | Meaning | ASCII shape |
|--------|-------|---------|---------------|
| Chevron shape | Blue/red | The Endever (you) — twin red engines | "V" |
| Angular bracket shape | Red | Jovian warship | <*> |
| Spoked ring | Blue | United Planet base | +O+ |
| Dots | Mixed colors | Stars and Planets (white, blue, red) |  .  |
| Line from ship | Blue | Maser beam |  |
| Ball | Blue | Triton missile | * |
| Line from enemy | Red | Jovian weapons fire |  |
| Expanding ring | White→Red→Blue | Explosion |  |

Black holes are invisible. You only discover them when a weapon shot vanishes
into empty space, or when you fly into one (which destroys you instantly).

## Commands

Commands are entered by pressing a number key. Some commands require additional
input (angles, coordinates, energy levels). You can move the Endever with the
arrow keys at any time — even while typing another command. Press **CLEAR** (backslash key) to cancel any command in progress.

### 1 — Damage Report

Press **1** to see the status of your five ship systems and the overall war
situation:

```
42 JOVIANS LEFT                  6 BASES LEFT

DAMAGE

ION ENGINES                          81%
HYPERDRIVE                           19%
SCANNERS                             57%
DEFLECTORS                           77%
MASERS                               82%
```

Each system shows its operational percentage. At 100%, the system works
perfectly. As damage accumulates, systems degrade:

- **Ion engines** — movement slows (below 67%: speed 2, below 34%: speed 1). At 0% the ship is frozen.
- **Hyperdrive** — below ~20%, warping becomes unreliable or impossible.
- **Scanners** — long range scan shows incomplete or garbled data.
- **Deflectors** — shield strength depends on deflector health. Below 40%, damage bleeds through.
- **Masers** — reduced damage output.

**Field repair** heals one system at a time, in priority order, up to a maximum
of 75%. Systems below 25% are too damaged for field repair — only a starbase
can fix them. This means a hard fight can leave you permanently weakened until
you dock.

Press any key to return to the tactical view.

### 2 — Hyperdrive

Press **2**, then enter a two-digit destination: column (0-7) then row (0-7).
The Endever warps instantly to the new quadrant, arriving at a random position.

Hyperdrive consumes energy based on distance (Manhattan distance × 2, max 10
energy per jump). If hyperdrive is heavily damaged, the warp may fail or send
you to the wrong quadrant.

You always arrive at a safe position — the ship will never spawn inside an
inescapable gravity well.

### 3 — Long Range Scan

Press **3** to replace the tactical view with the galaxy map:

```
LONG RANGE SCAN

     0    1    2    3    4    5    6    7
0              B 0       B 0
1
2
3                   B 3            B 2  B 1
4         1    1         1         B 3
5         1  E B 0  1         B 1
6   B 2   1    2    M    B 2
7                             B 1
```

- **E** — the Endever's current quadrant.
- **B** — a United Planet base is present.
- **1, 2, 3** — number of Jovian ships in that quadrant.
- **M** — magnetic storm. Blocks scanner readings in that quadrant; you won't
  know what's there until you warp in.
- Empty cells contain only stars (or nothing).

The scanner shows the state of the galaxy as of your last scan. Jovians move
between quadrants, so the map goes stale. Scan frequently.

If your scanners are damaged, some quadrants may show garbled or missing data.

Press any key to return to the tactical view.

### 4 — Deflectors

Press **4** to toggle shields UP or DOWN. No energy cost to maintain shields.

**Shields UP:** Incoming Jovian beams are absorbed by the deflector system
instead of damaging ship systems. Each hit reduces deflector health by 15%.
Below 40% deflector health, some damage bleeds through to other systems.
At 0% deflectors, shields are forced DOWN.

**Shields DOWN:** No protection. All damage hits ship systems directly. But
you must lower shields to dock at a starbase.

**At 0% deflectors:** Pressing 4 diverts energy to rebuild the deflector
system instead of toggling shields.

**The tradeoff:** Shields protect you but degrade with every hit. Once deflectors
drop below 40%, the protection becomes unreliable. Field repair only restores
deflectors to 75% — you need a starbase for full recovery.

### 5 — Masers

Press **5**, then enter a firing angle from 0 to 360 degrees:

- **0** — fire to the right
- **90** — fire upward
- **180** — fire to the left
- **270** — fire downward

The maser beam appears as a visible blue bolt streaking from the Endever
across the tactical view. The bolt travels along its path and stops at the
first obstacle — a Jovian, a star, or any other object in the way. **Masers
cannot fire through obstacles.** If a star is between you and a Jovian, you
need a clear line of sight.

If the bolt hits a Jovian ship, damage is dealt based on distance (closer =
more damage) and your shield level (higher shields = weaker masers).

Masers do not destroy in one hit — it typically takes 2-4 shots depending on
range and shields. Masers consume ship energy with each shot.

If your maser system is damaged, the beam is weaker.

### 6 — Triton Missiles

Press **6**, then enter a firing angle from 0 to 360 degrees (same compass as
masers).

Triton missiles are your heavy weapon: **one hit, one kill** at any range. The
missile is a red energy ball that moves from the Endever to the target,
alternating between + and x shapes as it flies.

But they're scarce. You start with 10 and can only restock by docking at a
base. **Each base carries only 25 missiles total.** Docking refills your supply
up to 10 from the base's pool. After 2-3 docks, the base runs dry — no more
missiles from that base, ever. Across the galaxy, missiles are a finite
resource.

Triton missiles are blocked by stars and other obstacles — if the flight path
intersects an obstacle, the missile detonates harmlessly.

Use them when you can't afford to miss, or when a target is too far for
effective maser fire. Waste them and you'll face the endgame with masers only.

### 7 — Self-Destruct

Press **7**, then enter the code **123** and press ENTER.

A countdown begins: **5… 4… 3… 2… 1…** displayed in the command area. The
countdown runs in real time — **the game continues** during the countdown.
You can move, fire, dodge enemy beams. Each count lasts about half a second.

**To abort**, type **7123** during the countdown. The display shows **ABORT**
and the self-destruct is cancelled.

You can also press the **CLEAR** key (backslash on most keyboards in XRoar) at any time during command input to cancel the current command and return to the COMMAND prompt.

If the countdown reaches zero, the Endever detonates in a massive explosion.
Any Jovian within range takes 200 damage — enough to destroy even a
full-health ship. Proximity-killed Jovians chain-explode in sequence.

This is a last resort — useful when you're surrounded and doomed, and taking
the enemy with you is better than letting them survive to attack more bases.

The game ends after self-destruct.

### Arrow Keys — Ion Engines

Press and hold the arrow keys to move the Endever within the current quadrant:

- **Left arrow** — move left
- **Right arrow** — move right
- **Up arrow** — move up
- **Down arrow** — move down

The Endever accelerates smoothly when you hold an arrow key — a quick tap
moves exactly 1 pixel, while holding builds to full cruise speed over 3 frames.
Release the key and inertial dampers bring the ship to a controlled stop in
2 frames. Diagonal movement works by holding two keys simultaneously.

**You can move while entering other commands** — this is critical for dodging
enemy fire while lining up a shot.

Movement consumes a small amount of ship energy. If ion engines are damaged,
maximum speed is reduced. If they're destroyed, you're a sitting target.

## Docking

To dock at a United Planet base, maneuver the Endever directly above or below
the base using the arrow keys. When you're close enough, the status panel
shows **ENDEVER DOCKED** and your condition changes to **DOCKED**.

Docking restores your ship:
- All five systems repair to 100% (the only way to fully heal — field repair
  caps at 75% and can't fix systems below 25%)
- Ship energy recharges to 100%
- Triton missiles resupply from the base's pool (up to 10 capacity; each base
  starts with 25 total)
- You must lower deflectors (shields DOWN) before the base will accept docking

Docking takes time — stardates continue to advance while you're docked, and
Jovians elsewhere in the galaxy continue their assault on bases. Don't linger.

## Jovian Behavior

The Jovians are not passive targets. Each Jovian has a unique genome that determines its personality — aggression, pilot skill, speed, and appearance.

### Three States

- **IDLE** — Jovians start undetected. They don't chase you, but if you wander within their detection range, they'll take opportunistic pot shots. Detection range depends on pilot skill and emotional state.
- **ATTACK** — Once a Jovian detects you, it actively pursues. Aggressive Jovians close in tight (as close as 20px). Cautious ones keep their distance (up to 65px). They fire red beams when they have line of sight.
- **FLEE** — Wounded Jovians (health below 50%) retreat from you, turning blue with fear. They never fire while fleeing.

### Personality Traits

No two Jovians are identical. Their genome controls:

- **Aggression** — determines resting emotional state and how quickly they rage. Aggressive Jovians have shorter engagement distances and faster fire rates.
- **Pilot skill** — skilled pilots avoid stars at greater distances (6-13px radius), react faster, and detect you from further away. Dumb ones blunder into gravity wells.
- **Speed** — tick rate determines movement speed, reaction time, and apparent intelligence. Fast Jovians literally think more often.
- **Appearance** — each Jovian's sprite is procedurally generated from a seed. Emotion tints the color: blue for fear, white for neutral, red for rage.

### Emotion

Jovians react emotionally to events:
- Entering their quadrant raises alertness (+2)
- Killing a fellow Jovian causes rage or panic (+3)
- Docking at a base emboldens them (+1)
- Over time, emotion decays back to their genetic baseline

Emotion affects everything: fire rate, engagement distance, detection sensitivity, and sprite color. A calm Jovian keeps its distance; an enraged one charges in firing rapidly.

### Collision

Everything bumps. The Endever cannot fly through Jovians — you'll stop against them. Jovians cannot fly through you or through each other. Use this tactically: you can block a Jovian's path to a base.

### Other Behaviors

- **Target bases** — Jovians in a quadrant with a base will threaten it. When an SOS alert appears, you have **3 stardates** (~3 minutes) to warp there and clear the Jovians. If the timer runs out, the base is destroyed. In the current quadrant, a Jovian within 30px of the base will fire beams at it — intercept the beam with your ship to reset the local attack timer.
- **Avoid obstacles** — Jovians navigate around stars, black holes, and bases. Skilled pilots give hazards a wide berth; poor pilots barely notice until it's too late.
- **Get pulled by gravity** — black holes and stars pull Jovians just like they pull you. Skilled pilots resist the pull; dumb ones get sucked in. A Jovian caught by a black hole is destroyed.
- **Quadrant memory** — Jovians remember their emotional state between visits. Leave a quadrant where you killed a Jovian, and when you return, the survivors will be on edge.

## Hazards

### Black Holes

Black holes are invisible. They occupy a position in the quadrant but nothing
appears on the tactical display. You discover them when:

- A maser beam or triton missile stops short, hitting something invisible
- You feel your ship being pulled off course by an unseen force
- You fly into one (instant destruction — the Endever vanishes without a trace)

Black holes have a powerful gravity well that extends 30 pixels in all
directions. As you approach, the pull grows stronger. At the outer edge you
can fight free with ion engines; closer in, escape becomes impossible. If you
notice your ship drifting for no reason, reverse course immediately.

Stars also exert a weaker gravitational pull within 10 pixels. Flying too
close to a star destroys the Endever on contact.

Once you've detected a black hole by observing a blocked shot or a
gravitational pull, remember its approximate position.

### Magnetic Storms

Magnetic storms block scanner readings. Quadrants affected by storms show
**M** on the long range scan instead of contents. You won't know if there are
enemies, bases, or nothing in a storm-affected quadrant until you warp there.

Storms can also interfere with hyperdrive navigation through affected
quadrants.

## Winning and Losing

**You win** when all Jovian ships are destroyed and at least one UP base
survives:

```
YOU HAVE DESTROYED ALL 40 JOVIAN SHIPS
THE UNITED PLANET SYSTEM IS SAVED       YOUR SCORE IS 168

                                    CARE TO PLAY AGAIN?
```

**You lose** when:
- All UP bases are destroyed (Jovians conquer the galaxy)
- All five ship systems reach 0% (ship destroyed)
- The Endever flies into a black hole or collides with a star
- Self-destruct is triggered

```
THE ENDEVER HAS BEEN DESTROYED
THE UNITED PLANET SYSTEM WILL BE CONQUERED   YOUR SCORE IS 0

                                    CARE TO PLAY AGAIN?
```

## Scoring

Your score (1-250) reflects your overall performance:

- Destroying more Jovians relative to the total fleet increases your score
- Preserving more bases increases your score
- Completing the mission in fewer stardates increases your score
- The difficulty level is factored into the final score

A score of 200+ on difficulty 5 or higher is exceptional. A perfect 250 on
difficulty 10 may not be achievable.

## Strategy Tips

1. **Scan first.** Always check the long range scan at the start. Identify
   where the bases and Jovians are. Plan your route.

2. **Prioritize SOS alerts.** When a base calls for help, warp there
   immediately. Once a base is gone, it's gone forever — and you need bases
   to dock and resupply.

3. **Toggle shields tactically.** Shields UP absorbs damage but degrades
   deflectors with each hit. Below 40% deflector health, damage bleeds
   through. Drop shields when you're confident, raise them when outnumbered.
   Remember: shields must be DOWN to dock.

4. **Save triton missiles.** Each base only carries 25. After 2-3 docks,
   a base runs dry. Use masers for most combat. Save missiles for distant
   targets, guaranteed kills, or when energy is too low for masers. In the
   late game, missiles may be gone entirely.

5. **Dock strategically.** Docking is the only way to fully heal — field
   repair caps at 75% and can't fix systems below 25%. But each dock draws
   from the base's limited missile pool. Don't dock just for missiles if
   your systems are healthy.

6. **Keep moving.** A stationary Endever is a dead Endever. Use the arrow
   keys constantly, especially during combat. The Jovians' aim isn't perfect
   — movement makes you much harder to hit.

7. **Watch your energy.** Every action costs energy. If you're below 20%,
   find a base and dock immediately. Running out of energy in a quadrant
   with enemies is a death sentence.

8. **Remember black hole positions.** If a shot vanishes into empty space,
   note where it happened. Don't fly there.

## Quick Reference Card

```
COMMANDS                              TACTICAL SYMBOLS
  1  Damage report                      Blue chevron    = Endever
  2  Hyperdrive (enter col, row)        Red shape       = Jovian
  3  Long range scan                    Blue ring       = UP Base
  4  Deflectors (toggle UP/DOWN)        Colored dots    = Stars
  5  Masers (enter 0-360 degrees)       (invisible)     = Black hole
  6  Triton missiles (enter 0-360)
  7  Self-destruct (confirm: 123)     SCANNER SYMBOLS
  CLEAR  Cancel current command         E = Endever    B = Base
                                        1-3 = Jovians  M = Storm
MOVEMENT
  Arrow keys — tap or hold            CONDITION
  Smooth accel, inertial damping        GREEN  = No enemies
  (works during any command)            YELLOW = Jovians in galaxy
                                        RED    = Jovians in quadrant
DOCKING (shields must be DOWN)          DOCKED = At base
  Fly onto a base                       SOS    = Base under threat
```
