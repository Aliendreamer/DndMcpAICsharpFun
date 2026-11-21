"""Scores current extraction against the page-verified regression fixture and the committed baseline."""
from __future__ import annotations

import gzip
import json
import re
import sys
import time
from collections import defaultdict
from pathlib import Path

from bookextractor.boundary_score import f1
from bookextractor.pages_io import extract_pages, page_from_dict
from bookextractor.textnorm import values_match

DATA = Path(__file__).parent / "data"
FIXTURE = DATA / "extraction_regression.json.gz"
BASELINE = DATA / "extraction_regression.baseline.json"
NAME_SIMILARITY = 0.8
NAME_MARGIN = 0.1


def load_fixture() -> tuple[dict, list[dict]]:
    """Returns (pages keyed '<slug>:<n>' as Page objects, entries)."""
    raw = json.loads(gzip.decompress(FIXTURE.read_bytes()))
    return {k: page_from_dict(v) for k, v in raw["pages"].items()}, raw["entries"]


def _slug(name: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", name.lower()).strip("-")


def _levenshtein(a: str, b: str) -> int:
    prev = list(range(len(b) + 1))
    for i, ca in enumerate(a, 1):
        cur = [i]
        for j, cb in enumerate(b, 1):
            cur.append(min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (ca != cb)))
        prev = cur
    return prev[-1]


def _similarity(a: str, b: str) -> float:
    longest = max(len(a), len(b))
    return 1.0 if longest == 0 else 1 - _levenshtein(a, b) / longest


def _entity_pages(e: dict) -> set[int]:
    pages = {p["page"] for p in e.get("provenance", {}).get("pages", [])}
    if e.get("page") is not None:
        pages.add(e["page"])
    return pages


def find_entity(entry: dict, entities: list[dict]) -> dict | None:
    for e in entities:
        if e["id"] == entry["id"]:
            return e
    want = _slug(entry["name"])
    scored = sorted(
        ((_similarity(want, _slug(e["name"])), e) for e in entities
         if e["type"] == entry["type"] and _entity_pages(e) & set(entry["pages"])),
        key=lambda t: t[0], reverse=True)
    if not scored or scored[0][0] < NAME_SIMILARITY:
        return None
    if len(scored) > 1 and scored[1][0] >= scored[0][0] - NAME_MARGIN:
        return None
    return scored[0][1]


def _actual(path: str, entity: dict):
    if path.startswith("fields."):
        return entity.get("fields", {}).get(path[len("fields."):])
    return entity.get(path)


def score_entry(entry: dict, entities: list[dict]) -> dict:
    entity = find_entity(entry, entities)
    if entity is None:
        return {"id": entry["id"], "found": False, "f1": 0.0, "match": False}
    expected = entry["expected"]
    paths = list(entry["overridden"])
    match = True
    for path in paths:
        want = entry["name"] if path == "name" else (
            expected["fields"].get(path[len("fields."):]) if path.startswith("fields.") else expected.get(path))
        if not values_match(_actual(path, entity), want):
            match = False
    return {"id": entry["id"], "found": True,
            "f1": round(f1(entity.get("canonicalText") or "", expected["canonicalText"]), 4), "match": match}


def score_all() -> list[dict]:
    pages, entries = load_fixture()
    by_slug: dict[str, list[dict]] = defaultdict(list)
    for e in entries:
        by_slug[e["slug"]].append(e)
    results = []
    for slug, group in by_slug.items():
        numbers = sorted({n for e in group for n in e["ctx_pages"]})
        entities = extract_pages([pages[f"{slug}:{n}"] for n in numbers], slug)
        results.extend(score_entry(e, entities) for e in group)
    return results


def load_baseline() -> dict:
    return json.loads(BASELINE.read_text())


def main(argv: list[str]) -> None:
    start = time.time()
    results = score_all()
    if "--update-baseline" in argv:
        BASELINE.write_text(json.dumps(
            {r["id"]: {"found": r["found"], "f1": r["f1"], "match": r["match"]} for r in results},
            indent=1, sort_keys=True) + "\n")
        print(f"baseline written: {len(results)} entries")
    base = load_baseline()
    improved = worse = 0
    for r in results:
        b = base.get(r["id"])
        if b is None:
            continue
        if (r["match"], r["f1"]) > (b["match"], b["f1"] + 0.005):
            improved += 1
        elif r["match"] < b["match"] or r["f1"] < b["f1"] - 0.005:
            worse += 1
    n = len(results)
    print(f"matches {sum(r['match'] for r in results)} / found {sum(r['found'] for r in results)} / total {n} "
          f"/ mean f1 {sum(r['f1'] for r in results) / n:.4f} / improved {improved} / worse {worse} "
          f"({time.time() - start:.0f}s)")


if __name__ == "__main__":
    main(sys.argv[1:])
