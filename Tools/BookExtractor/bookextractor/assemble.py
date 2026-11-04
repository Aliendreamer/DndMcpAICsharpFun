"""Assembler: pages -> parsed entities -> ids, common checks, provenance, report."""
from __future__ import annotations

import re
from collections import Counter
from dataclasses import dataclass

from .checks import Check
from .layout import fix_digits, reading_order
from .model import Line, Page
from .names import Vocabulary, build_vocabulary, own_heading_counts, repair_name
from .pages import classify_page, is_table_page
from .parsers import Parsed
from .parsers.item import parse_items
from .parsers.monster import parse_monsters
from .parsers.spell import parse_spells
from .report import build_report

ALL_TYPES = ("Monster", "Spell", "MagicItem")
PROSE_LINES_PER_PAGE = 10
_PARSERS = {"Monster": parse_monsters, "Spell": parse_spells, "MagicItem": parse_items}
_DICE_LIKE = re.compile(r"\b[0-9SOolIZB]+d[0-9SOolIZB]+\b")
_MIXED = re.compile(r"\b(?:[0-9]+[SOolIZB]+|[SOolIZB]+[0-9]+)[0-9SOolIZB]*\b")
_NUMERIC_KEYS = {"formula": re.compile(r"\d+d\d+(?:\s*[+-]\s*\d+)?"), "cr": re.compile(r"[\d/]+")}
# Numeric fields are checked directly by key (any string value there must be a clean number).
_PLAIN_NUMBER = re.compile(r"[+-]?\d+(?:[./,]\d+)*")
_NUMBER_KEYS = {"ac", "average", "passive", "str", "dex", "con", "int", "wis", "cha", "level", "number",
                "amount", "walk", "fly", "swim", "climb", "burrow", "hover"}


@dataclass
class ExtractionResult:
    entities: list[dict]
    report: dict


