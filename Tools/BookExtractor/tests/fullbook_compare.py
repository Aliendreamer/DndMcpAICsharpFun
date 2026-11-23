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
their text landed in another entity; OCR lines of 30+ chars held by a ref entity but by no working-tree entity
(newly orphaned: text that left an entity that still exists), and those orphaned in both. Use `--detail N` to list more than the default rows of each list.
"""
from __future__ import annotations

import argparse
import glob
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
from collections import Counter
from pathlib import Path

HERE = Path(__file__).resolve().parent
PKG_ROOT = HERE.parent  # Tools/BookExtractor
MONSTER_FIELDS = ("trait", "action", "reaction", "legendary", "lair")
F1_DROP = 0.05  # a Monster whose fields' token-F1 against the ref falls by more than this is listed
SHIFT = 0.20
SHORT_LINE = 10  # `--prev` also compares lines this short (a heading, a sign-off), down from MIN_LINE
MIN_LINE = 30  # an OCR line this long is content, not a label or a page number
LANDED = 0.9  # a vanished entity's text "landed" when this share of its lines is in the new entities' text


def worker(root: str, cache: str, file_hash: str, slug: str, out: str) -> None:
    """Extract one book with the package under `root`; write the entities' comparable parts to `out`."""
    sys.path.insert(0, root)
    from bookextractor.assemble import ALL_TYPES, extract_book
    from bookextractor.model import load_ocr_page

    files = sorted(glob.glob(f"{cache}/{file_hash}/ocr/*.json"))
    pages = [load_ocr_page(f, int(os.path.basename(f)[:3])) for f in files]
    result = extract_book(pages, slug, set(ALL_TYPES))
    entities = result.entities
    slim = [{"id": e["id"], "type": e["type"], "name": e["name"], "page": e["page"], "text": e["canonicalText"], "size": len(e["canonicalText"]),
             "notes": " ".join(n["text"] for n in e.get("notes", [])), "note_count": len(e.get("notes", [])), "note_list": e.get("notes", []),
             "action": len(json.dumps(e["fields"].get("action") or "")),
             "trait": len(json.dumps(e["fields"].get("trait") or "")),
             "mfields": {k: json.dumps(e["fields"].get(k) or "") for k in MONSTER_FIELDS} if e["type"] == "Monster" else {},
             "fails": sorted(c["name"] for c in e["checks"] if not c["passed"])} for e in entities]
    left: dict[int, list[str]] = {}
    for pg, t in getattr(result, "left_out", []):
        left.setdefault(pg, []).append(t)  # (reading order may merge two OCR rows into one line: match by containment)
    ocr = [[p.number, l.text.strip(), any(l.text.strip() in t for t in left.get(p.number, ()))]
           for p in pages for l in p.lines if len(l.text.strip()) >= MIN_LINE]
    sig = re.compile(r"^[-\u2014\u2013]\s*\S")
    sigs = [[p.number, l.text.strip(), False] for p in pages for l in p.lines
            if sig.match(l.text.strip()) and len(l.text.strip()) < MIN_LINE]
    shorts = [[p.number, l.text.strip(), False] for p in pages for l in p.lines
              if SHORT_LINE <= len(l.text.strip()) < MIN_LINE and not sig.match(l.text.strip())]
    Path(out).write_text(json.dumps({"coverage": getattr(result, "coverage", None), "entities": slim, "ocr": ocr, "sigs": sigs, "shorts": shorts}))


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


def _flat(text: str) -> str:
    """Whitespace and bullet glyphs aside: the text may set a bullet or a line break differently than the OCR line."""
    return re.sub(r"[\s\u2022]+", " ", text)


def coverage(old: list[dict], new: list[dict], ocr: list) -> dict:
    """OCR lines held by a ref entity but by no working-tree entity (newly orphaned), and by neither (pre-existing)."""
    old_text = _flat("\n".join(e["text"] for e in old))
    new_text = _flat("\n".join(e["text"] + " " + e.get("notes", "") for e in new))  # a margin note is held by its entity
    seen: set[str] = set()
    newly, pre, quotes = [], [], 0
    for page, line, exempt in ocr:
        if line in seen:
            continue
        seen.add(line)
        line = _flat(line[:-1] if line.endswith("-") else line).lstrip(".,;:'\"\u2022 ")  # the text joins a hyphenated word across lines
        if line in new_text:
            continue
        if exempt:
            quotes += 1
        elif line in old_text:
            newly.append((page, line))
        else:
            pre.append((page, line))
    return {"new": newly, "pre": pre, "quotes": quotes}


