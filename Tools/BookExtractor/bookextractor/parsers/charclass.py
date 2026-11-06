"""Class parser (PHB / TCE class chapters) and its verification checks.

A class is anchored on its "Hit Dice:" line (next to a "Class Features" heading), named only from the chapter
heading above it, and runs to its subclass-group heading ("Primal Paths"). Feature refs come from the level
table's Features column; each row's level is its row index (the ordinal cell may be OCR-garbled).
"""
from __future__ import annotations

import re
import statistics

from rapidfuzz import fuzz

from ..checks import Check
from ..layout import fix_digits
from ..model import Line, Page
from ..tables import Table, read_table
from . import ParseContext, Parsed
from .charcommon import ABILITIES, SKILLS, SUBCLASS_GROUPS, SUBCLASS_LEVELS, class_source, ordinal
from .charcommon import like as _like
from .charcommon import squash as _squash
from .monster import _join_text
from .spell import _paragraphs

_MAX_SECTION = 1500  # lines a class section may span before the subclass-group heading
_SECTION_HEADS = ("Hit Points", "Proficiencies", "Equipment", "Starting Equipment", "Quick Build",
                  "Class Features")
_NUMBER_WORDS = {"one": 1, "two": 2, "three": 3, "four": 4, "five": 5, "six": 6}
# a uses marker, not part of the feature's name: "Action Surge (one use)", "Channel Divinity (2/rest)"
_FIRST_GRANT = re.compile(r"\s*\((?:one use|\d/rest)\)\s*$", re.I)
_PAREN = re.compile(r"\s*\([^)]*\)\s*$")


def _heading_like(t: str) -> bool:
    t = t.strip()
    return 0 < len(t.split()) <= 6 and t[:1].isalpha() and not t.endswith((".", ",", ";", ":"))


def _label(lines: list[Line], i: int, label: str) -> str | None:
    """Value of a 'Label: value' line at i, with its wrapped continuation lines joined. The book indents a
    wrapped value under its label; across a column break, the value must visibly continue (a trailing comma,
    hyphen or joining word, or a lowercase start)."""
    head = lines[i]
    m = re.match(r"^\s*([A-Za-z ]{3,20}?)\s*[:;]\s*(.*)$", head.text)
    if not m or not _like(m.group(1), label, 85):
        return None
    parts = [m.group(2)]
    for j in range(i + 1, min(i + 4, len(lines))):
        nl, nt = lines[j], lines[j].text.strip()
        if re.match(r"^[A-Za-z ]{3,24}[:;]", nt):
            break
        same_col = nl.page == head.page and abs(nl.box[0] - head.box[0]) < 4 * head.height
        if same_col:
            if nl.box[0] - head.box[0] < 0.4 * head.height:
                break
        elif not (parts[-1].rstrip().endswith((",", "-")) or re.search(r"\b(?:and|or|of|from)$", parts[-1])
                  or nt[:1].islower()):
            break
        parts.append(nt)
    return _join_text([p for p in parts if p]).strip()


# ---------- anchors and section bounds ----------
def _is_class_features(t: str) -> bool:
    return _heading_like(t) and _like(t, "Class Features", 85)


def _anchors(L: list[Line]) -> list[int]:
    """Indices of 'Hit Dice:' lines followed by 'Hit Points at 1st Level' with a Class Features heading on
    the same, previous or next page (TCE prints the heading after the Hit Points block)."""
    cf_pages = {l.page for l in L if _is_class_features(l.text)}
    out = []
    for i, l in enumerate(L):
        m = re.match(r"^\s*([A-Za-z ]{6,12})\s*[:;]", l.text)
        if not m or not _like(m.group(1), "Hit Dice", 85):
            continue
        if not any(_like(L[j].text[:22], "Hit Points at 1st Level", 80) for j in range(i + 1, min(i + 3, len(L)))):
            continue
        if cf_pages & {l.page - 1, l.page, l.page + 1}:
            out.append(i)
    return out


def _class_heading(L: list[Line], a: int, lo: int, med: float) -> tuple[int | None, str]:
    """(index, name) of the chapter heading: the tallest heading line before the anchor on the anchor page or
    the two pages before it. The name is "" unless that heading is a known class."""
    page = L[a].page
    cands = [i for i in range(lo, a) if L[i].page >= page - 2 and _heading_like(L[i].text)
             and len(L[i].text.split()) <= 3 and not re.search(r"\d", L[i].text)
             and sum(c.isalpha() for c in L[i].text) >= 3 and L[i].height >= 1.35 * med
             and not _is_class_features(L[i].text)]
    if not cands:
        return None, ""

    def key(i: int) -> str | None:
        return next((c for c in SUBCLASS_GROUPS if _squash(c) == _squash(L[i].text)), None)

    # a decorative art word ('VERSE') can be taller than the chapter heading; a printed class name wins
    named = [i for i in cands if key(i)]
    best = max(named or cands, key=lambda i: L[i].height)
    return best, key(best) or ""


