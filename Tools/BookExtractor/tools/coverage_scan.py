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


def held_texts(entities: list[dict]) -> list[str]:
    out = []
    for e in entities:
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
            d = json.load(open(f"books/canonical/{slug}.json"))
        except (OSError, ValueError):
            continue
        fh = d.get("book", {}).get("fileHash") if isinstance(d, dict) else None
        if not fh:
            continue
        files = sorted(glob.glob(f"{args.cache}/{fh}/ocr/*.json"))
        if not files:
            print(f"{slug} no OCR cache, skipped")
            continue
        lines = []
        for f in files:
            pg = int(os.path.basename(f)[:3])
            for l in json.load(open(f))["lines"]:
                lines.append((pg, l["t"], float(l.get("s", 1.0))))
        cov = measure(lines, held_texts(d["entities"]), [])
        per = defaultdict(list)
        for pg, t in cov.unheld:
            per[pg].append(t)
        with open(Path(args.out) / f"{slug}.gaps.txt", "w") as fo:
            for pg in sorted(per):
                fo.write(f"p{pg} ({len(per[pg])})\n" + "".join(f"   {t}\n" for t in per[pg]))
        print(f"{slug} {cov.content_lines} {len(cov.unheld)}")


if __name__ == "__main__":
    main()
