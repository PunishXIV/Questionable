#!/usr/bin/env python3
"""
Rewrites quest path files in place, touching only the text that changes.

    strip-routing  Remove routing that MappingTheRealm now decides: AetheryteShortcut, AethernetShortcut,
                   their SkipConditions, Fly, Land, IgnoreDistanceToObject, StopDistance on steps with a
                   Position, and steps that only routed (Vesper Bay ticket use, position-less "None" teleports).

    v2             quest-v2 layout: "$schema" -> quest-v2.json, every {"X", "Y", "Z"} object -> [x, y, z] (number
                   text kept), and each sequence's most common TerritoryId moved onto the sequence (steps elsewhere
                   keep their own). Files already in v2 are left alone.

    strip-travel   Remove travel steps MappingTheRealm replaces (needs quest-v2 files): walks onto a zone line,
                   walk-only waypoints whose next step is in another zone or whose note only avoids aggro, and
                   door steps into cities, open world or the Gold Saucer. Only when the next step in the sequence
                   has a position (or is an attune step); story-room doors, dialogue doors and sequence ends stay.

    strip-fly      Remove WalkTo waypoints that only run with flying unlocked (skip "Flying: Locked", nothing but a
                   position and a note) when the next step in the same zone has a position.

    strip-implicit Empty sequence 0/255 when its only step is a plain accept/turn-in Questionable derives from game
                   data. --list names the sequences (<path> TAB <sequence>); the list comes from game data (same NPC,
                   within 3 yalms), which this script can't read.

Every edited file is checked to parse to exactly the expected data. Re-runnable after merging from new-main.

Usage:
    python MigratePaths.py strip-routing|v2|strip-travel|strip-fly|strip-implicit [ROOT] [--write] [--list FILE]
"""
from __future__ import annotations

import argparse
import copy
import json
import re
import sys
from pathlib import Path

ROUTING_KEYS = {
    "AetheryteShortcut", "AethernetShortcut", "Fly", "Land", "Sprint", "DisableNavmesh",
    "RestartNavigationIfCancelled", "IgnoreDistanceToObject",
}
ROUTING_SKIPS = {"AetheryteShortcutIf", "AethernetShortcutIf"}
VESPER_BAY_TICKET = 30362


# --- span-aware JSON parsing -------------------------------------------------------------------------------

def _ws(s: str, i: int) -> int:
    while s[i] in " \t\r\n":
        i += 1
    return i


def parse(s: str, i: int = 0) -> tuple[dict, int]:
    """Node: {start, end, members: [(key, key_start, node)] | items: [node]}."""
    i = _ws(s, i)
    start = i
    c = s[i]
    if c == "{":
        members = []
        i = _ws(s, i + 1)
        if s[i] == "}":
            return {"start": start, "end": i + 1, "members": members}, i + 1
        while True:
            key_start = i
            key, i = _string(s, i)
            i = _ws(s, i)
            assert s[i] == ":"
            node, i = parse(s, i + 1)
            members.append((key, key_start, node))
            i = _ws(s, i)
            if s[i] == ",":
                i = _ws(s, i + 1)
                continue
            assert s[i] == "}"
            return {"start": start, "end": i + 1, "members": members}, i + 1
    if c == "[":
        items = []
        i = _ws(s, i + 1)
        if s[i] == "]":
            return {"start": start, "end": i + 1, "items": items}, i + 1
        while True:
            node, i = parse(s, i)
            items.append(node)
            i = _ws(s, i)
            if s[i] == ",":
                i += 1
                continue
            assert s[i] == "]"
            return {"start": start, "end": i + 1, "items": items}, i + 1
    if c == '"':
        _, end = _string(s, i)
        return {"start": start, "end": end}, end
    end = i
    while end < len(s) and s[end] not in ",}] \t\r\n":
        end += 1
    return {"start": start, "end": end}, end


def _string(s: str, i: int) -> tuple[str, int]:
    j = i + 1
    while s[j] != '"':
        j += 2 if s[j] == "\\" else 1
    return json.loads(s[i:j + 1]), j + 1


