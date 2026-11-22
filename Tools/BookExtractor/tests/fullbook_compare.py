"""Whole-book extractor comparison: a git ref against the working tree (a dev tool, not a test).

The fixture tests run a few pages each; some regressions only show when a whole book goes through, because the
recurring-heading and column rules look at the book. This runs entire OCR-cached books through two extractors and
prints what moved. Not collected by pytest (no `test_` prefix) and not run in CI.

    python tests/fullbook_compare.py --cache <ocr-cache-dir> [--ref 2760852d] mm14 xge [slug:fileHash ...]

`<ocr-cache-dir>/<fileHash>/ocr/NNN.json` holds the OCR pages. A book is a slug (its fileHash is read from
`books/canonical/<slug>.json` in HEAD, through git's filters: the files are git-crypt'd) or `slug:fileHash`.
The ref is unpacked with `git archive` into a temp dir; each extractor runs in its own interpreter.

Per book it prints: entity counts by type; Monsters whose text, action or trait length moved more than 20%;
check-failure deltas by check name, and Monsters that gained a failing check; entities that disappeared, and whether
their text landed in another entity. Use `--detail N` to list more than the default rows of each list.
"""
from __future__ import annotations

import argparse
import glob
import json
import os
import re
import subprocess
import sys
import tempfile
from collections import Counter
from pathlib import Path

HERE = Path(__file__).resolve().parent
PKG_ROOT = HERE.parent  # Tools/BookExtractor
SHIFT = 0.20
LANDED = 0.9  # a vanished entity's text "landed" when this share of its lines is in the new entities' text


def worker(root: str, cache: str, file_hash: str, slug: str, out: str) -> None:
    """Extract one book with the package under `root`; write the entities' comparable parts to `out`."""
    sys.path.insert(0, root)
    from bookextractor.assemble import ALL_TYPES, extract_book
    from bookextractor.model import load_ocr_page

    files = sorted(glob.glob(f"{cache}/{file_hash}/ocr/*.json"))
    pages = [load_ocr_page(f, int(os.path.basename(f)[:3])) for f in files]
    entities = extract_book(pages, slug, set(ALL_TYPES)).entities
    slim = [{"id": e["id"], "type": e["type"], "name": e["name"], "page": e["page"], "text": e["canonicalText"], "size": len(e["canonicalText"]),
             "action": len(json.dumps(e["fields"].get("action") or "")),
             "trait": len(json.dumps(e["fields"].get("trait") or "")),
             "fails": sorted(c["name"] for c in e["checks"] if not c["passed"])} for e in entities]
    Path(out).write_text(json.dumps(slim))


def _repo_root() -> Path:
    return Path(subprocess.run(["git", "-C", str(HERE), "rev-parse", "--show-toplevel"], check=True,
                               capture_output=True, text=True).stdout.strip())


def file_hash_of(slug: str, repo: Path) -> str:
    raw = subprocess.run(["git", "-C", str(repo), "cat-file", "--filters", f"HEAD:books/canonical/{slug}.json"],
                         check=True, capture_output=True, text=True).stdout
    return json.loads(raw)["book"]["fileHash"]


def unpack_ref(ref: str, repo: Path, into: Path) -> str:
    archive = subprocess.run(["git", "-C", str(repo), "archive", ref, "Tools/BookExtractor/bookextractor"],
                             check=True, capture_output=True).stdout
    subprocess.run(["tar", "-x", "-C", str(into)], input=archive, check=True)
    return str(into / "Tools" / "BookExtractor")


def _norm(t: str) -> str:
    return re.sub(r"\s+", " ", t).strip()


def _landed(text: str, haystack: str) -> float:
    lines = [_norm(l) for l in text.splitlines() if len(_norm(l)) >= 20]
    return sum(l in haystack for l in lines) / len(lines) if lines else 1.0


def _moved(a: int, b: int) -> bool:
    return abs(a - b) > SHIFT * max(a, 1) if a else b > 0


