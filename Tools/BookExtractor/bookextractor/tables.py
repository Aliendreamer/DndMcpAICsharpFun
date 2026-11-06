"""Class-table reader: header boxes define columns, ordinal level cells anchor rows. Works on raw OCR pages."""
from __future__ import annotations

import re
from dataclasses import dataclass
from statistics import median

from rapidfuzz import fuzz

from .layout import fix_digits
from .model import Line, Page
from .parsers.charcommon import ordinal

MAX_ROWS = 20


@dataclass
class Table:
    headers: list[str]
    rows: list[list[str]]
    page: int
    lines: list[Line]
    # page-pixel extent (x0, y0, x1, y1) from the header top to the last row's bottom, across the columns the
    # header spans (a cell may overhang to the right; prose beside the table is outside it)
    box: tuple[float, float, float, float] | None = None


def _yc(l: Line) -> float:
    return (l.box[1] + l.box[3]) / 2


def _xc(l: Line) -> float:
    return (l.box[0] + l.box[2]) / 2


def _overlap(a: Line, lo: float, hi: float) -> bool:
    return a.box[0] < hi and a.box[2] > lo


def _split(l: Line, bounds: list[float], column, right: float, spans) -> list[Line]:
    """Split an OCR line that runs into the next column ('Primal Champion Unlimited' covering the Features
    and Rages cells). Only words that start past `right` -- how far the other lines starting in this column
    reach -- move, and only onto the cells of the column they land in (`spans(col)`: x extent of that
    column's other cells), so a long cell that merely overhangs ('Spellcasting, Arcane Recovery', 'Ability
    Score Improvement, Destroy Undead') stays whole. Each word is placed by its character offset."""
    x0, x1 = l.box[0], l.box[2]
    w = x1 - x0
    if w <= 0 or x1 <= right or not any(x0 < b < x1 for b in bounds):
        return [l]
    per = w / max(len(l.text), 1)
    words = [(m.start(), m.end(), m.group(0)) for m in re.finditer(r"\S+", l.text)]
    cut = next((k for k, (a, _, _) in enumerate(words) if k and x0 + a * per > right), None)
    if cut is None:
        return [l]

    def part(ws: list[tuple[int, int, str]]) -> Line:
        return Line(text=" ".join(t for _, _, t in ws), box=(x0 + ws[0][0] * per, l.box[1], x0 + ws[-1][1] * per,
                                                             l.box[3]), height=l.height, page=l.page, frags=l.frags)

    head, tail = part(words[:cut]), part(words[cut:])
    if column(tail) == column(head):
        return [l]
    lo, hi = spans(column(tail))
    return [head, tail] if tail.box[0] < hi and tail.box[2] > lo else [l]


def _fill_dropped_rows(anchors: list[Line], pitch: float) -> list[Line]:
    """The OCR can drop a level cell entirely (the Monk's '3rd'). Where two readable ordinals two apart
    ('2nd', '4th') sit two row pitches apart, put an empty level cell between them so later rows keep
    their level."""
    out: list[Line] = []
    for a, b in zip(anchors, anchors[1:] + [None]):
        out.append(a)
        if b is None or len(out) >= MAX_ROWS:
            continue
        oa, ob = ordinal(a.text.strip()), ordinal(b.text.strip())
        if oa is not None and ob == oa + 2 and b.box[1] - a.box[1] > 1.6 * pitch:
            mid = (a.box[1] + b.box[1]) / 2
            out.append(Line(text="", box=(a.box[0], mid, a.box[2], mid + a.height), height=a.height, page=a.page))
    return out[:MAX_ROWS]


