#!/usr/bin/env python3
"""Issue dashboard — watches issues.jsonl and displays changes in real time."""

import json
import os
import sys
import time
from collections import defaultdict
from datetime import datetime

try:
    from rich.console import Console
    from rich.layout import Layout
    from rich.live import Live
    from rich.panel import Panel
    from rich.table import Table
    from rich.text import Text
except ImportError:
    print("This tool requires 'rich'. Install with:")
    print("  pip install rich")
    sys.exit(1)

# Fields to track for changes (skip description — too noisy)
TRACKED_FIELDS = ("status", "priority", "type", "summary", "file", "updated")

STATUS_COLORS = {
    "open": "yellow",
    "in-progress": "cyan",
    "done": "green",
    "wont-fix": "dim",
    "future": "blue",
}

STATUS_ORDER = ["in-progress", "open", "future", "done", "wont-fix"]

PRIORITY_SORT = {"critical": 0, "high": 1, "medium": 2, "low": 3}


def load_issues(path):
    """Load issues.jsonl, return dict keyed by id."""
    issues = {}
    try:
        with open(path) as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                try:
                    issue = json.loads(line)
                    issues[issue["id"]] = issue
                except (json.JSONDecodeError, KeyError):
                    continue
    except FileNotFoundError:
        pass
    return issues


def get_mtime(path):
    try:
        return os.stat(path).st_mtime
    except FileNotFoundError:
        return 0


def diff_issues(old, new):
    """Return list of change dicts."""
    changes = []
    now = datetime.now().strftime("%H:%M:%S")

    # New issues
    for id_ in sorted(set(new) - set(old)):
        changes.append({
            "time": now,
            "id": id_,
            "summary": new[id_].get("summary", ""),
            "kind": "NEW",
            "detail": f'status={new[id_].get("status", "?")}  priority={new[id_].get("priority", "?")}',
        })

    # Removed issues (distinguish done from truly removed)
    for id_ in sorted(set(old) - set(new)):
        old_status = old[id_].get("status", "")
        changes.append({
            "time": now,
            "id": id_,
            "summary": old[id_].get("summary", ""),
            "kind": "DONE" if old_status == "done" else "REMOVED",
            "detail": "",
        })

    # Changed issues
    for id_ in sorted(set(old) & set(new)):
        field_changes = []
        for field in TRACKED_FIELDS:
            ov = str(old[id_].get(field, ""))
            nv = str(new[id_].get(field, ""))
            if ov != nv:
                field_changes.append(f"{field}: {ov} → {nv}")
        if field_changes:
            changes.append({
                "time": now,
                "id": id_,
                "summary": new[id_].get("summary", ""),
                "kind": "CHANGED",
                "detail": "  ".join(field_changes),
            })

    return changes


def build_summary_bar(issues):
    """Top panel: counts by status + timestamp."""
    counts = defaultdict(int)
    for iss in issues.values():
        counts[iss.get("status", "unknown")] += 1

    parts = [f"[bold white]{len(issues)}[/] total"]
    for status in STATUS_ORDER:
        if counts[status]:
            color = STATUS_COLORS.get(status, "white")
            parts.append(f"[{color}]{status}: {counts[status]}[/]")

    # Any statuses not in STATUS_ORDER
    for status in sorted(counts):
        if status not in STATUS_ORDER:
            parts.append(f"{status}: {counts[status]}")

    parts.append(f"[dim]updated {datetime.now().strftime('%H:%M:%S')}[/]")
    return Panel(Text.from_markup("   ".join(parts)), title="Issues", border_style="blue")


