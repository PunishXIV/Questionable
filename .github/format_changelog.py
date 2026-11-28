#!/usr/bin/env python3
"""Groups CHANGELOG.md lines ("- Feature: text -Kage") into New / Changed / Fixed / Removed for release posts."""
import re
import sys

GROUPS = [("Feature", "New"), ("Change", "Changed"), ("Bug fix", "Fixed"), ("Removed", "Removed")]
LINE = re.compile(r"^-\s*(Feature|Change|Bug fix|Removed):\s*(.*?)(?:\s+-(\w+))?\s*$")


def grouped(text):
    sections = {title: [] for _, title in GROUPS}
    other = []
    authors = []
    for raw in text.splitlines():
        line = raw.strip()
        if not line:
            continue
        match = LINE.match(line)
        if not match:
            other.append(line)
            continue
        kind, body, author = match.groups()
        sections[dict(GROUPS)[kind]].append((body, author))
        if author and author not in authors:
            authors.append(author)

    # One author gets a single credit at the end; several keep theirs on each line.
    several = len(authors) > 1
    parts = []
    for title, items in sections.items():
        if items:
            lines = [f"- {body} ({author})" if several and author else f"- {body}" for body, author in items]
            parts.append(f"**{title}**\n" + "\n".join(lines))
    if other:
        parts.append("\n".join(other))
    if len(authors) == 1:
        parts.append("by " + authors[0])
    return "\n\n".join(parts)


if __name__ == "__main__":
    path = sys.argv[1] if len(sys.argv) > 1 else "CHANGELOG.md"
    with open(path, encoding="utf-8-sig") as file:
        print(grouped(file.read()))
