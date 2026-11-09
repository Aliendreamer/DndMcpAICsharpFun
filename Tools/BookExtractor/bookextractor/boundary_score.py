"""Token-multiset F1 used to score section-boundary trimming against corrected text."""
from __future__ import annotations

import re
from collections import Counter


def tokens(s: str) -> list[str]:
    return re.findall(r"\w+", s.lower())


def f1(a: str, b: str) -> float:
    ca, cb = Counter(tokens(a)), Counter(tokens(b))
    if not ca or not cb:
        return 0.0
    common = sum((ca & cb).values())
    if common == 0:
        return 0.0
    p, r = common / sum(cb.values()), common / sum(ca.values())
    return 2 * p * r / (p + r)


def entries_text(entries: list) -> str:
    """Join entries (strings or {"items": [...]} dicts) into one space-separated text."""
    parts: list[str] = []
    for e in entries:
        if isinstance(e, str):
            parts.append(e)
        elif isinstance(e, dict):
            parts.extend(i for i in e.get("items", []) if isinstance(i, str))
    return " ".join(parts)