def build_changes_panel(change_log):
    """Middle panel: recent changes feed."""
    if not change_log:
        return Panel("[dim]Watching for changes…[/]", title="Recent Changes", border_style="magenta")

    table = Table(show_header=True, header_style="bold", expand=True, box=None)
    table.add_column("Time", width=8, no_wrap=True)
    table.add_column("#", width=4, no_wrap=True)
    table.add_column("Event", width=8, no_wrap=True)
    table.add_column("Summary", ratio=2, no_wrap=True)
    table.add_column("Details", ratio=3)

    for entry in change_log[:30]:
        kind = entry["kind"]
        if kind == "NEW":
            style = "bold green"
            label = "NEW"
        elif kind == "DONE":
            style = "green"
            label = "DONE"
        elif kind == "REMOVED":
            style = "bold red"
            label = "REMOVED"
        else:
            # Find the most interesting status change for coloring
            style = "white"
            label = "CHANGED"
            if "status:" in entry["detail"]:
                for s, c in STATUS_COLORS.items():
                    if f"→ {s}" in entry["detail"]:
                        style = c
                        break

        summary = entry["summary"]
        if len(summary) > 50:
            summary = summary[:47] + "…"

        table.add_row(
            entry["time"],
            str(entry["id"]),
            Text(label, style=style),
            summary,
            Text(entry["detail"], style="dim" if kind != "NEW" else style),
        )

    return Panel(table, title="Recent Changes", border_style="magenta")


def build_status_table(issues):
    """Bottom panel: all issues grouped by status."""
    table = Table(show_header=True, header_style="bold", expand=True, box=None)
    table.add_column("#", width=4, no_wrap=True)
    table.add_column("Status", width=12, no_wrap=True)
    table.add_column("Pri", width=6, no_wrap=True)
    table.add_column("Type", width=10, no_wrap=True)
    table.add_column("Summary", ratio=3)
    table.add_column("File", ratio=1, no_wrap=True, style="dim")

    # Sort: status order, then priority, then id
    sorted_issues = sorted(
        issues.values(),
        key=lambda i: (
            STATUS_ORDER.index(i.get("status", "open")) if i.get("status", "open") in STATUS_ORDER else 99,
            PRIORITY_SORT.get(i.get("priority", "medium"), 9),
            i.get("id", 0),
        ),
    )

    # Only show non-done issues by default (done list is huge)
    active = [i for i in sorted_issues if i.get("status") != "done"]
    done_count = len(sorted_issues) - len(active)

    for iss in active:
        status = iss.get("status", "?")
        color = STATUS_COLORS.get(status, "white")
        summary = iss.get("summary", "")
        if len(summary) > 60:
            summary = summary[:57] + "…"
        table.add_row(
            str(iss.get("id", "?")),
            Text(status, style=color),
            iss.get("priority", "?"),
            iss.get("type", "?"),
            summary,
            iss.get("file", ""),
        )

    if done_count:
        table.add_row("", "", "", "", f"[dim]+ {done_count} done issues (hidden)[/]", "")

    return Panel(table, title="Active Issues", border_style="green")


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else "issues.jsonl"
    if not os.path.exists(path):
        print(f"File not found: {path}")
        sys.exit(1)

    console = Console()
    issues = load_issues(path)
    last_mtime = get_mtime(path)
    change_log = []

    def build_display():
        layout = Layout()
        layout.split_column(
            Layout(name="summary", size=3),
            Layout(name="changes", ratio=2),
            Layout(name="status", ratio=3),
        )
        layout["summary"].update(build_summary_bar(issues))
        layout["changes"].update(build_changes_panel(change_log))
        layout["status"].update(build_status_table(issues))
        return layout

    console.print(f"[bold]Watching[/] {os.path.abspath(path)}")
    console.print("[dim]Press Ctrl+C to quit[/]\n")

    with Live(build_display(), console=console, refresh_per_second=1, screen=True) as live:
        while True:
            time.sleep(1)
            mtime = get_mtime(path)
            if mtime != last_mtime:
                last_mtime = mtime
                new_issues = load_issues(path)
                changes = diff_issues(issues, new_issues)
                if changes:
                    change_log = changes + change_log
                    change_log = change_log[:50]  # cap history
                issues = new_issues
            live.update(build_display())


if __name__ == "__main__":
    main()