def _group_end(L: list[Line], a: int, stop: int, cls: str) -> int | None:
    """Index of the subclass-group heading ('Primal Paths') after the anchor, or None."""
    if not cls:
        return None
    plural = SUBCLASS_GROUPS[cls][1]
    for i in range(a + 1, stop):
        t = L[i].text.strip()
        if _heading_like(t) and len(t.split()) <= 4 and _squash(t).endswith("s") and _like(t, plural, 88):
            return i
    return None


# ---------- fields ----------
def _hd(text: str) -> dict | None:
    m = re.search(r"Hit Dice\s*[:;]?\s*(\S+)", text, re.I)
    d = re.fullmatch(r"(\d+)d(\d+)", fix_digits(m.group(1))) if m else None
    return {"number": int(d.group(1)), "faces": int(d.group(2))} if d else None


def _abilities(value: str) -> list[str] | None:
    out = []
    for tok in re.split(r",|\band\b|&", value):
        tok = tok.strip(" .")
        if not tok:
            continue
        hit = next((v for k, v in ABILITIES.items() if _like(tok, k, 85)), None)
        if hit is None:
            return None
        out.append(hit)
    return out or None


_ARMOR = {"light armor": ["light"], "medium armor": ["medium"], "heavy armor": ["heavy"], "shields": ["shield"],
          "shield": ["shield"], "all armor": ["light", "medium", "heavy"]}
_WEAPONS = {"simple weapons": "simple", "martial weapons": "martial", "all simple weapons": "simple",
            "all martial weapons": "martial"}


def _items(value: str) -> list[str]:
    value = re.sub(r"\s+", " ", value).strip(" .")
    if not value or value.lower().startswith("none"):
        return []
    return [t.strip() for t in re.split(r",\s*(?![^()]*\))", value) if t.strip()]


def _armor(value: str) -> list | None:
    out: list = []
    for t in _items(value):
        key = t.lower()
        bare = _PAREN.sub("", key).strip()
        if key in _ARMOR:
            out += [x for x in _ARMOR[key] if x not in out]
        elif bare in _ARMOR and len(_ARMOR[bare]) == 1:  # "shields (druids will not ... made of metal)"
            out.append({"proficiency": _ARMOR[bare][0], "full": key})
        else:
            out.append(key)
    return out or None


def _weapons(value: str) -> list | None:
    out = [_WEAPONS.get(t.lower(), t.lower()) for t in _items(value)]
    return out or None


def _skills(value: str) -> list | None:
    v = re.sub(r"\s+", " ", value)
    m = re.match(r"^choose (\w+)(?: skills?)? from (.*)$", v.strip(" ."), re.I)
    if m:
        count = _NUMBER_WORDS.get(m.group(1).lower())
        names = []
        for tok in re.split(r",|\band\b", m.group(2)):
            tok = tok.strip(" .")
            if not tok:
                continue
            hit = next((s for s in SKILLS if _like(tok, s, 85)), None)
            if hit is None:
                return None
            names.append(hit.lower())
        return [{"choose": {"from": names, "count": count}}] if count and names else None
    m = re.match(r"^choose any (\w+)", v.strip(), re.I)
    if m and _NUMBER_WORDS.get(m.group(1).lower()):
        return [{"any": _NUMBER_WORDS[m.group(1).lower()]}]
    return None


def _proficiencies(L: list[Line], a: int, end: int) -> tuple[list[str] | None, dict | None]:
    vals: dict[str, str] = {}
    for i in range(a, min(end, a + 40)):
        for label in ("Armor", "Weapons", "Tools", "Saving Throws", "Skills"):
            if label not in vals:
                v = _label(L, i, label)
                if v is not None:
                    vals[label] = v
    saves = _abilities(vals["Saving Throws"]) if "Saving Throws" in vals else None
    sp: dict = {}
    if "Armor" in vals and (arm := _armor(vals["Armor"])):
        sp["armor"] = arm
    if "Weapons" in vals and (wpn := _weapons(vals["Weapons"])):
        sp["weapons"] = wpn
    if "Tools" in vals and (tools := _items(vals["Tools"])):
        sp["tools"] = [t.lower() for t in tools]
    if "Skills" in vals and (sk := _skills(vals["Skills"])):
        sp["skills"] = sk
    return saves, sp or None