_SIGNATURE = re.compile(r"^[-\u2014\u2013]\s*\S")
_FIX_DIGITS = None


def _digits(text: str) -> str:
    """The extractor's own OCR digit repair (ld6 -> 1d6), loaded lazily from the working tree."""
    global _FIX_DIGITS
    if _FIX_DIGITS is None:
        sys.path.insert(0, str(PKG_ROOT))
        from bookextractor.layout import fix_digits
        _FIX_DIGITS = fix_digits
    return _FIX_DIGITS(text)


def _match_form(text: str) -> str:
    """Matching form, applied to both sides: digit repairs, case, whitespace, end-of-line hyphenation, dash variants."""
    t = _digits(re.sub(r"-[ \t]*\n\s*(?=[a-z])", "", text)).lower()
    t = re.sub(r"[\u2014\u2013]", "-", t)
    return re.sub(r"[\s\u2022]+", " ", t)


def _held_forms(entities: list[dict]) -> tuple[str, str, str]:
    """(everything held, the entities' text only, the notes only) in matching form. A note's signature counts as the
    dash line it was printed as."""
    def notes(e: dict) -> str:
        parts = [e.get("notes", "")]
        parts += [f"-{n['by']}" for n in e.get("note_list", []) if n.get("by")]
        return " \n".join(p for p in parts if p)
    body = _match_form("\n".join(e["text"] for e in entities))
    nts = _match_form("\n".join(notes(e) for e in entities))
    return body + " \n" + nts, body, nts


def _line_key(line: str) -> str:
    return _match_form(line[:-1] if line.endswith("-") else line).lstrip(".,;:'\"\u2022 ").strip()


def _sig_count(key: str, held: str) -> int:
    return len(re.findall(r"(?<!\S)" + re.escape(key) + r"(?!\w)", held))


def kept_since(prev: list[dict], new: list[dict], ocr: list) -> dict:
    """Against an earlier extraction (`--prev`): every OCR line the earlier one held, by an entity's text or a note, must
    still be held by an entity's text or a note ("lost": held by nothing now), and a line a note held must not now sit in
    an entity's text ("glued": an aside newly merged into body text). Notes count as held; left_out does NOT: a line
    that moved from held into left_out is lost (left_out holds only what no earlier run held). Signature lines (a dash
    and a name) are checked at any length, by how many times the signature is held (one name signs many asides), the
    other lines from 10 characters (the worker lists them). Both sides are matched after case, whitespace, hyphenation and OCR digit repair.
    The `exempt` flag of an OCR row is not read: it cannot excuse a loss."""
    prev_all, _, prev_notes = _held_forms(prev)
    new_all, new_body, new_notes = _held_forms(new)
    lost, glued, seen = [], [], set()
    sigs: dict[str, list] = {}
    for page, line, _exempt in ocr:
        key = _line_key(line)
        if _SIGNATURE.match(key):
            sigs.setdefault(key, []).append((page, line))
            continue
        if key in seen or len(key) < SHORT_LINE:
            continue
        seen.add(key)
        if key in prev_all and key not in new_all:
            lost.append((page, line))
        elif key in prev_notes and key in new_body and key not in new_notes:
            glued.append((page, line))
    for key, where in sigs.items():
        if _sig_count(key, prev_all) > _sig_count(key, new_all):
            lost.extend(where)  # every page the signature is printed on (the shortfall is how many copies are gone)
    return {"lost": lost, "glued": glued}


def _f1(a: str, b: str) -> float:
    ta, tb = Counter(re.findall(r"\w+", a.lower())), Counter(re.findall(r"\w+", b.lower()))
    if not ta and not tb:
        return 1.0
    common = sum((ta & tb).values())
    if not common:
        return 0.0
    p, r = common / sum(tb.values()), common / sum(ta.values())
    return 2 * p * r / (p + r)


def field_drops(old: list[dict], new: list[dict]) -> list[tuple]:
    """Monsters in both runs whose trait/action/reaction/legendary/lair token-F1 against the ref fell more than F1_DROP
    (the ref's value is the reference; a Monster that gained a failing check is not the only way to get worse)."""
    by_new = {e["id"]: e for e in new}
    out = []
    for o in old:
        n = by_new.get(o["id"])
        if not n or o["type"] != "Monster":
            continue
        for k in MONSTER_FIELDS:
            f = _f1(o["mfields"].get(k, ""), n["mfields"].get(k, ""))
            if f < 1 - F1_DROP:
                out.append((o["name"], o["page"], k, round(f, 2)))
    return out


def _moved(a: int, b: int) -> bool:
    return abs(a - b) > SHIFT * max(a, 1) if a else b > 0


