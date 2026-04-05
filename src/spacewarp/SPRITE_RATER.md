# Jovian Sprite Rater

A mobile-friendly "hot or not" web app for evaluating all 256 procedural
Jovian sprite patterns. Rate sprites on menace, quality, and visual category
to build a ranked catalog that maps appearance seeds to gameplay disposition.

## Quick Start

```sh
cd src/spacewarp
python3 sprite_rater.py
```

Open http://localhost:8080/ on your phone or browser.

No dependencies beyond Python 3 stdlib.

## How It Works

Each Jovian sprite is generated from a single byte (the "appearance seed",
genome byte 2). The PRNG produces a bilaterally symmetric pixel pattern
at one of six size classes (5x5, 5x7, 7x5, 7x7, 9x5, 9x7). The rater
shows every seed in random order with all three emotion colors (white,
blue, red) and collects human ratings.

## Rating a Sprite

Each sprite gets three ratings:

**Menace** (how threatening does it look?)
- weak / meh / mid / tough / scary
- Maps to 0.1 - 0.9
- Keyboard: keys 1-5

**Quality** (how good is the design?)
- blob / meh / ok / good / crisp
- Maps to 0.1 - 0.9
- A "blob" is shapeless noise; "crisp" is a clean, recognizable silhouette
- Keyboard: keys Q-W-E-R-T

**Category** (what does it look like?)
- ship, insect, face, creature, robot, crown, abstract, blob
- Tap one. No keyboard shortcut.

Then tap **HOT** (keep) or **NAH** (reject). Or **skip** to defer.

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| 1-5 | Set menace (1=weak, 5=scary) |
| Q W E R T | Set quality (Q=blob, T=crisp) |
| Right arrow or H | HOT (submit, keep) |
| Left arrow or N | NAH (submit, reject) |
| Space | Skip (no vote, next sprite) |

## Endpoints

| URL | What |
|-----|------|
| `/` | The rater UI |
| `/results` | Aggregated ratings table (HTML) |
| `/api/export` | Raw ratings data (JSON download) |
| `/api/progress` | How many seeds rated (JSON) |

## Deployment

### Local (default)

```sh
python3 sprite_rater.py
# http://127.0.0.1:8080/
```

### Raspberry Pi / LAN

```sh
python3 sprite_rater.py --host 0.0.0.0 --port 8080
# Access from any device on the network at http://<pi-ip>:8080/
```

### AWS Lambda

The server uses stdlib `http.server` which doesn't directly map to Lambda.
To deploy as Lambda:

1. Rewrite the routes as API Gateway endpoints, or
2. Wrap with Flask + mangum:
   ```python
   pip install flask mangum
   # Adapt routes to Flask, use mangum as Lambda handler
   ```
3. Use S3 for static hosting of the HTML (it's self-contained in the
   Python file as a string constant) and Lambda for the `/api/*` endpoints.
4. Replace `ratings.json` file storage with DynamoDB.

## Data Storage

Ratings save to `ratings.json` (or `--data <path>`) after every vote.
Format:

```json
{
  "42": {
    "votes": [
      {"voter": "user-a3f2c1", "hot": 1.0, "menace": 0.7, "quality": 0.9, "category": "insect"}
    ]
  }
}
```

Multiple voters are tracked by random voter ID (generated per browser
session). The results page aggregates across all voters.

## Using Ratings in the Game

After rating, export the data and update `sprite_data.py`:

```sh
curl http://localhost:8080/api/export > ratings.json
```

Then regenerate the sprite catalog:

```sh
python3 gen_sprite_report.py
open sprite_catalog.html
```

The long-term goal: use rated seeds to map genome appearance byte to
Jovian disposition. Aggressive Jovians get high-menace seeds. Timid ones
get low-menace seeds. Quality filters out the blobs so every Jovian in
the game looks intentionally designed.

## Related Files

| File | Purpose |
|------|---------|
| `sprite_rater.py` | The rater server (this tool) |
| `sprite_data.py` | Seed descriptions and ratings (editable) |
| `gen_sprite_report.py` | Generates `sprite_catalog.html` from sprite_data |
| `sprite_catalog.html` | Interactive catalog with scatter/grid/page views |
| `sprtest.fs` | Forth test app that renders all seeds on the CoCo |
| `archives/sprtest_*.png` | Screenshot captures from sprtest |