def removal_ranges(children: list[tuple[int, int]], remove: list[bool]) -> list[tuple[int, int]]:
    """Text ranges that delete the marked children (spans) of one container, keeping commas right."""
    assert not all(remove), "would empty the container"
    ranges = []
    i = 0
    while i < len(children):
        if not remove[i]:
            i += 1
            continue
        j = i
        while j + 1 < len(children) and remove[j + 1]:
            j += 1
        if j + 1 < len(children):
            ranges.append((children[i][0], children[j + 1][0]))
        else:
            ranges.append((children[i - 1][1], children[j][1]))
        i = j + 1
    return ranges


def member_spans(node: dict) -> list[tuple[int, int]]:
    return [(key_start, value["end"]) for _, key_start, value in node["members"]]


def apply(text: str, ranges: list[tuple[int, int]]) -> str:
    for start, end in sorted(ranges, reverse=True):
        text = text[:start] + text[end:]
    return text


# --- strip-routing -----------------------------------------------------------------------------------------

def only_routes(step: dict) -> bool:
    if step.get("ItemId") == VESPER_BAY_TICKET:
        return True
    rest = {k for k in step if not k.startswith("$")} - ROUTING_KEYS - {"TerritoryId", "InteractionType", "Comment"}
    skips = set(step.get("SkipConditions") or {}) - ROUTING_SKIPS - {"StepIf"}
    return (step.get("InteractionType") == "None" and "Position" not in step
            and ("AetheryteShortcut" in step or "AethernetShortcut" in step)
            and rest <= {"SkipConditions"} and not skips)


def step_key_removed(step: dict, key: str) -> bool:
    return key in ROUTING_KEYS or (key == "StopDistance" and "Position" in step)


def strip_step(step: dict) -> dict:
    out = {k: v for k, v in step.items() if not step_key_removed(step, k)}
    if "SkipConditions" in out:
        skips = {k: v for k, v in out["SkipConditions"].items() if k not in ROUTING_SKIPS}
        if skips:
            out["SkipConditions"] = skips
        else:
            del out["SkipConditions"]
    return out


def strip_routing(text: str) -> tuple[str, dict]:
    data = json.loads(text.lstrip("\ufeff"))
    stats = {"keys": 0, "steps": 0, "kept_only_step": 0}
    if "QuestSequence" not in data:
        return text, stats

    root, _ = parse(text, 1 if text.startswith("\ufeff") else 0)
    expected = copy.deepcopy(data)
    ranges = []
    sequences_node = next(n for k, _, n in root["members"] if k == "QuestSequence")
    for seq_data, seq_node, seq_expected in zip(data["QuestSequence"], sequences_node["items"], expected["QuestSequence"]):
        steps_node = next((n for k, _, n in seq_node["members"] if k == "Steps"), None)
        if steps_node is None:
            continue
        steps = seq_data["Steps"]
        drop = [only_routes(s) for s in steps]
        if drop and all(drop):
            drop[0] = False
            stats["kept_only_step"] += 1
        ranges += removal_ranges([(n["start"], n["end"]) for n in steps_node["items"]], drop)
        stats["steps"] += sum(drop)

        kept = []
        for step, step_node, dropped in zip(steps, steps_node["items"], drop):
            if dropped:
                continue
            kept.append(strip_step(step))
            remove = [step_key_removed(step, k) for k, _, _ in step_node["members"]]
            for k, _, value in step_node["members"]:
                if k != "SkipConditions":
                    continue
                skip_remove = [sk in ROUTING_SKIPS for sk, _, _ in value["members"]]
                if skip_remove and all(skip_remove):
                    remove[[m[0] for m in step_node["members"]].index("SkipConditions")] = True
                elif any(skip_remove):
                    ranges += removal_ranges(member_spans(value), skip_remove)
                stats["keys"] += sum(skip_remove)
            stats["keys"] += sum(remove) - sum(1 for (k, _, _), r in zip(step_node["members"], remove)
                                               if r and k == "SkipConditions")
            if any(remove):
                ranges += removal_ranges(member_spans(step_node), remove)
        seq_expected["Steps"] = kept

    if not ranges:
        return text, stats
    new = apply(text, ranges)
    assert json.loads(new.lstrip("\ufeff")) == expected, "edited text doesn't parse to the expected data"
    return new, stats