def _multiclassing(text: str) -> dict | None:
    """Only when the class pages print it (TCE does; the PHB prints multiclassing in chapter 6)."""
    flat = re.sub(r"\s+", " ", text)
    if not re.search(r"Ability Score Minimum", flat, re.I):
        return None
    req: dict = {}
    for word, score in re.findall(r"\b(?:an?|and)\s+([A-Z][a-z]+)\s+score of (\d+)", flat):
        ab = ABILITIES.get(word)
        if ab:
            req[ab] = int(score)
    if not req:
        return None
    out: dict = {"requirements": req}
    m = re.search(r"Proficiencies Gained\..*?:\s*(.*?)\.(?:\s|$)", flat)
    if m:
        gained: dict = {}
        for t in _items(m.group(1)):
            key = t.lower()
            if key in _ARMOR:
                gained.setdefault("armor", []).extend(x for x in _ARMOR[key] if x not in gained.get("armor", []))
            elif key in _WEAPONS:
                gained.setdefault("weapons", []).append(_WEAPONS[key])
            elif "tools" in key or "kit" in key:
                gained.setdefault("tools", []).append(key)
            else:
                gained = {}
                break
        if gained:
            out["proficienciesGained"] = gained
    return out


def _features_col(t: Table) -> int | None:
    return next((i for i, h in enumerate(t.headers) if _squash(h).endswith("features")), None)


def _cell_features(cell: str) -> list[str]:
    cell = re.sub(r"\s+", " ", cell).strip()
    if cell in ("", "-", "—"):
        return []
    return [x.strip() for x in re.split(r",\s*(?![^()]*\))", cell) if x.strip() and x.strip() not in ("-", "—")]


def _is_gain(name: str, cls: str, level: int) -> bool:
    if level not in SUBCLASS_LEVELS.get(cls, []):
        return False
    s = _squash(name)
    return s == _squash(SUBCLASS_GROUPS[cls][0]) or s.endswith("feature") or s.endswith("features")


def _majority_spelling(names: list[str]) -> dict[str, str]:
    """A name printed once that is a near-copy of a name printed several times ('Ability Score Improvernent'
    among fifteen 'Ability Score Improvement') is the OCR misreading it: map it to the majority spelling."""
    counts: dict[str, int] = {}
    for n in names:
        counts[n] = counts.get(n, 0) + 1
    out = {}
    for n, c in counts.items():
        if c == 1:
            best = max((m for m in counts if m != n and counts[m] >= 2 and _like(n, m, 92)),
                       key=lambda m: counts[m], default=None)
            if best:
                out[n] = best
    return out


def _class_features(t: Table, cls: str, src: str) -> list | None:
    col = _features_col(t)
    if col is None:
        return None
    cells = [_cell_features(row[col] if col < len(row) else "") for row in t.rows]
    fix = _majority_spelling([n for c in cells for n in c])
    out: list = []
    for idx, names in enumerate(cells):
        level = idx + 1  # R3: the row index, never the (possibly garbled) ordinal cell
        for name in (fix.get(n, n) for n in names):
            ref = f"{_FIRST_GRANT.sub('', name)}|{cls}|{src}|{level}"
            out.append({"classFeature": ref, "gainSubclassFeature": True} if _is_gain(name, cls, level) else ref)
    return out



def _ref(r) -> str:
    return r["classFeature"] if isinstance(r, dict) else r


def _ref_name(r) -> str:
    return _ref(r).split("|")[0]


def _base(name: str) -> str:
    return _PAREN.sub("", name).strip()


def _feature_names(refs: list | None) -> list[str]:
    """Distinct base feature names a heading is expected for. Later subclass gains ('Path feature') and
    improvements to an earlier feature ('Wild Shape improvement') have no heading of their own."""
    out: list[str] = []
    for r in refs or []:
        n = _base(_ref_name(r))
        sq = _squash(n)
        if isinstance(r, dict) and (sq.endswith("feature") or sq.endswith("features")):
            continue
        if re.search(r"improvements?$", sq) and sq != "abilityscoreimprovement":
            continue
        if not any(_squash(n) == _squash(o) for o in out):
            out.append(n)
    return out


def _feature_sections(L: list[Line], a: int, end: int, names: list[str], skip: set[int]) \
        -> list[tuple[str, int]]:
    """(feature name, heading index) for each expected feature, in text order. Each name takes its
    best-matching heading line (earliest on a tie), so 'Artificer Spell List' cannot claim 'Artificer
    Specialist' when the real heading is printed later; a line serves one name only."""
    scored = []
    for i in range(a + 1, end):
        if i in skip or not _heading_like(L[i].text):
            continue
        for n in names:
            r = fuzz.ratio(_squash(L[i].text), _squash(n))
            if r >= 85:
                scored.append((-r, i, n))
    found: dict[str, int] = {}
    taken: set[int] = set()
    for _, i, n in sorted(scored):
        if n not in found and i not in taken:
            found[n] = i
            taken.add(i)
    return sorted(found.items(), key=lambda kv: kv[1])


