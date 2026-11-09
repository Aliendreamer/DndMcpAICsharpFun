"""Rules deciding where a section's printed text ends (sibling headings, sidebars, map labels)."""
from __future__ import annotations

import re
import statistics
from dataclasses import dataclass
from difflib import SequenceMatcher

HEADING_ANCHOR_FRAC = 0.9
HEADING_BODY_RATIO = 1.15
ANCHOR_SIMILARITY = 0.85

_MAP_RES = [
    re.compile(r"^MAP\s*\d", re.IGNORECASE),
    re.compile(r"\b1\s*square\s*=\s*\d+\s*(feet|foot|ft)\b", re.IGNORECASE),
    re.compile(r"^Side View$", re.IGNORECASE),
]
_ID_SUFFIX = re.compile(r"-p?\d+$")


@dataclass
class BLine:
    text: str
    page: int
    col: int
    h: float
    box: tuple[float, float, float, float]
    shade: float = 0.0


@dataclass
class Cut:
    drop: set[int]
    start: int | None
    rule: str | None  # "heading" | "shaded" | "map"


def headlike(text: str) -> bool:
    t = text.strip()
    words = t.split()
    if not 1 <= len(words) <= 7:
        return False
    if "|" in t or t[-1] in ".,;:" or not t[0].isupper():
        return False
    return sum(1 for w in words if len(w) > 3 and w[0].islower()) <= 1


def _norm(s: str) -> str:
    return "".join(c for c in s.casefold() if c.isalnum())


def find_anchor(lines: list[BLine], name: str, first_page: int) -> int | None:
    """Index of the single first-page line matching the entity name, else None."""
    target = _norm(_ID_SUFFIX.sub("", name.strip()))
    if not target:
        return None
    hits = [i for i, l in enumerate(lines)
            if l.page == first_page
            and SequenceMatcher(None, _norm(l.text), target).ratio() >= ANCHOR_SIMILARITY]
    return hits[0] if len(hits) == 1 else None


def _median_body(lines: list[BLine]) -> float:
    hs = [l.h for l in lines if len(l.text) >= 30] or [l.h for l in lines]
    return statistics.median(hs) if hs else 0.0


def cut(lines: list[BLine], name: str, first_page: int,
        sub_heading_idx: set[int] = frozenset(), shade_min: float = 15.0) -> Cut:
    anchor = find_anchor(lines, name, first_page)
    body = _median_body(lines)
    starts: list[tuple[int, str]] = []
    drop: set[int] = set()

    if anchor is not None:
        a = lines[anchor]
        for i in range(anchor + 1, len(lines)):
            c = lines[i]
            if (i not in sub_heading_idx and headlike(c.text)
                    and c.h >= HEADING_ANCHOR_FRAC * a.h and c.h >= HEADING_BODY_RATIO * body
                    and (c.col == a.col or c.page > a.page)):
                drop |= set(range(i, len(lines)))
                starts.append((i, "heading"))
                break
        i = anchor + 1
        while i < len(lines):
            if lines[i].shade <= shade_min:
                i += 1
                continue
            j = i
            while j < len(lines) and lines[j].shade > shade_min:
                j += 1
            prev = lines[i - 1].text.rstrip()
            if (headlike(lines[i].text) and lines[i].h >= HEADING_BODY_RATIO * body
                    and not re.search(r"reads?:$", prev, re.IGNORECASE)):
                drop |= set(range(i, j))
                starts.append((i, "shaded"))
            i = j

    for i, l in enumerate(lines):
        if i != anchor and any(r.search(l.text.strip()) for r in _MAP_RES):
            drop.add(i)
    drop -= set(sub_heading_idx)
    if anchor is not None:
        drop.discard(anchor)
    if not drop:
        return Cut(set(), None, None)
    live = [(i, r) for i, r in starts if i in drop]
    start, rule = min(live) if live else (min(drop), "map")
    return Cut(drop, start, rule)


_TOLERATED_CHECKS = {"text-complete", "neighbour-absorbed"}


def approvable(before: list, after: list, cut: Cut, name: str, checks: list[dict]) -> bool:
    """Approval gate for an automatic boundary trim (entries exclude the heading itself)."""
    from .boundary_score import entries_text, tokens

    if cut.rule not in ("heading", "shaded", "map") or not cut.drop or after == before:
        return False
    if not after or not before or after[0] != before[0]:
        return False
    a, b = tokens(entries_text(after)), tokens(entries_text(before))
    if not a or len(a) < 0.5 * len(b):
        return False
    return all(c.get("passed", True) or c.get("name") in _TOLERATED_CHECKS for c in checks)