# --- v2 ----------------------------------------------------------------------------------------------------

SCHEMA_V2 = "https://raw.githubusercontent.com/PunishXIV/Questionable/new-main/QuestPaths/quest-v2.json"


def is_vector(value) -> bool:
    return isinstance(value, dict) and set(value) == {"X", "Y", "Z"}


def sequence_zone(steps: list[dict]) -> int | None:
    zones = [s["TerritoryId"] for s in steps if "TerritoryId" in s]
    if not zones:
        return None
    return max(dict.fromkeys(zones), key=lambda z: (zones.count(z), -zones.index(z)))


def to_v2(value):
    if is_vector(value):
        return [value["X"], value["Y"], value["Z"]]
    if isinstance(value, dict):
        return {k: to_v2(v) for k, v in value.items()}
    if isinstance(value, list):
        return [to_v2(v) for v in value]
    return value


def convert_v2(text: str) -> tuple[str, dict]:
    stats = {"positions": 0, "zones": 0}
    data = json.loads(text.lstrip("\ufeff"))
    if "QuestSequence" not in data:
        return text, stats

    root, _ = parse(text, 1 if text.startswith("\ufeff") else 0)
    newline = "\r\n" if "\r\n" in text else "\n"
    edits: list[tuple[int, int, str]] = []

    def vectors(node: dict, value) -> None:
        if is_vector(value):
            parts = {k: text[n["start"]:n["end"]] for k, _, n in node["members"]}
            edits.append((node["start"], node["end"], f"[{parts['X']}, {parts['Y']}, {parts['Z']}]"))
            stats["positions"] += 1
        elif isinstance(value, dict):
            for (k, _, child), v in zip(node["members"], value.values()):
                vectors(child, v)
        elif isinstance(value, list):
            for child, v in zip(node["items"], value):
                vectors(child, v)

    vectors(root, data)

    expected = to_v2(data)
    for k, _, node in root["members"]:
        if k == "$schema" and data["$schema"] != SCHEMA_V2:
            edits.append((node["start"], node["end"], json.dumps(SCHEMA_V2)))
    expected["$schema"] = SCHEMA_V2 if "$schema" in data else expected.get("$schema")
    if "$schema" not in data:
        del expected["$schema"]

    sequences_node = next(n for k, _, n in root["members"] if k == "QuestSequence")
    for seq_data, seq_node, seq_expected in zip(data["QuestSequence"], sequences_node["items"], expected["QuestSequence"]):
        members = {k: (key_start, n) for k, key_start, n in seq_node["members"]}
        if "TerritoryId" in members or "Steps" not in members or "Sequence" not in members:
            continue
        zone = sequence_zone(seq_data["Steps"])
        if zone is None:
            continue
        stats["zones"] += 1

        key_start, sequence_value = members["Sequence"]
        indent = text[text.rfind("\n", 0, key_start) + 1:key_start]
        edits.append((sequence_value["end"], sequence_value["end"], f",{newline}{indent}\"TerritoryId\": {zone}"))

        for step, step_node in zip(seq_data["Steps"], members["Steps"][1]["items"]):
            remove = [k == "TerritoryId" and step.get("TerritoryId") == zone for k, _, _ in step_node["members"]]
            if any(remove):
                edits += [(a, b, "") for a, b in removal_ranges(member_spans(step_node), remove)]

        reordered = {}
        for k, v in seq_expected.items():
            reordered[k] = v
            if k == "Sequence":
                reordered["TerritoryId"] = zone
        seq_expected.clear()
        seq_expected.update(reordered)
        for step in seq_expected["Steps"]:
            if step.get("TerritoryId") == zone:
                del step["TerritoryId"]

    if not edits:
        return text, stats
    for start, end, replacement in sorted(edits, key=lambda e: (e[0], e[1]), reverse=True):
        text = text[:start] + replacement + text[end:]
    assert json.loads(text.lstrip("\ufeff")) == expected, "edited text doesn't parse to the expected data"
    return text, stats


# --- strip-travel ------------------------------------------------------------------------------------------

