"""Two-column reading order, same-row fragment merging, digit repair, hyphen joining."""
from __future__ import annotations

import re
from dataclasses import replace

from .model import Fragment, Line, Page

_DIGITISH = str.maketrans({"S": "5", "s": "5", "O": "0", "o": "0", "l": "1", "I": "1",
                           "i": "1", "Z": "2", "z": "2", "B": "8"})
_NUMERIC_TOKEN = re.compile(r"[0-9SOolIZ]*\d[0-9SOolIZ]*")
_DICE_TOKEN = re.compile(r"[0-9SOolIZ]+d[0-9SOolIZ]+")
_FRACTION_TOKEN = re.compile(r"[0-9SOolIZ]+/[0-9SOolIZ]+")


def fix_digits(text: str) -> str:
    """Repair OCR digit noise (S->5, O->0, l/I->1, Z->2, B->8) only inside numeric/dice tokens."""
    def fix(token: str) -> str:
        return token.translate(_DIGITISH)

    def tok(m: re.Match[str]) -> str:
        w = m.group(0)
        if _DICE_TOKEN.fullmatch(w):
            a, b = w.split("d", 1)
            return fix(a) + "d" + fix(b)
        if _NUMERIC_TOKEN.fullmatch(w):
            return fix(w)
        return w

    def frac(m: re.Match[str]) -> str:
        a, b = m.group(0).split("/", 1)
        return fix(a) + "/" + fix(b)

    # fractions first (Challenge l/4); tokens of digit-like letters only, lone letters excluded below
    text = re.sub(r"(?<![A-Za-z0-9])[0-9SOolIZ]+/[0-9SOolIZ]+(?![A-Za-z0-9])", frac, text)
    return re.sub(r"[0-9A-Za-z]+", tok, text)


def _is_edge_noise(line: Line, page: Page) -> bool:
    edge = line.box[3] < 0.05 * page.height or line.box[1] > 0.95 * page.height
    if not edge:
        return False
    t = line.text
    return bool(re.fullmatch(r"\d{1,3}", t) or "|" in t or re.match(r"(?i)^chapter \d", t)
                or re.fullmatch(r"[A-Z][A-Z '&-]{2,40}", t))


def reading_order(page: Page) -> list[Line]:
    """Left column then right column, same-row fragments merged (sorted by x0), headers/footers dropped."""
    mid = page.width / 2
    lines = [l for l in page.lines if l.text]
    rows: list[Line] = []
    for col in (0, 1):
        cl = sorted((l for l in lines if (1 if (l.box[0] + l.box[2]) / 2 > mid else 0) == col),
                    key=lambda l: l.box[1])
        col_rows: list[Line] = []
        for l in cl:
            last = col_rows[-1] if col_rows else None
            if last and abs((last.box[1] + last.box[3]) / 2 - (l.box[1] + l.box[3]) / 2) \
                    < 0.5 * min(last.height, l.height):
                frags: list[Fragment] = sorted(last.frags + l.frags, key=lambda f: f.box[0])
                x0 = min(last.box[0], l.box[0]); x1 = max(last.box[2], l.box[2])
                y0 = min(last.box[1], l.box[1]); y1 = max(last.box[3], l.box[3])
                col_rows[-1] = Line(" ".join(f.text for f in frags), (x0, y0, x1, y1),
                                    max(last.height, l.height), page.number, frags)
            else:
                col_rows.append(replace(l, frags=list(l.frags)))
        rows.extend(col_rows)
    return [r for r in rows if not _is_edge_noise(r, page)]


def join_hyphens(lines: list[Line]) -> list[Line]:
    """Join a line ending in '-' with a following lowercase-start line (dehyphenate)."""
    out: list[Line] = []
    for l in lines:
        if out and out[-1].text.endswith("-") and l.text[:1].islower():
            p = out[-1]
            box = (min(p.box[0], l.box[0]), min(p.box[1], l.box[1]),
                   max(p.box[2], l.box[2]), max(p.box[3], l.box[3]))
            out[-1] = Line(p.text[:-1] + l.text, box, max(p.height, l.height), p.page, p.frags + l.frags)
        else:
            out.append(l)
    return out