def _entries(L: list[Line], sections: list[tuple[str, int]], end: int) -> list[dict]:
    heads = {i for _, i in sections}
    out = []
    for name, i in sections:
        body: list[Line] = []
        for j in range(i + 1, end):
            t = L[j].text.strip()
            if j in heads or (_heading_like(t) and any(_like(t, h, 90) for h in _SECTION_HEADS)):
                break
            body.append(L[j])
        out.append({"type": "entries", "name": name, "entries": _paragraphs(body) if body else []})
    return out


def _in_table(l: Line, t: Table | None) -> bool:
    """A body line printed inside the level table (by position: reading order rebuilds Line objects)."""
    if t is None or t.box is None or l.page != t.page:
        return False
    x, y = (l.box[0] + l.box[2]) / 2, (l.box[1] + l.box[3]) / 2
    return t.box[0] <= x <= t.box[2] and t.box[1] <= y <= t.box[3]


def _find_table(ctx: ParseContext, first: int, last: int) -> Table | None:
    for n in range(first, last + 1):
        pg: Page | None = ctx.pages.get(n)
        if pg is None:
            continue
        t = read_table(pg, "Level")
        if t is not None and _features_col(t) is not None:
            return t
    return None


def parse_classes(body: list[Line], ctx: ParseContext) -> list[Parsed]:
    """Parse every class chapter in `body` (reading-ordered lines of the whole book)."""
    L = body
    if not L:
        return []
    med = statistics.median(l.height for l in L) or 1.0
    anchors = _anchors(L)
    out: list[Parsed] = []
    prev_end = 0
    for k, a in enumerate(anchors):
        nxt = anchors[k + 1] if k + 1 < len(anchors) else len(L)
        start, cls = _class_heading(L, a, prev_end, med)
        start = a if start is None else start
        stop = min(nxt, a + _MAX_SECTION, len(L))
        g = _group_end(L, a, stop, cls)
        end = g if g is not None else stop
        if g is None and k + 1 < len(anchors):
            # without a group heading, stop before the next class's chapter heading
            nstart, _ = _class_heading(L, nxt, a + 1, med)
            end = min(end, nstart if nstart is not None else nxt)
        prev_end = end
        lines = L[start:end]
        text = "\n".join(l.text for l in lines)
        table = _find_table(ctx, L[start].page, L[a].page + 1)
        src = class_source(cls, ctx.source) if cls else ""
        refs = _class_features(table, cls, src) if table is not None and cls else None
        skip_idx = {i for i in range(a, end) if _in_table(L[i], table)}
        sections = _feature_sections(L, a, end, _feature_names(refs), skip_idx)
        saves, sp = _proficiencies(L, a, end)
        fields: dict = {
            "hd": _hd(L[a].text), "proficiency": saves, "startingProficiencies": sp,
            "multiclassing": _multiclassing(text),
            "subclassTitle": SUBCLASS_GROUPS[cls][0] if cls and _squash(SUBCLASS_GROUPS[cls][0]) in _squash(text)
            else None,
            "classFeatures": refs, "entries": _entries(L, sections, end) or None,
        }
        p = Parsed(type="Class", name=cls, fields=fields, text=text, lines=lines)
        p.checks = class_checks(fields, table, [L[i].text for _, i in sections], text, name=cls)
        out.append(p)
    return out


# ---------- checks ----------
def _bonus(cell: str) -> str:
    """Normalise a proficiency-bonus cell. The OCR reads a printed '+3' as 'E+' (a known confusion)."""
    c = re.sub(r"\s+", "", cell)
    return "+3" if c in ("E+", "ε+", "3+") else fix_digits(c)


def class_checks(fields: dict, table: Table | None, feature_headings: list[str], text: str,
                 name: str = "") -> list[Check]:
    who = name or (m.group(1).title() if (m := re.search(r"per (\w+) level", text)) else "this class")
    return [_hit_die(fields, text), _table_read(table, who), _bonus_steps(table, who),
            _features_match(fields, table, feature_headings, who)]


