#!/usr/bin/env python3
"""
gen-lib-readme.py — regenerate lib/README.md from lib/*.fs sources.

Walks every lib/*.fs file, extracts:
  - Leading \\-comment block (description, "Provides:", "Requires:")
  - Word definitions: : ... ;, CODE, KCODE, VARIABLE, CONSTANT
  - Stack-effect comments (from the same line for colon defs, or a
    preceding "\\ name ( ... -- ... )" comment for CODE/KCODE words)

Filters out underscore-prefixed names (the "_foo" private convention).
Writes a single combined Markdown file with a TOC and one section per
library file.
"""

import json
import html
import re
import sys
from pathlib import Path

ROOT      = Path(__file__).parent.parent
LIB_DIR   = ROOT / "lib"
OUT_PATH  = LIB_DIR / "README.md"
METRICS   = ROOT / "build" / "lib-metrics.json"
REFERENCE = ROOT / "reference.html"

# `\ word — short description.`  Em-dash (U+2014) or whitespace-isolated `--`.
# Name starts with a letter/digit so we skip box-drawing divider lines like
# `\ ── snd-zap ( ... ) ──────`.
DESC_RE = re.compile(
    r"^\s*\\\s+([A-Za-z0-9][\w\-/?!.]*)\s+(?:—|--)\s+(\S.*?)\s*$"
)

# reference.html section: <p class="lib-path">INCLUDE lib/NAME.fs</p>
LIB_PATH_RE = re.compile(r'<p class="lib-path">INCLUDE lib/([^<\s]+)</p>')

# reference.html row: <td class="word-name">N</td><td...>STACK or VALUE</td><td>DESC</td>
# The middle cell is `class="stack-effect"` for words, or unclassed `<code>$XX</code>`
# for constants — accept either.
HTML_ROW_RE = re.compile(
    r'<td class="word-name">([^<]+)</td>\s*'
    r'<td[^>]*>.*?</td>\s*'
    r'<td>(.*?)</td>',
    re.DOTALL,
)

DEFN_RE = re.compile(r"^(:|CODE|KCODE|VARIABLE)\s+(\S+)(.*)?$")
# Forth's CONSTANT is postfix: `<value> CONSTANT <name>`. Match anywhere on a line.
CONST_RE = re.compile(r"^\s*\S+\s+CONSTANT\s+(\S+)\s*(.*)?$")
STACK_RE = re.compile(r"\(\s*([^()]*?)--\s*([^()]*?)\s*\)")

KIND_LABEL = {
    ":":        "colon",
    "CODE":     "CODE",
    "KCODE":    "KCODE",
    "VARIABLE": "var",
    "CONSTANT": "const",
}


def parse_header(lines):
    """Return (short_description, provides, requires).

    short_description is the first non-`Provides:`/`Requires:` comment line,
    with any leading "filename.fs — " stripped (the heading shows the
    filename already, so the prefix is redundant in the description).

    Multi-line `Provides:` / `Requires:` blocks are joined by spaces.
    The header ends at the first blank or non-`\\` line.
    """
    short = ""
    provides, requires, footprint = [], [], []
    current = None  # most recent labelled block, for continuation joining
    started = False
    for line in lines:
        s = line.rstrip()
        if not s:
            if started:
                break
            continue
        if not s.startswith("\\"):
            break
        started = True
        # Drop leading "\\" and at most one space.
        body = s[1:]
        if body.startswith(" "):
            body = body[1:]
        if not body.strip():
            current = None
            continue
        is_continuation = body.startswith((" ", "\t"))
        content = body.strip()
        low = content.lower()
        if low.startswith("provides:"):
            provides.append(content[9:].strip())
            current = "provides"
        elif low.startswith("requires:"):
            requires.append(content[9:].strip())
            current = "requires"
        elif is_continuation and current == "provides" and provides:
            provides[-1] += " " + content
        elif is_continuation and current == "requires" and requires:
            requires[-1] += " " + content
        elif low.startswith("footprint:"):
            footprint.append(content[10:].strip())
            current = "footprint"
        elif is_continuation and current == "footprint" and footprint:
            footprint[-1] += " " + content
        elif not short:
            # Treat the first plain line as the short description.
            short = content
            current = None
        # else: drop later description lines — they're file-internal docs.
    # Strip "filename.fs — " or "filename.fs - " prefix if the short line
    # opens with the filename plus an em-dash separator.
    short = re.sub(r"^[A-Za-z0-9._/-]+\s*[—-]\s*", "", short)
    return short, provides, requires, " ".join(footprint)


