#!/usr/bin/env python3
"""
update_quest_checked.py

Reads a JSON array of quest completion entries and updates the LastChecked
field in each matching quest file found in a directory tree.
"""

import argparse
import json
import shutil
import sys
from pathlib import Path
from datetime import datetime


def find_quest_file(quest_id: str, search_dir: Path) -> tuple[Path | None, bool]:
    """
    Search recursively for a quest file.

    Returns (path, exact_match) where exact_match is False if only a
    prefix match was found. Returns (None, False) if nothing was found.
    """
    exact_name = f"{quest_id}.json"

    # First pass: exact match
    for candidate in search_dir.rglob(f"{exact_name}"):
        return candidate, True

    # Second pass: prefix match (filename starts with quest_id)
    for candidate in search_dir.rglob("*.json"):
        if candidate.name.startswith(quest_id):
            return candidate, False

    return None, False


def process_entry(entry: dict, search_dir: Path, username: str, backup_suffix: str | None, dry_run: bool = False) -> bool:
    """
    Process a single quest completion entry.
    Returns True on success, False on failure.
    """
    quest_id = entry.get("Quest")
    last_checked = entry.get("LastChecked")

    if not quest_id or not last_checked:
        print(f"[WARN] Skipping malformed entry (missing Quest or LastChecked): {entry}", file=sys.stderr)
        return False

    path, exact = find_quest_file(quest_id, search_dir)

    if path is None:
        print(f"[WARN] No file found for quest: {quest_id}", file=sys.stderr)
        return False

    if not exact:
        print(f"[WARN] Exact match not found for '{quest_id}'; using '{path.name}' (prefix match)")

    # Optionally back up the file before modifying
    if backup_suffix:
        backup_path = path.with_name(path.name + backup_suffix)
        shutil.copy2(path, backup_path)

    try:
        with path.open("r", encoding="utf-8-sig") as f:
            data = f.read()
    except (OSError) as e:
        print(f"[ERROR] Could not read '{path}': {e}", file=sys.stderr)
        return False

    LastChecked = '"LastChecked": {"Username": "%s", "Date": "%s"},\n  ' % (username, last_checked)
    
    if '"LastChecked":' in data:
        before, date = data.split('"LastChecked":',1)
        date = date.split('"Date":',1)[1].split('"',1)[1].split('"',1)[0]
        if date == last_checked or datetime.strptime(date,"%Y-%m-%d") > datetime.strptime(last_checked,"%Y-%m-%d"):
            print(f"[WARN] {date} >= {last_checked}, skipping: '{quest_id}'", file=sys.stderr)
            return True
    else:
        before = data.split('"QuestSequence":')[0]
    after = data.split('"QuestSequence":')[1]
    output = before + LastChecked + '"QuestSequence":' + after

    if not dry_run:
        try:
            with path.open("w", encoding="utf-8-sig") as f:
                f.write(output)
        except OSError as e:
            print(f"[ERROR] Could not write '{path}': {e}", file=sys.stderr)
            return False

    print(f"[OK] Updated '{path}' with Username:'{username}' Date:'{last_checked}'" + (" (sim)" if dry_run else ""))
    return True


def main():
    parser = argparse.ArgumentParser(
        description="Update LastChecked in quest JSON files from a completion log."
    )
    parser.add_argument(
        "input",
        type=Path,
        help="Path to the JSON completion log (array of {Quest, LastChecked} objects).",
    )
    parser.add_argument(
        "-d", "--directory",
        type=Path,
        default=Path("."),
        metavar="DIR",
        help="Directory to search for quest files (default: current directory).",
    )
    parser.add_argument(
        "-u", "--username",
        default="Anonymous",
        metavar="NAME",
        help="Username to write into LastChecked (default: Anonymous).",
    )
    parser.add_argument(
        "-i", "--backup-suffix",
        default=None,
        metavar="SUFFIX",
        dest="backup_suffix",
        help="If set, back up each file with this suffix before modifying (e.g. .bak), like sed -i.bak.",
    )
    parser.add_argument(
        "-s", "--dry-run",
        action="store_true",
        help="If set, no changes will occur."
    )
    args = parser.parse_args()

    if not args.input.is_file():
        print(f"[ERROR] Input file not found: {args.input}", file=sys.stderr)
        sys.exit(1)

    # If the user didn't explicitly supply a username, derive one from the
    # input filename — unless it's the default log name, in which case keep
    # "Anonymous" so generic runs don't accidentally stamp a log filename.
    if args.username == "Anonymous" and args.input.stem != "QuestCompletionLog":
        args.username = args.input.stem
        print(f"[INFO] No username supplied; using '{args.username}' (derived from input filename).")

    if args.dry_run:
        print(f"[INFO] Dry run enabled", file=sys.stderr)

    if not args.directory.is_dir():
        print(f"[ERROR] Search directory not found: {args.directory}", file=sys.stderr)
        sys.exit(1)

    try:
        with args.input.open("r", encoding="utf-8-sig") as f:
            entries = json.load(f)
    except (json.JSONDecodeError, OSError) as e:
        print(f"[ERROR] Could not read input file: {e}", file=sys.stderr)
        sys.exit(1)

    if not isinstance(entries, list):
        print("[ERROR] Input file must contain a JSON array.", file=sys.stderr)
        sys.exit(1)

    ok = sum(
        process_entry(entry, args.directory, args.username, args.backup_suffix, args.dry_run)
        for entry in entries
    )
    total = len(entries)
    print(f"\nDone: {ok}/{total} entries updated.")
    if ok < total:
        sys.exit(1)
    elif ok == total and ok != 0:
        if not args.dry_run:
            with args.input.open("w", encoding="utf-8-sig") as f:
                f.write("[]")


if __name__ == "__main__":
    main()