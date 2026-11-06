"""Equipment-table parsers (weapons, armor) and the cell checks shared by every equipment type.

Each printed table row is one entity, named by its label cell exactly as printed ("Crossbow, light", "Padded").
Tables are found on any page by their header row, never by page number:

- weapons: "Name | Cost | Damage | Weight | Properties" (PHB), and the DMG firearms tables
  "<Renaissance|Modern|Futuristic> Item | Cost | Damage | Weight Properties" (the Weight and Properties headers set
  as one line, so each cell holds "3 lb. Ammunition (range 30/90), loading");
- armor: "Armor | Cost | Armor Class (AC) | Strength | Stealth | Weight".

Fields use the 5etools encodings of `WeaponFields` / `ArmorFields`:

- `value` in copper (15 gp = 1500), `weight` in lb (1/4 lb. = 0.25);
- weapons: `type` M or R and `weaponCategory` simple/martial from the sub-heading row ("Martial Melee Weapons"),
  `dmg1` and `dmgType` (a letter code), `property` codes (A, F, H, L, LD, R, S, T, 2H, V; in a firearms table
  ammunition is AF|DMG, burst fire BF|DMG and reload RLD), `range` from "(range 20/60)", `dmg2` from
  "versatile (1d10)", `reload` from "reload (15 shots)"; a firearms table adds `age` (its header's first word) and
  `firearm` when the page prints a "Firearms" heading. Rows under an "Ammunition" sub-heading are items, not weapons;
- armor: `type` LA, MA, HA or S from the sub-heading row, `ac` (the base number, or the shield's "+2"), `strength`
  ("Str 13" -> "13", as 5etools stores it), `stealth` true for "Disadvantage".

Values and checks read the cells as the OCR printed them (before the digit repair); the entity text shows the
repaired cells. A cell that does not read cleanly leaves its field absent and fails its check naming the row and the
cell. A blank Cost, Damage or Weight cell also fails: the OCR drops the dash the book prints in an empty cell, so a
blank cell cannot be told from a cell the OCR lost (those rows go to review). A printed dash ("-") passes.

A row that repeats a header (the next table's "Modern Item | Cost | ..." read as a row) is skipped and the rows
below it take that table's age; a row that only half resembles a header is kept, but it and every row after it in
that table fail `table-rows-read`, as do the rows after a repeated header naming no known table. A line
the table reader could not place inside a row's span fails that row's `row-complete`.
"""
from __future__ import annotations

import re

from rapidfuzz import fuzz

from ..checks import Check
from ..model import Line, Page
from ..tables import Row, RowTable, read_row_tables
from . import ParseContext, Parsed

WEAPON_HEADERS = ["Name", "Cost", "Damage", "Weight", "Properties"]
FIREARM_AGES = ("Renaissance", "Modern", "Futuristic")
ARMOR_HEADERS = ["Armor", "Cost", "Armor Class (AC)", "Strength", "Stealth", "Weight"]

_COIN = {"cp": 1, "sp": 10, "ep": 50, "gp": 100, "pp": 1000}
_DASH = re.compile(r"^[-–—]+$")
_COST = re.compile(r"^(\d{1,3}(?:,\d{3})+|\d+)\s*(cp|sp|ep|gp|pp)\.?$", re.I)
_WEIGHT = re.compile(r"^(\d+/\d+|\d*\.\d+|\d+)\s*[lI]bs?\.?$")
DAMAGE_TYPES = {"acid": "A", "bludgeoning": "B", "cold": "C", "fire": "F", "force": "O", "lightning": "L",
                "necrotic": "N", "piercing": "P", "poison": "I", "psychic": "Y", "radiant": "R", "slashing": "S",
                "thunder": "T"}
_DAMAGE = re.compile(r"^(\d+d\d+|\d+)\s+(" + "|".join(DAMAGE_TYPES) + r")$", re.I)
# printed property name -> 5etools code (a firearms table's own codes in _FIREARM_CODES)
PROPERTIES = {"ammunition": "A", "finesse": "F", "heavy": "H", "light": "L", "loading": "LD", "reach": "R",
              "special": "S", "thrown": "T", "two-handed": "2H", "versatile": "V", "reload": "RLD",
              "burst fire": "BF|DMG"}
