#!/usr/bin/env python3
"""Normalize entity names in canonical JSON files.

Usage:
    python3 scripts/normalize_canonical_names.py              # process all data/canonical/*.json
    python3 scripts/normalize_canonical_names.py --dry-run    # print changes, do not write
    python3 scripts/normalize_canonical_names.py --file tce.json  # single file
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

CANONICAL_DIR = Path("data/canonical")

SMALL_WORDS = frozenset([
    "a", "an", "the", "and", "but", "or", "for", "nor",
    "on", "at", "to", "by", "in", "of", "up", "as", "with",
])

SPLIT_WORD_RE = re.compile(r"(?:^| )[a-z] [a-z]")
NOISE_RE = re.compile(r"\.{3,}")


def dnd_title_case(name: str) -> str:
    """Convert an all-caps D&D name to proper title case."""
    def convert_word(word: str, is_first: bool) -> str:
        low = word.lower()
        if is_first or low not in SMALL_WORDS:
            cap = low.capitalize()
            # Fix 'S => 's  (Python capitalize produces 'S after apostrophe)
            cap = re.sub(r"'[A-Z]", lambda m: m.group(0).lower(), cap)
            return cap
        return low

    parts = name.split(" ")
    result = []
    for i, part in enumerate(parts):
        if "-" in part:
            sub = part.split("-")
            result.append("-".join(convert_word(s, i == 0 and j == 0) for j, s in enumerate(sub)))
        else:
            result.append(convert_word(part, i == 0))
    return " ".join(result)


def count_case_alternations(word: str) -> int:
    letters = [c for c in word if c.isalpha()]
    return sum(1 for i in range(1, len(letters)) if letters[i].isupper() != letters[i - 1].isupper())


def has_ocr_artifacts(name: str) -> bool:
    """Return True if the name has OCR-quality problems."""
    if not name:
        return False
    if len(name) > 1 and name == name.upper() and any(c.isalpha() for c in name):
        return True
    if SPLIT_WORD_RE.search(name.lower()):
        return True
    if NOISE_RE.search(name):
        return True
    for word in re.split(r"[\s\-]", name):
        if count_case_alternations(word) >= 2:
            return True
    return False


def normalize_entity(entity: dict) -> dict:
    """Normalize a single entity dict. Modifies in-place and returns it."""
    name = entity.get("name", "")
    if not isinstance(name, str) or not name:
        entity.setdefault("needsReview", False)
        return entity

    is_all_caps = name == name.upper() and any(c.isalpha() for c in name) and len(name) > 1
    has_other_artifacts = (
        SPLIT_WORD_RE.search(name.lower()) is not None
        or NOISE_RE.search(name) is not None
        or any(count_case_alternations(w) >= 2 for w in re.split(r"[\s\-]", name))
    )

    if is_all_caps and not has_other_artifacts:
        entity["name"] = dnd_title_case(name)
        entity.setdefault("needsReview", False)
    elif has_ocr_artifacts(name):
        entity["needsReview"] = True
    else:
        entity.setdefault("needsReview", False)

    return entity


def process_file(path: Path, dry_run: bool) -> tuple[int, int, int]:
    """Process one canonical JSON file. Returns (title_cased, flagged, unchanged)."""
    with open(path, encoding="utf-8") as f:
        data = json.load(f)

    entities = data.get("entities", [])
    title_cased = flagged = unchanged = 0

    for entity in entities:
        old_name = entity.get("name", "")
        old_review = entity.get("needsReview", False)
        normalize_entity(entity)
        new_name = entity.get("name", "")
        new_review = entity.get("needsReview", False)

        if new_name != old_name:
            title_cased += 1
            if dry_run:
                print(f"  CASE  {path.name}: {old_name!r} => {new_name!r}")
        elif new_review and not old_review:
            flagged += 1
            if dry_run:
                print(f"  FLAG  {path.name}: {old_name!r}")
        else:
            unchanged += 1

    if not dry_run:
        with open(path, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2, ensure_ascii=False)
            f.write("\n")

    return title_cased, flagged, unchanged


def main() -> int:
    parser = argparse.ArgumentParser(description="Normalize entity names in canonical JSON files.")
    parser.add_argument("--dry-run", action="store_true", help="Print changes without writing.")
    parser.add_argument("--file", metavar="FILENAME", help="Process only this file (name only, e.g. tce.json).")
    args = parser.parse_args()

    if args.file:
        paths = [CANONICAL_DIR / args.file]
    else:
        paths = sorted(p for p in CANONICAL_DIR.glob("*.json")
                       if not any(p.name.endswith(s) for s in
                                  [".errors.json", ".warnings.json", ".progress.json", ".progress.errors.json"]))

    total_cased = total_flagged = total_unchanged = 0
    for path in paths:
        if not path.exists():
            print(f"ERROR: {path} not found", file=sys.stderr)
            return 1
        c, f, u = process_file(path, args.dry_run)
        print(f"{path.name}: {c} title-cased, {f} flagged, {u} unchanged")
        total_cased += c
        total_flagged += f
        total_unchanged += u

    print(f"\nTotal: {total_cased} title-cased, {total_flagged} flagged, {total_unchanged} unchanged")
    if args.dry_run:
        print("(dry-run -- no files written)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