# TerritoryIntendedUse 0 (city), 1 (open world) and 23 (Gold Saucer squares), read from the TerritoryType sheet.
# Story rooms (15), duty areas and special zones are left out on purpose: MTR may not know their doors.
# The Gold Saucer itself (144) stays out: before its unlock quest the airship is the only way in.
DOOR_DROP_ZONES = {
    128, 129, 130, 131, 132, 133, 134, 135, 137, 138, 139, 140, 145, 146, 147, 148, 152, 153, 154, 155, 156,
    180, 250, 388, 398, 399, 400, 418, 419, 478, 612, 613, 614, 620, 621, 622, 628, 635, 759, 814, 815, 816, 819,
    958, 959, 960, 962, 963, 1186, 1187, 1191, 1197, 1360,
}
ROUTING_STEP_IF = {
    "InTerritory", "NotInTerritory", "AetheryteUnlocked", "AetheryteLocked", "NearPosition", "NotNearPosition",
    "QuestsCompleted", "CompletionQuestVariablesFlags", "Never",
}
ROUTE_ONLY_KEYS = {"Position", "TerritoryId", "InteractionType", "Comment", "TargetTerritoryId", "StopDistance"}
SELF_ROUTING = {"AttuneAetheryte", "AttuneAethernetShard", "RegisterFreeOrFavoredAetheryte"}
KEEP_NOTE = re.compile(r"door|stair|lift|elevator|jump|navmesh|land|ledge|wall|stuck|trigger|cutscene", re.IGNORECASE)
AGGRO_NOTE = re.compile(r"aggro|combat", re.IGNORECASE)


def _note(step: dict) -> str:
    return " ".join(str(v) for k, v in step.items() if k == "Comment" or k.startswith("$"))


def _routing_skips_only(step: dict) -> bool:
    skips = step.get("SkipConditions")
    if skips is None:
        return True
    return set(skips) == {"StepIf"} and set(skips["StepIf"] or {}) <= ROUTING_STEP_IF


def _keys_only(step: dict, allowed: set[str]) -> bool:
    return all(k in allowed or k.startswith("$") for k in step)


def travel_kind(step: dict, next_step: dict | None, zone: int | None, next_zone: int | None) -> str | None:
    """Why this step can go, or None to keep it."""
    if next_step is None or ("Position" not in next_step and next_step.get("InteractionType") not in SELF_ROUTING):
        return None
    note = _note(step)
    kind = step.get("InteractionType")
    target = step.get("TargetTerritoryId")
    if kind == "WalkTo" and target is not None and target != zone:
        if _keys_only(step, ROUTE_ONLY_KEYS | {"SkipConditions"}) and _routing_skips_only(step) and not KEEP_NOTE.search(note):
            return "zone-line walk"
        return None
    if kind == "WalkTo" and target in (None, zone) and _keys_only(step, ROUTE_ONLY_KEYS) and "Position" in step:
        if next_zone != zone and not KEEP_NOTE.search(note):
            return "waypoint before another zone"
        if AGGRO_NOTE.search(note) and not KEEP_NOTE.search(note):
            return "aggro waypoint"
        return None
    # Both sides: leaving a story room needs MTR to know that room as much as entering it does.
    if kind == "Interact" and target is not None and target != zone and target in DOOR_DROP_ZONES and zone in DOOR_DROP_ZONES:
        if _keys_only(step, ROUTE_ONLY_KEYS | {"DataId", "SkipConditions"}) and _routing_skips_only(step):
            return "door into city/field"
    return None


def fly_kind(step: dict, next_step: dict | None, zone: int | None, next_zone: int | None) -> str | None:
    """Waypoints that only run with flying unlocked steered flights; MappingTheRealm picks its own flight path."""
    if (step.get("InteractionType") == "WalkTo" and "Position" in step
            and _keys_only(step, {"InteractionType", "Position", "TerritoryId", "SkipConditions", "Comment"})
            and step.get("SkipConditions") == {"StepIf": {"Flying": "Locked"}}
            and next_step is not None and "Position" in next_step and zone == next_zone):
        return "flying-only waypoint"
    return None


