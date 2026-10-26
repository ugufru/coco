#!/usr/bin/env python3
"""Build issues.html, the browsable view of issues.jsonl and roadmap.jsonl.

Both .jsonl files stay the source of truth. This only ever reads them.

    render  <issues.jsonl> <out.html> [roadmap.jsonl] [--title=NAME]

The page fetches the .jsonl files itself and renders in the browser, so edits
to either show up on reload with no rebuild. Browsers block that fetch under
file://, so a snapshot of both is inlined as a fallback and the page says so
when it falls back. Rendering lives only in the page's JavaScript; this script
just fills the shell.

roadmap.jsonl carries only rank, id and why. Summary and status are joined from
issues.jsonl in the browser, so the two cannot drift: an entry naming an issue
that is missing, or one already closed, is flagged on the page rather than
quietly going stale.
"""

import json
import sys
from pathlib import Path


def load(path):
    if not path or not Path(path).exists():
        return []
    return [json.loads(l) for l in Path(path).read_text().splitlines() if l.strip()]


def render(issues, roadmap, title="Issues"):
    data = json.dumps({"issues": issues, "roadmap": roadmap}, ensure_ascii=False)
    # </script> inside a description would close the data island early.
    page = TEMPLATE.replace("__SNAPSHOT__", data.replace("</", "<\\/"))
    return page.replace("__TITLE__", html_escape(title))


def html_escape(s):
    return (s.replace("&", "&amp;").replace("<", "&lt;")
             .replace(">", "&gt;").replace('"', "&quot;"))


TEMPLATE = r"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>__TITLE__</title>
<style>
:root {
  color-scheme: light dark;
  --bg: #fbfaf7; --panel: #fff; --ink: #1b1a17; --dim: #6b6862; --line: #e3ded4;
  --accent: #8a4b2a; --accent-ink: #fff;
  --open: #b4541f; --inprog: #8a6d1f; --done: #3f6b3a; --defer: #4a5a75; --wont: #6b6862;
  --high: #a8322a; --med: #8a6d1f; --low: #6b6862;
}
@media (prefers-color-scheme: dark) {
  :root {
    --bg: #16151a; --panel: #1e1d23; --ink: #ece8e1; --dim: #9b968d; --line: #32303a;
    --accent: #d98a5a; --accent-ink: #16151a;
    --open: #e08b52; --inprog: #d4b95e; --done: #8fc182; --defer: #93aad6; --wont: #9b968d;
    --high: #e5776c; --med: #d4b95e; --low: #9b968d;
  }
}
* { box-sizing: border-box; }
body {
  margin: 0; background: var(--bg); color: var(--ink);
  font: 15px/1.55 ui-sans-serif, -apple-system, "Segoe UI", Roboto, sans-serif;
}
.wrap { max-width: 60rem; margin: 0 auto; padding: 2rem 1.25rem 4rem; }
h1 { font-size: 1.5rem; margin: 0 0 .2rem; letter-spacing: -.01em; }
h2 { font-size: .78rem; text-transform: uppercase; letter-spacing: .08em;
  color: var(--dim); margin: 0 0 .6rem; }
.lede { color: var(--dim); margin: 0 0 1.5rem; font-size: .9rem; }
.note { font-size: .8rem; color: var(--dim); background: var(--panel);
  border: 1px solid var(--line); border-left: 3px solid var(--med);
  border-radius: 6px; padding: .5rem .7rem; margin: 0 0 1.25rem; }
.roadmap { margin: 0 0 1.75rem; }
.plan { list-style: none; margin: 0; padding: 0; counter-reset: step; }
.step { display: flex; gap: .7rem; padding: .55rem .7rem; border: 1px solid var(--line);
  border-radius: 8px; background: var(--panel); margin-bottom: .35rem; }
