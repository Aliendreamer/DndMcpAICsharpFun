"""Coverage: printed content lines that no entity holds (and that were not left out on purpose)."""
from __future__ import annotations

import re
from collections.abc import Iterable
from dataclasses import dataclass, field

from .layout import fix_digits

MIN_CHARS = 30  # an OCR line this long is content, not a label or a page number
MIN_SCORE = 0.9
MIN_REDUCED = 20


def reduce_text(text: str) -> str:
    """Lower-case letters and digits only: whitespace, punctuation and case differences do not matter."""
    return re.sub(r"[^a-z0-9]", "", text.lower())


def is_content_line(text: str, score: float) -> bool:
    t = text.strip()
    return len(t) >= MIN_CHARS and score >= MIN_SCORE and len(reduce_text(t)) >= MIN_REDUCED


@dataclass
class Coverage:
    content_lines: int
    unheld: list[tuple[int, str]] = field(default_factory=list)

    def to_dict(self) -> dict:
        return {"contentLines": self.content_lines, "unheld": len(self.unheld),
                "unheldLines": [{"page": p, "text": t} for p, t in self.unheld]}


def measure(lines: Iterable[tuple[int, str, float]], held_texts: Iterable[str],
            left_out: Iterable[tuple[int, str]]) -> Coverage:
    """`lines` are (page, text, score); a content line is held when its reduced text (trailing hyphen dropped, or its
    digit-repaired form) sits in the reduced held texts, or it lies inside a left-out text of the same page."""
    held = "|".join(reduce_text(t) for t in held_texts)
    left: dict[int, list[str]] = {}
    for pg, t in left_out:
        left.setdefault(pg, []).append(t)
    content = 0
    unheld: list[tuple[int, str]] = []
    for page, text, score in lines:
        t = text.strip()
        if not is_content_line(t, score):
            continue
        content += 1
        base = t[:-1] if t.endswith("-") else t
        if reduce_text(base) in held or reduce_text(fix_digits(base)) in held:
            continue
        if any(t in lo for lo in left.get(page, ())):
            continue
        unheld.append((page, t))
    return Coverage(content, unheld)
