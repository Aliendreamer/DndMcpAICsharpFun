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
                or re.fullmatch(r"(?:\d{1,3} )?[A-Z][A-Z '&-]{2,40}(?: \d{1,3})?", t))  # "SLAADI 277", "166 GOBLINS"


def reading_order(page: Page) -> list[Line]:
    """Left column then right column, same-row fragments merged (sorted by x0), headers/footers dropped."""
    mid = page.width / 2
    lines = [l for l in page.lines if l.text]
    columns: list[list[Line]] = []
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
        columns.append(col_rows)
    rows = _interleave_stat_blocks(*columns, page.height)
    return [r for i, r in enumerate(rows)
            if not _is_edge_noise(r, page) or _heads_text(r, rows[i + 1] if i + 1 < len(rows) else None, page)]


_SIZE_LINE = re.compile(r"^(?:tiny|small|medium|large|huge|gargantuan)\b", re.I)
_ARMOR_CLASS = re.compile(r"^armor class\b", re.I)


def _stat_block_starts(rows: list[Line]) -> list[int]:
    """Indices of name lines that head a stat block (name, size line, then Armor Class within 3 rows)."""
    return [i for i in range(len(rows) - 2)
            if _SIZE_LINE.match(rows[i + 1].text)
            and any(_ARMOR_CLASS.match(r.text) for r in rows[i + 2:i + 5])]


_STAT_MARKER = re.compile(r"\b(?:Melee|Ranged) (?:Weapon|Spell) Attack\b|^(?:Bonus Actions|Reactions|Actions|Legendary Actions)$"
                          r"|^Multiattack\b|\bHit:|^[A-Z][\w' ]+ \((?:Recharge|\d+/Day|Costs)", re.I)


def _continues_block(rows: list[Line], c: int) -> bool:
    """rows[:c] is the stat block's own right-column text, not lore above a box: the cut at c falls inside a
    sentence (no gap, no sentence end before it, a lower-case start after it) and rows[:c] holds stat-block
    features. Reading the block's name ahead of that text would hand its traits to the neighbouring block."""
    if c == 0 or rows[c].box[1] - rows[c - 1].box[3] > 2 * rows[c].height:
        return False
    mid_sentence = not rows[c - 1].text.rstrip().endswith((".", "!", "?", ":", '"', "\u201d")) \
        and rows[c].text.lstrip()[:1].islower()
    return mid_sentence and any(_STAT_MARKER.search(r.text.strip()) for r in rows[:c])


PROSE_TAIL_MAX_GAP = 0.5  # in line heights: the cut sits at normal line spacing (no box edge above it)


def _is_prose_tail(left_last: Line, right: list[Line], c: int) -> bool:
    """The block ends a finished sentence in the left column while right[:c] ends mid-sentence and right[c]
    carries on in lower case at normal spacing: the right column is prose lore running past the block's name,
    not the block's continuation, so cutting it at c would glue the lore's tail into the block."""
    if c == 0 or right[c].box[1] - right[c - 1].box[3] >= PROSE_TAIL_MAX_GAP * right[c].height:
        return False
    ends = (".", "!", "?", '"', "\u201d")
    return left_last.text.rstrip().endswith(ends) and not right[c - 1].text.rstrip().endswith(ends + (":",)) \
        and right[c].text.lstrip()[:1].islower()


def _interleave_stat_blocks(left: list[Line], right: list[Line], page_height: float) -> list[Line]:
    """A stat block set in a two-column box starts in the left column, runs to its foot, and carries on in
    the right column at the same height as its name line (lore may sit above it there). Read that
    continuation straight after the block's left half instead of after the whole left column."""
    starts = _stat_block_starts(left)
    if not starts:
        return left + right
    s = starts[-1]
    # the block's rows run on until a footer or a gap too wide to sit inside one stat block
    split = next((i for i in range(s + 1, len(left))
                  if left[i].box[1] > 0.94 * page_height
                  or left[i].box[1] - left[i - 1].box[3] > 3 * left[s].height), len(left))
    if left[split - 1].box[3] < 0.85 * page_height:
        return left + right  # the block ends inside the left column: nothing is cut
    first_right_block = min(_stat_block_starts(right), default=len(right))  # that block is the right's own
    tol = 0.6 * left[s].height
    c = next((j for j in range(first_right_block) if abs(right[j].box[1] - left[s].box[1]) <= tol), None)
    if c is None or _continues_block(right, c) or _is_prose_tail(left[split - 1], right, c):
        return left + right  # the right text flows on from the block itself: it is no boxed lore
    return left[:s] + right[:c] + left[s:split] + right[c:] + left[split:]


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
