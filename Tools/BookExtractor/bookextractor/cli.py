"""Command line: python -m bookextractor extract <pdf> --slug S --cache DIR --out FILE."""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from .assemble import ALL_TYPES, extract_book
from .reader import BookReader


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(prog="bookextractor")
    sub = ap.add_subparsers(dest="cmd", required=True)
    ex = sub.add_parser("extract", help="extract entities from a PDF")
    ex.add_argument("pdf", type=Path)
    ex.add_argument("--slug", required=True)
    ex.add_argument("--cache", type=Path, required=True)
    ex.add_argument("--out", type=Path, required=True)
    ex.add_argument("--threads", type=int, default=4)
    ex.add_argument("--processes", type=int, default=2)
    ex.add_argument("--types", default=",".join(ALL_TYPES), help="comma list of Monster,Spell,MagicItem")
    sc = sub.add_parser("score", help="dev: score an extract output against 5etools")
    sc.add_argument("book", choices=["mm", "phb", "dmg"])
    sc.add_argument("extract_output", type=Path)
    sc.add_argument("--fivetools", type=Path, required=True, help="5etools data directory")
    sc.add_argument("--diffs", type=Path, default=None, help="write missing/wrong list here")
    args = ap.parse_args(argv)

    if args.cmd == "score":
        from . import score  # lazy: dev-only, never on the extraction path
        return score.run(args.book, args.extract_output, args.fivetools, args.diffs)

    types = {t.strip() for t in args.types.split(",") if t.strip()}
    unknown = types - set(ALL_TYPES)
    if unknown:
        ap.error(f"unknown types: {sorted(unknown)}")

    def progress(done: int, total: int) -> None:
        if done == total or done % 25 == 0:
            print(f"read {done}/{total} pages", file=sys.stderr, flush=True)

    reader = BookReader(args.pdf, args.cache, threads=args.threads, processes=args.processes)
    pages = reader.read(progress)
    result = extract_book(pages, args.slug, types)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps({"entities": result.entities, "report": result.report}, indent=1),
                        encoding="utf-8")
    r = result.report
    print(f"{r['book']}: {r['counts']} passing {r['passingShare']:.1%} coverage {r['coverageShare']:.1%}")
    for w in r["warnings"]:
        print("WARNING", w, file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
