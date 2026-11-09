"""Dev tool: trim flagged (NeedsReview) world/rule entities whose text runs past the section's printed end.

For each candidate the section's OCR lines are re-read, `boundary.cut` finds where the section really ends
(sibling heading, shaded sidebar, map label) and the dropped lines are removed from `fields.entries`; canonicalText is
re-rendered. Both land in `<book>.overrides.json` as overrides; a trim that passes `boundary.approvable` also gets an
approval. Anything that cannot be reproduced with certainty is left untouched.

    python -m bookextractor.boundary_fix --canonical books/canonical --books-dir books --ocr-cache <dir> \
        [--books ggr,erlw] [--report out.json] [--dry-run]
"""
from __future__ import annotations

import argparse
import json
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

from .boundary import BLine, approvable, cut, waivable_failures
from .boundary_align import drop_runs, remove, section_lines
from .boundary_score import entries_text, tokens
from .layout import reading_order
from .model import load_ocr_page
from .reader import pdf_sha256
from .shade import line_shades

REASON = "boundary trim"
SKIP_BOOKS = {"system-reference-document", "dragonborn-slice"}
TYPES = {"Lore", "Rule", "God", "Plane", "Faction", "Location"}
_FIELDS = ("fields.entries", "canonicalText")


def _load_json(p: Path):
    return json.loads(p.read_text("utf-8"))


def _now() -> str:
    return datetime.now(timezone.utc).isoformat()


def flat(entries: list) -> list[str]:
    """The extractor's flattening of entries into canonicalText body lines (parsers/world.py, parsers/rule.py)."""
    return [x if isinstance(x, str) else "\n".join(x.get("items", [])) for x in entries]


def render(canonical_text: str, entries: list) -> str:
    """canonicalText = heading line + flattened entries; the heading is kept as printed."""
    return "\n".join([canonical_text.split("\n", 1)[0]] + flat(entries))


def pdf_map(books_dir: Path) -> dict[str, Path]:
    return {pdf_sha256(p): p for p in sorted(books_dir.glob("*.pdf"))}


def _lines(ocr: Path, pages: list[int], pdf: Path | None) -> list[BLine] | None:
    out: list[BLine] = []
    for n in pages:
        f = ocr / f"{n:03d}.json"
        if not f.exists():
            return None
        page = load_ocr_page(f, n)
        ordered = reading_order(page)
        shades = line_shades(pdf, n, [l.box for l in ordered]) if pdf else [0.0] * len(ordered)
        for l, s in zip(ordered, shades):
            out.append(BLine(l.text, n, int((l.box[0] + l.box[2]) / 2 > page.width / 2), l.height, l.box, s))
    return out


def _candidate(e: dict) -> bool:
    return bool(e.get("needsReview")) and e["type"] in TYPES and bool((e.get("fields") or {}).get("entries"))


