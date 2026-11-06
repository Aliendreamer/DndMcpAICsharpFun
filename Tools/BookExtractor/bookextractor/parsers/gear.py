"""Mundane item parser: adventuring gear, tools, packs, mounts and tack, vessels, trade goods, food, coins, the DMG
treasure tables (gemstones, art objects), explosives and firearm ammunition, and instrument lists printed as lead-ins.

Each printed table row is one Item named by its label cell exactly as printed ("Rope, hempen (50 feet)"). Tables are
found on any page by their header row, never by page number. The 5etools `type` code comes from the row's
sub-heading when it names a kind of gear (Ammunition A; Arcane focus, Druidic focus and Holy symbol SCF; Artisan's
tools AT; Gaming set GS; Musical instrument INS), else from the table's printed title (Adventuring Gear G, Tools T,
Mounts and Other Animals MNT, Waterborne Vehicles SHP, Food, Drink, and Lodging FD, Trade Goods TG, Standard Exchange
Rates $C, Explosives EXP|DMG). The "Tack, Harness, and Drawn Vehicles" table mixes tack and vehicles in one list, so
its rows carry no type. A sub-heading that is not a kind of gear is the first word of its rows' names ("Saddle" over
"Exotic" -> "Saddle, exotic", as the book itself prints "Clothes, fine").

Values use the 5etools encodings: `value` in copper, `weight` in lb. Rows naming a poison or a potion are left to the
DiseasePoison and MagicItem parsers; a row whose cells hold only a footnote mark ("Vehicles (land or water)*") points
elsewhere and is skipped.

Descriptions: a paragraph whose lead-in ("Crowbar. Using a crowbar ...") names a row - casing, a bracketed note
("Acid (vial)") and a plural aside - becomes that row's `entries`, but only inside the row's own chapter ("Chapter 5:
Equipment"; when no chapter heading precedes either, within ten pages). A lead-in naming no row but the part of a
label before its comma ("Rope." for "Rope, hempen (50 feet)" and "Rope, silk (50 feet)") describes those rows.
Lines inside a table are not description text, so a paragraph broken by a table page reads through it.

Other printed forms:

- packs: a lead-in "<Name> Pack (<cost>). Includes ..." is an Item (type G) whose `entries` list its contents;
- DMG gemstone and art object tables ("d12 | Stone Description" under "10 gp Gemstones", "d10 | Object" under "25 gp
  Art Objects"): each row is an Item, type $G|DMG or $A|DMG, valued from the table's title; a stone's bracketed
  description is its `entries`;
- firearm tables' Ammunition rows (DMG): type AF|DMG; a label printed under more than one age ("Bullets (10)") is
  prefixed with its table's age ("Renaissance bullets (10)");
- a "Musical Instruments" heading over "Name: description" lead-ins (SCAG): one Item (type INS) per lead-in.
"""
from __future__ import annotations

import re
from dataclasses import replace

from rapidfuzz import fuzz

from ..checks import Check
from ..model import Line, Page
from ..tables import Row, RowTable, read_row_tables
from . import ParseContext, Parsed
from .charcommon import SENTENCE_END, indented, prose_entries, title_case
from .equipment import (_MONEY, ARMOR_HEADERS, WEAPON_HEADERS, _rows, _table_checks, _weapon_tables, cost_check,
                        cost_value, is_heading, money_check, weight_check, weight_value)
from .monster import _join_text
from .monster import _title as title_name

# (headers, label column); a more specific header set first: ["Item", "Cost"] also matches the wider tables
_TABLES: list[tuple[list[str], int]] = [
    (["Item", "Cost", "Speed", "Carrying Capacity"], 0),
    (["Item", "Cost", "Speed"], 0),
    (["Item", "Cost", "Weight"], 0),
    (["Renaissance Item", "Cost", "Weight"], 0),
    (["Modern Item", "Cost", "Weight"], 0),
    (["Cost", "Goods"], 1),
    (["Coin", "cp", "sp", "ep", "gp", "pp"], 0),
    (["Item", "Cost"], 0),
]
# printed table title -> 5etools type code (None: the table mixes kinds)
_TITLES: dict[str, str | None] = {
    "adventuring gear": "G", "tools": "T", "mounts and other animals": "MNT", "waterborne vehicles": "SHP",
    "food, drink, and lodging": "FD", "trade goods": "TG", "standard exchange rates": "$C", "explosives": "EXP|DMG",
    "tack, harness, and drawn vehicles": None,
}
# the "Tack, Harness, and Drawn Vehicles" table: a row naming one of the drawn vehicles is VEH, any other row
# (bridles, saddles, barding, feed, stabling) TAH
_DRAWN = {"carriage", "cart", "chariot", "sled", "wagon"}
# a sub-heading naming a kind of gear -> its type code; any other sub-heading starts its rows' names
_GROUPS = {"ammunition": "A", "arcane focus": "SCF", "druidic focus": "SCF", "holy symbol": "SCF",
           "artisan's tools": "AT", "gaming set": "GS", "musical instrument": "INS"}