def format_stack(before, after):
    """Render a stack effect compactly. ('', '') -> '( -- )'."""
    b, a = before.strip(), after.strip()
    if not b and not a:
        return "( -- )"
    if not b:
        return f"( -- {a} )"
    if not a:
        return f"( {b} -- )"
    return f"( {b} -- {a} )"


def find_code_stack(lines, idx, name):
    """Find a stack effect for the CODE word at `lines[idx]`.

    Looks in two places:
      - up to 8 preceding lines, for a Forth comment `\\ ... NAME ( a -- b ) ...`
        (works around dividers like `\\ ── NAME ( a -- b ) ───`).
      - up to 4 following lines, for a `;;; ( a -- b )` bare-stack comment
        inside the CODE body (a common kernel-style convention).

    Forth names contain hyphens, slashes, `?`, `!` — Python's `\\b` doesn't
    cope with those at the boundaries, so we use explicit name-char lookarounds.
    """
    namechar = r"[A-Za-z0-9_\-/\?\!]"
    name_pat = re.compile(
        r"(?<!" + namechar + r")" + re.escape(name) +
        r"(?!" + namechar + r")\s*\(\s*([^()]*?)--\s*([^()]*?)\s*\)")
    bare_pat = re.compile(r"^\s*;{2,}\s*\(\s*([^()]*?)--\s*([^()]*?)\s*\)")

    for line in lines[max(0, idx - 8):idx]:
        if not line.lstrip().startswith("\\"):
            continue
        m = name_pat.search(line)
        if m:
            return format_stack(m.group(1), m.group(2))

    for line in lines[idx + 1:idx + 5]:
        m = bare_pat.match(line)
        if m:
            return format_stack(m.group(1), m.group(2))

    return ""


def parse_file(path):
    text = path.read_text()
    lines = text.split("\n")
    short, provides, requires, footprint = parse_header(lines)

    words = []
    for i, line in enumerate(lines):
        # Try the standard prefix-keyword form first.
        m = DEFN_RE.match(line)
        if m:
            kind, name, rest = m.group(1), m.group(2), m.group(3) or ""
            if name.startswith("_"):
                continue
            stack = ""
            if kind in (":", "CODE", "KCODE"):
                # Try same-line stack effect first (works for `: name ( a -- b )`
                # and `CODE name  \\ ( a -- b )` on the same line).
                sm = STACK_RE.search(rest)
                if sm:
                    stack = format_stack(sm.group(1), sm.group(2))
            if not stack and kind in (":", "CODE", "KCODE"):
                # Fall back to preceding `\\` block or following `;;;` comment.
                stack = find_code_stack(lines, i, name)
            words.append((name, stack, KIND_LABEL[kind]))
            continue

        # Forth's `<value> CONSTANT <name>` postfix form.
        m = CONST_RE.match(line)
        if m:
            name = m.group(1)
            if name.startswith("_"):
                continue
            words.append((name, "", KIND_LABEL["CONSTANT"]))

    return short, provides, requires, footprint, words


def slug(filename):
    """Approximate the GFM auto-anchor for a heading containing this filename."""
    s = filename.lower()
    s = re.sub(r"[^a-z0-9-]", "", s)
    return s


def normalise_desc(s):
    """Compare descriptions ignoring whitespace + trailing punctuation."""
    return re.sub(r"\s+", " ", s.strip()).rstrip(".!? ").lower()


def scan_source_descriptions(path, defined_names):
    """Return {lower_name: description} for every `\\ name — desc` line in `path`
    where `name` matches a word actually defined in this file.

    Lines containing a stack-effect comment (`( a -- b )`) are skipped — the
    `--` there is not a description separator.
    """
    out = {}
    for line in path.read_text().splitlines():
        if "(" in line and "--" in line:
            continue
        m = DESC_RE.match(line)
        if not m:
            continue
        name = m.group(1).lower()
        if name in defined_names:
            out[name] = m.group(2).strip()
    return out


