"""Two-column reading order, same-row fragment merging, digit repair, hyphen joining."""
from __future__ import annotations

import re
from dataclasses import replace

from .model import Fragment, Line, Page

_DIGITISH = str.maketrans({"S": "5", "s": "5", "O": "0", "o": "0", "l": "1", "I": "1",
                           "i": "1", "Z": "2", "z": "2", "B": "8"})
_D = "0-9SOolIZB"
_NUMERIC_TOKEN = re.compile(rf"[{_D}]*\d[{_D}]*")
_DICE_TOKEN = re.compile(rf"[{_D}]+d[{_D}]+")
_FRACTION = re.compile(rf"(?<![A-Za-z0-9])[{_D}]+/[{_D}]+(?![A-Za-z0-9])")
_CHALLENGE = re.compile(r"(?<=Challenge )[SOolIZB]+(?![A-Za-z0-9/])")


def fix_digits(text: str) -> str:
    """Repair OCR digit noise (S->5, O->0, l/I->1, Z->2, B->8) only inside numeric/dice tokens.

    Letters are rewritten only in tokens that already contain a real digit (or a dice/fraction
    shape with one), so plain words such as "Bludgeoning" are untouched.
    """
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
        if not re.search(r"\d", m.group(0)):
            return m.group(0)  # e.g. "I/O" stays
        a, b = m.group(0).split("/", 1)
        return fix(a) + "/" + fix(b)

    text = _FRACTION.sub(frac, text)
    text = _CHALLENGE.sub(lambda m: fix(m.group(0)), text)
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
    return [r for i, r in enumerate(rows)
            if not _is_edge_noise(r, page) or _heads_text(r, rows[i + 1] if i + 1 < len(rows) else None, page)]


def _heads_text(line: Line, nxt: Line | None, page: Page) -> bool:
    """A top-edge caps line with body text directly below it in its column is a heading ('ARCANE
    GRIMOIRE' over its type line), not a running header, which stands apart from the text."""
    if nxt is None or line.box[1] > 0.5 * page.height or not re.fullmatch(r"[A-Z][A-Z '&-]{2,40}", line.text):
        return False
    same_col = abs(nxt.box[0] - line.box[0]) < 3 * line.height
    return same_col and 0 <= nxt.box[1] - line.box[3] < 0.6 * line.height


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