_OTHER_TYPE = re.compile(r"^\s*(poison\b|potion of\b)", re.I)  # DiseasePoison and MagicItem rows
_FOOTNOTE_ONLY = re.compile(r"^[*†‡]+$")
_WHOLE = re.compile(r"^\d{1,3}(?:,\d{3})+$|^\d+$")


def _norm(t: str) -> str:
    return re.sub(r"\s+", " ", re.sub(r"[^a-z' ,]", "", t.lower().replace("’", "'"))).strip()


def _set(fields: dict, key: str, value) -> None:
    if value is not None:
        fields[key] = value


def _lower_first(t: str) -> str:
    return t[:1].lower() + t[1:] if t[:1].isupper() and not t[1:2].isupper() else t


# ---------- table titles ----------
def _title(page: Page, t: RowTable) -> str | None:
    """The known title printed just above the table's header (within four line heights, over its columns)."""
    x0, y0, x1, _ = t.box
    above = sorted((l for l in page.lines if l.box[3] <= y0 + 2 and y0 - l.box[3] < 4 * l.height and
                    l.box[0] < x1 and l.box[2] > x0), key=lambda l: y0 - l.box[3])
    for l in above:
        n = _norm(l.text)
        hit = max(_TITLES, key=lambda k: fuzz.ratio(n, k))
        if fuzz.ratio(n, hit) >= 85:
            return hit
    return None


# ---------- row tables ----------
def _row_tables(page: Page) -> list[tuple[RowTable, str | None]]:
    """(table, title) for every item table on the page, top to bottom. A table printed without a title right
    under another (DMG "Modern Item | Cost | Weight" below the Explosives table) continues that table's title."""
    out: list[tuple[RowTable, str | None]] = []
    for headers, lab in _TABLES:
        for t in read_row_tables(page, headers, lab):
            out.append((t, _title(page, t)))
    out.sort(key=lambda x: (x[0].box[1], x[0].box[0]))
    for k, (t, title) in enumerate(out):
        if title is None:
            prev = [(u, ti) for u, ti in out[:k] if ti and u.box[0] < t.box[2] and u.box[2] > t.box[0] and
                    0 <= t.box[1] - u.box[3] < 3 * (t.rows[0].lines[0].height if t.rows else 30)]
            if prev:
                out[k] = (t, prev[-1][1])
    return out


def _new(r: Row, seen: set) -> bool:
    """Whether no line of the row was read before: two header matches can read the same table (["Item", "Cost"]
    reads the rows of an "Item | Cost | Weight" table too), and a narrower read may take fewer of the row's
    lines."""
    keys = {(l.page, tuple(round(x, 1) for x in l.box)) for l in r.lines}
    if keys & seen:
        return False
    seen |= keys
    return True


def _from_group(name: str, where: str, label: str) -> Check:
    """`name-from-group` (passing, for review visibility): the name joins printed words from two places."""
    return Check("name-from-group", True, f"name {name!r} is the {where} and the printed row label {label!r}")


def _coin_check(cell: str, row: str) -> Check:
    t = cell.strip()
    if _WHOLE.match(t):
        return Check("cost-valid", True)
    return Check("cost-valid", False, f"cp cell of {row!r} {cell!r} is not a whole number of copper"
                 if t else f"cp cell of {row!r} is blank")


def _table_item(r: Row, t: RowTable, suspect: str | None, title: str | None) -> Parsed | None:
    if _OTHER_TYPE.match(r.label):
        return None
    raw = {h: c.strip() for h, c in r.raw.items()}
    if raw and all(not c or _FOOTNOTE_ONLY.match(c) for c in raw.values()) and \
            any(_FOOTNOTE_ONLY.match(c) for c in raw.values()):
        return None  # "Vehicles (land or water) | *": described in another section
    group = (r.group or "").strip()
    gkey = _norm(group)
    name = r.label
    checks: list[Check] = []
    if group and gkey not in _GROUPS:
        name = f"{group}, {_lower_first(r.label)}"
        checks.append(_from_group(name, f"sub-heading {group!r}", r.label))
    f: dict = {}
    kind = _GROUPS.get(gkey) or (_TITLES.get(title) if title else None)
    if title == "tack, harness, and drawn vehicles":
        kind = "VEH" if _norm(name).split(",")[0].strip() in _DRAWN else "TAH"
    _set(f, "type", kind)
    if "cp" in raw and "Coin" in t.headers:
        if _WHOLE.match(raw["cp"]):
            f["value"] = int(raw["cp"].replace(",", ""))
        checks.append(_coin_check(raw["cp"], name))
    elif "Cost" in raw:
        _set(f, "value", cost_value(raw["Cost"]))
        checks.append(cost_check(raw["Cost"], name))
    if "Weight" in raw:
        _set(f, "weight", weight_value(raw["Weight"]))
        checks.append(weight_check(raw["Weight"], name))
    checks += _table_checks(r, t, suspect)
    p = Parsed(type="Item", name=name, fields=f, text=_row_text(name, r, r.cells), lines=list(r.lines),
                   table_row=True)
    p.checks = checks
    return p