_FIREARM_CODES = {"ammunition": "AF|DMG"}
_PROPERTY = re.compile(r"^([a-z][a-z -]*?)\s*(?:\((.*)\))?$", re.I)
_RANGE = re.compile(r"^range\s+(\d+)/(\d+)$", re.I)
_DICE = re.compile(r"^\d+d\d+$")
_SHOTS = re.compile(r"^(\d+)\s+shots?$", re.I)
_WEAPON_GROUP = re.compile(r"(simple|martial)\s*(melee|ranged)\s*weapons?", re.I)
_ARMOR_GROUP = {"light": "LA", "medium": "MA", "heavy": "HA"}
_AC = re.compile(r"^(\d+)(\s*\+\s*dex(?:terity)?\s+modifier(?:\s*\(max\s+2\))?)?$", re.I)
_SHIELD_AC = re.compile(r"^\+\s*(\d+)$")
_STRENGTH = re.compile(r"^str(?:ength)?\.?\s*(\d+)$", re.I)


# ---------- shared cell checks ----------
_BLANK = " (the OCR drops a printed dash, so a blank cell may be a dash or a lost cell)"


def _who(row: str | None) -> str:
    return f" of {row!r}" if row else ""


def _blank(cell: str) -> bool:
    return not cell.strip()


def cost_check(cell: str, row: str | None = None) -> Check:
    """`cost-valid`: a number with a cp/sp/ep/gp/pp unit, or a printed dash. A blank cell fails."""
    t = cell.strip()
    if not t:
        return Check("cost-valid", False, f"cost cell{_who(row)} is blank{_BLANK}")
    if _DASH.match(t) or _COST.match(t):
        return Check("cost-valid", True)
    return Check("cost-valid", False, f"cost cell{_who(row)} {cell!r} is not a number with a cp/sp/ep/gp/pp unit")


def cost_value(cell: str) -> int | None:
    """The cost in copper, or None for a blank, dashed or unreadable cell."""
    m = _COST.match(cell.strip())
    return int(m.group(1).replace(",", "")) * _COIN[m.group(2).lower()] if m else None


def weight_check(cell: str, row: str | None = None) -> Check:
    """`weight-valid`: a number with "lb.", or a printed dash. A blank cell fails."""
    t = cell.strip()
    if not t:
        return Check("weight-valid", False, f"weight cell{_who(row)} is blank{_BLANK}")
    if _DASH.match(t) or _WEIGHT.match(t):
        return Check("weight-valid", True)
    return Check("weight-valid", False, f"weight cell{_who(row)} {cell!r} is not a number of lb.")


def weight_value(cell: str) -> float | int | None:
    m = _WEIGHT.match(cell.strip())
    if not m:
        return None
    n = m.group(1)
    if "/" in n:
        a, b = n.split("/")
        return int(a) / int(b) if int(b) else None
    v = float(n)
    return int(v) if v.is_integer() else v


def damage_check(cell: str, row: str | None = None) -> Check:
    """`damage-valid`: dice or a flat number plus a damage type from the closed set, or a printed dash. A blank
    cell fails."""
    t = cell.strip()
    if not t:
        return Check("damage-valid", False, f"damage cell{_who(row)} is blank{_BLANK}")
    if _DASH.match(t) or _DAMAGE.match(t):
        return Check("damage-valid", True)
    return Check("damage-valid", False, f"damage cell{_who(row)} {cell!r} is not dice (or a number) and a damage "
                                        "type")


def split_properties(cell: str) -> list[str]:
    """'Ammunition (range 80/320), loading, two-handed' -> its properties (commas inside brackets kept)."""
    t = re.sub(r"-\s+", "-", cell.strip())  # "two-" / "handed" wrapped onto two lines
    out, depth, cur = [], 0, ""
    for ch in t:
        depth += (ch == "(") - (ch == ")")
        if ch == "," and depth == 0:
            out.append(cur.strip())
            cur = ""
        else:
            cur += ch
    if cur.strip():
        out.append(cur.strip())
    return out