def strip_travel(text: str, kind_of=travel_kind) -> tuple[str, list[tuple[int, int, str, dict]]]:
    data = json.loads(text.lstrip("\ufeff"))
    removed: list[tuple[int, int, str, dict]] = []
    if "QuestSequence" not in data:
        return text, removed

    root, _ = parse(text, 1 if text.startswith("\ufeff") else 0)
    expected = copy.deepcopy(data)
    ranges = []
    sequences_node = next(n for k, _, n in root["members"] if k == "QuestSequence")
    for seq, seq_node, seq_expected in zip(data["QuestSequence"], sequences_node["items"], expected["QuestSequence"]):
        steps_node = next((n for k, _, n in seq_node["members"] if k == "Steps"), None)
        if steps_node is None:
            continue
        steps = seq["Steps"]
        zone_of = [s.get("TerritoryId", seq.get("TerritoryId")) for s in steps]
        kinds = [kind_of(s, steps[i + 1] if i + 1 < len(steps) else None, zone_of[i],
                         zone_of[i + 1] if i + 1 < len(steps) else None) for i, s in enumerate(steps)]
        drop = [k is not None for k in kinds]
        if not any(drop):
            continue
        ranges += removal_ranges([(n["start"], n["end"]) for n in steps_node["items"]], drop)
        removed += [(seq["Sequence"], i, k, s) for i, (s, k) in enumerate(zip(steps, kinds)) if k]
        seq_expected["Steps"] = [s for s, d in zip(seq_expected["Steps"], drop) if not d]

    if not ranges:
        return text, removed
    new = apply(text, ranges)
    assert json.loads(new.lstrip("\ufeff")) == expected, "edited text doesn't parse to the expected data"
    return new, removed


def strip_implicit(text: str, numbers: set[int]) -> str:
    """Empty the listed sequences (0 and/or 255): Questionable then accepts or turns in at the quest giver."""
    data = json.loads(text.lstrip("﻿"))
    root, _ = parse(text, 1 if text.startswith("﻿") else 0)
    newline = "\r\n" if "\r\n" in text else "\n"
    expected = copy.deepcopy(data)
    sequences_node = next(n for k, _, n in root["members"] if k == "QuestSequence")
    edits = []
    for seq, node, seq_expected in zip(data["QuestSequence"], sequences_node["items"], expected["QuestSequence"]):
        if seq["Sequence"] not in numbers:
            continue
        assert set(seq) <= {"Sequence", "TerritoryId", "Steps"} and len(seq["Steps"]) == 1, f"sequence {seq['Sequence']} isn't plain"
        indent = text[text.rfind("\n", 0, node["start"]) + 1:node["start"]]
        replacement = (f"{{{newline}{indent}  \"Sequence\": {seq['Sequence']},{newline}"
                       f"{indent}  \"Steps\": []{newline}{indent}}}")
        edits.append((node["start"], node["end"], replacement))
        seq_expected.clear()
        seq_expected.update({"Sequence": seq["Sequence"], "Steps": []})
    for start, end, replacement in sorted(edits, reverse=True):
        text = text[:start] + replacement + text[end:]
    assert json.loads(text.lstrip("﻿")) == expected, "edited text doesn't parse to the expected data"
    return text


def run_strip_implicit(root: Path, write: bool, listing: Path) -> int:
    wanted: dict[str, set[int]] = {}
    for line in listing.read_text(encoding="utf-8").splitlines():
        if line.strip():
            rel, number = line.split("\t")
            wanted.setdefault(rel, set()).add(int(number))
    counts = {0: 0, 255: 0}
    errors = 0
    for rel, numbers in sorted(wanted.items()):
        path = root / rel
        raw = path.read_bytes().decode("utf-8")
        try:
            new = strip_implicit(raw, numbers)
        except (AssertionError, json.JSONDecodeError, StopIteration) as e:
            print(f"[skip] {rel}: {e}", file=sys.stderr)
            errors += 1
            continue
        for number in numbers:
            counts[number] += 1
        if write:
            path.write_bytes(new.encode("utf-8"))
    print(f"{'Emptied' if write else 'Would empty'} {counts[0]} accept and {counts[255]} turn-in sequences "
          f"in {len(wanted) - errors} files; errors: {errors}")
    return 1 if errors else 0