.step .rank { font-variant-numeric: tabular-nums; color: var(--dim); font-size: .85rem; }
.step .what { flex: 1; }
.step a { color: inherit; font-weight: 550; text-decoration: none; }
.step a:hover { text-decoration: underline; text-decoration-color: var(--accent); }
.step .why { display: block; color: var(--dim); font-size: .84rem; margin-top: .15rem; }
.step.bad { border-color: var(--high); }
.step .warn { color: var(--high); font-size: .78rem; }
.controls {
  position: sticky; top: 0; z-index: 5; background: var(--bg);
  padding: .75rem 0; border-bottom: 1px solid var(--line); margin-bottom: 1.25rem;
}
#q {
  width: 100%; padding: .6rem .75rem; font: inherit; color: var(--ink);
  background: var(--panel); border: 1px solid var(--line); border-radius: 8px;
}
#q:focus { outline: 2px solid var(--accent); outline-offset: 1px; }
.chip.th { background: color-mix(in srgb, var(--accent) 18%, transparent); color: var(--fg); }
.morebtn { font-size: .72rem; background: none; color: var(--dim); cursor: pointer;
  border: 1px dashed var(--line); border-radius: 999px; padding: .2rem .55rem; }
.morebtn:hover { color: var(--fg); }
.pills { display: flex; flex-wrap: wrap; gap: .35rem; margin-top: .6rem; align-items: center; }
.pills .label { font-size: .72rem; text-transform: uppercase; letter-spacing: .06em;
  color: var(--dim); margin-right: .15rem; }
.pill {
  font: inherit; font-size: .8rem; padding: .2rem .6rem; cursor: pointer;
  background: var(--panel); color: var(--ink);
  border: 1px solid var(--line); border-radius: 999px;
}
.pill:hover { border-color: var(--accent); }
.pill[aria-pressed="true"] { background: var(--accent); color: var(--accent-ink); border-color: var(--accent); }
.pill .n { opacity: .6; font-variant-numeric: tabular-nums; }
.bar { display: flex; gap: .75rem; align-items: center; margin-top: .6rem;
  font-size: .8rem; color: var(--dim); }
.bar button { font: inherit; background: none; border: 0; color: var(--accent);
  cursor: pointer; padding: 0; text-decoration: underline; }
#count { margin-right: auto; font-variant-numeric: tabular-nums; }
.card { background: var(--panel); border: 1px solid var(--line);
  border-radius: 10px; margin-bottom: .5rem; overflow: hidden; }
.card:target { outline: 2px solid var(--accent); }
.row {
  width: 100%; display: flex; gap: .7rem; align-items: baseline; text-align: left;
  font: inherit; color: inherit; background: none; border: 0;
  padding: .7rem .9rem; cursor: pointer;
}
.row:hover { background: color-mix(in srgb, var(--accent) 7%, transparent); }
.id { color: var(--dim); font-variant-numeric: tabular-nums; font-size: .85rem; min-width: 2.2rem; }
.summary { flex: 1; font-weight: 550; }
.chips { display: flex; gap: .3rem; flex-wrap: wrap; }
.chip { font-size: .7rem; text-transform: uppercase; letter-spacing: .04em;
  padding: .1rem .45rem; border-radius: 4px; border: 1px solid currentColor; white-space: nowrap; }
.st-open { color: var(--open); } .st-in-progress { color: var(--inprog); }
.st-done { color: var(--done); } .st-deferred { color: var(--defer); }
.st-wontfix { color: var(--wont); }
.pr-high { color: var(--high); } .pr-medium { color: var(--med); } .pr-low { color: var(--low); }
.ty { color: var(--dim); }
.caret { width: .55rem; height: .55rem; border-right: 2px solid var(--dim);
  border-bottom: 2px solid var(--dim); transform: rotate(45deg); transition: transform .15s; }