_FIREARM_ONLY = ("reload", "burst fire")


def _read_property(token: str, firearm: bool = False) -> tuple[str, str | None] | None:
    """(printed name lower-cased, bracket text) of a known property whose bracket reads, else None. Reload and
    burst fire are known only in a firearms table (`firearm`)."""
    m = _PROPERTY.match(token.strip())
    if not m:
        return None
    name = re.sub(r"\s+", " ", m.group(1)).lower()
    arg = m.group(2).strip() if m.group(2) is not None else None
    if name not in PROPERTIES or (name in _FIREARM_ONLY and not firearm):
        return None
    if name in ("ammunition", "thrown"):
        ok = arg is None or _RANGE.match(arg)
    elif name == "versatile":
        ok = arg is not None and _DICE.match(arg)
    elif name == "reload":
        ok = arg is not None and _SHOTS.match(arg)
    else:
        ok = arg is None
    return (name, arg) if ok else None


def properties_check(props: list[str], row: str | None = None, firearm: bool = False) -> Check:
    """`properties-known`: every property is from the PHB list (plus, in a firearms table, the DMG's reload and
    burst fire), with a readable bracket ("(range 20/60)", "(1d10)", "(15 shots)") where it takes one."""
    bad = [p for p in props if _read_property(p, firearm) is None]
    if not bad:
        return Check("properties-known", True)
    return Check("properties-known", False,
                 f"unknown or unreadable propert{'y' if len(bad) == 1 else 'ies'}{_who(row)}: "
                 + ", ".join(repr(b) for b in bad))


# ---------- table rows ----------
def _key(l: Line) -> tuple:
    return l.page, tuple(round(x, 1) for x in l.box)


_COIN_CASE = re.compile(r"(\d\s*)(cp|sp|ep|gp|pp)\b", re.I)  # "15 gP" (OCR case noise) -> "15 gp"
_FIRST_HEADERS = ("Name", "Armor", *(f"{a} Item" for a in ("Renaissance", "Modern", "Futuristic")))


def _like(a: str, b: str) -> float:
    return fuzz.ratio(a.strip().lower(), b.strip().lower())


def _header_likeness(r: Row, t: RowTable) -> tuple[bool, bool]:
    """(repeats a header, half resembles one) for a table row. A row repeats a header when its label reads as a
    table's first header ("Modern Item", "Name", "Armor") or two of its cells read as their own header names."""
    label = max(_like(r.label, h) for h in (*_FIRST_HEADERS, t.headers[0]))
    cells = sum(_like(c, h) >= 80 for h, c in r.raw.items() if c.strip())
    return label >= 80 or cells >= 2, label >= 60 or cells >= 1


def _rows(tables: list[RowTable], seen: set, age: str | None = None) \
        -> list[tuple[Row, RowTable, str | None, str | None]]:
    """(row, its table, why the row is suspect or None, firearm age) for the rows of `tables` not already read (two
    header matches can read the same rows). A row that repeats a header is the next table's header read as a row
    (the Renaissance table runs on into "Modern Item | Cost | ..."): it is skipped, and the rows below it take that
    table's age. When the repeated header names no known table, or a row only half resembles a header (so it is
    kept as data), it and every row after it in the table are suspect."""
    out: list[tuple[Row, RowTable, str | None, str | None]] = []
    for t in tables:
        suspect: str | None = None
        cur = age
        for r in t.rows:
            repeats, resembles = _header_likeness(r, t)
            if repeats:
                named = [a for a in FIREARM_AGES if _like(r.label, f"{a} Item") >= 80]
                if named:
                    cur = named[0]
                elif age is not None and suspect is None:  # a firearms table's next header, its age unread
                    suspect = f"rows after the header row {r.label!r} were read without knowing their table"
                continue
            if resembles and suspect is None:
                suspect = f"row {r.label!r} ({' | '.join(c for c in r.raw.values() if c.strip())}) looks like a " \
                          "table header read as data"
            k = _key(r.lines[0])
            if k in seen:
                continue
            seen.add(k)
            out.append((r, t, suspect, cur))
    return out