def slugify(name: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", name.lower()).strip("-")


def _is_prose(text: str) -> bool:
    letters = sum(c.isalpha() for c in text)
    return len(text) >= 25 and len(text.split()) >= 5 and letters / len(text) >= 0.75


def _provenance(lines: list[Line], pages: dict[int, Page]) -> dict:
    """Per page, one box per column (union of that column's lines), in page-image pixels."""
    by_page: dict[int, dict[int, list[tuple[float, float, float, float]]]] = {}
    for l in lines:
        pg = pages.get(l.page)
        mid = (pg.width / 2) if pg else 0
        col = 1 if pg and (l.box[0] + l.box[2]) / 2 > mid else 0
        by_page.setdefault(l.page, {}).setdefault(col, []).append(l.box)
    out = []
    for number in sorted(by_page):
        boxes = []
        for col in sorted(by_page[number]):
            bs = by_page[number][col]
            boxes.append({"x0": min(b[0] for b in bs), "y0": min(b[1] for b in bs),
                          "x1": max(b[2] for b in bs), "y1": max(b[3] for b in bs)})
        out.append({"page": number, "boxes": boxes})
    return {"pages": out}


def _repair_digits(v):
    """Apply the OCR digit repair (ld6 -> 1d6, I00 -> 100) to every string, in place for containers."""
    if isinstance(v, str):
        return fix_digits(v)
    if isinstance(v, list):
        return [_repair_digits(x) for x in v]
    if isinstance(v, dict):
        return {k: _repair_digits(x) for k, x in v.items()}
    return v


def _numeric_problems(fields: dict) -> list[str]:
    """Letters left inside numbers after repair. Deliberately also scans prose entries (damage dice live in
    descriptions, not only in structured fields), so a digit-noise token the repair could not fix is flagged."""
    bad: list[str] = []

    def walk(v, path: str) -> None:
        if isinstance(v, dict):
            for k, x in v.items():
                if k in _NUMERIC_KEYS and isinstance(x, str) and not _NUMERIC_KEYS[k].fullmatch(x.strip()):
                    bad.append(f"{path}.{k}={x!r}")
                if k in _NUMBER_KEYS and isinstance(x, str) and not _PLAIN_NUMBER.fullmatch(x.strip()):
                    bad.append(f"{path}.{k}={x!r}")
                walk(x, f"{path}.{k}")
        elif isinstance(v, list):
            for i, x in enumerate(v):
                walk(x, f"{path}[{i}]")
        elif isinstance(v, str):
            for m in _DICE_LIKE.finditer(v):
                if re.search(r"\d", m.group(0)) and not re.fullmatch(r"\d+d\d+", m.group(0)):
                    bad.append(f"{path}: {m.group(0)!r}")
            for m in _MIXED.finditer(v):
                bad.append(f"{path}: {m.group(0)!r}")

    walk(fields, "fields")
    return bad


def _replace_heading(text: str, old_name: str, new_name: str) -> str:
    """Put the repaired name on the first line of canonicalText when that line is the printed heading."""
    first, sep, rest = text.partition("\n")
    squash = lambda s: re.sub(r"[^a-z]", "", s.lower())  # noqa: E731
    if squash(first) == squash(old_name):
        return new_name + sep + rest
    return text


def _id_and_checks(p: Parsed, book_slug: str, seen: Counter, vocab: Vocabulary) -> tuple[str, list[Check]]:
    p.fields = _repair_digits(p.fields)
    p.text = fix_digits(p.text)
    raw = p.name
    res = repair_name(raw, vocab, own_heading_counts(raw)) if raw.strip() else None
    name = res.name if res else ""
    checks = list(p.checks)
    if res and res.repaired:
        checks.append(Check("name-repaired", True, f"{res.original!r} -> {res.name!r}"))
    checks.append(Check("name-found", bool(name.strip()), "" if name.strip() else "no entity name found"))
    if res:
        checks.append(Check("name-words-known", not res.unknown,
                            "" if not res.unknown else "unknown words in name: " + ", ".join(res.unknown)))
    else:
        checks.append(Check("name-words-known", False, "no name to check"))
    checks.append(Check("text-present", len(p.text.strip()) >= 30, "" if len(p.text.strip()) >= 30
                        else "entity text is empty or too short"))
    bad = _numeric_problems(p.fields)
    checks.append(Check("numeric-clean", not bad, "" if not bad else "letters in numeric values: " + "; ".join(bad[:4])))
    page = p.lines[0].page if p.lines else 0
    base = f"{book_slug}.{p.type.lower()}.{slugify(name) or f'unnamed-{page}'}"
    seen[base] += 1
    eid = base if seen[base] == 1 else f"{base}-{seen[base]}"
    checks.append(Check("unique-id", seen[base] == 1,
                        "" if seen[base] == 1 else f"id {base} already used; suffixed -{seen[base]}"))
    p.name = name
    if res and res.repaired:
        p.text = _replace_heading(p.text, raw, name)
    return eid, checks


def extract_book(pages: list[Page], book_slug: str, types: set[str]) -> ExtractionResult:
    by_number = {p.number: p for p in pages}
    kinds = {p.number: classify_page(p) for p in pages}
    rules = [p for p in pages if kinds[p.number] == "rules"]
    ordered = {p.number: reading_order(p) for p in pages if kinds[p.number] in ("rules", "art")}

    listed = [l.text for p in pages if kinds[p.number] in ("index", "toc") for l in p.lines]
    vocab = build_vocabulary((l.text for p in rules for l in ordered[p.number]), listed)

    body: list[Line] = [l for p in pages if p.number in ordered for l in ordered[p.number]]
    entities: list[dict] = []
    covered: set[int] = set()
    seen: Counter = Counter()
    for t in ALL_TYPES:
        if t not in types:
            continue
        for p in _PARSERS[t](body):
            eid, checks = _id_and_checks(p, book_slug, seen, vocab)
            covered.update(l.page for l in p.lines)
            entities.append({
                "id": eid, "type": p.type, "name": p.name,
                "page": p.lines[0].page if p.lines else 0,
                "canonicalText": p.text, "fields": p.fields,
                "checks": [{"name": c.name, "passed": c.passed, "reason": c.reason} for c in checks],
                "provenance": _provenance(p.lines, by_number),
            })

    for p in rules:
        if sum(_is_prose(l.text) for l in ordered[p.number]) >= PROSE_LINES_PER_PAGE or is_table_page(p):
            covered.add(p.number)
    rules_numbers = {p.number for p in rules}
    report = build_report(book_slug, len(pages), len(rules_numbers), len(covered & rules_numbers), entities)
    return ExtractionResult(entities=entities, report=report)