def parse_reference_html():
    """Parse reference.html → {(lib_file, lower_name): description}.

    Each row belongs to the library named by the nearest
    `INCLUDE lib/<file>` line above it, so two libraries that define the
    same word (font5x7.fs and font-art.fs both have init-font) keep their
    own descriptions. Rows outside any library section (kernel
    primitives) get lib_file None.

    Strips inline tags to plain text; truncates very long descriptions to
    their first sentence (full prose stays in the HTML reference itself).
    """
    if not REFERENCE.exists():
        return {}
    out = {}
    text = REFERENCE.read_text()
    libs = [(m.start(), m.group(1)) for m in LIB_PATH_RE.finditer(text)]
    for m in HTML_ROW_RE.finditer(text):
        lib = None
        for pos, name_ in libs:
            if pos > m.start():
                break
            lib = name_
        name = html.unescape(m.group(1).strip()).lower()
        desc = re.sub(r"<[^>]+>", "", m.group(2))
        # Collapse whitespace and decode the few HTML entities the reference uses.
        desc = re.sub(r"\s+", " ", desc).strip()
        desc = (desc.replace("&amp;", "&").replace("&lt;", "<").replace("&gt;", ">")
                    .replace("&mdash;", "—").replace("&ndash;", "–")
                    .replace("&hellip;", "…").replace("&nbsp;", " ")
                    .replace("&quot;", '"').replace("&#39;", "'"))
        if len(desc) > 120:
            cut = desc.find(". ")
            if 0 < cut <= 120:
                desc = desc[:cut + 1]
            else:
                desc = desc[:117].rstrip() + "..."
        out[(lib, name)] = desc
    return out


def load_descriptions(files_and_words):
    """Build {lower_name: (description, source)} for every public lib word.

    `files_and_words` is a list of `(Path, [(name, ...)])` tuples — one per lib
    file. Source comments (`\\ name — desc`) take precedence over reference.html.

    Returns (descriptions, drift_warnings, missing) where:
      - drift_warnings: list of (name, source_desc, ref_desc) for words where
        the two sources disagree (after whitespace/punctuation normalisation).
      - missing: list of word names with no description from either source.
    """
    descriptions = {}
    for path, words in files_and_words:
        defined = {n.lower() for (n, _stack, _kind) in words}
        for name, desc in scan_source_descriptions(path, defined).items():
            descriptions[(path.name, name)] = (desc, "source")

    # A word in reference.html's section for its own file; else the first
    # row of that name anywhere (kernel primitives described in a library's
    # section, or a library with no section of its own).
    ref = parse_reference_html()
    anywhere = {}
    for (lib, name), ref_desc in ref.items():
        anywhere.setdefault(name, ref_desc)
    drift = []
    for path, words in files_and_words:
        for name, _stack, _kind in words:
            key = (path.name, name.lower())
            ref_desc = ref.get(key)
            if key in descriptions:
                src_desc, _ = descriptions[key]
                if ref_desc and normalise_desc(src_desc) != normalise_desc(ref_desc):
                    drift.append((f"{path.name} {name}", src_desc, ref_desc))
            elif ref_desc:
                descriptions[key] = (ref_desc, "reference.html")
            elif name.lower() in anywhere:
                descriptions[key] = (anywhere[name.lower()], "reference.html")

    # Missing = any defined word with no description from either source.
    missing = sorted(f"{path.name} {name}" for path, words in files_and_words
                     for name, _stack, _kind in words
                     if (path.name, name.lower()) not in descriptions)
    return descriptions, drift, missing


def load_metrics():
    """Load build/lib-metrics.json and return a flat {lower(word): entry} map.

    Returns empty dict (with a warning) if the file is missing — callers
    should still produce a valid README without bytes/cycles columns.
    """
    if not METRICS.exists():
        print(f"  warning: {METRICS.relative_to(ROOT)} missing — "
              f"run `make lib-readme` (which builds it) for byte/cycle columns.",
              file=sys.stderr)
        return {}
    data = json.loads(METRICS.read_text())
    flat = {}
    for section in ("forth_words", "code_words", "kcode_words", "variables"):
        for k, v in data.get(section, {}).items():
            flat[k.lower()] = v
    return flat


def fmt_cycles(entry):
    """Format a metrics entry's cycle range. ('' if entry has no cycle data)."""
    if "cy_min" not in entry:
        return ""
    cmin, cmax = entry["cy_min"], entry["cy_max"]
    if cmin == 0 and cmax == 0:
        return ""
    return f"{cmin}" if cmin == cmax else f"{cmin}-{cmax}"


