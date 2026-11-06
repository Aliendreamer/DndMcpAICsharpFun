"""Trap parser: printed traps and hazards (5etools `trapshazards.json` `trap` and `hazard`).

Traps are found by the line printed under their heading, which also gives `trapHazType`:

- "Mechanical trap" / "Magic trap" (DMG sample traps): MECH / MAG. `entries` are the prose paragraphs.
- "Simple trap (level 1-4, dangerous threat)" / "Complex trap (...)" (XGE): SMPL / CMPX, with `rating`
  [{tier, threat}] (the tier is the printed level range: 1-4, 5-10, 11-16, 17-20). The description is `entries`;
  the "Trigger.", "Effect.", "Initiative.", "Active/Dynamic/Constant Element(s)." and "Countermeasures." paragraphs
  are `trigger`, `effect`, `initiative` (5etools code: 1 = count 10, 2 = count 20, 3 = both), `eActive`, `eDynamic`,
  `eConstant` and `countermeasures`. A complex trap's named parts ("Whirling Blades (Initiative 20). ...") are list
  items.
- "Haunt Bonus +4" (VRGR haunted traps): HAUNT, with `hauntBonus`.

Hazards are the sub-headings (or "Name. ..." lead-in paragraphs, when the section has no sub-headings) of a
section whose heading names hazards: "Wilderness Hazards" / "Natural Hazards" (WLD), "Weather" (WTH), "Eldritch
Storms" (EST); "Dungeon Hazards" prints no kind, so `trapHazType` is absent. A heading about the hazards themselves
("Spell Equivalents of Natural Hazards") is not one. A section ends at a major heading (set wider than a
sub-heading) or a chapter title.

Every entity ends at the next heading: a short title-case line after a finished sentence, followed by prose, set
at heading size (an art caption, set small and narrow, is not one). Running heads and feet, page numbers, art
credits, sidebars, art text (set much larger) and captions set apart from the column are not text.

`detectDc` and `disarmDc` are recorded only where printed: a DC in a sentence about spotting / revealing /
deducing the trap ("The DC to spot the trip wire is 10", "A successful DC 15 Wisdom (Perception) check reveals")
or about disabling it ("A successful DC 15 Dexterity check using thieves' tools disables"); a spell's DC ("dispel
magic (DC 13) ... destroys the trap") counts for disarming only when no ability check is printed for it. Parts
printed with different DCs (the four DMG pits) leave the field absent, and say so. `trap-dcs-parsed` fails on a DC
it cannot read.

Not registered in assemble yet (Phase 2b ruling P1).
"""
from __future__ import annotations

import re
from dataclasses import replace
import statistics

from ..checks import Check
from ..layout import fix_digits
from ..model import Line
from . import ParseContext, Parsed
from .charcommon import SENTENCE_END, character_body, heading_like, prose_entries, title_case
from .monster import _is_heading_caps, _join_text, _title

_MECH = re.compile(r"^(mechanical|magic|magical) trap$", re.I)
_LEVELLED = re.compile(r"^(simple|complex) trap\s*\(\s*level\s*(?P<lv>[^,]*?)\s*,\s*(?P<threat>\w+) threat\s*\)?$",
                       re.I)
_HAUNT = re.compile(r"^haunt bonus\s*(?P<b>[+-]\s*\S+)$", re.I)
_TIERS = {"14": 1, "510": 2, "1116": 3, "1720": 4}
_THREATS = {"moderate", "dangerous", "deadly"}

_SECTION = re.compile(r"^(?:(?P<kind>dungeon|wilderness|natural) hazards|(?P<weather>weather)|"
                      r"(?P<storm>eldritch storms))$", re.I)
_SECTION_CODE = {"wilderness": "WLD", "natural": "WLD"}
_META = re.compile(r"\b(hazards?|traps?)\b", re.I)  # a heading about hazards or traps, not one of them

_KEYS = {"trigger": "trigger", "effect": "effect", "initiative": "initiative", "active element": "eActive",
         "active elements": "eActive", "dynamic element": "eDynamic", "dynamic elements": "eDynamic",
         "constant element": "eConstant", "constant elements": "eConstant", "countermeasures": "countermeasures"}