def _table_checks(r: Row, t: RowTable, suspect: str | None) -> list[Check]:
    """`table-rows-read` (no header-like row read as data at or above this row) and `row-complete` (no line the
    table reader could not place sits inside this row's span)."""
    y0, y1 = min(l.box[1] for l in r.lines), max(l.box[3] for l in r.lines)
    stray = [l.text for l in t.unassigned if y0 <= (l.box[1] + l.box[3]) / 2 <= y1]
    return [
        Check("table-rows-read", suspect is None, suspect or ""),
        Check("row-complete", not stray, "" if not stray else
              f"text inside the row {r.label!r} not placed in any cell: " + ", ".join(repr(s) for s in stray)),
    ]


def _text(r: Row) -> str:
    """The row as text: its label, each printed cell (digit-repaired) under its header, and its sub-heading."""
    parts = [r.label + (r.marker or "")]
    parts += [f"{h}: {_COIN_CASE.sub(lambda m: m.group(1) + m.group(2).lower(), c)}" for h, c in r.cells.items()
              if c.strip()]
    if r.group:
        parts.append(r.group)
    return "\n".join(parts)


def _set(fields: dict, key: str, value) -> None:
    if value is not None:
        fields[key] = value


def _cells(r: Row) -> dict[str, str]:
    return {h: c.strip() for h, c in r.raw.items()}


# ---------- weapons ----------
def _weapon(r: Row, t: RowTable, suspect: str | None, age: str | None, firearm: bool) -> Parsed:
    c = _cells(r)
    weight, props = c.get("Weight", ""), c.get("Properties", "")
    if "Weight Properties" in c:  # one cell for both: "3 lb. Ammunition (range 30/90), loading"
        cell = c["Weight Properties"]
        # the weight is the leading "<number> lb."; a leading number whose unit does not read stays the weight cell
        m = re.match(r"^(\S+\s*[lI]bs?\.?)\s*(.*)$", cell) or re.match(r"^(\d\S*\s+\S+)\s*(.*)$", cell)
        weight, props = (m.group(1), m.group(2)) if m else ("", cell)
    f: dict = {}
    g = _WEAPON_GROUP.search(r.group or "")
    if g:
        f["type"] = "M" if g.group(2).lower() == "melee" else "R"
        f["weaponCategory"] = g.group(1).lower()
    if age:
        f["age"] = age.lower()
        if firearm:
            f["firearm"] = True
    _set(f, "value", cost_value(c.get("Cost", "")))
    _set(f, "weight", weight_value(weight))
    if m := _DAMAGE.match(c.get("Damage", "")):
        f["dmg1"], f["dmgType"] = m.group(1), DAMAGE_TYPES[m.group(2).lower()]
    tokens = split_properties(props)
    codes: list[str] = []
    for tok in tokens:
        got = _read_property(tok, age is not None)
        if got is None:
            continue
        name, arg = got
        codes.append(_FIREARM_CODES.get(name, PROPERTIES[name]) if age else PROPERTIES[name])
        if arg and (m := _RANGE.match(arg)):
            f["range"] = f"{m.group(1)}/{m.group(2)}"
        elif name == "versatile":
            f["dmg2"] = arg
        elif name == "reload":
            f["reload"] = int(_SHOTS.match(arg).group(1))
    if codes:
        f["property"] = codes
    checks = [
        Check("category-known", bool(g), "" if g else f"sub-heading {r.group!r} of {r.label!r} names no simple or "
                                                      "martial, melee or ranged weapon group"),
        cost_check(c.get("Cost", ""), r.label),
        weight_check(weight, r.label),
        damage_check(c.get("Damage", ""), r.label),
        properties_check(tokens, r.label, firearm=age is not None),
        *_table_checks(r, t, suspect),
    ]
    p = Parsed(type="Weapon", name=r.label, fields=f, text=_text(r), lines=r.lines)
    p.checks = checks
    return p


