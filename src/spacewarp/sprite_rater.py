#!/usr/bin/env python3
"""Jovian Sprite Rater — mobile-friendly "hot or not" for sprite seeds.

Usage:
    python3 sprite_rater.py [--port 8080] [--data ratings.json]

Deployable as:
    - Local dev:  python3 sprite_rater.py
    - Raspberry Pi: same, bind 0.0.0.0 with --host 0.0.0.0
    - AWS Lambda: wrap with mangum/aws-wsgi (see bottom of file)

No dependencies beyond Python stdlib.
"""

import json
import os
import sys
import argparse
from http.server import HTTPServer, BaseHTTPRequestHandler
from urllib.parse import urlparse, parse_qs

# ── Sprite generation (matches 6809 PRNG exactly) ──────────────────────

def seed_to_dims(seed):
    w_bits = (seed >> 6) & 3
    h_bits = (seed >> 4) & 3
    width = {0: 5, 1: 7, 2: 7, 3: 9}[w_bits]
    height = 5 if h_bits < 2 else 7
    return width, height

def generate_pixels(seed):
    width, height = seed_to_dims(seed)
    half_width = (width + 1) // 2
    state = seed & 0xFF
    pixels = set()
    for row in range(height):
        state = (state * 5 + 3) & 0xFF
        for col in range(half_width):
            if (state >> col) & 1:
                pixels.add((col, row))
                mirror = width - 1 - col
                if mirror != col:
                    pixels.add((mirror, row))
    pixels.add((half_width - 1, height // 2))
    return width, height, pixels

def sprite_svg(seed, color="#ffffff", px=1):
    w, h, pixels = generate_pixels(seed)
    rects = ''.join(
        f'<rect x="{c*px}" y="{r*px}" width="{px}" height="{px}" fill="{color}"/>'
        for c, r in sorted(pixels))
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w*px}" height="{h*px}" '
            f'viewBox="0 0 {w*px} {h*px}" style="image-rendering:pixelated">'
            f'{rects}</svg>')

# ── Ratings storage ────────────────────────────────────────────────────

class RatingStore:
    def __init__(self, path):
        self.path = path
        self.data = {}
        if os.path.exists(path):
            with open(path) as f:
                self.data = json.load(f)

    def save(self):
        with open(self.path, 'w') as f:
            json.dump(self.data, f, indent=1)

    def rate(self, seed, voter, ratings):
        key = str(seed)
        if key not in self.data:
            self.data[key] = {"votes": []}
        self.data[key]["votes"].append({"voter": voter, **ratings})
        self.save()

    def summary(self):
        """Return aggregated ratings per seed."""
        out = {}
        for seed_str, info in self.data.items():
            votes = info["votes"]
            if not votes:
                continue
            n = len(votes)
            avg = {}
            for k in ["menace", "quality", "hot"]:
                vals = [v[k] for v in votes if k in v]
                if vals:
                    avg[k] = round(sum(vals) / len(vals), 2)
            cats = [v.get("category", "") for v in votes if v.get("category")]
            if cats:
                avg["category"] = max(set(cats), key=cats.count)
            avg["n"] = n
            out[seed_str] = avg
        return out

    def progress(self):
        return len(self.data)

# ── HTML ───────────────────────────────────────────────────────────────

INDEX_HTML = r"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0, user-scalable=no">
<title>Jovian Sprite Rater</title>
<style>
* { margin:0; padding:0; box-sizing:border-box; -webkit-tap-highlight-color:transparent; }
body {
    background:#000; color:#ccc; font-family:-apple-system,system-ui,sans-serif;
    min-height:100dvh; display:flex; flex-direction:column;
    overflow:hidden; touch-action:manipulation;
}