.row[aria-expanded="true"] .caret { transform: rotate(-135deg); }
.body { padding: 0 .9rem .9rem; border-top: 1px solid var(--line); }
.desc { max-width: 62ch; }
.desc p { margin: .8rem 0; }
code { font: .87em ui-monospace, SFMono-Regular, Menlo, monospace;
  background: color-mix(in srgb, var(--ink) 7%, transparent);
  padding: .05em .3em; border-radius: 3px; }
footer { display: flex; gap: .4rem; flex-wrap: wrap; align-items: center;
  margin-top: .9rem; padding-top: .7rem; border-top: 1px dashed var(--line); }
.tag { font-size: .72rem; color: var(--dim); border: 1px solid var(--line);
  border-radius: 999px; padding: .05rem .45rem; }
.meta { margin-left: auto; font-size: .75rem; color: var(--dim); }
.empty { color: var(--dim); padding: 2rem 0; text-align: center; }
@media (max-width: 40rem) {
  .row { flex-wrap: wrap; } .summary { flex-basis: 100%; order: 2; }
  .chips { order: 3; } .meta { margin-left: 0; }
}
</style>
</head>
<body>
<div class="wrap">
<h1>__TITLE__</h1>
<p class="lede" id="lede">Loading...</p>
<p class="note" id="note" hidden></p>

<section class="roadmap" id="roadmap" hidden>
  <h2>Roadmap</h2>
  <ol class="plan" id="plan"></ol>
</section>

<div class="controls">
  <input id="q" type="search" placeholder="Search summaries and descriptions..." autocomplete="off">
  <div class="pills"><span class="label">status</span><span id="f-status"></span></div>
  <div class="pills"><span class="label">theme</span><span id="f-theme"></span></div>
  <div class="pills"><span class="label">type</span><span id="f-type"></span></div>
  <div class="pills"><span class="label">tag</span><span id="f-tag"></span></div>
  <div class="bar">
    <span id="count"></span>
    <button id="expand">expand all</button>
    <button id="collapse">collapse all</button>
    <button id="reset">clear filters</button>
  </div>
</div>

<main id="list"></main>
<p class="empty" id="empty" hidden>Nothing matches those filters.</p>
</div>