def compare(slug: str, old: list[dict], new: list[dict], detail: int) -> dict:
    by_old = {e["id"]: e for e in old}
    by_new = {e["id"]: e for e in new}
    lines: list[str] = []
    counts_old, counts_new = Counter(e["type"] for e in old), Counter(e["type"] for e in new)
    kinds = sorted(set(counts_old) | set(counts_new))
    lines.append("  entities: " + ", ".join(f"{k} {counts_old[k]}->{counts_new[k]}" for k in kinds))

    shifts = []
    for i, o in by_old.items():
        n = by_new.get(i)
        if n and o["type"] == "Monster" and any(_moved(o[k], n[k]) for k in ("size", "action", "trait")):
            shifts.append((o["name"], o["page"], [(k if k != "size" else "text", o[k], n[k])
                                                  for k in ("size", "action", "trait")]))
    lines.append(f"  monsters moved >{int(SHIFT * 100)}%: {len(shifts)}")
    for name, page, ks in shifts[:detail]:
        lines.append(f"    {name} p{page}: " + ", ".join(f"{k} {a}->{b}" for k, a, b in ks if _moved(a, b)))

    fo, fn = Counter(f for e in old for f in e["fails"]), Counter(f for e in new for f in e["fails"])
    deltas = {k: (fo[k], fn[k]) for k in sorted(set(fo) | set(fn)) if fo[k] != fn[k]}
    lines.append("  check failures (old->new): " + (", ".join(f"{k} {a}->{b}" for k, (a, b) in deltas.items()) or "same"))
    gained = [(e["name"], e["page"], sorted(set(e["fails"]) - set(by_old[e["id"]]["fails"])))
              for e in new if e["type"] == "Monster" and e["id"] in by_old
              and set(e["fails"]) - set(by_old[e["id"]]["fails"])]
    lines.append(f"  monsters that gained a failing check: {len(gained)}")
    for name, page, names in gained[:detail]:
        lines.append(f"    {name} p{page}: {', '.join(names)}")

    new_text = _norm(" \n".join(e["text"] for e in new).replace("\n", " "))
    gone = [e for i, e in by_old.items() if i not in by_new]
    lost = []
    for e in gone:
        share = _landed(e["text"], new_text)
        if len(e["text"]) >= 100 and share < LANDED:
            lost.append((e["type"], e["id"], e["page"], len(e["text"]), share))
    lines.append(f"  entities that disappeared: {len(gone)}; content not found elsewhere: {len(lost)}")
    for t, i, page, size, share in lost[:detail]:
        lines.append(f"    {t} {i} p{page} ({size} chars, {share:.0%} of its lines elsewhere)")
    return {"slug": slug, "lines": lines, "moved": len(shifts), "gained": len(gained), "lost": len(lost),
            "summary": f"{slug}: entities {len(old)}->{len(new)}, monsters moved {len(shifts)}, "
                       f"monsters gained a check {len(gained)}, disappeared {len(gone)} ({len(lost)} with content lost)"}


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("books", nargs="+", help="slug or slug:fileHash")
    ap.add_argument("--cache", required=True, help="OCR cache dir: <cache>/<fileHash>/ocr/NNN.json")
    ap.add_argument("--ref", default="HEAD", help="git ref of the extractor to compare against (default HEAD)")
    ap.add_argument("--detail", type=int, default=12, help="rows per list (default 12)")
    args = ap.parse_args()

    repo = _repo_root()
    with tempfile.TemporaryDirectory() as tmp:
        old_root = unpack_ref(args.ref, repo, Path(tmp))
        summaries = []
        for book in args.books:
            slug, _, fh = book.partition(":")
            fh = fh or file_hash_of(slug, repo)
            procs, outs = [], {}
            for label, root in (("old", old_root), ("new", str(PKG_ROOT))):
                outs[label] = str(Path(tmp) / f"{slug}.{label}.json")
                procs.append(subprocess.Popen([sys.executable, "-I", str(Path(__file__).resolve()), "--worker", root,
                                               args.cache, fh, slug, outs[label]]))
            if any(p.wait() for p in procs):
                print(f"{slug}: extraction failed", file=sys.stderr)
                continue
            res = compare(slug, json.loads(Path(outs["old"]).read_text()), json.loads(Path(outs["new"]).read_text()),
                          args.detail)
            print(f"== {slug} ({args.ref} -> working tree)")
            print("\n".join(res["lines"]), flush=True)
            summaries.append(res["summary"])
    print("\nSUMMARY")
    print("\n".join(summaries))


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "--worker":
        worker(*sys.argv[2:7])
    else:
        main()
