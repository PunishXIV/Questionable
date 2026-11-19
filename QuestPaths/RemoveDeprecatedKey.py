#!/usr/bin/env python3
"""
Remove the named deprecated key from JSON files in subdirectories.
If removing it would leave the parent object empty, the parent is removed too,
recursively, fixing any trailing comma on the preceding sibling.

Usage:
    python RemoveDeprecatedKey.py [ROOT] [KEY] [--write] [--verify]

Defaults:
    ROOT = current directory. Files directly in ROOT are skipped;
    only files under subdirectories are processed.
    KEY = named key e.g InSameTerritory

Authored with LLM assistance, changes must be reviewed and owned by a human.
Initial version reviewed and owned by @alydevs
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

VALIDATOR_PROJECT = Path(__file__).resolve().parent.parent / "QuestPaths.JsonValidator" / "QuestPaths.JsonValidator.csproj"

KEY = '"InSameTerritory"'


def _split_newline(line: str) -> tuple[str, str]:
    """Return (content_without_trailing_newline, newline_suffix)."""
    for nl in ("\r\n", "\n", "\r"):
        if line.endswith(nl):
            return line[: -len(nl)], nl
    return line, ""


def _strip_trailing_comma(line: str) -> str:
    body, nl = _split_newline(line)
    stripped = body.rstrip()
    if stripped.endswith(","):
        trailing_ws = body[len(stripped):]  # whitespace between last non-ws and newline
        return stripped[:-1] + trailing_ws + nl
    return line


def _prev_nonblank(lines: list[str], i: int) -> int:
    """Return the greatest index <= i whose line is not blank, or -1."""
    while i >= 0 and lines[i].strip() == "":
        i -= 1
    return i


def _next_nonblank(lines: list[str], i: int) -> int:
    """Return the smallest index >= i whose line is not blank, or len(lines)."""
    while i < len(lines) and lines[i].strip() == "":
        i += 1
    return i


def _delete_range(lines: list[str], start: int, end: int) -> None:
    """
    Delete lines[start..end] inclusive. If that leaves the enclosing
    object/array empty (the nearest non-blank line before `start` ends with
    '{' or '[' and the nearest non-blank line after `end` starts with '}' or
    ']'), cascade by expanding the range to include the wrapping open/close
    lines and any blank lines between them, and recurse. Otherwise perform
    the deletion and, if we removed the last child of a container, strip a
    dangling trailing comma from the preceding sibling.

    Blank lines between the deletion region and the following non-blank line
    are swallowed by the deletion so we don't leave orphan blank lines inside
    a parent container after a cascade bottoms out.
    """
    prev_idx = _prev_nonblank(lines, start - 1)
    next_idx = _next_nonblank(lines, end + 1)

    prev_body, _ = _split_newline(lines[prev_idx]) if prev_idx >= 0 else ("", "")
    next_body, _ = _split_newline(lines[next_idx]) if next_idx < len(lines) else ("", "")
    prev_rstripped = prev_body.rstrip()
    next_lstripped = next_body.lstrip()

    prev_opens = prev_rstripped.endswith("{") or prev_rstripped.endswith("[")
    next_closes = next_lstripped.startswith("}") or next_lstripped.startswith("]")

    if prev_opens and next_closes:
        # Cascade: the enclosing container will be empty once we remove the
        # current range together with its opening and closing lines. Expand
        # the range to swallow those (and any blanks between them) and
        # recurse so the check runs again at the next level up.
        _delete_range(lines, prev_idx, next_idx)
        return

    # Bottom out: physically delete the range, including trailing blanks up
    # to (but not including) the next non-blank line.
    del lines[start:next_idx]

    # If the next non-blank line is now a container close, we removed the
    # last child; strip a trailing comma from the previous non-blank sibling.
    j = _next_nonblank(lines, start)
    if j < len(lines):
        after_lstripped = _split_newline(lines[j])[0].lstrip()
        if after_lstripped.startswith("}") or after_lstripped.startswith("]"):
            k = _prev_nonblank(lines, start - 1)
            if k >= 0:
                lines[k] = _strip_trailing_comma(lines[k])


def _process_text(text: str, strip_blank_lines: bool = False) -> str:
    # Preserve BOM if present.
    bom = ""
    if text.startswith("\ufeff"):
        bom = "\ufeff"
        text = text[1:]

    lines = text.splitlines(keepends=True)

    if strip_blank_lines:
        lines = [ln for ln in lines if ln.strip() != ""]

    # Repeatedly find the next line containing the deprecated key and remove it.
    # Every occurrence in the observed data is either its own line or an inline
    # form whose entire line represents a single key of an enclosing object;
    # in both cases deleting the whole line is the correct primitive, and any
    # cascading is handled by _delete_line.
    i = 0
    while i < len(lines):
        if KEY in lines[i]:
            _delete_range(lines, i, i)
            # Don't advance; the line at i is new content that may also match
            # (unlikely, but cheap to re-check).
            continue
        i += 1

    return bom + "".join(lines)


def iter_target_files(root: Path):
    root = root.resolve()
    for p in root.rglob("*.json"):
        if p.parent.resolve() == root:
            continue  # skip files directly in root
        yield p


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("root", nargs="?", default=".", type=Path)
    ap.add_argument("key", nargs="?", default="InSameTerritory", type=str)
    ap.add_argument("--write", action="store_true",
                    help="Write changes back to disk (default: dry-run).")
    ap.add_argument("--verify", action="store_true",
                    help="json.loads the result and refuse to write if invalid.")
    ap.add_argument("--strip-blank-lines", action="store_true",
                    help="Also strip all blank lines from files that are "
                         "modified. JSON has no semantic use for blank lines "
                         "and they can defeat line-based neighbour checks.")
    args = ap.parse_args()

    global KEY
    KEY = f'"{args.key}"'

    changed = 0
    scanned = 0
    errors = 0

    for path in iter_target_files(args.root):
        try:
            raw = path.read_bytes().decode("utf-8")
        except UnicodeDecodeError as e:
            print(f"[skip] {path}: {e}", file=sys.stderr)
            errors += 1
            continue

        scanned += 1
        if KEY not in raw:
            continue

        new = _process_text(raw, strip_blank_lines=args.strip_blank_lines)
        if new == raw:
            continue

        if args.verify:
            try:
                # Strip BOM before parsing; json.loads rejects a leading BOM.
                json.loads(new.lstrip("\ufeff"))
            except json.JSONDecodeError as e:
                print(f"[INVALID after edit, skipped] {path}: {e}", file=sys.stderr)
                errors += 1
                continue

        changed += 1
        action = "write" if args.write else "would change"
        print(f"[{action}] {path}")

        if args.write:
            # Preserve original newline style: splitlines(keepends=True) already did.
            # Write bytes to avoid any platform newline translation.
            path.write_bytes(new.encode("utf-8"))

    mode = "wrote" if args.write else "would change"
    print(f"\nScanned {scanned} files; {mode} {changed}; errors: {errors}")

    # After any write, run the JSON validator test suite so users of this
    # script always verify their changes against the project's validators.
    if args.write and changed > 0:
        if not VALIDATOR_PROJECT.is_file():
            print(f"\n[validator] project not found at {VALIDATOR_PROJECT}; skipping.",
                  file=sys.stderr)
            return 1
        print(f"\n[validator] dotnet test {VALIDATOR_PROJECT}")
        result = subprocess.run(
            ["dotnet", "test", str(VALIDATOR_PROJECT), "--nologo"],
        )
        if result.returncode != 0:
            print("[validator] tests FAILED; review the changes above.",
                  file=sys.stderr)
            return result.returncode
        print("[validator] tests passed.")

    return 0 if errors == 0 else 1


if __name__ == "__main__":
    sys.exit(main())