_KEY_LEAD = re.compile(r"^(?P<key>trigger|effect|initiative|(?:active|dynamic|constant) elements?|countermeasures)"
                       r"\.\s*(?P<rest>.*)$", re.I)
_PART = re.compile(r"^(?P<name>[A-Z][^.]{1,70}?)\.\s+(?P<rest>[A-Z(\"'].*)$")
_LEAD = re.compile(r"^(?P<name>[A-Z][A-Za-z'’-]*(?: (?:[A-Z][A-Za-z'’-]*|of|and|the|a|an|in|on|to)){0,4})\.\s+"
                   r"(?P<rest>\S.*)$")

_RUNNING = re.compile(r"(?i)^(\d+\s+)?(chapter|part|appendix)\s+\w+\s*\|")
_CHAPTER = re.compile(r"(?i)^chapter\s+\d+\s*:\s*[A-Z]")
_PAGE_NO = re.compile(r"^\d{1,3}\W?$")

# geometry, in book-median line heights (measured on DMG, XGE, TCE and VRGR)
_SMALL, _LARGE = 0.75, 2.0  # a line set this much smaller (caption) or larger (art text) is not body text
_OFF_COLUMN = 6.0  # a line starting this far right of its column's text edge is a caption beside the art
_HEAD_WIDTH = 0.45  # a heading's average character width, at least (a narrow art caption is less)
_GAP = 1.8  # a heading after a table has this much space above it (line tops; rows are about 1.3 apart)
_MAJOR_WIDTH = 0.70  # a section-level heading's average character width, at least (a sub-heading is less)


def _medians(L: list[Line]) -> dict[int, float]:
    """The book's median line height, keyed by page (an art page's few huge lines don't set the scale)."""
    m = statistics.median(l.height for l in L)
    return {l.page: m for l in L}


def _clean(L: list[Line], ctx: ParseContext) -> list[Line]:
    """The body lines that are text: no running head or foot, page number, art text or off-column caption."""
    med = _medians(L)
    edges: dict[tuple[int, int], float] = {}

    def col(l: Line) -> tuple[int, int]:
        pg = ctx.pages.get(l.page)
        mid = pg.width / 2 if pg else 800
        return l.page, int((l.box[0] + l.box[2]) / 2 > mid)

    for l in L:
        if _SMALL * med[l.page] <= l.height <= _LARGE * med[l.page] and len(l.text) >= 20:
            k = col(l)
            edges[k] = min(edges.get(k, l.box[0]), l.box[0])
    out = []
    for l in L:
        t, m = l.text.strip(), med[l.page]
        if not t or _RUNNING.match(t) or _PAGE_NO.fullmatch(t) or l.height > _LARGE * m:
            continue
        if l.height < _SMALL * m and (not re.search(r"[a-z]", t) or heading_like(t) and title_case(t)):
            continue  # a caption set small ("HAUNTED TRAP", "ELDritch StoRm"); a paragraph's short last line
            # ("can escape.") is text
        if l.box[0] - edges.get(col(l), l.box[0]) > _OFF_COLUMN * m:
            continue
        out.append(l)
    return [l for k, l in enumerate(out) if not _caption(out, k)]


def _caption(L: list[Line], k: int) -> bool:
    """An art caption read into the middle of a sentence ("... the area in the storm" / "ELDritch StoRm" / "is
    heavily obscured"): a title-case line between a sentence's halves, set well apart from the line after it."""
    t = L[k].text.strip()
    if not (0 < k < len(L) - 1 and heading_like(t) and title_case(t)):
        return False
    prev, nxt = L[k - 1], L[k + 1]
    return not prev.text.rstrip().endswith(SENTENCE_END) and nxt.text.strip()[:1].islower() and \
        nxt.page == L[k].page and nxt.box[1] - L[k].box[1] >= _GAP * L[k].height


def _width(l: Line, med: float) -> float:
    return (l.box[2] - l.box[0]) / max(len(l.text.strip()), 1) / med


def _prose(t: str) -> bool:
    return sum(w[:1].islower() for w in t.split()) >= 3