def compare(slug: str, old: list[dict], new: list[dict], ocr: list, detail: int) -> dict:
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
    cov = coverage(old, new, ocr)
    lines.append(f"  OCR lines >= {MIN_LINE} chars in no working-tree entity: newly orphaned {len(cov['new'])}, "
                 f"orphaned in the ref too {len(cov['pre'])}, lines the extractor leaves out on purpose (drop_art, quote box) skipped {cov['quotes']}")
    for page, line in cov["new"][:detail]:
        lines.append(f"    NEW p{page}: {line[:100]}")
    drops = field_drops(old, new)
    lines.append(f"  Monsters whose field token-F1 fell more than {F1_DROP} vs the ref: {len(drops)}")
    for name, page, k, f in drops[:detail]:
        lines.append(f"    F1 {name} p{page} {k}: {f}")
    return {"slug": slug, "lines": lines, "f1drops": len(drops), "moved": len(shifts), "gained": len(gained), "lost": len(lost),
            "summary": f"{slug}: entities {len(old)}->{len(new)}, monsters moved {len(shifts)}, "
                       f"monsters gained a check {len(gained)}, disappeared {len(gone)} ({len(lost)} with content lost), "
                       f"field-F1 drops {len(drops)}, newly orphaned lines {len(cov['new'])}, pre-existing orphaned lines {len(cov['pre'])}"}


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("books", nargs="+", help="slug or slug:fileHash")
    ap.add_argument("--cache", required=True, help="OCR cache dir: <cache>/<fileHash>/ocr/NNN.json")
    ap.add_argument("--ref", default="HEAD", help="git ref of the extractor to compare against (default HEAD)")
    ap.add_argument("--detail", type=int, default=12, help="rows per list (default 12)")
    ap.add_argument("--prev", help="an earlier extractor ref whose held lines (text or notes) must all stay held")
    ap.add_argument("--keep", help="directory to keep the two extractions' JSON in (<slug>.old.json, <slug>.new.json)")
    args = ap.parse_args()

    repo = _repo_root()
    with tempfile.TemporaryDirectory() as tmp:
        old_root = unpack_ref(args.ref, repo, Path(tmp))
        prev_dir = Path(tmp) / "prev"
        prev_dir.mkdir()
        prev_root = unpack_ref(args.prev, repo, prev_dir) if args.prev else None
        summaries = []
        for book in args.books:
            slug, _, fh = book.partition(":")
            fh = fh or file_hash_of(slug, repo)
            procs, outs = [], {}
            for label, root in (("old", old_root), ("new", str(PKG_ROOT)), *([("prev", prev_root)] if prev_root else [])):
                outs[label] = str(Path(tmp) / f"{slug}.{label}.json")
                procs.append(subprocess.Popen([sys.executable, "-I", str(Path(__file__).resolve()), "--worker", root,
                                               args.cache, fh, slug, outs[label]]))
            if any(p.wait() for p in procs):
                print(f"{slug}: extraction failed", file=sys.stderr)
                continue
            old = json.loads(Path(outs["old"]).read_text())
            new = json.loads(Path(outs["new"]).read_text())
            if args.keep:
                Path(args.keep).mkdir(parents=True, exist_ok=True)
                shutil.copy(outs["old"], Path(args.keep) / f"{slug}.old.json")
                shutil.copy(outs["new"], Path(args.keep) / f"{slug}.new.json")
                if prev_root:
                    shutil.copy(outs["prev"], Path(args.keep) / f"{slug}.prev.json")
            res = compare(slug, old["entities"], new["entities"], new["ocr"], args.detail)
            if prev_root:
                prev = json.loads(Path(outs["prev"]).read_text())
                ks = kept_since(prev["entities"], new["entities"], new["ocr"] + new["sigs"] + new.get("shorts", []))
                res["lines"].append(f"  vs {args.prev}: held lines now held by nothing {len(ks['lost'])}, "
                                    f"note lines now glued into an entity's text {len(ks['glued'])}")
                for page, line in ks["lost"][:args.detail]:
                    res["lines"].append(f"    LOST p{page}: {line[:90]}")
                for page, line in ks["glued"][:args.detail]:
                    res["lines"].append(f"    GLUED p{page}: {line[:90]}")
                res["summary"] += f", vs prev: lost {len(ks['lost'])}, glued {len(ks['glued'])}"
            oc, nc = old.get("coverage"), new.get("coverage")
            if nc:
                res["lines"].append("  coverage: old " + (f"{oc['unheld']}/{oc['contentLines']}" if oc else "n/a")
                                    + f" unheld, new {nc['unheld']}/{nc['contentLines']} unheld")
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