_COIN_CASE = re.compile(r"(\d\s*)(cp|sp|ep|gp|pp)\b", re.I)


def _row_text(name: str, r: Row, cells: dict[str, str]) -> str:
    parts = [name]
    parts += [f"{h}: {_COIN_CASE.sub(lambda m: m.group(1) + m.group(2).lower(), c)}" for h, c in cells.items()
              if c.strip()]
    if r.group and _norm(r.group) in _GROUPS:
        parts.append(r.group)
    return "\n".join(parts)


def _ammunition(page: Page, seen: set) -> list[Parsed]:
    """Ammunition rows of the firearms tables (type AF|DMG). A label printed under more than one age is prefixed
    with its table's age."""
    got = [(r, t, suspect, age) for tb, a in _weapon_tables(page) for r, t, suspect, age in _rows([tb], set(), a)
           if age and r.group and re.fullmatch(r"ammunition", r.group.strip(), re.I) and _new(r, seen)]
    labels = [r.label.lower() for r, *_ in got]
    out: list[Parsed] = []
    for r, t, suspect, age in got:
        name = f"{age} {_lower_first(r.label)}" if labels.count(r.label.lower()) > 1 else r.label
        c = {h: x.strip() for h, x in r.raw.items()}
        cell = c.get("Weight Properties", c.get("Weight", ""))
        m = re.match(r"^(\S+\s*(?:[lI]bs?|oz)\.?)", cell)
        weight = m.group(1) if m else cell
        f: dict = {"type": "AF|DMG"}
        _set(f, "value", cost_value(c.get("Cost", "")))
        _set(f, "weight", weight_value(weight))
        p = Parsed(type="Item", name=name, fields=f, text=_row_text(name, r, r.cells), lines=list(r.lines),
                   table_row=True)
        p.checks = [cost_check(c.get("Cost", ""), name), weight_check(weight, name), *_table_checks(r, t, suspect)]
        if name != r.label:
            p.checks.append(_from_group(name, f"table age {age!r}", r.label))
        out.append(p)
    return out


# ---------- DMG treasure tables: gemstones and art objects ----------
_TIER = re.compile(r"^([\d,]+)\s*gp\s+(gemstones|art objects)$", re.I)
_STONE = re.compile(r"^(?P<name>[^()]+?)\s*\((?P<desc>[^()]*)\)$")


def _die_tables(page: Page) -> list[tuple[list[list[Line]], str, int, Line]]:
    """(rows as label lines, kind 'gem'/'art', value in copper, tier title line) for each gemstone or art object
    table on the page: a "Stone Description" or "Object" header under a "<n> gp Gemstones" / "<n> gp Art Objects"
    title. A row's label starts with a capital; a line starting lower case continues the label above it."""
    out = []
    for hd in page.lines:
        t = hd.text.strip().lower()
        kind = "gem" if fuzz.ratio(t, "stone description") >= 85 else "art" if fuzz.ratio(t, "object") >= 90 else None
        if kind is None:
            continue
        h = hd.height
        titles = sorted((l for l in page.lines if l.box[3] <= hd.box[1] + 2 and hd.box[1] - l.box[3] < 3 * h and
                         l.box[0] < hd.box[2] and l.box[2] > hd.box[0] - 6 * h and _TIER.match(l.text.strip())),
                        key=lambda l: hd.box[1] - l.box[3])
        if not titles:
            continue
        m = _TIER.match(titles[0].text.strip())
        if ("gem" if m.group(2).lower() == "gemstones" else "art") != kind:
            continue
        value = int(m.group(1).replace(",", "")) * 100
        col = sorted((l for l in page.lines if l.box[1] > hd.box[3] - 2 and abs(l.box[0] - hd.box[0]) < h),
                     key=lambda l: l.box[1])
        rows: list[list[Line]] = []
        last = hd.box[3]
        for l in col:
            s = l.text.strip()
            if l.box[1] - last > 2.2 * h or _TIER.match(s) or fuzz.ratio(s.lower(), "stone description") >= 85 \
                    or fuzz.ratio(s.lower(), "object") >= 90:
                break
            if rows and (s[:1].islower() or rows[-1][-1].text.count("(") > rows[-1][-1].text.count(")")):
                rows[-1].append(l)
            else:
                rows.append([l])
            last = l.box[3]
        out.append((rows, kind, value, titles[0]))
    return out


