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
- **CONDITION** — GREEN (no enemies), RED (Jovians present), or DOCKED.
- **SOS-BASE x y** — flashes when a base in another quadrant is under attack,
  showing its coordinates. Get there fast.
- **COMMAND?** — waiting for your next command.

### What You See in the Tactical View

| Symbol | Color | Meaning | ASCII shape |
|--------|-------|---------|---------------|
| Chevron shape | Blue | The Endever (you) | "V" |
| Angular bracket shape | Red | Jovian warship | <*> |
| Cross/ring shape | Blue | United Planet base | +O+ |
| Dots | Mixed colors | Stars and Planets (white, blue, red) |  .  |
| Line from ship | Blue | Maser beam |  |
| Ball | Blue | Triton missile | * |
| Line from enemy | Red | Jovian weapons fire |  |
| Burst pattern | Red | Explosion |  |

Black holes are invisible. You only discover them when a weapon shot vanishes
into empty space, or when you fly into one (which destroys you instantly).

## Commands

Commands are entered by pressing a number key. Some commands require additional
input (angles, coordinates, energy levels). You can move the Endever with the
arrow keys at any time — even while typing another command.

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

- **Ion engines** — movement slows, then stops entirely.
- **Hyperdrive** — below ~20%, warping becomes unreliable or impossible.
- **Scanners** — long range scan shows incomplete or garbled data.
- **Deflectors** — maximum shield setting is capped at the damage level.
- **Masers** — reduced damage output.

Press any key to return to the tactical view.

### 2 — Hyperdrive

Press **2**, then enter a two-digit destination: column (0-7) then row (0-7).
The Endever warps instantly to the new quadrant, arriving at a random position.

Hyperdrive consumes energy proportional to distance traveled. If hyperdrive
is heavily damaged, the warp may fail or send you to the wrong quadrant.

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

Press **4**, then enter a number from 0 to 100 to set your deflector shield
energy level.

Shields protect you from damage, but they cost you firepower:

| Shield setting | Damage absorbed | Maser power loss |
|----------------|----------------|-----------------|
| 0% | None | None |
| 25% | Low | Minor |
| 50% | Moderate | 1/3 reduction |
| 75% | High | 1/2 reduction |
| 100% | Maximum | 2/3 reduction |

**The tradeoff is everything.** Shields up means you survive longer but your
masers hit softer. Shields down means maximum firepower but one good hit could
finish you. There is no correct answer — it depends on how many enemies you're
facing, how much energy you have, and how good your aim is.

If your deflector system is damaged, the maximum shield setting is capped at
the damage percentage (e.g., 60% damage = max 60% shields).

### 5 — Masers

Press **5**, then enter a firing angle from 0 to 360 degrees:

- **0** — fire to the right
- **90** — fire upward
- **180** — fire to the left
- **270** — fire downward

The maser beam appears as a visible blue line streaking from the Endever
across the tactical view. If it hits a Jovian ship, damage is dealt based on
distance (closer = more damage) and your shield level (higher shields = weaker
masers).

Masers do not destroy in one hit — it typically takes 2-4 shots depending on
range and shields. Masers consume ship energy with each shot.

If your maser system is damaged, the beam is weaker.

### 6 — Triton Missiles

Press **6**, then enter a firing angle from 0 to 360 degrees (same compass as
masers).

Triton missiles are your heavy weapon: **one hit, one kill** at any range. The
missile is a red energy ball that moves from the Endever to the target.

But they're scarce. You start with 10 and can only get more by docking at a
base. Triton missiles are blocked by stars and black holes — if the flight
path intersects an obstacle, the missile detonates harmlessly.

Use them when you can't afford to miss, or when a target is too far for
effective maser fire.

### 7 — Self-Destruct

Press **7**. You will be asked to confirm with the code **123** followed by
ENTER. 

Self-destruct destroys the Endever and every Jovian ship in the current
quadrant. This is a last resort — useful only when you're surrounded and
doomed, and taking the enemy with you is better than letting them survive to
attack more bases.

The game ends after self-destruct. Your score reflects whatever you
accomplished before triggering it.

### Arrow Keys — Ion Engines

Press and hold the arrow keys to move the Endever within the current quadrant:

- **Left arrow** — move left
- **Right arrow** — move right
- **Up arrow** — move up
- **Down arrow** — move down

Movement is continuous while the key is held. **You can move while entering
other commands** — this is critical for dodging enemy fire while lining up a
shot.

Movement consumes a small amount of ship energy. If ion engines are damaged,
movement is slower. If they're destroyed, you're a sitting target.

## Docking

To dock at a United Planet base, maneuver the Endever directly above or below
the base using the arrow keys. When you're close enough, the status panel
shows **ENDEVER DOCKED** and your condition changes to **DOCKED**.

Docking restores your ship completely:
- All five systems repaired to 100%
- Ship energy restored to 100%
- Triton missiles replenished to 10
- Deflector setting preserved

Docking takes time — stardates continue to advance while you're docked, and
Jovians elsewhere in the galaxy continue their assault on bases. Don't linger.

## Jovian Behavior

The Jovians are not passive targets. They:

- **Fire at you** — Jovian weapons fire appears as red lines on the tactical
  display. The message "ENDEVER HIT" appears when you take damage.
- **Move within quadrants** — they dodge your shots and maneuver tactically.
- **Move between quadrants** — Jovians actively hunt for bases to destroy.
  When a base is under attack, you'll see the SOS alert.
- **Attack bases** — if Jovians remain in a quadrant with a base for too long,
  the base is destroyed. The message "BASE DESTROYED" appears.
- **Get smarter at higher difficulty** — more aggressive movement, faster fire
  rate, more likely to target bases.

At higher difficulty levels, Jovians may also call reinforcements — if a
quadrant has a base, nearby Jovians will attempt to move into that quadrant.

## Hazards

### Black Holes

Black holes are invisible. They occupy a position in the quadrant but nothing
appears on the tactical display. You discover them when:

- A maser beam or triton missile disappears into empty space
- You fly into one (instant destruction of the Endever)

Once you've detected a black hole by observing a blocked shot, remember its
approximate position.

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
- The Endever is destroyed (energy reaches 0%, or fly into a black hole)
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

3. **Keep shields around 50%.** This balances survivability with firepower.
   Drop shields to 0% only when you're confident you can kill the target
   before it fires back. Raise shields to 100% when you're outnumbered and
   just trying to survive until you can warp out.

4. **Save triton missiles for emergencies.** Use masers for most combat.
   Save missiles for: distant targets you can't reach with masers, situations
   where you need a guaranteed kill, or when you're low on energy and can't
   afford maser shots.

5. **Dock whenever you can.** Don't wait until you're critically damaged.
   If you're in a quadrant with a base and no enemies, dock. Full repair and
   resupply takes only a moment and could save your life later.

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
  2  Hyperdrive (enter col, row)        Red angular     = Jovian
  3  Long range scan                    Blue cross      = UP Base
  4  Deflectors (enter 0-100)           Colored dots    = Stars
  5  Masers (enter 0-360 degrees)       (invisible)     = Black hole
  6  Triton missiles (enter 0-360)
  7  Self-destruct (confirm: 123)     SCANNER SYMBOLS
                                        E = Endever    B = Base
MOVEMENT                               1-3 = Jovians  M = Storm
  Arrow keys — hold to move
  (works during any command)          CONDITION
                                        GREEN  = No enemies
DOCKING                                 RED    = Jovians present
  Fly directly above/below a base       DOCKED = At base
```