def process_book(data: dict, ocr_cache: Path, pdfs, existing: dict, rep: dict) -> tuple[list, list]:
    touched = {o["entityId"] for o in existing.get("overrides", []) if o.get("field") in _FIELDS}
    approved = {a["entityId"] for a in existing.get("approvals", [])}
    ocr = ocr_cache / data["book"]["fileHash"] / "ocr"
    pdf = None
    overrides: list = []
    approvals: list = []
    skipped: Counter = rep["skipped"]

    def get_pdf():
        m = pdfs() if callable(pdfs) else pdfs
        return m.get(data["book"]["fileHash"])

    pdf_resolved = False
    for e in data["entities"]:
        if not _candidate(e):
            continue
        rep["candidates"] += 1
        eid = e["id"]
        if eid in touched:
            skipped["already-overridden"] += 1
            continue
        if eid in approved:
            skipped["approved"] += 1
            continue
        pages = [p["page"] for p in (e.get("provenance") or {}).get("pages", [])]
        if not pages or any(not (ocr / f"{n:03d}.json").exists() for n in pages):
            skipped["missing-ocr-page"] += 1
            continue
        old = e["fields"]["entries"]
        text = e["canonicalText"]
        if render(text, old) != text:
            skipped["render-mismatch"] += 1
            continue
        if not pdf_resolved:
            pdf, pdf_resolved = get_pdf(), True
        if pdf is None:
            rep["no-pdf-shade"] += 1
        L = _lines(ocr, pages, pdf)
        S = [L[i] for i in section_lines(L, [text.split("\n", 1)[0]] + old)]
        if not S:
            skipped["no-anchor"] += 1
            continue
        c = cut(S, e["name"], first_page=pages[0])
        if not c.drop:
            skipped["no-change"] += 1
            continue
        new = remove(old, drop_runs([l.text for l in S], c.drop))
        if new is None:
            skipped["ambiguous-removal"] += 1
            continue
        if new == old:
            skipped["no-change"] += 1
            continue
        at = _now()
        overrides.append({"entityId": eid, "field": "fields.entries", "value": new, "extracted": old,
                          "reason": REASON, "at": at, "extractedAbsent": False})
        overrides.append({"entityId": eid, "field": "canonicalText", "value": render(text, new), "extracted": text,
                          "reason": REASON, "at": at, "extractedAbsent": False})
        rep["trimmed"] += 1
        ok = approvable(old, new, c, e["name"], e["checks"])
        if ok:
            approvals.append({"entityId": eid, "reason": REASON, "at": at,
                              "waivedChecks": waivable_failures(e["checks"])})
            rep["approved"] += 1
        else:
            rep["flagged"] += 1
        b = len(tokens(entries_text(old)))
        rep["entities"].append({"id": eid, "rule": c.rule,
                                "keptRatio": round(len(tokens(entries_text(new))) / b, 3) if b else 0.0,
                                "approved": ok})
    return overrides, approvals


def run(canonical: Path, books_dir: Path, ocr_cache: Path, books: list[str] | None = None,
        dry_run: bool = False) -> dict:
    cache: dict = {}

    def pdfs():
        if "m" not in cache:
            cache["m"] = pdf_map(books_dir)
        return cache["m"]

    summary: dict = {}
    for path in sorted(canonical.glob("*.json")):
        if "." in path.stem or path.stem in SKIP_BOOKS or (books and path.stem not in books):
            continue
        data = _load_json(path)
        if data.get("schemaVersion") != "2":
            continue
        ov_path = path.with_name(path.stem + ".overrides.json")
        existing = _load_json(ov_path) if ov_path.exists() else {"overrides": [], "approvals": []}
        rep = {"candidates": 0, "trimmed": 0, "approved": 0, "flagged": 0, "no-pdf-shade": 0,
               "skipped": Counter(), "entities": []}
        new_o, new_a = process_book(data, ocr_cache, pdfs, existing, rep)
        rep["skipped"] = dict(rep["skipped"])
        summary[path.stem] = rep
        if new_o and not dry_run:
            merged = {"overrides": existing.get("overrides", []) + new_o,
                      "approvals": existing.get("approvals", []) + new_a}
            ov_path.write_text(json.dumps(merged, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return summary


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(prog="boundary_fix")
    ap.add_argument("--canonical", type=Path, required=True)
    ap.add_argument("--books-dir", type=Path, required=True)
    ap.add_argument("--ocr-cache", type=Path, required=True)
    ap.add_argument("--books", default="", help="comma list of book slugs (default: every programmatic book)")
    ap.add_argument("--report", type=Path, default=None)
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args(argv)
    want = [b.strip() for b in a.books.split(",") if b.strip()]
    summary = run(a.canonical, a.books_dir, a.ocr_cache, want or None, a.dry_run)
    for slug, r in summary.items():
        print(slug, {k: v for k, v in r.items() if k != "entities"})
    if a.report:
        a.report.write_text(json.dumps(summary, indent=2), encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
