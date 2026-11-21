"""Dev script (not a test): append-only builder of tests/data/extraction_regression.json.

Usage: python tests/extraction_regression_build.py --books-dir ../../books --ocr-cache <dir> [--out F]

An entity enters the fixture when a human touched it (an override or an approval). Its expected
canonicalText/fields are the canonical entity with every override value set at its path. Entries
already in the output are kept verbatim, never rewritten. Pages come from the OCR cache
(<fileHash>/ocr/NNN.json) or, when absent there, the text layer of the book's source PDF.
Not run in CI.
"""
from __future__ import annotations

import argparse
import copy
import json
from pathlib import Path

import pymupdf

from bookextractor.model import load_ocr_page
from bookextractor.pages_io import page_to_dict
from bookextractor.reader import _layer_page, pdf_sha256

DEFAULT_OUT = Path(__file__).parent / "data" / "extraction_regression.json"
_SIDE_SUFFIXES = (".report.json", ".errors.json", ".warnings.json", ".declined.json")


class _Pages:
    """Resolves (fileHash, page) to a page dict; hashes source PDFs lazily, only on an OCR miss."""

    def __init__(self, books_dir: Path, ocr_cache: Path):
        self.books_dir, self.ocr_cache = books_dir, ocr_cache
        self._pdfs: dict[str, Path] | None = None

    def _pdf_for(self, file_hash: str) -> Path | None:
        if self._pdfs is None:
            self._pdfs = {pdf_sha256(p): p for p in sorted(self.books_dir.glob("*.pdf"))}
        return self._pdfs.get(file_hash)

    def get(self, file_hash: str, number: int) -> dict | None:
        ocr = self.ocr_cache / file_hash / "ocr" / f"{number:03d}.json"
        if ocr.exists():
            return page_to_dict(load_ocr_page(ocr, number))
        pdf = self._pdf_for(file_hash)
        if pdf is None:
            return None
        with pymupdf.open(pdf) as doc:
            if not 1 <= number <= len(doc):
                return None
            return page_to_dict(_layer_page(doc[number - 1], number))


def _load_books(books_dir: Path) -> dict[str, tuple[dict, dict]]:
    """slug -> (canonical, overrides-doc) for every v2 canonical file."""
    out: dict[str, tuple[dict, dict]] = {}
    canon_dir = books_dir / "canonical"
    for f in sorted(canon_dir.glob("*.json")):
        if f.name.endswith(_SIDE_SUFFIXES) or ".progress" in f.name or f.name.endswith(".overrides.json"):
            continue
        data = json.loads(f.read_text(encoding="utf-8"))
        if not (isinstance(data, dict) and "book" in data and "entities" in data):
            continue
        slug = f.name[: -len(".json")]
        ov = canon_dir / f"{slug}.overrides.json"
        doc = json.loads(ov.read_text(encoding="utf-8")) if ov.exists() else {}
        out[slug] = (data, doc)
    return out


def _set_path(entity: dict, field: str, value) -> None:
    if field.startswith("fields."):
        entity["fields"][field[len("fields."):]] = value
    else:
        entity[field] = value


def _entity_pages(entity: dict) -> list[int]:
    pages = {entity["page"]} if entity.get("page") else set()
    pages |= {p["page"] for p in entity.get("provenance", {}).get("pages", [])}
    return sorted(pages)


def build(books_dir: Path, ocr_cache: Path, existing: list[dict]) -> tuple[list[dict], list[str]]:
    entries = list(existing)
    seen = {e["id"] for e in existing}
    skipped: list[str] = []
    pages = _Pages(books_dir, ocr_cache)
    for slug, (canon, doc) in _load_books(books_dir).items():
        overrides = doc.get("overrides", [])
        ids = list(dict.fromkeys([o["entityId"] for o in overrides] + [a["entityId"] for a in doc.get("approvals", [])]))
        by_id = {e["id"]: e for e in canon["entities"]}
        file_hash = canon["book"]["fileHash"]
        for eid in ids:
            if eid in seen:
                continue
            if eid not in by_id:
                skipped.append(f"{eid} (not in {slug}.json)")
                continue
            entity = copy.deepcopy(by_id[eid])
            overridden: list[str] = []
            for o in overrides:
                if o["entityId"] == eid:
                    _set_path(entity, o["field"], o["value"])
                    if o["field"] not in overridden:
                        overridden.append(o["field"])
            core = _entity_pages(entity)
            ctx_numbers = sorted({n for p in core for n in (p - 1, p, p + 1) if n >= 1})
            got = {n: pages.get(file_hash, n) for n in ctx_numbers}
            missing = [n for n in core if got[n] is None]
            if missing:
                skipped.append(f"{eid} (page {missing} not in OCR cache or PDF)")
                continue
            entries.append({
                "id": eid, "slug": slug, "type": entity["type"], "name": entity["name"],
                "pages": core, "ctx_pages": [got[n] for n in ctx_numbers if got[n] is not None],
                "expected": {"canonicalText": entity["canonicalText"], "fields": entity["fields"]},
                "overridden": overridden,
            })
            seen.add(eid)
    return entries, skipped


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--books-dir", type=Path, required=True)
    ap.add_argument("--ocr-cache", type=Path, required=True)
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT)
    a = ap.parse_args()
    existing = json.loads(a.out.read_text(encoding="utf-8")) if a.out.exists() else []
    entries, skipped = build(a.books_dir, a.ocr_cache, existing)
    a.out.parent.mkdir(parents=True, exist_ok=True)
    a.out.write_text(json.dumps(entries, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"{len(entries)} entries ({len(entries) - len(existing)} new), {len(skipped)} skipped")
    for s in skipped:
        print("  skipped:", s)


if __name__ == "__main__":
    main()
