#!/usr/bin/env python3
"""Generate interactive Jovian sprite catalog as HTML+SVG.

Reproduces the exact PRNG pixel generation from gen-jov-sprite in Python,
renders each sprite as inline SVG at native pixel resolution, and builds
an interactive report with sorting, filtering, and 2D scatter views.
"""

import os

from sprite_data import SPRITE_DATA

OUTPUT = "sprite_catalog.html"

# ── Sprite generation (matches gen-jov-sprite CODE word exactly) ────────

def seed_to_dims(seed):
    """Decode width/height from appearance seed byte."""
    w_bits = (seed >> 6) & 3
    h_bits = (seed >> 4) & 3
    width = {0: 5, 1: 7, 2: 7, 3: 9}[w_bits]
    height = 5 if h_bits < 2 else 7
    return width, height


def generate_sprite(seed):
    """Generate pixel grid from seed, matching the 6809 PRNG exactly.
    Returns (width, height, pixels) where pixels is a set of (col, row) tuples.
    """
    width, height = seed_to_dims(seed)
    half_width = (width + 1) // 2
    state = seed & 0xFF
    pixels = set()

    for row in range(height):
        # PRNG update: state = (state * 5 + 3) & 0xFF
        state = (state * 5 + 3) & 0xFF

        for col in range(half_width):
            # Extract bit at position col
            bit = (state >> col) & 1
            if bit:
                pixels.add((col, row))
                # Mirror (unless center column)
                mirror_col = width - 1 - col
                if mirror_col != col:
                    pixels.add((mirror_col, row))

    # Center column guarantee
    center_col = half_width - 1
    center_row = height // 2
    pixels.add((center_col, center_row))

    return width, height, pixels


def sprite_to_svg(seed, color="#ffffff", pixel_size=2, bg=None):
    """Render a sprite as an inline SVG string.
    pixel_size: size of each artifact pixel in SVG units.
    """
    width, height, pixels = generate_sprite(seed)
    svg_w = width * pixel_size
    svg_h = height * pixel_size

    parts = [f'<svg xmlns="http://www.w3.org/2000/svg" '
             f'width="{svg_w}" height="{svg_h}" '
             f'viewBox="0 0 {svg_w} {svg_h}" '
             f'style="image-rendering:pixelated">']

    if bg:
        parts.append(f'<rect width="{svg_w}" height="{svg_h}" fill="{bg}"/>')

    for (cx, cy) in sorted(pixels):
        x = cx * pixel_size
        y = cy * pixel_size
        parts.append(f'<rect x="{x}" y="{y}" '
                     f'width="{pixel_size}" height="{pixel_size}" '
                     f'fill="{color}"/>')

    parts.append('</svg>')
    return ''.join(parts)


def sprite_svg_data_uri(seed, color="#ffffff", pixel_size=2):
    """Generate a data URI for a sprite SVG (for use in <img> tags)."""
    import base64
    svg = sprite_to_svg(seed, color, pixel_size, bg="#000000")
    b64 = base64.b64encode(svg.encode('utf-8')).decode('ascii')
    return f"data:image/svg+xml;base64,{b64}"


# ── Color definitions ───────────────────────────────────────────────────

ARTIFACT_COLORS = {
    "white": "#ffffff",
    "blue":  "#4488ff",
    "red":   "#ff6633",
}

CATEGORY_COLORS = {
    "ship": "#4a9eff", "insect": "#66ff66", "face": "#ff6666",
    "creature": "#cc66ff", "abstract": "#888888", "blob": "#555555",
    "crown": "#ffaa00", "robot": "#00ccff",
}

# ── HTML generation ─────────────────────────────────────────────────────