def _weapon_tables(page: Page) -> list[tuple[RowTable, str | None]]:
    out: list[tuple[RowTable, str | None]] = [(t, None) for t in read_row_tables(page, WEAPON_HEADERS)]
    for age in FIREARM_AGES:
        out += [(t, age) for t in read_row_tables(page, [f"{age} Item", "Cost", "Damage", "Weight Properties"])]
    return out


def parse_weapons(body: list[Line], ctx: ParseContext) -> list[Parsed]:
    """One Weapon per printed weapon-table row, on any page of the book (rows under an Ammunition sub-heading are
    items, not weapons)."""
    out: list[Parsed] = []
    seen: set = set()
    for n in sorted(ctx.pages):
        page = ctx.pages[n]
        firearm = any(re.sub(r"[^a-z]", "", l.text.lower()) == "firearms" for l in page.lines)
        for t, age in sorted(_weapon_tables(page), key=lambda x: (x[0].box[1], x[0].box[0])):
            for r, tb, suspect, row_age in _rows([t], seen, age):
                if r.group and re.fullmatch(r"ammunition", r.group.strip(), re.I):
                    continue
                out.append(_weapon(r, tb, suspect, row_age, firearm))
    return out


# ---------- armor ----------
def _armor_type(group: str | None) -> str | None:
    g = (group or "").strip().lower()
    if re.fullmatch(r"shields?", g):
        return "S"
    m = re.fullmatch(r"(light|medium|heavy)\s*armou?r", g)
    return _ARMOR_GROUP[m.group(1)] if m else None


def _armor(r: Row, t: RowTable, suspect: str | None) -> Parsed:
    c = _cells(r)
    f: dict = {}
    kind = _armor_type(r.group)
    _set(f, "type", kind)
    ac_cell = c.get("Armor Class (AC)", "")
    m = (_SHIELD_AC if kind == "S" else _AC).match(ac_cell)
    if m:
        f["ac"] = int(m.group(1))
    st = c.get("Strength", "")
    if sm := _STRENGTH.match(st):
        f["strength"] = sm.group(1)
    stealth = c.get("Stealth", "")
    if stealth.lower() == "disadvantage":
        f["stealth"] = True
    _set(f, "value", cost_value(c.get("Cost", "")))
    _set(f, "weight", weight_value(c.get("Weight", "")))
    checks = [
        Check("category-known", kind is not None, "" if kind else f"sub-heading {r.group!r} of {r.label!r} names "
                                                                  "no light, medium or heavy armor or shield group"),
        cost_check(c.get("Cost", ""), r.label),
        weight_check(c.get("Weight", ""), r.label),
        Check("ac-valid", m is not None, "" if m else f"armor class cell of {r.label!r} {ac_cell!r} is not a number"
                                                      " (optionally + Dex modifier (max 2)), or +N for a shield"),
        Check("strength-valid", not st or sm is not None,
              "" if not st or sm else f"strength cell of {r.label!r} {st!r} is not 'Str N'"),
        Check("stealth-valid", not stealth or "stealth" in f,
              "" if not stealth or "stealth" in f else f"stealth cell of {r.label!r} {stealth!r} is not "
                                                        "'Disadvantage'"),
        *_table_checks(r, t, suspect),
    ]
    p = Parsed(type="Armor", name=r.label, fields=f, text=_text(r), lines=r.lines)
    p.checks = checks
    return p


def parse_armor(body: list[Line], ctx: ParseContext) -> list[Parsed]:
    """One Armor per printed armor-table row, on any page of the book."""
    seen: set = set()
    return [_armor(r, t, suspect) for n in sorted(ctx.pages)
            for r, t, suspect, _ in _rows(read_row_tables(ctx.pages[n], ARMOR_HEADERS), seen)]