def run_strip_travel(root: Path, write: bool, listing: Path | None, kind_of=travel_kind) -> int:
    files = errors = 0
    kinds: dict[str, int] = {}
    rows = []
    for path in sorted(root.rglob("*.json")):
        if path.parent == root or "obj" in path.relative_to(root).parts:
            continue
        raw = path.read_bytes().decode("utf-8")
        try:
            new, removed = strip_travel(raw, kind_of)
        except (AssertionError, json.JSONDecodeError, StopIteration) as e:
            print(f"[skip] {path}: {e}", file=sys.stderr)
            errors += 1
            continue
        if not removed:
            continue
        files += 1
        for sequence, index, kind, step in removed:
            kinds[kind] = kinds.get(kind, 0) + 1
            p = step.get("Position") or ["", "", ""]
            x, y, z = (p["X"], p["Y"], p["Z"]) if isinstance(p, dict) else p
            rows.append(f"{path.relative_to(root)}\t{sequence}\t{index}\t{step.get('TerritoryId', '')}\t{x}, {y}, {z}\t{kind}")
        if write:
            path.write_bytes(new.encode("utf-8"))
    if listing:
        listing.write_text("\n".join(rows) + "\n", encoding="utf-8")
    summary = ", ".join(f"{v} {k}" for k, v in sorted(kinds.items()))
    print(f"{'Removed' if write else 'Would remove'} {sum(kinds.values())} steps in {files} files ({summary}); errors: {errors}")
    return 1 if errors else 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("command", choices=["strip-routing", "v2", "strip-travel", "strip-fly", "strip-implicit"])
    ap.add_argument("root", nargs="?", default=Path(__file__).resolve().parent, type=Path)
    ap.add_argument("--write", action="store_true", help="write changes (default: dry run)")
    ap.add_argument("--list", type=Path, help="strip-travel/strip-fly: write a TSV of every removed step; "
                                              "strip-implicit: read the TSV of <path>\\t<sequence> to empty")
    args = ap.parse_args()

    root = args.root.resolve()
    if args.command == "v2":
        return run_v2(root, args.write)
    if args.command == "strip-travel":
        return run_strip_travel(root, args.write, args.list)
    if args.command == "strip-fly":
        return run_strip_travel(root, args.write, args.list, fly_kind)
    if args.command == "strip-implicit":
        if args.list is None:
            ap.error("strip-implicit needs --list with the qualifying sequences")
        return run_strip_implicit(root, args.write, args.list)
    totals = {"files": 0, "keys": 0, "steps": 0, "kept_only_step": 0, "errors": 0}
    for path in sorted(root.rglob("*.json")):
        if path.parent == root or "obj" in path.relative_to(root).parts:
            continue
        raw = path.read_bytes().decode("utf-8")
        try:
            new, stats = strip_routing(raw)
        except (AssertionError, json.JSONDecodeError, StopIteration) as e:
            print(f"[skip] {path}: {e}", file=sys.stderr)
            totals["errors"] += 1
            continue
        if new == raw:
            continue
        totals["files"] += 1
        for k in ("keys", "steps", "kept_only_step"):
            totals[k] += stats[k]
        if args.write:
            path.write_bytes(new.encode("utf-8"))

    print(f"{'Changed' if args.write else 'Would change'} {totals['files']} files: {totals['keys']} keys and "
          f"{totals['steps']} routing-only steps removed; {totals['kept_only_step']} kept as a sequence's only step; "
          f"errors: {totals['errors']}")
    return 1 if totals["errors"] else 0


def run_v2(root: Path, write: bool) -> int:
    files = positions = zones = errors = 0
    for path in sorted(root.rglob("*.json")):
        if path.parent == root or "obj" in path.relative_to(root).parts:
            continue
        raw = path.read_bytes().decode("utf-8")
        try:
            new, stats = convert_v2(raw)
        except (AssertionError, json.JSONDecodeError, StopIteration) as e:
            print(f"[skip] {path}: {e}", file=sys.stderr)
            errors += 1
            continue
        if new == raw:
            continue
        files += 1
        positions += stats["positions"]
        zones += stats["zones"]
        if write:
            path.write_bytes(new.encode("utf-8"))
    print(f"{'Changed' if write else 'Would change'} {files} files: {positions} positions to arrays, "
          f"{zones} sequences got a TerritoryId; errors: {errors}")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