def main():
    os.chdir(os.path.dirname(os.path.abspath(__file__)))

    html = []
    html.append(r"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<title>Jovian Sprite Catalog</title>
<style>
* { margin: 0; padding: 0; box-sizing: border-box; }
body {
    background: #0a0a0a; color: #ccc;
    font-family: 'Menlo', 'Monaco', 'Consolas', monospace;
    font-size: 12px;
}

/* ── Controls ── */
.controls {
    position: sticky; top: 0; z-index: 100;
    background: #111; border-bottom: 1px solid #333;
    padding: 8px 16px; display: flex; gap: 12px; align-items: center;
    flex-wrap: wrap;
}
.controls label { font-size: 11px; color: #999; }
.controls select, .controls input {
    background: #222; color: #ccc; border: 1px solid #444;
    padding: 3px 6px; font-size: 11px; font-family: inherit;
    border-radius: 3px;
}
.controls .sep { border-left: 1px solid #333; height: 20px; }
#count { color: #666; font-size: 11px; }
.tab-bar { display: flex; gap: 2px; }
.tab {
    padding: 4px 10px; background: #222; border: 1px solid #333;
    border-radius: 4px 4px 0 0; cursor: pointer; font-size: 11px;
    color: #888;
}
.tab.active { background: #1a1a1a; color: #fff; border-bottom-color: #1a1a1a; }

/* ── Grid view ── */
#grid-view { padding: 16px; }
.grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(110px, 1fr));
    gap: 4px;
}
.card {
    background: #111; border: 1px solid #1a1a1a; border-radius: 3px;
    padding: 6px; text-align: center;
    cursor: default; transition: border-color 0.15s;
}
.card:hover { border-color: #444; }
.card.hidden { display: none; }
.card .sprites {
    display: flex; justify-content: center; gap: 3px;
    margin-bottom: 4px; min-height: 14px; align-items: flex-end;
    background: #000; padding: 4px 2px; border-radius: 2px;
}
.card .seed { font-size: 11px; font-weight: bold; color: #ddd; }
.card .meta { font-size: 9px; color: #666; margin-top: 1px; }
.card .cat { font-size: 9px; margin-top: 1px; }
.card .desc { font-size: 9px; color: #777; margin-top: 2px; }
.card .ratings { font-size: 9px; margin-top: 2px; }
.badge {
    display: inline-block; padding: 0 3px; border-radius: 2px;
    font-size: 9px;
}
.badge.high { background: #2a1a1a; color: #f88; }
.badge.mid  { background: #2a2a1a; color: #ee8; }
.badge.low  { background: #1a2a1a; color: #8e8; }

/* ── Page view (8×8 grid like the actual screenshots) ── */
#page-view { padding: 16px; display: none; }
.page-grid {
    display: inline-grid;
    grid-template-columns: repeat(8, auto);
    gap: 0;
    background: #000;
    padding: 8px;
    border: 1px solid #222;
    border-radius: 4px;
}
.page-cell {
    width: 48px; text-align: center; padding: 2px;
}
.page-cell .page-seed { font-size: 8px; color: #555; margin-bottom: 1px; }
.page-cell svg { display: block; margin: 0 auto; }
.page-controls { margin-bottom: 12px; }

/* ── Scatter view (2D sort) ── */
#scatter-view { padding: 16px; display: none; }
.scatter-container {
    position: relative;
    width: 800px; height: 800px;
    background: #0a0a0a;
    border: 1px solid #222;
    border-radius: 4px;
    margin: 12px auto;
}
.scatter-container .axis-label {
    position: absolute; color: #555; font-size: 10px;
}
.scatter-container .axis-label.x-axis {
    bottom: -18px; left: 50%; transform: translateX(-50%);
}
.scatter-container .axis-label.y-axis {
    left: -22px; top: 50%; transform: rotate(-90deg) translateX(-50%);
    transform-origin: left center;
}
.scatter-dot {
    position: absolute; cursor: pointer;
    transition: transform 0.3s ease;
}
.scatter-dot:hover { transform: scale(2); z-index: 10; }
.scatter-dot .tooltip {
    display: none; position: absolute; bottom: 100%; left: 50%;
    transform: translateX(-50%);
    background: #222; border: 1px solid #444; border-radius: 3px;
    padding: 4px 6px; font-size: 10px; color: #ccc;
    white-space: nowrap; z-index: 20;
}
.scatter-dot:hover .tooltip { display: block; }
</style>
</head>
<body>

<div class="controls">
    <div class="tab-bar">
        <div class="tab active" data-view="grid">Catalog</div>
        <div class="tab" data-view="page">Pages</div>
        <div class="tab" data-view="scatter">Scatter</div>
    </div>
    <div class="sep"></div>
    <label>Size <select id="filter-size">
        <option value="all">All</option>
        <option value="5x5">5x5</option><option value="5x7">5x7</option>
        <option value="7x5">7x5</option><option value="7x7">7x7</option>
        <option value="9x5">9x5</option><option value="9x7">9x7</option>
    </select></label>
    <label>Cat <select id="filter-cat">
        <option value="all">All</option>
        <option value="ship">Ship</option><option value="insect">Insect</option>
        <option value="face">Face</option><option value="creature">Creature</option>
        <option value="robot">Robot</option><option value="crown">Crown</option>
        <option value="abstract">Abstract</option><option value="blob">Blob</option>
    </select></label>
    <label>Sort <select id="sort-by">
        <option value="seed">Seed</option>
        <option value="menace-desc">Menace ↓</option>
        <option value="quality-desc">Quality ↓</option>
        <option value="category">Category</option>
    </select></label>
    <label>Color <select id="sprite-color">
        <option value="all">All 3</option>
        <option value="white">White</option>
        <option value="blue">Blue</option>
        <option value="red">Red</option>
    </select></label>
    <label><input type="text" id="search" placeholder="search..." style="width:100px"></label>
    <span id="count"></span>
</div>

<!-- ═══ Grid View ═══ -->
<div id="grid-view">
<div class="grid" id="grid">
""")

    # Generate all sprite cards
    for seed in range(256):
        w, h = seed_to_dims(seed)
        size_class = f"{w}x{h}"
        desc, menace, quality, category = SPRITE_DATA.get(
            seed, ("unclassified", 0.5, 0.5, "abstract"))

        cat_color = CATEGORY_COLORS.get(category, "#888")

        # Pre-generate SVGs for all 3 colors
        svgs = {}
        for cname, chex in ARTIFACT_COLORS.items():
            svgs[cname] = sprite_to_svg(seed, chex, pixel_size=2, bg=None)

        def badge(label, val):
            cls = "high" if val >= 0.7 else "mid" if val >= 0.4 else "low"
            return f'<span class="badge {cls}">{label} {val:.1f}</span>'

        # Embed all 3 SVGs but hide/show based on color filter
        sprite_html = []
        for cname in ["white", "blue", "red"]:
            sprite_html.append(
                f'<span class="spr spr-{cname}">{svgs[cname]}</span>')

        html.append(f"""<div class="card" data-seed="{seed}" data-size="{size_class}"
  data-menace="{menace}" data-quality="{quality}"
  data-category="{category}" data-desc="{desc}">
  <div class="sprites">{' '.join(sprite_html)}</div>
  <div class="seed">#{seed:03d}</div>
  <div class="meta">{size_class} <span class="cat" style="color:{cat_color}">{category}</span></div>
  <div class="desc">{desc}</div>
  <div class="ratings">{badge("M", menace)} {badge("Q", quality)}</div>
</div>
""")

    html.append('</div></div>\n')

    # ── Page View ──
    html.append('<!-- ═══ Page View ═══ -->\n<div id="page-view">\n')
    html.append('<div class="page-controls">')
    html.append('<label>Color <select id="page-color">')
    for c in ARTIFACT_COLORS:
        html.append(f'<option value="{c}">{c}</option>')
    html.append('</select></label>')
    html.append(' <label>Page <select id="page-num">')
    for p in range(4):
        s = p * 64
        html.append(f'<option value="{p}">{s:03d}-{s+63:03d}</option>')
    html.append('</select></label></div>\n')

    # Pre-generate all 4 pages × 3 colors
    for cname, chex in ARTIFACT_COLORS.items():
        for page in range(4):
            start = page * 64
            vis = "block" if cname == "white" and page == 0 else "none"
            html.append(f'<div class="page-grid" id="pg-{cname}-{page}" '
                        f'style="display:{vis}">\n')
            for row in range(8):
                for col in range(8):
                    seed = start + row * 8 + col
                    if seed < 256:
                        svg = sprite_to_svg(seed, chex, pixel_size=3, bg=None)
                        desc = SPRITE_DATA.get(seed, ("",))[0]
                        html.append(f'<div class="page-cell" title="#{seed:03d} {desc}">')
                        html.append(f'<div class="page-seed">{seed:03d}</div>{svg}</div>\n')
                    else:
                        html.append('<div class="page-cell"></div>\n')
            html.append('</div>\n')

    html.append('</div>\n')

    # ── Scatter View ──
    html.append('<!-- ═══ Scatter View ═══ -->\n<div id="scatter-view">\n')
    html.append("""<div style="margin-bottom:8px">
    <label>X axis <select id="scatter-x">
        <option value="menace">Menace</option>
        <option value="quality">Quality</option>
        <option value="seed">Seed</option>
    </select></label>
    <label>Y axis <select id="scatter-y">
        <option value="quality">Quality</option>
        <option value="menace" selected>Menace</option>
        <option value="seed">Seed</option>
    </select></label>
</div>
<div class="scatter-container" id="scatter">
    <div class="axis-label x-axis" id="scatter-x-label">menace →</div>
    <div class="axis-label y-axis" id="scatter-y-label">↑ quality</div>
</div>
""")
    html.append('</div>\n')

    # ── Sprite data as JS ──
    html.append('<script>\n')
    html.append('const SPRITES = {\n')
    for seed in range(256):
        w, h = seed_to_dims(seed)
        desc, menace, quality, category = SPRITE_DATA.get(
            seed, ("unclassified", 0.5, 0.5, "abstract"))
        # Generate tiny SVG for scatter dots
        svg_white = sprite_to_svg(seed, "#ffffff", pixel_size=2, bg="#000")
        svg_esc = svg_white.replace("'", "\\'").replace("\n", "")
        html.append(f"  {seed}: {{w:{w},h:{h},menace:{menace},quality:{quality},"
                    f"cat:'{category}',desc:'{desc}',"
                    f"svg:'{svg_esc}'}},\n")
    html.append('};\n')

    html.append(r"""
const grid = document.getElementById('grid');
const cards = [...grid.querySelectorAll('.card')];
const views = {
    grid: document.getElementById('grid-view'),
    page: document.getElementById('page-view'),
    scatter: document.getElementById('scatter-view'),
};

// ── Tab switching ──
document.querySelectorAll('.tab').forEach(tab => {
    tab.addEventListener('click', () => {
        document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
        tab.classList.add('active');
        Object.values(views).forEach(v => v.style.display = 'none');
        views[tab.dataset.view].style.display = 'block';
        if (tab.dataset.view === 'scatter') buildScatter();
    });
});

// ── Filters ──
function applyFilters() {
    const size = document.getElementById('filter-size').value;
    const cat = document.getElementById('filter-cat').value;
    const search = document.getElementById('search').value.toLowerCase();
    const color = document.getElementById('sprite-color').value;
    let vis = 0;
    cards.forEach(c => {
        let show = true;
        if (size !== 'all' && c.dataset.size !== size) show = false;
        if (cat !== 'all' && c.dataset.category !== cat) show = false;
        if (search && !c.dataset.desc.includes(search)) show = false;
        c.classList.toggle('hidden', !show);
        if (show) vis++;
        // Color visibility
        c.querySelectorAll('.spr').forEach(s => {
            if (color === 'all') { s.style.display = ''; }
            else { s.style.display = s.classList.contains('spr-'+color) ? '' : 'none'; }
        });
    });
    document.getElementById('count').textContent = vis + '/256';
}

function applySort() {
    const by = document.getElementById('sort-by').value;
    const sorted = [...cards].sort((a, b) => {
        if (by === 'seed') return a.dataset.seed - b.dataset.seed;
        if (by === 'menace-desc') return b.dataset.menace - a.dataset.menace;
        if (by === 'quality-desc') return b.dataset.quality - a.dataset.quality;
        if (by === 'category') return a.dataset.category.localeCompare(b.dataset.category);
        return 0;
    });
    sorted.forEach(c => grid.appendChild(c));
}

['filter-size','filter-cat','sprite-color','search'].forEach(id =>
    document.getElementById(id).addEventListener(id==='search'?'input':'change', applyFilters));
document.getElementById('sort-by').addEventListener('change', () => { applySort(); applyFilters(); });
applyFilters();

// ── Page view ──
function showPage() {
    const color = document.getElementById('page-color').value;
    const page = document.getElementById('page-num').value;
    document.querySelectorAll('.page-grid').forEach(g => g.style.display = 'none');
    const el = document.getElementById('pg-' + color + '-' + page);
    if (el) el.style.display = 'inline-grid';
}
document.getElementById('page-color').addEventListener('change', showPage);
document.getElementById('page-num').addEventListener('change', showPage);

// ── Scatter view ──
function buildScatter() {
    const container = document.getElementById('scatter');
    const xAttr = document.getElementById('scatter-x').value;
    const yAttr = document.getElementById('scatter-y').value;
    document.getElementById('scatter-x-label').textContent = xAttr + ' →';
    document.getElementById('scatter-y-label').textContent = '↑ ' + yAttr;

    // Clear old dots
    container.querySelectorAll('.scatter-dot').forEach(d => d.remove());

    const size = 800;
    const margin = 30;
    const range = size - 2 * margin;

    for (const [seed, data] of Object.entries(SPRITES)) {
        const xVal = xAttr === 'seed' ? seed / 255 : data[xAttr];
        const yVal = yAttr === 'seed' ? seed / 255 : data[yAttr];

        const x = margin + xVal * range;
        const y = size - margin - yVal * range; // invert Y

        const dot = document.createElement('div');
        dot.className = 'scatter-dot';
        dot.style.left = x + 'px';
        dot.style.top = y + 'px';
        dot.innerHTML = data.svg +
            `<div class="tooltip">#${String(seed).padStart(3,'0')} ${data.desc}<br>` +
            `M:${data.menace} Q:${data.quality} [${data.cat}]</div>`;
        container.appendChild(dot);
    }
}

document.getElementById('scatter-x').addEventListener('change', buildScatter);
document.getElementById('scatter-y').addEventListener('change', buildScatter);
""")

    html.append('</script>\n</body>\n</html>\n')

    with open(OUTPUT, 'w') as f:
        f.write(''.join(html))

    size_kb = os.path.getsize(OUTPUT) / 1024
    print(f"Wrote {OUTPUT} ({size_kb:.0f} KB)")


if __name__ == '__main__':
    main()
