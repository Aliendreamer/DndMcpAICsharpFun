"""Class-table reader: header boxes define columns, ordinal level cells anchor rows. Works on raw OCR pages."""
from __future__ import annotations

import re
from dataclasses import dataclass, field
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
    x_left = min(c[0] for c in cols)
    # prose in the page column left of the table (TCE prints a table in the right column beside text) and an
    # art credit set far taller than the cells, over the table, are not cells
    lines = [l for l in page.lines if l.box[2] > x_left and l.height <= 2 * h]
    region = [l for l in lines if l not in header_lines and l.box[1] >= header_bottom - 2]

    def start_col(l: Line) -> int:
        return sum(l.box[0] > b for b in bounds)

    def reach(l: Line) -> float:  # right edge of the other lines that start in l's column
        return max((o.box[2] for o in region if o is not l and start_col(o) == start_col(l)), default=l.box[2])

    def spans(c: int) -> tuple[float, float]:  # x extent of the lines that start in column c
        xs = [o.box for o in region if start_col(o) == c]
        return (min(b[0] for b in xs), max(b[2] for b in xs)) if xs else (0.0, 0.0)

    split = {id(l): _split(l, bounds, column, reach(l), spans) for l in region}
    body = [part for l in lines for part in split.get(id(l), [l])]
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


# ---------- label-anchored tables (equipment, treasure): one Row per printed row ----------
_MARK = re.compile(r"\s*([*†‡]+)$")


@dataclass
class Row:
    label: str
    cells: dict[str, str]          # header -> cell text ("" when the book prints nothing there)
    group: str | None              # the sub-heading row the row sits under ("Simple Melee Weapons")
    marker: str | None             # a footnote marker printed after the label ("*", "†")
    lines: list[Line]
    raw: dict[str, str] = field(default_factory=dict)  # the cells as the OCR read them, before the digit repair


@dataclass
class RowTable:
    headers: list[str]
    rows: list[Row]
    page: int
    box: tuple[float, float, float, float]
    # lines inside the table that no row, cell or sub-heading took (an OCR fragment between the header and the
    # first row, a line set far taller than the cells): text the reader could not place, kept so checks can flag it
    unassigned: list[Line] = field(default_factory=list)


def _continues(prev: str, line: str) -> bool:
    """Whether a cell-less first-column line reads as the rest of the label above it: it starts in lower case or
    with "(", or the label above stops mid-phrase (a trailing comma or hyphen, an unclosed parenthesis)."""
    prev, line = prev.strip(), line.strip()
    return bool(line) and (line[0].islower() or line[0] == "(" or prev.endswith((",", "-", "(")) or
                           prev.count("(") > prev.count(")"))


def _header_cells(page: Page, first: Line, headers: list[str]) -> list[Line] | None:
    """The header cells of a table whose first cell is `first`: the lines on its row, left to right, matching
    the remaining headers in order (fuzzy: the OCR reads "Cost" as "csst"). A header set on two lines
    ("Carrying" over "Capacity") matches the two joined."""
    h = first.height
    row = sorted((l for l in page.lines if l is not first and l.box[0] > first.box[2] and
                  abs(_yc(l) - _yc(first)) <= 0.6 * h), key=lambda l: l.box[0])
    out = [first]
    for want in headers[1:]:
        if not row:
            return None
        l = row.pop(0)
        above = [o for o in page.lines if _overlap(o, l.box[0], l.box[2]) and
                 0.5 * h < l.box[1] - o.box[1] < 2 * h and o.box[3] < _yc(l)]
        texts = [l.text] + [" ".join(o.text for o in above + [l])] if above else [l.text]
        if max(fuzz.ratio(t.strip().lower(), want.lower()) for t in texts) < 70:
            return None
        out.append(l)
    return out


