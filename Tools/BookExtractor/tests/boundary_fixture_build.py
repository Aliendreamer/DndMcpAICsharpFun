"""Dev script (not a test): build tests/data/boundary_regression.json from ChatGPT-reviewed answers.

Usage: python tests/boundary_fixture_build.py [--gpt-dir D] [--ocr-cache D] [--out F]
"""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

from bookextractor.boundary import BLine
from bookextractor.layout import reading_order
from bookextractor.model import load_ocr_page
from bookextractor.shade import line_shades

REPO = Path(__file__).resolve().parents[3]
SCRATCH = Path("/tmp/claude-1000/-home-aliendreamer-projects-DndMcpAICsharpFun")
GPT = SCRATCH / "6ab16f2f-2ad0-495d-ad20-e45f0bab2658/scratchpad/gpt_full"
OCR = SCRATCH / "3b4f2531-5f72-489e-91f8-0d4dc7e34d71/scratchpad/ocr_cache"
DEFAULT_OUT = Path(__file__).parent / "data" / "boundary_regression.json"


def verdict_and_corrected(answer: str) -> tuple[str, str | None]:
    m = re.match(r"\s*VERDICT:\s*(\w+)", answer)
    verdict = m.group(1).upper() if m else "UNSURE"
    corrected = None
    if "CORRECTED:" in answer:
        corrected = answer.split("CORRECTED:", 1)[1].strip() or None
    return verdict, corrected


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--gpt-dir", type=Path, default=GPT)
    ap.add_argument("--ocr-cache", type=Path, default=OCR)
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT)
    ap.add_argument("--books-dir", type=Path, default=REPO / "books")
    a = ap.parse_args()

    todo: dict[str, dict] = {}
    for name in ("todo.before5e.json", "todo.json"):
        for t in json.loads((a.gpt_dir / name).read_text()):
            todo[t["id"]] = t
    hashes = {b["filePath"]: b["fileHash"] for b in json.loads((a.gpt_dir / "books.json").read_text())}

    canon: dict[str, dict] = {}
    overrides: dict[tuple[str, str], dict] = {}

    def book_entities(slug: str) -> dict:
        if slug not in canon:
            data = json.loads((a.books_dir / "canonical" / f"{slug}.json").read_text())
            canon[slug] = {e["id"]: e for e in data["entities"]}
            op = a.books_dir / "canonical" / f"{slug}.overrides.json"
            if op.exists():
                for o in json.loads(op.read_text()).get("overrides", []):
                    overrides[(o["entityId"], o["field"])] = o
        return canon[slug]

    rows, skipped = [], {"unsure": 0, "no_pages": 0, "no_entity": 0, "no_todo": 0}
    for f in sorted((a.gpt_dir / "ans").glob("*.json")):
        ans = json.loads(f.read_text())
        eid = ans["id"]
        verdict, corrected = verdict_and_corrected(ans["answer"])
        if verdict not in ("FIXED", "MATCH"):
            skipped["unsure"] += 1
            continue
        t = todo.get(eid)
        if t is None:
            skipped["no_todo"] += 1
            continue
        slug = eid.split(".", 1)[0]
        ent = book_entities(slug).get(eid)
        if ent is None:
            skipped["no_entity"] += 1
            continue
        pdf_name = Path(t["pdf"]).name
        pdf = a.books_dir / pdf_name
        cache = a.ocr_cache / hashes.get(t["pdf"], "") / "ocr"
        pages = [p["page"] for p in ent["provenance"]["pages"]]
        if not pdf.exists() or not pages or any(not (cache / f"{p:03d}.json").exists() for p in pages):
            skipped["no_pages"] += 1
            continue
        lines = []
        for p in pages:
            page = load_ocr_page(cache / f"{p:03d}.json", p)
            ordered = reading_order(page)
            shades = line_shades(pdf, p, [l.box for l in ordered])
            for l, s in zip(ordered, shades):
                col = int((l.box[0] + l.box[2]) / 2 > page.width / 2)
                lines.append(BLine(l.text, p, col, l.height, l.box, s))
        ent_entries = overrides.get((eid, "fields.entries"))
        entries = ent_entries["extracted"] if ent_entries else ent["fields"].get("entries", [])
        ct = overrides.get((eid, "canonicalText"))
        ctext = ct["extracted"] if ct else ent["canonicalText"]
        r1 = lambda x: round(x, 1)  # noqa: E731
        rows.append({
            "id": eid, "book": slug, "verdict": verdict, "name": ent["name"],
            "heading_line": ctext.split("\n", 1)[0],
            "entries": [e for e in entries if isinstance(e, str)],
            "corrected": corrected if verdict == "FIXED" else None,
            "checks": [{"name": c["name"], "passed": c["passed"]} for c in ent.get("checks", [])],
            "lines": [{"text": l.text, "page": l.page, "col": l.col, "h": r1(l.h),
                       "box": [r1(v) for v in l.box], "shade": r1(l.shade)} for l in lines],
        })
    a.out.parent.mkdir(parents=True, exist_ok=True)
    a.out.write_text(json.dumps(rows, separators=(",", ":"), ensure_ascii=False))
    from collections import Counter
    print("rows", len(rows), dict(Counter(r["verdict"] for r in rows)), "skipped", skipped,
          "bytes", a.out.stat().st_size)


if __name__ == "__main__":
    main()