<script id="snapshot" type="application/json">__SNAPSHOT__</script>
<script>
(function () {
  var STATUS_ORDER = ['open', 'in-progress', 'done', 'deferred', 'wontfix'];
  var PRIORITY_ORDER = ['high', 'medium', 'low'];
  var TAG_PILL_LIMIT = 20;   // the rest hide behind a "+N more" toggle
  // Themes are the coarse grouping: one per issue, from a fixed vocabulary,
  // unlike the free-form tags. Order is the order they are worked in.
  var THEME_ORDER = ['trust', 'kit', 'input', 'experience', 'platform', 'storage', 'devices'];
  var LIVE = ['open', 'in-progress'];
  var KEY = 'issues-view:' + location.pathname;
  // Longest alternative first: "jsonl" must win over "json".
  var PATH_RE = /\b((?:[\w.-]+\/)*[\w.-]+\.(?:jsonl|json|md|html|css|js|ts|tsx|jsx|py|rb|go|rs|java|c|h|cpp|sh|toml|ya?ml|cfg|ini|txt|bas|dsk|png))(:\d+(?:-\d+)?)?/g;

  var issues = [];
  var active = { status: [], type: [], tag: [], theme: [] };
  var q = document.getElementById('q');
  var list = document.getElementById('list');
  var count = document.getElementById('count');
  var empty = document.getElementById('empty');

  function esc(s) {
    return String(s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }

  function rank(v, order) {
    var i = order.indexOf(v);
    return i === -1 ? order.length : i;
  }

  function markup(text) {
    return String(text).split('\n\n').map(function (para) {
      return '<p>' + esc(para).replace(PATH_RE, '<code>$&</code>').replace(/\n/g, '<br>') + '</p>';
    }).join('');
  }

  function summaryOf(i) { return i.summary || i.title || '(untitled)'; }
  function tagsOf(i) { return i.tags || i.labels || []; }

  function card(i) {
    var status = i.status || '?', priority = i.priority || '?', type = i.type || '?';
    var el = document.createElement('article');
    el.className = 'card';
    el.id = 'i' + i.id;
    el.dataset.status = status;
    el.dataset.type = type;
    el.dataset.tags = tagsOf(i).join(' ');
    el.dataset.theme = i.theme || '';
    el.innerHTML =
      '<header><button class="row" aria-expanded="false">' +
      '<span class="id">#' + esc(i.id) + '</span>' +
      '<span class="summary">' + esc(summaryOf(i)) + '</span>' +
      '<span class="chips">' +
        '<span class="chip st-' + esc(status) + '">' + esc(status) + '</span>' +
        '<span class="chip pr-' + esc(priority) + '">' + esc(priority) + '</span>' +
        '<span class="chip ty">' + esc(type) + '</span>' +
        (i.theme ? '<span class="chip th">' + esc(i.theme) + '</span>' : '') +
      '</span><span class="caret" aria-hidden="true"></span></button></header>' +
      '<div class="body" hidden><div class="desc">' +
      markup(i.description || '(no description)') + '</div><footer>' +
      tagsOf(i).map(function (t) { return '<span class="tag">' + esc(t) + '</span>'; }).join('') +
      '<span class="meta"><code>' + esc(i.file || '-') + '</code> &middot; created ' +
      esc(i.created || '?') + ' &middot; updated ' + esc(i.updated || '?') +
      '</span></footer></div>';
    el.querySelector('.row').addEventListener('click', function () {
      setOpen(el, el.querySelector('.body').hidden);
    });
    return el;
  }

  function setOpen(el, open) {
    el.querySelector('.row').setAttribute('aria-expanded', open);
    el.querySelector('.body').hidden = !open;
  }

  // The roadmap stores only rank, id and why. Everything shown here is joined
  // from the tracker, so an entry pointing at a missing or finished issue is
  // visibly wrong rather than quietly stale.
  function drawRoadmap(plan, byId) {
    if (!plan.length) return;
    var host = document.getElementById('plan');
    plan.slice().sort(function (a, b) { return (a.rank || 0) - (b.rank || 0); })
      .forEach(function (e) {
        var issue = byId[e.id];
        var li = document.createElement('li');
        li.className = 'step';
        var head, warn = '';
        if (!issue) {
          head = '#' + esc(e.id) + ' is not in the tracker';
          warn = 'no such issue';
          li.className += ' bad';
        } else {
          head = '<a href="#i' + esc(e.id) + '">#' + esc(e.id) + ' ' +
                 esc(summaryOf(issue)) + '</a> <span class="chip st-' +
                 esc(issue.status) + '">' + esc(issue.status) + '</span>';
          if (LIVE.indexOf(issue.status) === -1) {
            warn = 'already ' + issue.status + ', take it off the roadmap';
            li.className += ' bad';
          }
        }
        li.innerHTML = '<span class="rank">' + esc(e.rank || '?') + '.</span>' +
          '<span class="what">' + head +
          '<span class="why">' + esc(e.why || '') + '</span>' +
          (warn ? '<span class="warn">' + esc(warn) + '</span>' : '') + '</span>';
        var link = li.querySelector('a');
        if (link) {
          link.addEventListener('click', function () {
            var target = document.getElementById('i' + e.id);
            if (target) { target.hidden = false; setOpen(target, true); }
          });
        }
        host.appendChild(li);
      });
    document.getElementById('roadmap').hidden = false;
  }

  function pills(host, filter, values, counts, limit) {
    var hidden = (limit && values.length > limit) ? values.length - limit : 0;
    document.getElementById(host).innerHTML = values.map(function (v, idx) {
      var extra = hidden && idx >= limit;
      return '<button class="pill' + (extra ? ' extra' : '') + '"' +
        (extra ? ' hidden' : '') +
        ' data-filter="' + filter + '" data-value="' + esc(v) + '">' +
        esc(v) + (counts ? ' <span class="n">' + counts[v] + '</span>' : '') + '</button>';
    }).join('') + (hidden
      ? '<button class="morebtn" data-host="' + host + '">+' + hidden + ' more</button>'
      : '');
  }

  function save() {
    try {
      localStorage.setItem(KEY, JSON.stringify({ active: active, q: q.value }));
    } catch (e) {}
  }

  function apply() {
    var needle = q.value.trim().toLowerCase();
    var shown = 0;
    [].forEach.call(list.children, function (el) {
      var ok =
        (!active.status.length || active.status.indexOf(el.dataset.status) !== -1) &&
        (!active.type.length || active.type.indexOf(el.dataset.type) !== -1) &&
        (!active.theme.length || active.theme.indexOf(el.dataset.theme) !== -1) &&
        (!active.tag.length || active.tag.some(function (t) {
          return el.dataset.tags.split(' ').indexOf(t) !== -1;
        })) &&
        (!needle || el.textContent.toLowerCase().indexOf(needle) !== -1);
      el.hidden = !ok;
      if (ok) shown++;
    });
    empty.hidden = shown !== 0;
    count.textContent = shown + ' of ' + issues.length + ' shown';
    [].forEach.call(document.querySelectorAll('.pill'), function (p) {
      p.setAttribute('aria-pressed', active[p.dataset.filter].indexOf(p.dataset.value) !== -1);
    });
    save();
  }

  function draw(data, plan, note) {
    issues = data.slice().sort(function (a, b) {
      return rank(a.status, STATUS_ORDER) - rank(b.status, STATUS_ORDER) ||
             rank(a.priority, PRIORITY_ORDER) - rank(b.priority, PRIORITY_ORDER) ||
             (a.id || 0) - (b.id || 0);
    });

    var counts = {}, byId = {};
    STATUS_ORDER.forEach(function (s) { counts[s] = 0; });
    issues.forEach(function (i) {
      counts[i.status] = (counts[i.status] || 0) + 1;
      byId[i.id] = i;
    });

    list.innerHTML = '';
    issues.forEach(function (i) { list.appendChild(card(i)); });
    drawRoadmap(plan || [], byId);

    pills('f-status', 'status', STATUS_ORDER.filter(function (s) { return counts[s]; }), counts);
    pills('f-type', 'type', unique(issues.map(function (i) { return i.type || '?'; })));

    var themeCounts = {};
    issues.forEach(function (i) {
      if (i.theme) themeCounts[i.theme] = (themeCounts[i.theme] || 0) + 1;
    });
    var themes = THEME_ORDER.filter(function (t) { return themeCounts[t]; })
      .concat(Object.keys(themeCounts).filter(function (t) {
        return THEME_ORDER.indexOf(t) === -1;
      }).sort());
    document.getElementById('f-theme').parentNode.hidden = !themes.length;
    pills('f-theme', 'theme', themes, themeCounts);
    // A project can accumulate hundreds of tags, most of them used once.
    // Showing every one buries the results, so lead with the most used and
    // keep the long tail behind a toggle.
    var tagCounts = {};
    issues.forEach(function (i) {
      tagsOf(i).forEach(function (t) { tagCounts[t] = (tagCounts[t] || 0) + 1; });
    });
    var tagNames = Object.keys(tagCounts).sort(function (a, b) {
      return tagCounts[b] - tagCounts[a] || (a < b ? -1 : a > b ? 1 : 0);
    });
    pills('f-tag', 'tag', tagNames, tagCounts, TAG_PILL_LIMIT);

    [].forEach.call(document.querySelectorAll('.pill'), function (p) {
      p.addEventListener('click', function () {
        var l = active[p.dataset.filter], i = l.indexOf(p.dataset.value);
        if (i === -1) l.push(p.dataset.value); else l.splice(i, 1);
        apply();
      });
    });

    [].forEach.call(document.querySelectorAll('.morebtn'), function (b) {
      b.addEventListener('click', function () {
        var host = document.getElementById(b.dataset.host);
        var extras = host.querySelectorAll('.pill.extra');
        var show = extras.length && extras[0].hidden;
        [].forEach.call(extras, function (p) { p.hidden = !show; });
        b.textContent = show ? 'fewer' : '+' + extras.length + ' more';
      });
    });

    var open = (counts.open || 0) + (counts['in-progress'] || 0);
    document.getElementById('lede').innerHTML =
      issues.length + ' issues, ' + open + ' of them still open' +
      ((plan && plan.length) ? ', ' + plan.length + ' on the roadmap' : '') +
      '. Read from <code>issues.jsonl</code>.';
    if (note) {
      var n = document.getElementById('note');
      n.textContent = note;
      n.hidden = false;
    }

    try {
      var s = JSON.parse(localStorage.getItem(KEY) || '{}');
      if (s.active) active = s.active;
      if (s.q) q.value = s.q;
    } catch (e) {}

    apply();

    if (location.hash) {
      var target = document.querySelector(location.hash);
      if (target && target.classList.contains('card')) {
        target.hidden = false;
        setOpen(target, true);
        target.scrollIntoView();
      }
    }
  }

  function unique(a) {
    return a.filter(function (v, i) { return a.indexOf(v) === i; }).sort();
  }

  q.addEventListener('input', apply);
  document.getElementById('reset').addEventListener('click', function () {
    active = { status: [], type: [], tag: [], theme: [] };
    q.value = '';
    apply();
  });
  document.getElementById('expand').addEventListener('click', function () {
    [].forEach.call(list.children, function (c) { if (!c.hidden) setOpen(c, true); });
  });
  document.getElementById('collapse').addEventListener('click', function () {
    [].forEach.call(list.children, function (c) { setOpen(c, false); });
  });
  document.addEventListener('keydown', function (e) {
    if (e.key === '/' && document.activeElement !== q) { e.preventDefault(); q.focus(); }
    if (e.key === 'Escape' && document.activeElement === q) { q.value = ''; apply(); }
  });

  function lines(text) {
    return text.split('\n').filter(function (l) { return l.trim(); }).map(JSON.parse);
  }

  function grab(name) {
    return fetch(name, { cache: 'no-store' }).then(function (r) {
      if (!r.ok) throw new Error('HTTP ' + r.status);
      return r.text();
    }).then(lines);
  }

  // The live files first. Browsers refuse a file:// fetch, so fall back to the
  // snapshot baked in at build time and say which one is on screen. A missing
  // roadmap.jsonl is not an error: the section just stays hidden.
  grab('issues.jsonl')
    .then(function (data) {
      grab('roadmap.jsonl')
        .catch(function () { return []; })
        .then(function (plan) { draw(data, plan); });
    })
    .catch(function () {
      var snap = JSON.parse(document.getElementById('snapshot').textContent);
      draw(snap.issues, snap.roadmap,
        'Showing the snapshot saved when this page was last built, because the browser would ' +
        'not read the .jsonl files from disk. To see them live, serve the directory ' +
        '(python3 -m http.server) and open it over http, or rebuild this page.');
    });
})();
</script>
</body>
</html>
"""


def main(argv):
    title = "Issues"
    args = []
    for a in argv[1:]:
        if a.startswith("--title="):
            title = a.split("=", 1)[1]
        else:
            args.append(a)

    if len(args) in (3, 4) and args[0] == "render":
        issues = load(args[1])
        roadmap = load(args[3] if len(args) == 4 else None)
        Path(args[2]).write_text(render(issues, roadmap, title))
        print("built %s (%d issues, %d roadmap entries in the fallback snapshot)"
              % (args[2], len(issues), len(roadmap)))
        return 0

    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