def _hit_die(fields: dict, text: str) -> Check:
    name = "hit-die-consistent"
    hd = fields.get("hd") or {}
    faces = hd.get("faces")
    if not faces:
        return Check(name, False, "Hit Dice not read")
    flat = fix_digits(re.sub(r"\s+", " ", text))
    m = re.search(r"Hit Points at 1st Level\s*[:;]?\s*(\d+)", flat, re.I)
    if not m:
        return Check(name, False, f"Hit Dice 1d{faces} read but no 'Hit Points at 1st Level' value found")
    first = int(m.group(1))
    if first != faces:
        return Check(name, False, f"Hit Dice 1d{faces} but Hit Points at 1st Level is {first}")
    m = re.search(r"Hit Points at Higher Levels\s*[:;]?\s*1d(\d+)\s*\(or (\d+)\)", flat, re.I)
    if m and (int(m.group(1)) != faces or int(m.group(2)) != faces // 2 + 1):
        return Check(name, False, f"Hit Dice 1d{faces} but Hit Points at Higher Levels is "
                                  f"1d{m.group(1)} (or {m.group(2)}); expected 1d{faces} (or {faces // 2 + 1})")
    return Check(name, True)


def _table_read(table: Table | None, who: str) -> Check:
    name = "level-table-read"
    if table is None:
        return Check(name, False, f"no level table with a Features column read for {who}")
    if len(table.rows) != 20:
        return Check(name, False, f"{who} level table has {len(table.rows)} rows, expected 20")
    if _features_col(table) is None:
        return Check(name, False, f"{who} level table has no Features column (headers {table.headers})")
    return Check(name, True)


def _bonus_steps(table: Table | None, who: str) -> Check:
    name = "proficiency-bonus-steps"
    if table is None:
        return Check(name, False, f"no level table read for {who}")
    col = next((i for i, h in enumerate(table.headers)
                if "proficiency" in _squash(h) or _squash(h).endswith("bonus")), None)
    if col is None:
        return Check(name, False, f"{who} level table has no Proficiency Bonus column")
    bad = []
    for idx, row in enumerate(table.rows):
        lvl = idx + 1
        want = f"+{2 + (lvl - 1) // 4}"
        got = _bonus(row[col]) if col < len(row) else ""
        if got != want:
            bad.append(f"level {lvl}: {got or 'empty'!s} (expected {want})")
    if bad:
        return Check(name, False, f"{who} proficiency bonus off at " + "; ".join(bad[:5]))
    return Check(name, True)


def _features_match(fields: dict, table: Table | None, headings: list[str], who: str) -> Check:
    name = "features-match-table"
    if table is None:
        return Check(name, False, f"no level table read for {who}, so its features cannot be matched")
    refs = fields.get("classFeatures")
    if not refs:
        return Check(name, False, f"no features read from the {who} level table")
    problems = []
    # the printed heading must spell the table's name (casing aside; 'Signature Spell' / 'Signature Spells')
    missing = [n for n in _feature_names(refs) if not any(_squash(n).rstrip("s") == _squash(h).rstrip("s")
                                                          for h in headings)]
    if missing:
        problems.append("no heading in the class text for " + ", ".join(missing))
    cls = _ref(refs[0]).split("|")[1]
    if not any(_ref(r).endswith("|1") for r in refs):
        problems.append("level 1 lists no features")
    problems += _level_cues(fields.get("entries") or [], refs)
    gains = sorted({int(_ref(r).rsplit("|", 1)[1]) for r in refs if isinstance(r, dict)})
    want = SUBCLASS_LEVELS.get(cls)
    if want is not None and gains != want:
        problems.append(f"subclass feature levels {gains} but {cls} gains them at {want}")
    if problems:
        return Check(name, False, f"{who}: " + "; ".join(problems))
    return Check(name, True)


_CUE = re.compile(r"^(?:at|starting at|beginning at|when you reach)\s+(\S+)\s+level\b", re.I)


def _level_cues(entries: list, refs: list) -> list[str]:
    """A feature whose text opens with "At 2nd level" / "Starting at 3rd level" must sit at that level in the
    table (catches a Features cell read into the wrong row)."""
    out = []
    for e in entries:
        if not isinstance(e, dict) or not e.get("entries"):
            continue
        m = _CUE.match(str(e["entries"][0]).strip())
        lvl = ordinal(fix_digits(m.group(1)).rstrip(",")) if m else None
        if lvl is None:
            continue
        levels = sorted({int(_ref(r).rsplit("|", 1)[1]) for r in refs
                         if _squash(_base(_ref(r).split("|")[0])).rstrip("s") == _squash(e["name"]).rstrip("s")})
        if levels and lvl not in levels:
            out.append(f"{e['name']} text says {m.group(1)} level but the table lists it at level {levels}")
    return out