def fmt_notes(entry):
    return ", ".join(entry.get("notes", []) or [])


def render():
    files = sorted(LIB_DIR.glob("*.fs"))
    metrics = load_metrics()
    parsed = [(f, parse_file(f)) for f in files]
    files_and_words = [(f, parsed_data[4]) for f, parsed_data in parsed]
    descriptions, drift, missing = load_descriptions(files_and_words)

    if drift:
        print(f"  warning: {len(drift)} word(s) where source comment differs from reference.html:",
              file=sys.stderr)
        for name, src, ref in drift:
            print(f"    {name}", file=sys.stderr)
            print(f"      source:          {src}", file=sys.stderr)
            print(f"      reference.html: {ref}", file=sys.stderr)
        print("    (source wins; update reference.html to match, or fix the source comment)",
              file=sys.stderr)

    if missing:
        print(f"  note: {len(missing)} word(s) without descriptions:",
              file=sys.stderr)
        for chunk_start in range(0, len(missing), 8):
            print("    " + ", ".join(missing[chunk_start:chunk_start + 8]), file=sys.stderr)
        print("    (add `\\ name -- short description` near the definition, "
              "or document in reference.html)", file=sys.stderr)
    out = []
    out.append("# `lib/`: Shared Forth Libraries")
    out.append("")
    out.append(
        "> *Auto-generated by `tools/gen-lib-readme.py`. "
        "Do not hand-edit. Update each file's `.fs` header and "
        "re-run the generator (`make lib-readme` or "
        "`python3 tools/gen-lib-readme.py`).*")
    out.append("")
    out.append(
        "Each table lists the public words from one file. Words whose "
        "names begin with `_` are treated as private and omitted.")
    out.append("")
    out.append(
        "Each file shows a description list (what each word does) followed by "
        "a cost table (stack effect, kind, bytes, cycles). Descriptions are "
        "sourced from a `\\ name -- short description` comment placed within "
        "six lines of the word's definition (preferred), falling back to "
        "`reference.html` for words documented there. When both exist "
        "and disagree, the source comment wins and the generator emits a "
        "docs-drift warning.")
    out.append("")
    out.append("---")
    out.append("")
    out.append("## Files")
    out.append("")
    for f in files:
        out.append(f"- [`{f.name}`](#{slug(f.name)})")
    out.append("")
    out.append("---")
    out.append("")

    for f, (short, provides, requires, footprint, words) in parsed:
        out.append(f"## `{f.name}`")
        out.append("")
        if short:
            out.append(short)
            out.append("")
        if provides:
            out.append(f"**Provides:** {', '.join(provides)}")
            out.append("")
        if requires:
            out.append(f"**Requires:** {', '.join(requires)}")
            out.append("")
        if footprint:
            out.append(f"**Footprint:** {footprint}")
            out.append("")
        if words:
            # Description list first — what each word does, plus any flow-control notes.
            for name, _stack, _kind in words:
                desc, _src = descriptions.get((f.name, name.lower()), ("", ""))
                notes = fmt_notes(metrics.get(name.lower(), {}))
                bits = []
                if desc:
                    bits.append(desc)
                if notes:
                    bits.append(f"*({notes})*")
                tail = ": " + " ".join(bits) if bits else ""
                out.append(f"- **`{name}`**{tail}")
            out.append("")
            # Cost table second — narrow, scannable for size/speed comparisons.
            out.append("| Word | Stack | Kind | Bytes | Cycles |")
            out.append("|------|-------|------|-------|--------|")
            for name, stack, kind in words:
                stack_md = f"`{stack}`" if stack else ""
                entry = metrics.get(name.lower(), {})
                size = entry.get("bytes", "")
                cycles = fmt_cycles(entry)
                out.append(f"| `{name}` | {stack_md} | {kind} | {size} | {cycles} |")
            out.append("")
        else:
            out.append("*No public words.*")
            out.append("")
        out.append("---")
        out.append("")

    return "\n".join(out)


def main():
    if not LIB_DIR.is_dir():
        sys.exit(f"lib/ not found at {LIB_DIR}")
    text = render()
    OUT_PATH.write_text(text)
    print(f"Wrote {OUT_PATH.relative_to(Path.cwd())} ({len(text)} bytes)")


if __name__ == "__main__":
    main()