def _type_line(t: str) -> bool:
    t = t.strip()
    return bool(_MECH.match(t) or _LEVELLED.match(t) or _HAUNT.match(t))


def _is_head(L: list[Line], i: int, med: dict[int, float]) -> bool:
    """A heading: a short title-case line after a finished sentence, set at heading size, over prose or a trap's
    type line (a table caption sits over the table's header row instead)."""
    t = L[i].text.strip()
    if _CHAPTER.match(t):
        return True
    if not heading_like(t) or not (title_case(t) or _is_heading_caps(t)) or _type_line(t):
        return False
    if i > 0 and not (L[i - 1].text.rstrip().endswith(SENTENCE_END) or _set_apart(L[i - 1], L[i])):
        return False
    if _width(L[i], med[L[i].page]) < _HEAD_WIDTH:
        return False
    return i + 1 < len(L) and (_prose(L[i + 1].text) or _type_line(L[i + 1].text))


def _set_apart(prev: Line, l: Line) -> bool:
    """`l` opens a page or column, or has space above it (a heading after a table)."""
    return prev.page != l.page or prev.box[1] > l.box[1] or l.box[1] - prev.box[1] >= _GAP * l.height


def _major(L: list[Line], i: int, med: dict[int, float]) -> bool:
    return _width(L[i], med[L[i].page]) >= _MAJOR_WIDTH or bool(_CHAPTER.match(L[i].text.strip()))


def _name(t: str) -> str:
    return _title(t.strip())


# ---------- DCs ----------
_DETECT = re.compile(r"\b(spot|detect|notic|reveal|discern|deduc|recogni[sz]|locat|find|perception)", re.I)
_DISARM = re.compile(r"\b(disabl|disarm|destroys? the trap|destroy the trap|breaks? the trip wire|neutraliz|"
                     r"deactivat)", re.I)
_DC = re.compile(r"(?P<paren>\()?\bDC\s+(?:to\s+[^.]*?\s+is\s+|is\s+|of\s+)?(?P<v>[^\s,.;)]+)")


_OCR_DIGITS = str.maketrans({"S": "5", "s": "5", "O": "0", "o": "0", "l": "1", "I": "1", "Z": "2", "B": "8"})


def _dc_value(tok: str) -> int | None:
    """A printed DC ("15", or the OCR's "lS"); None when it isn't one."""
    v = tok.translate(_OCR_DIGITS) if re.fullmatch(r"[0-9SsOolIZB]{1,2}", tok) else tok
    return int(v) if v.isdigit() and 5 <= int(v) <= 30 else None


def _sentences(text: str) -> list[str]:
    return [s for s in re.split(r"(?<=[.;!?])\s+(?=[A-Z])", text) if s]


def trap_dcs(text: str) -> tuple[dict, Check]:
    """`detectDc` / `disarmDc` from the printed sentences, and the `trap-dcs-parsed` check."""
    found: dict[str, list[tuple[int, bool]]] = {"detectDc": [], "disarmDc": []}
    bad: list[str] = []
    for s in _sentences(text):
        if "saving throw" in s.lower():
            continue
        kinds = [k for k, rx in (("disarmDc", _DISARM), ("detectDc", _DETECT)) if rx.search(s)]
        if not kinds:
            continue
        kind = kinds[0]  # "reveals the trip wire. ... disables" are separate sentences; both words: disarming
        for m in _DC.finditer(s):
            v = _dc_value(m.group("v"))
            if v is None:
                bad.append(f"DC {m.group('v')!r} in {s[:60]!r}")
            else:
                found[kind].append((v, bool(m.group("paren"))))
    fields: dict = {}
    notes: list[str] = []
    for key, vals in found.items():
        checks = {v for v, spell in vals if not spell}
        spells = {v for v, spell in vals if spell}
        use = checks or spells
        if len(use) == 1:
            fields[key] = next(iter(use))
        elif use:
            notes.append(f"{key} not recorded: different DCs printed ({', '.join(map(str, sorted(use)))})")
    if bad:
        return fields, Check("trap-dcs-parsed", False, "unreadable " + "; ".join(bad))
    return fields, Check("trap-dcs-parsed", True, "; ".join(notes))