def _treasure(page: Page) -> list[Parsed]:
    out: list[Parsed] = []
    for rows, kind, value, title in _die_tables(page):
        for ls in rows:
            label = re.sub(r"-\s+", "-", " ".join(l.text.strip() for l in ls)).replace("( ", "(")
            f: dict = {"type": "$G|DMG" if kind == "gem" else "$A|DMG", "value": value}
            name = label
            checks = [Check("cost-valid", True)]
            if kind == "gem":
                m = _STONE.match(label)
                checks.append(Check("label-read", m is not None, "" if m else
                                    f"stone row {label!r} is not a name and a bracketed description"))
                if m:
                    name = m.group("name").strip()
                    f["entries"] = [m.group("desc").strip()]
            text = "\n".join([name] + f.get("entries", []) + [title.text.strip()])
            # not table_row: the value comes from the tier title, no cell of the row is checked (P17)
            p = Parsed(type="Item", name=name, fields=f, text=text, lines=list(ls))
            p.checks = checks
            out.append(p)
    return out


# ---------- lead-in paragraphs ----------
_SMALL = r"(?:of|and|or|the|a|an)"
_LEAD = re.compile(r"^(?P<name>[A-Z][A-Za-z'’-]*(?:,? (?:[A-Z][A-Za-z'’-]*|" + _SMALL + r"))*)"
                   r"(?:\s*\((?P<paren>[^)]*)\))?\.\s+(?P<rest>\S.*)$")
_PACK = re.compile(r"^(?P<name>.+ Pack)$")
_COLON_LEAD = re.compile(r"^(?P<name>[A-Z][A-Za-z'’-]*(?: [A-Z][A-Za-z'’-]*){0,2}):\s+(?P<rest>\S.*)$")


def _lead(L: list[Line], i: int) -> re.Match | None:
    m = _LEAD.match(L[i].text.strip())
    return m if m and len(m.group("name").split()) <= 6 and indented(L, i) else None


def _heading(L: list[Line], i: int) -> bool:
    t = L[i].text.strip()
    return title_case(t) and (i == 0 or L[i - 1].text.rstrip().endswith(SENTENCE_END))


def _jump(a: Line, b: Line) -> bool:
    """`b` starts another column or page than `a` (reading order moves up or to the next page)."""
    return b.page != a.page or b.box[1] < a.box[1] - a.height


def _sidebars(L: list[Line]) -> list[tuple[int, int]]:
    """(heading, end) of each inset sidebar printed inside a column's text: a heading whose section, down to the
    next column or page jump, sits right of the column text above it by 0.3 line heights or more (PHB p152
    "Equipment Packs" inside the Holy Water description). The column's own text resumes after the jump."""
    out: list[tuple[int, int]] = []
    for h in range(1, len(L)):
        if not _heading(L, h):
            continue
        above: list[Line] = []
        for j in range(h - 1, max(-1, h - 12), -1):
            if _jump(L[j], L[j + 1]):
                break
            above.append(L[j])
        e = h + 1
        while e < len(L) and not _jump(L[e - 1], L[e]):
            e += 1
        if len(above) < 2 or e - h < 2:
            continue
        if min(l.box[0] for l in L[h:e]) - min(l.box[0] for l in above) >= 0.3 * L[h].height:
            out.append((h, e))
    return out


def _paragraphs(L: list[Line]) -> list[tuple[re.Match, int, list[int]]]:
    """(lead-in match, first line, line indices) for each lead-in paragraph: it runs to the next lead-in, heading
    or footnote ("* You can also strap items ..."). Layout bounds it too: a lead-in inside an inset sidebar ends
    where the sidebar does, and a paragraph of the column's text reads past a sidebar printed inside it (the text
    resuming after the sidebar is the interrupted paragraph's, never the sidebar's last entry's)."""
    span: dict[int, tuple[int, int]] = {}
    for h, e in _sidebars(L):
        for i in range(h, e):
            span.setdefault(i, (h, e))
    leads = {i: m for i in range(len(L)) if (m := _lead(L, i))}
    out = []
    for s, m in leads.items():
        box = span.get(s)
        idx = [s]
        j = s + 1
        while j < len(L):
            if box and j >= box[1]:
                break
            if not box and j in span:
                j = span[j][1]
                continue
            if j in leads or _heading(L, j) or L[j].text.lstrip()[:1] in "*†‡":
                break
            idx.append(j)
            j += 1
        out.append((m, s, idx))
    return out


def _entries(L: list[Line], s: int, e: int, rest: str) -> list:
    return _para_entries(L, list(range(s, e)), rest)