.top-bar {
    padding:8px 16px; background:#111; border-bottom:1px solid #222;
    display:flex; justify-content:space-between; align-items:center;
    font-size:13px;
}
.top-bar .prog { color:#555; }

.stage {
    flex:1; display:flex; flex-direction:column;
    align-items:center; justify-content:center;
    padding:20px; gap:16px;
}

.sprite-display {
    display:flex; gap:16px; align-items:flex-end;
    padding:24px; background:#0a0a0a; border-radius:12px;
    border:1px solid #1a1a1a;
}
.sprite-display svg { display:block; }
.sprite-label { text-align:center; }
.sprite-label .clr { font-size:10px; color:#555; margin-top:2px; }

.seed-info {
    text-align:center; font-size:18px; font-weight:600; color:#fff;
    letter-spacing:1px;
}
.seed-dims { font-size:12px; color:#555; font-weight:400; }

/* ── Rating controls ── */
.rate-section {
    width:100%; max-width:360px;
}
.rate-row {
    display:flex; align-items:center; gap:8px;
    margin-bottom:10px;
}
.rate-label {
    width:60px; text-align:right; font-size:12px; color:#666;
    flex-shrink:0;
}
.rate-btns {
    display:flex; gap:4px; flex:1;
}
.rate-btn {
    flex:1; padding:10px 4px; border:1px solid #333; border-radius:6px;
    background:#151515; color:#888; font-size:12px; text-align:center;
    cursor:pointer; transition:all 0.15s;
    -webkit-user-select:none; user-select:none;
}
.rate-btn:active, .rate-btn.sel { transform:scale(0.95); }
.rate-btn.sel { border-color:#4a9eff; color:#fff; background:#1a2a3a; }

.cat-grid {
    display:grid; grid-template-columns:repeat(4, 1fr); gap:4px;
}
.cat-btn {
    padding:8px 2px; border:1px solid #333; border-radius:6px;
    background:#151515; color:#888; font-size:10px; text-align:center;
    cursor:pointer; transition:all 0.15s;
}
.cat-btn:active, .cat-btn.sel { transform:scale(0.95); }
.cat-btn.sel { border-color:#4a9eff; color:#fff; background:#1a2a3a; }

.submit-row {
    display:flex; gap:8px; margin-top:8px; width:100%; max-width:360px;
}
.btn-nah, .btn-hot, .btn-skip {
    flex:1; padding:14px; border:none; border-radius:8px;
    font-size:15px; font-weight:600; cursor:pointer;
    transition:all 0.15s;
}
.btn-nah  { background:#2a1515; color:#f66; }
.btn-skip { background:#222; color:#666; font-size:12px; }
.btn-hot  { background:#152a15; color:#6f6; }
.btn-nah:active { background:#3a1515; }
.btn-hot:active { background:#153a15; }

/* ── Results ── */
.results { display:none; padding:20px; text-align:center; }
.results h2 { color:#fff; margin-bottom:16px; }

/* ── Swipe hint ── */
@keyframes pulse { 0%,100%{opacity:0.3} 50%{opacity:0.6} }
.swipe-hint { font-size:11px; color:#333; animation:pulse 3s infinite; }
</style>
</head>
<body>

<div class="top-bar">
    <span>Jovian Sprite Rater</span>
    <span class="prog" id="progress">0/256</span>
</div>

<div class="stage" id="stage">
    <div class="sprite-display" id="sprites"></div>
    <div class="seed-info">
        <span id="seed-num">#000</span>
        <span class="seed-dims" id="seed-dims">5x5</span>
    </div>

    <div class="rate-section">
        <div class="rate-row">
            <span class="rate-label">menace</span>
            <div class="rate-btns" id="menace-btns">
                <div class="rate-btn" data-v="0.1" data-g="menace">weak</div>
                <div class="rate-btn" data-v="0.3" data-g="menace">meh</div>
                <div class="rate-btn" data-v="0.5" data-g="menace">mid</div>
                <div class="rate-btn" data-v="0.7" data-g="menace">tough</div>
                <div class="rate-btn" data-v="0.9" data-g="menace">scary</div>
            </div>
        </div>
        <div class="rate-row">
            <span class="rate-label">quality</span>
            <div class="rate-btns" id="quality-btns">
                <div class="rate-btn" data-v="0.1" data-g="quality">blob</div>
                <div class="rate-btn" data-v="0.3" data-g="quality">meh</div>
                <div class="rate-btn" data-v="0.5" data-g="quality">ok</div>
                <div class="rate-btn" data-v="0.7" data-g="quality">good</div>
                <div class="rate-btn" data-v="0.9" data-g="quality">crisp</div>
            </div>
        </div>
        <div class="rate-row">
            <span class="rate-label">looks like</span>
            <div class="cat-grid" id="cat-btns">
                <div class="cat-btn" data-cat="ship">ship</div>
                <div class="cat-btn" data-cat="insect">insect</div>
                <div class="cat-btn" data-cat="face">face</div>
                <div class="cat-btn" data-cat="creature">creature</div>
                <div class="cat-btn" data-cat="robot">robot</div>
                <div class="cat-btn" data-cat="crown">crown</div>
                <div class="cat-btn" data-cat="abstract">abstract</div>
                <div class="cat-btn" data-cat="blob">blob</div>
            </div>
        </div>
    </div>

    <div class="submit-row">
        <button class="btn-nah" id="btn-nah">NAH</button>
        <button class="btn-skip" id="btn-skip">skip</button>
        <button class="btn-hot" id="btn-hot">HOT</button>
    </div>
    <div class="swipe-hint">tap a rating then HOT or NAH</div>
</div>

<div class="results" id="results">
    <h2>All rated!</h2>
    <p>Refresh to review or visit <code>/results</code> for data.</p>
</div>

<script>
// ── Sprite generation (PRNG mirror of 6809 code) ──
function genSprite(seed) {
    const wBits = (seed >> 6) & 3;
    const hBits = (seed >> 4) & 3;
    const w = [5,7,7,9][wBits];
    const h = hBits < 2 ? 5 : 7;
    const hw = (w + 1) >> 1;
    let state = seed & 0xFF;
    const px = new Set();
    for (let r = 0; r < h; r++) {
        state = (state * 5 + 3) & 0xFF;
        for (let c = 0; c < hw; c++) {
            if ((state >> c) & 1) {
                px.add(r * w + c);
                const m = w - 1 - c;
                if (m !== c) px.add(r * w + m);
            }
        }
    }
    px.add((h >> 1) * w + (hw - 1));
    return {w, h, px};
}

function spriteSVG(seed, color, pxSize) {
    const {w, h, px} = genSprite(seed);
    let rects = '';
    for (const p of px) {
        const c = p % w, r = Math.floor(p / w);
        rects += `<rect x="${c*pxSize}" y="${r*pxSize}" width="${pxSize}" height="${pxSize}" fill="${color}"/>`;
    }
    return `<svg xmlns="http://www.w3.org/2000/svg" width="${w*pxSize}" height="${h*pxSize}" ` +
           `viewBox="0 0 ${w*pxSize} ${h*pxSize}" style="image-rendering:pixelated">${rects}</svg>`;
}

// ── State ──
const COLORS = {white:'#ffffff', blue:'#4488ff', red:'#ff6633'};
let queue = [];
let current = null;
let rated = new Set();
let selections = {menace: null, quality: null, category: null};
const voter = 'user-' + Math.random().toString(36).slice(2, 8);

// Build queue: randomized seed order
for (let i = 0; i < 256; i++) queue.push(i);
queue.sort(() => Math.random() - 0.5);

// ── UI ──
function showSprite(seed) {
    current = seed;
    const {w, h} = genSprite(seed);
    const pxSize = Math.max(4, Math.min(8, Math.floor(120 / Math.max(w, h))));
    const container = document.getElementById('sprites');
    container.innerHTML = '';
    for (const [name, hex] of Object.entries(COLORS)) {
        const wrap = document.createElement('div');
        wrap.className = 'sprite-label';
        wrap.innerHTML = spriteSVG(seed, hex, pxSize) +
            `<div class="clr">${name}</div>`;
        container.appendChild(wrap);
    }
    document.getElementById('seed-num').textContent = '#' + String(seed).padStart(3, '0');
    document.getElementById('seed-dims').textContent = `${w}x${h}`;

    // Clear selections
    selections = {menace: null, quality: null, category: null};
    document.querySelectorAll('.rate-btn, .cat-btn').forEach(b => b.classList.remove('sel'));
    updateProgress();
}

function updateProgress() {
    document.getElementById('progress').textContent = `${rated.size}/256`;
}

function nextSprite() {
    if (queue.length === 0) {
        document.getElementById('stage').style.display = 'none';
        document.getElementById('results').style.display = 'block';
        return;
    }
    showSprite(queue.pop());
}

function submitRating(hot) {
    const payload = {
        seed: current,
        voter,
        hot: hot ? 1.0 : 0.0,
        menace: selections.menace ?? 0.5,
        quality: selections.quality ?? 0.5,
        category: selections.category ?? '',
    };
    fetch('/api/rate', {
        method: 'POST',
        headers: {'Content-Type': 'application/json'},
        body: JSON.stringify(payload),
    });
    rated.add(current);
    nextSprite();
}

// ── Event handlers ──
document.querySelectorAll('.rate-btn').forEach(btn => {
    btn.addEventListener('click', () => {
        const group = btn.dataset.g;
        document.querySelectorAll(`.rate-btn[data-g="${group}"]`).forEach(b => b.classList.remove('sel'));
        btn.classList.add('sel');
        selections[group] = parseFloat(btn.dataset.v);
    });
});

document.querySelectorAll('.cat-btn').forEach(btn => {
    btn.addEventListener('click', () => {
        document.querySelectorAll('.cat-btn').forEach(b => b.classList.remove('sel'));
        btn.classList.add('sel');
        selections.category = btn.dataset.cat;
    });
});

document.getElementById('btn-hot').addEventListener('click', () => submitRating(true));
document.getElementById('btn-nah').addEventListener('click', () => submitRating(false));
document.getElementById('btn-skip').addEventListener('click', () => nextSprite());

// Keyboard shortcuts
document.addEventListener('keydown', e => {
    if (e.key === 'ArrowRight' || e.key === 'h') submitRating(true);
    if (e.key === 'ArrowLeft' || e.key === 'n') submitRating(false);
    if (e.key === ' ') { e.preventDefault(); nextSprite(); }
    // Number keys 1-5 for menace
    if (e.key >= '1' && e.key <= '5') {
        const idx = parseInt(e.key) - 1;
        const btns = document.querySelectorAll('#menace-btns .rate-btn');
        btns.forEach(b => b.classList.remove('sel'));
        btns[idx].classList.add('sel');
        selections.menace = parseFloat(btns[idx].dataset.v);
    }
    // Q-T for quality
    const qkeys = 'qwert';
    const qi = qkeys.indexOf(e.key);
    if (qi >= 0) {
        const btns = document.querySelectorAll('#quality-btns .rate-btn');
        btns.forEach(b => b.classList.remove('sel'));
        btns[qi].classList.add('sel');
        selections.quality = parseFloat(btns[qi].dataset.v);
    }
});

// Start
nextSprite();
</script>
</body>
</html>
"""

RESULTS_HTML = r"""<!DOCTYPE html>
<html><head>
<meta charset="UTF-8"><meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>Sprite Ratings</title>
<style>
body { background:#000; color:#ccc; font-family:monospace; padding:16px; font-size:13px; }
table { border-collapse:collapse; width:100%%; }
th, td { padding:4px 8px; border:1px solid #222; text-align:left; }
th { background:#111; color:#888; }
.hot { color:#6f6; } .nah { color:#f66; }
</style>
</head><body>
<h2>Ratings Summary</h2>
<p>%d seeds rated, %d total votes</p>
<table>
<tr><th>Seed</th><th>Size</th><th>Votes</th><th>Hot%%</th><th>Menace</th><th>Quality</th><th>Category</th></tr>
%s
</table>
<p style="margin-top:16px"><a href="/api/export" style="color:#4a9eff">Download JSON</a></p>
</body></html>
"""

# ── HTTP Server ────────────────────────────────────────────────────────

class SpriteHandler(BaseHTTPRequestHandler):
    store = None

    def log_message(self, fmt, *args):
        pass  # quiet

    def respond(self, code, content_type, body):
        self.send_response(code)
        self.send_header('Content-Type', content_type)
        self.send_header('Access-Control-Allow-Origin', '*')
        if isinstance(body, str):
            body = body.encode('utf-8')
        self.send_header('Content-Length', len(body))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        path = urlparse(self.path).path

        if path == '/' or path == '/index.html':
            self.respond(200, 'text/html', INDEX_HTML)

        elif path == '/results':
            summary = self.store.summary()
            total_votes = sum(v.get('n', 0) for v in summary.values())
            rows = []
            for seed in range(256):
                s = summary.get(str(seed))
                if not s:
                    continue
                w, h = seed_to_dims(seed)
                hot_pct = s.get('hot', 0) * 100
                cls = 'hot' if hot_pct >= 50 else 'nah'
                rows.append(
                    f'<tr><td>#{seed:03d}</td><td>{w}x{h}</td>'
                    f'<td>{s["n"]}</td>'
                    f'<td class="{cls}">{hot_pct:.0f}%</td>'
                    f'<td>{s.get("menace", "")}</td>'
                    f'<td>{s.get("quality", "")}</td>'
                    f'<td>{s.get("category", "")}</td></tr>'
                )
            html = RESULTS_HTML % (len(summary), total_votes, '\n'.join(rows))
            self.respond(200, 'text/html', html)

        elif path == '/api/export':
            self.respond(200, 'application/json',
                         json.dumps(self.store.data, indent=2))

        elif path == '/api/progress':
            self.respond(200, 'application/json',
                         json.dumps({"rated": self.store.progress()}))

        else:
            self.respond(404, 'text/plain', 'Not found')

    def do_POST(self):
        path = urlparse(self.path).path

        if path == '/api/rate':
            length = int(self.headers.get('Content-Length', 0))
            body = json.loads(self.rfile.read(length))
            seed = body.pop('seed')
            voter = body.pop('voter', 'anon')
            self.store.rate(seed, voter, body)
            self.respond(200, 'application/json', '{"ok":true}')

        else:
            self.respond(404, 'text/plain', 'Not found')

    def do_OPTIONS(self):
        self.send_response(200)
        self.send_header('Access-Control-Allow-Origin', '*')
        self.send_header('Access-Control-Allow-Methods', 'GET, POST, OPTIONS')
        self.send_header('Access-Control-Allow-Headers', 'Content-Type')
        self.end_headers()


def run_server(host='127.0.0.1', port=8080, data_file='ratings.json'):
    store = RatingStore(data_file)
    SpriteHandler.store = store

    server = HTTPServer((host, port), SpriteHandler)
    print(f"Jovian Sprite Rater")
    print(f"  http://{host}:{port}/")
    print(f"  Results: http://{host}:{port}/results")
    print(f"  Data: {os.path.abspath(data_file)}")
    print(f"  Press Ctrl+C to stop")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print(f"\n{store.progress()} seeds rated. Saved to {data_file}")


# ── AWS Lambda handler (optional) ──────────────────────────────────────
# Deploy with: pip install mangum, then set handler = sprite_rater.lambda_handler
# Or use a simple API Gateway + Lambda proxy integration.
#
# To use:
#   1. pip install mangum
#   2. Set Lambda handler to sprite_rater.lambda_handler
#   3. Set RATINGS_TABLE env var for DynamoDB (or use /tmp for ephemeral)
#
# def lambda_handler(event, context):
#     from mangum import Mangum
#     # Would need WSGI wrapper — left as exercise since stdlib server
#     # isn't WSGI. For Lambda, consider rewriting routes as API Gateway
#     # endpoints or use Flask + mangum.


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Jovian Sprite Rater')
    parser.add_argument('--host', default='127.0.0.1',
                        help='Bind address (use 0.0.0.0 for network)')
    parser.add_argument('--port', type=int, default=8080)
    parser.add_argument('--data', default='ratings.json',
                        help='Ratings data file')
    args = parser.parse_args()
    run_server(args.host, args.port, args.data)