def _read_one(page: Page, head: list[Line], headers: list[str], right: float) \
        -> tuple[list[Row], tuple, list[Line]] | None:
    h = median(x.height for x in head)
    bounds = [(a.box[2] + b.box[0]) / 2 for a, b in zip(head, head[1:])]
    x_lo, x_hi = head[0].box[0] - 2 * h, min(right, head[-1].box[2] + h)
    top = max(x.box[3] for x in head)
    region = sorted((l for l in page.lines if l not in head and _yc(l) > top and x_lo <= l.box[0] < x_hi
                     and l.height <= 2 * h), key=lambda l: (l.box[1], l.box[0]))

    def column(l: Line) -> int:
        return sum(_xc(l) > b for b in bounds)

    # the table ends at a footnote ("* See the ..."), a gap of three rows, or the next table: a repeated first
    # header ("Item") with the title printed just above it ("Waterborne Vehicles")
    first = [l for l in region if column(l) == 0]
    end = page.height
    last = top
    for l in first:
        t = l.text.strip()
        if (len(t) > 2 and t[0] in "*†‡") or l.box[1] - last > 3 * 1.2 * h:
            end = l.box[1]
            break
        if fuzz.ratio(t.lower(), headers[0].lower()) >= 80:
            end = min([l.box[1]] + [o.box[1] for o in region if o.box[0] < bounds[0] and
                                     l.box[1] - h <= o.box[3] <= l.box[1] + 0.3 * h and o is not l])
            break
        last = l.box[3]
    region = [l for l in region if l.box[1] < end - 0.3 * h]
    first = [l for l in region if column(l) == 0]
    others = [l for l in region if column(l) > 0]

    def own_cells(l: Line) -> list[Line]:
        return [o for o in others if abs(_yc(o) - _yc(l)) <= 0.6 * h]

    rows: list[tuple[list[Line], str | None]] = []   # label lines, group
    group: tuple[str, float, bool] | None = None   # name, x0, rows indented under it
    headings: list[Line] = []
    for k, l in enumerate(first):
        if own_cells(l):
            if group and group[2] and l.box[0] <= group[1] + 0.4 * h:
                group = None               # back at an indented heading's own indent: out of its group
            rows.append(([l], group[0] if group else None))
            continue
        nxt = first[k + 1] if k + 1 < len(first) else None
        if l.box[2] > head[1].box[0]:      # a cell-less line reaching into the next column is prose: stop
            end = min(end, l.box[1])
            break
        prev = rows[-1][0] if rows else None
        if nxt is not None and nxt.box[0] > l.box[0] + 0.4 * h:
            group = (l.text.strip(), l.box[0], True)
            headings.append(l)
        elif prev and l.box[0] >= prev[0].box[0] - 0.4 * h and l.box[1] - prev[-1].box[3] < 0.8 * h and \
                _continues(" ".join(x.text for x in prev), l.text):
            prev.append(l)                 # a label wrapping onto a line with no cells of its own
        elif nxt is not None:              # a sub-heading the rows below are not indented under
            group = (l.text.strip(), l.box[0], False)
            headings.append(l)
    if not rows:
        return None
    centres = [_yc(r[0][0]) for r in rows]
    pitch = median(b - a for a, b in zip(centres, centres[1:])) if len(centres) > 1 else 1.2 * h
    # the last row reaches to the table end, at most a row pitch past its label (a cell wrapping below it)
    starts = [c - 0.6 * h for c in centres] + [min(end - 0.3 * h, rows[-1][0][-1].box[3] + pitch + 0.3 * h)]
    cells: list[list[list[Line]]] = [[[] for _ in head[1:]] for _ in rows]
    for o in others:
        for i in range(len(rows)):
            if starts[i] <= _yc(o) < starts[i + 1]:
                cells[i][column(o) - 1].append(o)
                break
    out: list[Row] = []
    used: list[Line] = list(head)
    for (labels, grp), cs in zip(rows, cells):
        label = " ".join(x.text.strip() for x in labels)
        m = _MARK.search(label)
        marker = m.group(1) if m else None
        if m:
            label = label[:m.start()].strip()
        raw = {headers[i + 1]: " ".join(x.text.strip() for x in sorted(c, key=lambda x: (x.box[1], x.box[0])))
               for i, c in enumerate(cs)}
        texts = {h: fix_digits(t) for h, t in raw.items()}
        lines = sorted(labels + [x for c in cs for x in c], key=lambda x: (x.box[1], x.box[0]))
        used += lines
        out.append(Row(label=label, cells=texts, group=grp, marker=marker, lines=lines, raw=raw))
    taken = used + headings
    box = (min(l.box[0] for l in taken), min(l.box[1] for l in taken), max(l.box[2] for l in taken),
           max(l.box[3] for l in taken))
    ids = {id(l) for l in taken}
    unassigned = [l for l in page.lines if id(l) not in ids and box[0] <= _xc(l) <= box[2] and
                  box[1] <= _yc(l) <= box[3]]
    return out, box, unassigned


def read_row_tables(page: Page, headers: list[str]) -> list[RowTable]:
    """Every label-anchored table on `page` whose header row prints `headers` (fuzzy) left to right, top to
    bottom: one Row per first-column label, with its cells by header, the sub-heading it sits under, and any
    footnote marker. A table printed twice side by side (the PHB Adventuring Gear table) reads as one, left
    half first."""
    cands = sorted((l for l in page.lines if fuzz.ratio(l.text.strip().lower(), headers[0].lower()) >= 80),
                   key=lambda l: (l.box[1], l.box[0]))
    seen: list[Line] = []
    out: list[RowTable] = []
    for c in cands:
        if c in seen or _header_cells(page, c, headers) is None:
            continue
        heads: list[list[Line]] = []
        # every copy of the header along the same row is the same table continued
        for o in sorted((o for o in cands if abs(_yc(o) - _yc(c)) <= 0.6 * c.height), key=lambda o: o.box[0]):
            seen.append(o)
            hd = _header_cells(page, o, headers)
            if hd is not None and not any(hd[0].box[0] < x[-1].box[2] for x in heads):
                heads.append(hd)
        rows: list[Row] = []
        boxes = []
        unassigned: list[Line] = []
        for k, hd in enumerate(heads):
            right = heads[k + 1][0].box[0] - 2 * hd[0].height if k + 1 < len(heads) else page.width
            got = _read_one(page, hd, headers, right)
            if got:
                rows += got[0]
                boxes.append(got[1])
                unassigned += got[2]
        if rows:
            box = (min(b[0] for b in boxes), min(b[1] for b in boxes), max(b[2] for b in boxes),
                   max(b[3] for b in boxes))
            out.append(RowTable(headers=list(headers), rows=rows, page=page.number, box=box,
                                unassigned=sorted(unassigned, key=lambda l: (l.box[1], l.box[0]))))
    return out


def read_rows(page: Page, headers: list[str]) -> RowTable | None:
    """The first table `read_row_tables` finds on `page`, or None when the page prints no such header row."""
    tables = read_row_tables(page, headers)
    return tables[0] if tables else None