def _try_table(page: Page, anchor: Line) -> Table | None:
    h = anchor.height
    yc = _yc(anchor)
    row = sorted((l for l in page.lines if abs(_yc(l) - yc) <= 0.6 * h and l.box[0] >= anchor.box[0] - 5),
                 key=lambda l: l.box[0])
    if len(row) < 3:
        return None
    # Header cells stacked above the row label ("Proficiency" over "Bonus"). A line that spans several row
    # labels (e.g. "Spells Slots per Spell Level") is a group heading, not a column label.
    above: dict[int, list[Line]] = {}
    for l in page.lines:
        if l in row or not (yc - 2.4 * h <= _yc(l) < yc - 0.6 * h):
            continue
        hits = [i for i, r in enumerate(row) if _overlap(l, r.box[0], r.box[2])]
        if len(hits) == 1:
            above.setdefault(hits[0], []).append(l)
    cols: list[tuple[float, float, list[Line]]] = []
    for i, r in enumerate(row):
        extra = sorted(above.get(i, []), key=_yc)
        members = extra + [r]
        cols.append((min(m.box[0] for m in members), max(m.box[2] for m in members), members))
    headers = [" ".join(m.text for m in members) for _, _, members in cols]
    bounds = [(cols[i][1] + cols[i + 1][0]) / 2 for i in range(len(cols) - 1)]

    def column(l: Line) -> int:
        x = _xc(l)
        return sum(x > b for b in bounds)

    header_bottom = max(m.box[3] for _, _, ms in cols for m in ms)
    header_lines = [m for _, _, ms in cols for m in ms]
    region = [l for l in page.lines if l not in header_lines and l.box[1] >= header_bottom - 2]

    def start_col(l: Line) -> int:
        return sum(l.box[0] > b for b in bounds)

    def reach(l: Line) -> float:  # right edge of the other lines that start in l's column
        return max((o.box[2] for o in region if o is not l and start_col(o) == start_col(l)), default=l.box[2])

    def spans(c: int) -> tuple[float, float]:  # x extent of the lines that start in column c
        xs = [o.box for o in region if start_col(o) == c]
        return (min(b[0] for b in xs), max(b[2] for b in xs)) if xs else (0.0, 0.0)

    split = {id(l): _split(l, bounds, column, reach(l), spans) for l in region}
    body = [part for l in page.lines for part in split.get(id(l), [l])]
    first = sorted((l for l in body if column(l) == 0 and l.box[1] >= header_bottom - 2),
                   key=lambda l: l.box[1])
    anchors: list[Line] = []
    for l in first:
        if len(anchors) == MAX_ROWS:
            break
        t = l.text.strip()
        # A level cell the OCR garbled to a short numeric token (e.g. "416") still sits in the level column
        # inside an unbroken run of ordinals, so it keeps its row.
        garbled = anchors and len(t) <= 4 and any(c.isdigit() for c in t) and ordinal(t) is None
        if ordinal(t) is None and not garbled:
            break
        anchors.append(l)
    if not anchors or ordinal(anchors[0].text.strip()) is None:
        return None
    pitch = median(b.box[1] - a.box[1] for a, b in zip(anchors, anchors[1:])) if len(anchors) > 1 else 3 * h
    anchors = _fill_dropped_rows(anchors, pitch)
    tol = 0.5 * h
    tops = [a.box[1] - tol for a in anchors] + [anchors[-1].box[1] + 1.5 * pitch]
    cells: list[list[list[Line]]] = [[[] for _ in cols] for _ in anchors]
    used: list[Line] = [m for _, _, ms in cols for m in ms]
    for l in body:  # rows bound the cells; a first-row cell may start above the last header's bottom
        if l in used:
            continue
        top = l.box[1]
        for i in range(len(anchors)):
            if tops[i] <= top < tops[i + 1]:
                cells[i][column(l)].append(l)
                used.append(l)
                break
    rows = [[fix_digits(" ".join(x.text for x in sorted(c, key=lambda x: (x.box[1], x.box[0])))) for c in r]
            for r in cells]
    x0 = min(c[0] for c in cols)
    inside = [l for l in used if l.box[0] < cols[-1][1]]
    box = (x0, min(m.box[1] for m in header_lines), max(l.box[2] for l in inside), max(l.box[3] for l in inside))
    return Table(headers=headers, rows=rows, page=page.number, lines=used, box=box)


def read_table(page: Page, first_header: str) -> Table | None:
    """Read the class table whose first header cell fuzzy-matches `first_header`. None when there is none."""
    cands = sorted((l for l in page.lines if fuzz.ratio(l.text.strip().lower(), first_header.lower()) >= 85),
                   key=lambda l: l.box[1])
    for c in cands:
        t = _try_table(page, c)
        if t:
            return t
    return None