def _para_entries(L: list[Line], idx: list[int], rest: str) -> list:
    sub = [replace(L[idx[0]], text=rest)] + [L[i] for i in idx[1:]]
    return prose_entries(sub, 0, len(sub))


def _key(name: str) -> str:
    """Match key: no bracketed note, lower case, a trailing plural s dropped from each word."""
    words = re.findall(r"[a-z0-9']+", re.sub(r"\([^)]*\)", " ", name.lower().replace("’", "'")))
    return " ".join(w[:-1] if len(w) > 3 and w.endswith("s") and not w.endswith("ss") else w for w in words)


_CHAPTER = re.compile(r"(?i)^chapter\s+(\d+)\s*:\s*[A-Z]")


def _chapters(body: list[Line]) -> list[int | None]:
    """The chapter number each body line sits in (the last chapter heading above it; None before any). A chapter
    heading reads "Chapter 5: Equipment" and is set taller than the text (1.4 times the median line height), so a
    cross-reference starting a line ("chapter 9 for ...") or a running foot ("CHAPTER 4 | CLASSES") is not one."""
    hs = sorted(l.height for l in body)
    tall = 1.4 * hs[len(hs) // 2] if hs else 0
    out: list[int | None] = []
    cur: int | None = None
    for l in body:
        m = _CHAPTER.match(l.text.strip())
        if m and l.height >= tall:
            cur = int(m.group(1))
        out.append(cur)
    return out


def _same_section(a: tuple[int, int | None], b: tuple[int, int | None]) -> bool:
    """Whether two (page, chapter) positions share a chapter (or, before any chapter heading, lie within ten
    pages of each other)."""
    if a[1] is None and b[1] is None:
        return abs(a[0] - b[0]) <= 10
    return a[1] == b[1]


def _table_boxes(ctx: ParseContext) -> dict[int, list[tuple]]:
    """Per page, (box, title band) of every equipment table: lines inside the box are not description text, nor is
    a title-case line in the band of 2.5 line heights above it (the table's title)."""
    out: dict[int, list[tuple]] = {}
    for n, page in ctx.pages.items():
        heads = [h for h, _ in _TABLES] + [WEAPON_HEADERS, ARMOR_HEADERS, ["Container", "Capacity"]]
        tabs = [t for h in heads for t in read_row_tables(page, h, 1 if h == ["Cost", "Goods"] else 0)]
        tabs += [t for t, _ in _weapon_tables(page)]
        boxes = []
        for t in tabs:
            h = t.rows[0].lines[0].height if t.rows else 30
            boxes.append((t.box, t.box[1] - 2.5 * h))
        if boxes:
            out[n] = boxes
    return out


def _outside_tables(body: list[Line], boxes: dict[int, list[tuple]]) -> list[Line]:
    def inside(l: Line) -> bool:
        xc, yc = (l.box[0] + l.box[2]) / 2, (l.box[1] + l.box[3]) / 2
        return any(b[0] <= xc <= b[2] and (b[1] <= yc <= b[3] or top <= yc < b[1] and title_case(l.text))
                   for b, top in boxes.get(l.page, []))
    out: list[Line] = []
    footnote = False
    for l in body:
        t = l.text.strip()
        # a table's footnote ("* You can also strap items ..." and its wrapped lines), a running head or foot
        # ("CHAPTER 4 | CLASSES", "PART 1 | EQUIPMENT") and a page number ("15:") are not text either
        if re.match(r"^[*†‡]+\s*[A-Z]", t):
            footnote = True
        if footnote:
            footnote = not t.endswith(SENTENCE_END)
            continue
        if inside(l) or re.match(r"(?i)^(chapter|part)\s+\d+\s*\|", t) or re.fullmatch(r"\d{1,3}\W?", t):
            continue
        out.append(l)
    return out


def _page_chapters(body: list[Line]):
    """page -> the chapter its last body line sits in (a page without body lines: the page before it)."""
    last: dict[int, int | None] = {}
    for l, c in zip(body, _chapters(body)):
        last[l.page] = c

    def at(n: int) -> int | None:
        before = [k for k in last if k <= n]
        return last[max(before)] if before else None
    return at


def _join(items: list[tuple[Parsed, int]], L: list[Line], chapters: list[int | None], page_chapter) -> None:
    """Give each table item (with its page) the description whose lead-in names it, inside its own chapter."""
    paras = _paragraphs(L)
    exact: dict[str, list[tuple[Parsed, int]]] = {}
    head: dict[str, list[tuple[Parsed, int]]] = {}
    for p, pg in items:
        exact.setdefault(_key(p.name), []).append((p, pg))
        bare = re.sub(r"\([^)]*\)", "", p.name)  # "Ball bearings (bag of 1,000)": no comma outside the bracket
        if "," in bare:
            head.setdefault(_key(bare.split(",")[0]), []).append((p, pg))
    for m, s, idx in paras:
        if m.group("paren") and _PACK.match(m.group("name")):
            continue
        k = _key(m.group("name"))
        here = (L[s].page, chapters[s])
        targets = [x for x in exact.get(k, []) if _same_section(here, (x[1], page_chapter(x[1])))]
        fallback = False
        if not targets and not exact.get(k):
            targets = [x for x in head.get(k, []) if _same_section(here, (x[1], page_chapter(x[1])))]
            fallback = bool(targets)
        lead = m.group("name")
        names = [p.name for p, _ in targets]
        for p, _ in targets:
            if "entries" in p.fields:
                continue
            ents = _para_entries(L, idx, m.group("rest"))
            if not ents:
                continue
            if fallback and len(targets) > 1:
                # one paragraph on several rows it may not all describe ("Saddles." on four saddles): review
                p.checks.append(Check("entries-shared", False, f"the description led by {lead!r} names no row; it "
                                      f"is shared by the rows {', '.join(repr(n) for n in names)}"))
            elif fallback:
                p.checks.append(Check("entries-from-lead-in", True, f"the description led by {lead!r} is the part "
                                      f"of {p.name!r} before its comma"))
            p.fields["entries"] = ents
            p.lines = p.lines + [L[i] for i in idx]
            p.text += "\n" + "\n".join(x if isinstance(x, str) else "\n".join(x.get("items", [])) for x in ents)


def _packs(L: list[Line]) -> list[Parsed]:
    """"<Name> Pack (<cost>). Includes ..." paragraphs: one Item per pack, its contents as entries."""
    out = []
    for m, s, idx in _paragraphs(L):
        paren = m.group("paren")
        if not paren or not _PACK.match(m.group("name")) or not m.group("rest").startswith("Includes"):
            continue
        name = m.group("name")
        f: dict = {"type": "G"}
        _set(f, "value", cost_value(paren))
        f["entries"] = _para_entries(L, idx, m.group("rest"))
        p = Parsed(type="Item", name=name, fields=f,
                   text="\n".join([f"{name} ({paren})"] + [x for x in f["entries"] if isinstance(x, str)]),
                   lines=[L[i] for i in idx])
        p.checks = [cost_check(paren, name)]
        out.append(p)
    return out


def _instrument_lists(L: list[Line]) -> list[Parsed]:
    """Under a "Musical Instruments" heading, each "Name: description" lead-in is an instrument (type INS). The
    list ends at the next heading."""
    out: list[Parsed] = []
    for i, l in enumerate(L):
        # a heading, not the words in a proficiency line ("... one type of musical instrument")
        if _norm(l.text) != "musical instruments" or not title_case(l.text.strip()):
            continue
        starts: list[int] = []
        end = len(L)
        for j in range(i + 1, len(L)):
            if _COLON_LEAD.match(L[j].text.strip()):
                starts.append(j)
            elif starts and _heading(L, j):
                end = j
                break
            elif not starts and j - i > 6:
                break
        for k, s in enumerate(starts):
            e = starts[k + 1] if k + 1 < len(starts) else end
            m = _COLON_LEAD.match(L[s].text.strip())
            ents = _entries(L, s, e, m.group("rest"))
            name = m.group("name")
            p = Parsed(type="Item", name=name, fields={"type": "INS", "entries": ents},
                       text="\n".join([name] + [x for x in ents if isinstance(x, str)]), lines=L[s:e])
            p.checks = [Check("entries-read", bool(ents), "" if ents else f"no description read for {name!r}")]
            out.append(p)
    return out


# ---------- goods sidebars ----------
# a heading naming a kind of goods sold as doses or pouches: its lead-ins are Items (EGW "Herbs of the Othemoor",
# "Blightshore Contraband", "Alchemical Items"; ERLW "Narcotics in Sharn")
_GOODS = re.compile(r"\b(narcotics|drugs|herbs|contraband|alchemical items)\b", re.I)


def _section_end(L: list[Line], i: int) -> int:
    """The end of the section under the heading at i: the next heading, or the end of the column (a sidebar's
    text stops there; the next column goes on with other text)."""
    for j in range(i + 1, len(L)):
        a, b = L[j - 1], L[j]
        if b.page != a.page or b.box[1] < a.box[1] - a.height or is_heading(L, j):
            return j
    return len(L)


def _price(name: str, text: str) -> tuple[list[str], str] | None:
    """(amounts, cue) of the price a goods paragraph prints: "Cost: 3 gp per dose." or "Black sap is valued at
    300 gp per dose." (the paragraph's own name as the subject). None when it prints neither."""
    m = re.search(r"\bCost:\s*([^.]*)", text) or \
        re.search(rf"\b{re.escape(name)}\s+(?:is|are)\s+valued at\s+([^.]*)", text, re.I)
    if not m:
        return None
    return [a.group(0) for a in _MONEY.finditer(m.group(1))], m.group(0)


def _goods_sections(L: list[Line]) -> list[Parsed]:
    """Under a heading naming a kind of goods, each lead-in paragraph ("Theki Root. This thick marsh root ...") is
    an Item. A lead-in starts a line after one ending a sentence (a word ending a wrapped sentence, "Sharn. The
    following drugs", is not one). The value is the one price after "Cost:" or "<name> is valued at"; a paragraph
    with no such cue prints no price, and one whose cue names several prices goes to review."""
    out: list[Parsed] = []
    for i, l in enumerate(L):
        if not (_GOODS.search(l.text) and is_heading(L, i)):
            continue
        end = _section_end(L, i)
        starts = [k for k in range(i + 1, end) if (m := _LEAD.match(L[k].text.strip())) and
                  len(m.group("name").split()) <= 6 and not m.group("paren") and
                  L[k - 1].text.rstrip().endswith(SENTENCE_END)]
        for n, s in enumerate(starts):
            e = starts[n + 1] if n + 1 < len(starts) else end
            m = _LEAD.match(L[s].text.strip())
            name = m.group("name")
            ents = _entries(L, s, e, m.group("rest"))
            f: dict = {}
            checks: list[Check] = []
            price = _price(name, _join_text([L[s].text.strip()] + [x.text.strip() for x in L[s + 1:e]]))
            if price:
                amounts, cue = price
                if len(amounts) == 1:
                    f["value"] = cost_value(amounts[0])
                checks.append(money_check(amounts, name, f"the price {cue!r}"))
                checks.append(Check("goods-attested", True))
            else:
                checks.append(Check("goods-attested", False, f"the lead-in {name!r} under the goods heading "
                                    f"{title_name(L[i].text.strip())!r} prints no price: review that it is an item"))
            f["entries"] = ents
            p = Parsed(type="Item", name=name, fields=f,
                       text="\n".join([name] + [x if isinstance(x, str) else "\n".join(x.get("items", []))
                                                for x in ents]), lines=L[s:e])
            p.checks = checks + [Check("entries-read", bool(ents), "" if ents else f"no description read for "
                                                                                    f"{name!r}")]
            out.append(p)
    return out


# ---------- a heading whose section prices it ----------
_RANGE_PRICE = re.compile(r"(\d{1,3}(?:,\d{3})+|\d+)\s*(?:to|-|–)\s*(\d{1,3}(?:,\d{3})+|\d+)\s*(cp|sp|ep|gp|pp)\b",
                          re.I)


def _priced_headings(L: list[Line]) -> list[Parsed]:
    """A heading of one to three words whose section prices the thing it names, as the subject of the sentence
    ("MOORBOUNDERS" ... "Moorbounders can cost anywhere from 300 to 500 gp each", EGW): an Item named by the
    heading. A price range is no value; a passing `price-range` check records it. Several prices go to review."""
    out: list[Parsed] = []
    for i, l in enumerate(L):
        head = l.text.strip()
        if not (1 <= len(head.split()) <= 3 and is_heading(L, i)) or _GOODS.search(head):
            continue
        end = _section_end(L, i)
        text = _join_text([x.text.strip() for x in L[i + 1:end]])
        m = re.search(rf"(?:^|[.!?]\s+){re.escape(head)}\s+(?:can\s+|each\s+)?costs?\b([^.]*)", text, re.I)
        if not m:
            continue
        name = title_name(head)
        f: dict = {}
        rng = _RANGE_PRICE.search(m.group(1))
        if rng:
            checks = [Check("price-range", True, f"{name!r} is priced as a range ({rng.group(0)}): no value")]
        else:
            amounts = [a.group(0) for a in _MONEY.finditer(m.group(1))]
            if not amounts:
                continue
            if len(amounts) == 1:
                f["value"] = cost_value(amounts[0])
            checks = [money_check(amounts, name, "the price sentence")]
        ents = prose_entries(L, i + 1, end)
        if ents:
            f["entries"] = ents
        p = Parsed(type="Item", name=name, fields=f,
                   text="\n".join([name] + [x for x in ents if isinstance(x, str)]), lines=L[i:end])
        p.checks = checks
        out.append(p)
    return out


# ---------- currency matrix ----------
_COST_CELL = re.compile(r"^(\d{1,3}(?:,\d{3})+|\d+)\s*(cp|sp|ep|gp|pp)$", re.I)


def _currency_matrix(page: Page) -> list[Parsed]:
    """A "<...> Currency" table whose first column is a Value ("1 cp") and whose other columns name the coin of
    that value in each issuer's coinage (GGR Ravnica Currency: Value | Azorius | Boros | Orzhov). Each named cell
    is an Item of type $C worth its row's value; a name wrapping onto a second line is one cell; the same name in
    two columns is one Item. A cell whose first line sits below its row's value cell lost that line to the OCR:
    it names nothing and is not read."""
    out: list[Parsed] = []
    for hd in page.lines:
        if not re.match(r"^value\b", hd.text.strip(), re.I):
            continue
        h = hd.height
        title = [l for l in page.lines if l.box[3] <= hd.box[1] + 2 and hd.box[1] - l.box[3] < 3 * h and
                 abs(l.box[0] - hd.box[0]) < 3 * h and re.search(r"\bcurrency\b", l.text, re.I)]
        if not title:
            continue
        vals = []
        last = hd.box[3]
        for l in sorted((l for l in page.lines if l.box[1] > hd.box[3] - 2 and abs(l.box[0] - hd.box[0]) < 1.5 * h),
                        key=lambda l: l.box[1]):
            if l.box[1] - last > 2.5 * h:
                break
            if _COST_CELL.match(l.text.strip()):
                vals.append(l)
                last = l.box[3]
        if len(vals) < 2:
            continue
        x_cells = max(v.box[0] for v in vals) + h  # a name cell starts right of the value cells' left edges
        bottom = vals[-1].box[3] + 2.5 * h
        # column starts: the header's own and each header word set apart to its right (a gap of under 5 line
        # heights; the next text column of the page is not the table's)
        cols = [hd.box[0]]
        right = hd.box[2]
        for l in sorted((l for l in page.lines if abs(l.box[1] - hd.box[1]) < 0.5 * h and l.box[0] > hd.box[2]),
                        key=lambda l: l.box[0]):
            if l.box[0] - right > 5 * h:
                break
            cols.append(l.box[0])
            right = l.box[2]
        cells: dict[str, tuple[list[int], list[Line]]] = {}  # name (lower case) -> (row values, lines)
        for k, v in enumerate(vals):
            top = v.box[1] - 0.3 * h
            nxt = vals[k + 1].box[1] - 0.3 * h if k + 1 < len(vals) else bottom
            by_col: dict[float, list[Line]] = {}
            for l in page.lines:
                if l in vals or l.box[0] < x_cells or not top <= l.box[1] < nxt or l.box[0] > cols[-1] + 2 * h:
                    continue
                by_col.setdefault(max([x for x in cols if x <= l.box[0] + h], default=cols[0]), []).append(l)
            value = cost_value(v.text)
            for _, ls in sorted(by_col.items()):
                ls.sort(key=lambda l: l.box[1])
                if ls[0].box[1] - v.box[1] > 0.5 * h:
                    continue  # the cell's first line is lost
                label = re.sub(r"-\s+", "-", " ".join(l.text.strip() for l in ls))
                key = label[:1].upper() + label[1:]
                cells.setdefault(key.lower(), ([], []))
                cells[key.lower()][0].append(value)
                cells[key.lower()][1].extend(ls)
        for key, (values, ls) in cells.items():
            name = key[:1].upper() + key[1:]
            vs = sorted(set(values))
            f = {"type": "$C"}
            if len(vs) == 1:
                f["value"] = vs[0]
            p = Parsed(type="Item", name=name, fields=f,
                       text="\n".join([name] + [f"Value: {v} cp" for v in vs] + [title[0].text.strip()]), lines=ls,
                       table_row=True)
            p.checks = [Check("cost-valid", len(vs) == 1, "" if len(vs) == 1 else
                              f"coin {name!r} is printed in rows of different values ({', '.join(f'{v} cp' for v in vs)})")]
            out.append(p)
    return out


def parse_items_mundane(body: list[Line], ctx: ParseContext) -> list[Parsed]:
    """Mundane Items from every item table, treasure table, pack paragraph and instrument list in the book."""
    table_items: list[tuple[Parsed, int]] = []
    out: list[Parsed] = []
    seen: set = set()
    for n in sorted(ctx.pages):
        page = ctx.pages[n]
        for t, title in _row_tables(page):
            for r, tb, suspect, _ in _rows([t], set()):
                if not _new(r, seen):
                    continue
                p = _table_item(r, tb, suspect, title)
                if p:
                    table_items.append((p, n))
        out += _ammunition(page, seen)
        out += _treasure(page)
        out += _currency_matrix(page)
    L = _outside_tables(body, _table_boxes(ctx))
    _join(table_items, L, _chapters(L), _page_chapters(body))
    return [p for p, _ in table_items] + out + _packs(L) + _instrument_lists(L) + _goods_sections(L) + _priced_headings(L)