# ---------- paragraphs ----------
def _paras(L: list[Line], s: int, e: int, first: str | None = None) -> list:
    """Prose paragraphs of L[s:e]; `first` replaces the text of L[s] (a lead-in's rest)."""
    if s >= e:
        return []
    sub = L[s:e]
    if first is not None:
        sub = [replace(sub[0], text=first)] + sub[1:]
    return prose_entries(sub, 0, len(sub))


def _part_name(name: str) -> bool:
    """A complex trap's named part ("Whirling Blades (Initiative 20)", "Intelligence (Investigation), DC 15")."""
    core = re.sub(r"\([^)]*\)", "", name)
    has_dc = bool(re.search(r",\s*DC\s*\S+\s*$", core))
    core = re.sub(r",\s*DC.*$", "", core).strip()
    return 0 < len(core.split()) <= 8 and (title_case(core) or has_dc)


def _element(L: list[Line], s: int, e: int, rest: str) -> list:
    """A complex trap's section: its introduction, then its named parts as a list."""
    starts = [j for j in range(s + 1, e) if L[j - 1].text.rstrip().endswith(SENTENCE_END)
              and (m := _PART.match(L[j].text.strip())) and _part_name(m.group("name"))]
    out = _paras(L, s, starts[0] if starts else e, rest)
    items = []
    for k, j in enumerate(starts):
        end = starts[k + 1] if k + 1 < len(starts) else e
        m = _PART.match(L[j].text.strip())
        body = _paras(L, j, end, m.group("rest"))
        item = {"type": "item", "name": m.group("name")}
        if len(body) == 1:
            item["entry"] = body[0]
        else:
            item["entries"] = body
        items.append(item)
    if items:
        out.append({"type": "list", "style": "list-hang-notitle", "items": items})
    return out


def _initiative(text: str) -> tuple[int | None, str | None]:
    counts = set(re.findall(r"initiative count (\d+)", text))
    code = {frozenset({"10"}): 1, frozenset({"20"}): 2, frozenset({"10", "20"}): 3}.get(frozenset(counts))
    note = re.search(r"\(([^)]*)\)", text)
    return code, note.group(1) if note else None


def _flat(e) -> str:
    if isinstance(e, str):
        return e
    if isinstance(e, list):
        return " ".join(_flat(x) for x in e)
    if isinstance(e, dict):
        return " ".join(_flat(e.get(k)) for k in ("name", "entry", "entries", "items") if e.get(k))
    return ""


# ---------- traps ----------
def _trap(L: list[Line], h: int, e: int) -> Parsed:
    name = _name(L[h].text)
    tline = L[h + 1].text.strip()
    f: dict = {}
    checks: list[Check] = []
    complex_ = False
    if m := _MECH.match(tline):
        f["trapHazType"] = "MECH" if m.group(1).lower() == "mechanical" else "MAG"
    elif m := _LEVELLED.match(tline):
        complex_ = m.group(1).lower() == "complex"
        f["trapHazType"] = "CMPX" if complex_ else "SMPL"
        lv = re.sub(r"[^0-9]", "", fix_digits(m.group("lv")))
        threat = m.group("threat").lower()
        if lv in _TIERS and threat in _THREATS:
            f["rating"] = [{"tier": _TIERS[lv], "threat": threat}]
            checks.append(Check("trap-rating-parsed", True))
        else:
            checks.append(Check("trap-rating-parsed", False, f"unreadable level or threat in {tline!r}"))
    elif m := _HAUNT.match(tline):
        f["trapHazType"] = "HAUNT"
        b = re.sub(r"\s+", "", m.group("b"))
        b = b[0] + fix_digits(b[1:])
        if re.fullmatch(r"[+-]\d+", b):
            f["hauntBonus"] = b
            checks.append(Check("trap-rating-parsed", True))
        else:
            checks.append(Check("trap-rating-parsed", False, f"unreadable haunt bonus in {tline!r}"))
    keys = [j for j in range(h + 2, e) if _KEY_LEAD.match(L[j].text.strip())
            and (j == h + 2 or L[j - 1].text.rstrip().endswith(SENTENCE_END))]
    entries = _paras(L, h + 2, keys[0] if keys else e)
    if entries:
        f["entries"] = entries
    for k, j in enumerate(keys):
        end = keys[k + 1] if k + 1 < len(keys) else e
        m = _KEY_LEAD.match(L[j].text.strip())
        key = _KEYS[m.group("key").lower()]
        if key == "initiative":
            text = _join_text([m.group("rest")] + [l.text.strip() for l in L[j + 1:end]])
            code, note = _initiative(text)
            if code:
                f["initiative"] = code
                if note:
                    f["initiativeNote"] = note
            continue
        f[key] = _element(L, j, end, m.group("rest")) if complex_ else _paras(L, j, end, m.group("rest"))
    dc_text = _flat(f.get("countermeasures")) if "countermeasures" in f else _flat(f.get("entries"))
    dcs, check = trap_dcs(dc_text)
    f.update(dcs)
    text = f"{name}. " + _join_text([l.text.strip() for l in L[h + 1:e]])
    return Parsed(type="Trap", name=name, fields=f, text=text, lines=L[h:e], checks=[check] + checks)


