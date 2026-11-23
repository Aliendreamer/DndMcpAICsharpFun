"""coverage_scan.py <ocr-cache> [slug...] [--out DIR] — run from the repo root.

Per book prints `slug contentLines unheld` for the committed canonical file (entities that are not Declined; their
text and notes count as held) and writes `<out>/<slug>.gaps.txt` listing the unheld lines by page.
"""
from __future__ import annotations

import argparse
import glob
import json
import os
import sys
from collections import defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from bookextractor.coverage import measure  # noqa: E402
from bookextractor.model import load_ocr_page  # noqa: E402
from bookextractor.pages import classify_page  # noqa: E402


def held_texts(canonical: dict) -> list[str]:
    out = []
    for e in canonical["entities"]:
        if e.get("disposition") == "Declined":
            continue
        out.append(e.get("canonicalText") or "")
        out.extend(n.get("text", "") for n in e.get("notes") or [])
    return out


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("cache")
    ap.add_argument("slugs", nargs="*")
    ap.add_argument("--out", default=".")
    args = ap.parse_args()
    slugs = args.slugs or sorted(os.path.basename(p)[:-5] for p in glob.glob("books/canonical/*.json")
                                 if os.path.basename(p).count(".") == 1)
    Path(args.out).mkdir(parents=True, exist_ok=True)
    for slug in slugs:
        try:
            with open(f"books/canonical/{slug}.json") as fh_:
                d = json.load(fh_)
        except (OSError, ValueError):
            continue
        fh = d.get("book", {}).get("fileHash") if isinstance(d, dict) else None
        if not fh:
            continue
        files = sorted(glob.glob(f"{args.cache}/{fh}/ocr/*.json"))
        if not files:
            print(f"{slug} no OCR cache, skipped")
            continue
        lines, left_out = [], []
        for f in files:
            pg = int(os.path.basename(f)[:3])
            page = load_ocr_page(Path(f), pg)
            front_back = classify_page(page) in ("toc", "index", "credits")  # left out on purpose, as extract_book does
            for l in page.lines:  # the pipeline's own cleaned lines
                lines.append((pg, l.text, min((fr.score for fr in l.frags), default=1.0)))
                if front_back:
                    left_out.append((pg, l.text.strip()))
        cov = measure(lines, held_texts(d), left_out)
        per = defaultdict(list)
        for pg, t in cov.unheld:
            per[pg].append(t)
        with open(Path(args.out) / f"{slug}.gaps.txt", "w") as fo:
            for pg in sorted(per):
                fo.write(f"p{pg} ({len(per[pg])})\n" + "".join(f"   {t}\n" for t in per[pg]))
        print(f"{slug} {cov.content_lines} {len(cov.unheld)}")


if __name__ == "__main__":
    main()