def _end(L: list[Line], i: int, heads: list[int]) -> int:
    later = [j for j in heads if j > i]
    return later[0] if later else len(L)


# ---------- hazards ----------
def _hazard(L: list[Line], name: str, s: int, e: int, code: str | None, rest: str | None, lines: list[Line]) -> Parsed:
    f: dict = {}
    if code:
        f["trapHazType"] = code
    entries = _paras(L, s, e, rest)
    if entries:
        f["entries"] = entries
    dcs, check = trap_dcs(_flat(entries))
    f.update(dcs)
    first = [rest] if rest is not None else []
    text = f"{name}. " + _join_text(first + [l.text.strip() for l in L[s + (rest is not None):e]])
    return Parsed(type="Trap", name=name, fields=f, text=text.strip(), lines=lines, checks=[check])


def _hazards(L: list[Line], heads: list[int], med: dict[int, float]) -> list[Parsed]:
    out = []
    for i in heads:
        m = _SECTION.match(L[i].text.strip())
        if not m:
            continue
        code = "WTH" if m.group("weather") else "EST" if m.group("storm") else \
            _SECTION_CODE.get((m.group("kind") or "").lower())
        later = [j for j in heads if j > i]
        if _major(L, i, med):
            stop = next((j for j in later if _major(L, j, med)), len(L))
        else:  # a sub-level section ("Eldritch Storms") ends at the next heading of any level
            stop = later[0] if later else len(L)
        subs = [j for j in later if j < stop and not (j + 1 < len(L) and _type_line(L[j + 1].text))]
        if subs and _major(L, i, med):
            for k, j in enumerate(subs):
                e = subs[k + 1] if k + 1 < len(subs) else stop
                if _META.search(L[j].text):
                    continue
                out.append(_hazard(L, _name(L[j].text), j + 1, e, code, None, L[j:e]))
            continue
        leads = [j for j in range(i + 1, stop) if L[j - 1].text.rstrip().endswith(SENTENCE_END)
                 and _LEAD.match(L[j].text.strip()) and _indented(L, j)]
        for k, j in enumerate(leads):
            e = leads[k + 1] if k + 1 < len(leads) else stop
            lm = _LEAD.match(L[j].text.strip())
            out.append(_hazard(L, _name(lm.group("name")), j, e, code, lm.group("rest"), L[j:e]))
    return out


def _indented(L: list[Line], j: int) -> bool:
    """A paragraph's first line: set in from the line before it in the same column."""
    a, b = L[j - 1], L[j]
    return a.page == b.page and abs(a.box[1] - b.box[1]) < 3 * b.height and b.box[0] - a.box[0] > 0.4 * b.height


def parse_traps(body: list[Line], ctx: ParseContext) -> list[Parsed]:
    L = _clean(character_body(body, ctx), ctx)
    if not L:
        return []
    med = _medians(L)
    heads = [i for i in range(len(L)) if _is_head(L, i, med)]
    out = []
    for i in heads:
        if i + 1 < len(L) and _type_line(L[i + 1].text):
            out.append(_trap(L, i, _end(L, i, heads)))
    return out + _hazards(L, heads, med)
