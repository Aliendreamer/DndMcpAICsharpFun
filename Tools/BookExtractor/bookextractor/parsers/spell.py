"""Spell parser (Player's Handbook layout) and its verification checks."""
from __future__ import annotations

import re
import statistics

from rapidfuzz import fuzz, process

from ..checks import Check
from ..model import Line
from . import Parsed
from .monster import _after_label, _is_heading_caps, _join_text, _lab, _title

SCHOOLS = {"abjuration": "A", "conjuration": "C", "divination": "D", "enchantment": "E",
           "evocation": "V", "illusion": "I", "necromancy": "N", "transmutation": "T"}
LABELS = ["Casting Time", "Range", "Components", "Duration"]
# how many lines a value may wrap over before the next label must appear
_WINDOW = {0: 4, 1: 3, 2: 10}
_DIGIT = str.maketrans({"I": "1", "i": "1", "l": "1", "O": "0", "o": "0", "S": "5", "Z": "2", "B": "8"})
_NUM = r"[0-9IiOoSZBl]+"
_LEVEL = re.compile(r"^([0-9IlOSZB]{1,2})(?:st|nd|rd|th)[- ]?level\s*([A-Za-z]{6,})\s*(\(\s*\w*\s*\)?)?$")
_CANTRIP = re.compile(r"^([A-Za-z]{6,})\s+cantrip\s*(\(\s*\w*\s*\)?)?$", re.I)
_TIME_UNITS = {"action": "action", "bonus action": "bonus", "reaction": "reaction", "round": "round",
               "minute": "minute", "hour": "hour", "day": "day"}
_TIME = re.compile(rf"^({_NUM})\s*(bonus action|action|reaction|round|minute|hour|day)s?\b", re.I)
_PAGE_FURNITURE = re.compile(r"^(?:part \d+\s*\|?\s*spells|spells\s*\|?\s*part \d+|chapter \d+\W.*)$", re.I)
_WORDS = ["one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten"]
_HIGHER = re.compile(r"\bat higher levels?\.\s*", re.I)
_ORD = re.compile(r"\b[Il](?=st\b)")


def _num(s: str) -> int:
    return int(s.translate(_DIGIT))


def _school(word: str) -> str | None:
    hit = process.extractOne(word.lower(), list(SCHOOLS), score_cutoff=70)
    return SCHOOLS[hit[0]] if hit else None


def _ritual(token: str | None) -> bool:
    word = re.sub(r"[^a-z]", "", (token or "").lower())
    return bool(word) and fuzz.ratio(word, "ritual") >= 70


def _level_line(t: str) -> tuple[int, str | None, bool] | None:
    m = _LEVEL.match(t)
    if m:
        return _num(m.group(1)), _school(m.group(2)), _ritual(m.group(3))
    m = _CANTRIP.match(t)
    if m:
        return 0, _school(m.group(1)), _ritual(m.group(2))
    return None


def _starts(L: list[Line]) -> list[int]:
    """Index of the 'Nth-level school' line, anchored on a Casting Time label right below it."""
    st: list[int] = []
    for i, l in enumerate(L):
        if i == 0 or _level_line(l.text) is None:
            continue
        if any(_lab(L[j].text, "Casting Time") for j in range(i + 1, min(i + 3, len(L)))):
            st.append(i)
    return st


# ---------- field conversion ----------
def _time_field(raw: str | None) -> list[dict] | None:
    if not raw:
        return None
    head, _, cond = raw.partition(",")
    out: list[dict] = []
    for part in re.split(r"\s+or\s+", head.strip(), flags=re.I):
        m = _TIME.match(part.strip())
        if not m:
            return None
        out.append({"number": _num(m.group(1)), "unit": _TIME_UNITS[m.group(2).lower()]})
    if cond.strip():
        out[-1]["condition"] = cond.strip()
    return out


def _dist_unit(w: str) -> str:
    return "miles" if w.lower().startswith("mile") else "feet"


def _range_field(raw: str | None) -> dict | None:
    if not raw:
        return None
    t = raw.strip().lower()
    for word in ("touch", "sight", "unlimited"):
        if t == word:
            return {"type": "point", "distance": {"type": word}}
    if t == "special":
        return {"type": "special"}
    if t == "self":
        return {"type": "point", "distance": {"type": "self"}}
    m = re.match(rf"^self\s*\(\s*({_NUM})[- ](foot|feet|mile|miles)\b(.*)\)$", t)
    if m:
        rest = m.group(3)
        shape = next((s for s in ("hemisphere", "sphere", "cone", "line", "cube") if s in rest), "radius")
        return {"type": shape, "distance": {"type": _dist_unit(m.group(2)), "amount": _num(m.group(1))}}
    m = re.match(rf"^({_NUM})\s*(feet|foot|miles?)$", t)
    if m:
        return {"type": "point", "distance": {"type": _dist_unit(m.group(2)), "amount": _num(m.group(1))}}
    return None


def _components_field(raw: str | None) -> dict | None:
    if not raw:
        return None
    raw = re.sub(r"\b([VSM])\s*[.;]\s*(?=[VSM]\b)", r"\1, ", raw.strip())  # OCR: "V. S" for "V, S"
    m = re.match(r"^([VSMvsm](?:\s*,\s*[VSMvsm])*)\s*(?:\((.*?)\)?)?\s*$", raw, re.S)
    if not m:
        return None
    letters = {x.strip().upper() for x in m.group(1).split(",")}
    out: dict = {}
    if "V" in letters:
        out["v"] = True
    if "S" in letters:
        out["s"] = True
    if "M" in letters:
        out["m"] = re.sub(r"\s+", " ", m.group(2) or "").strip()
    return out


def _duration_field(raw: str | None) -> list[dict] | None:
    if not raw:
        return None
    t = raw.strip().rstrip(".")
    if t.lower().startswith("instantaneous"):
        return [{"type": "instant"}]
    if t.lower() == "special":
        return [{"type": "special"}]
    if t.lower().startswith("until dispelled"):
        return [{"type": "permanent", "ends": ["dispel", "trigger"] if "trigger" in t.lower() else ["dispel"]}]
    t = re.sub(r"(?i)\b(one|two|three|four|five|six|seven|eight|nine|ten)\b",
               lambda m: str(_WORDS.index(m.group(1).lower()) + 1), t)
    m = re.match(rf"^(concentration,?\s*)?(?:up to\s+)?({_NUM})\s*(round|minute|hour|day)s?$", t, re.I)
    if not m:
        return None
    d: dict = {"type": "timed", "duration": {"type": m.group(3).lower(), "amount": _num(m.group(2))}}
    if m.group(1):
        d["concentration"] = True
    return [d]


# ---------- block parsing ----------
def _strip_label(t: str, label: str) -> str:
    """Drop 'Components:' / 'Component:' / 'Components:V' (OCR drops letters or the space after the colon)."""
    m = re.match(r"^[A-Za-z ]{3,16}?\s*[:;]\s*", t)
    return t[m.end():].strip() if m else _after_label(t, label).lstrip(": ").strip()


def _header(L: list[Line], i: int, end: int) -> tuple[dict[str, str | None], int]:
    """Read the four labelled lines below the level line; returns (raw values, index where the body starts)."""
    vals: dict[str, str | None] = {k: None for k in LABELS}
    j = i + 1
    pos: list[int] = []
    for k, label in enumerate(LABELS):
        lo = j if k == 0 else pos[-1] + 1
        hi = min(end, lo + (2 if k == 0 else _WINDOW[k - 1]))
        hit = next((x for x in range(lo, hi) if _lab(L[x].text, label)), None)
        if hit is None:
            break
        pos.append(hit)
    for k, p in enumerate(pos):
        stop = pos[k + 1] if k + 1 < len(pos) else p + 1
        parts = [_strip_label(L[p].text, LABELS[k])] + [L[x].text for x in range(p + 1, stop)]
        vals[LABELS[k]] = _join_text([s for s in parts if s])
    nxt = pos[-1] + 1 if pos else i + 1
    if len(pos) == 4 and _duration_field(vals["Duration"]) is None and nxt < end:
        longer = _join_text([vals["Duration"] or "", L[nxt].text])  # a wrapped duration value
        if _duration_field(longer) is not None:
            vals["Duration"] = longer
            nxt += 1
    return vals, nxt


def _paragraphs(body: list[Line]) -> list[str]:
    paras: list[list[str]] = []
    ref: float | None = None
    page: int | None = None
    prev = ""
    for l in body:
        x = l.box[0]
        if page != l.page or ref is None or abs(x - ref) > 3 * l.height:
            ref, page = x, l.page
        else:
            ref = min(ref, x)
        indented = 0.4 * l.height < x - ref <= 3 * l.height
        if paras and not (indented and re.search(r"[.!?:)\"']$", prev)):
            paras[-1].append(l.text)
        else:
            paras.append([l.text])
        prev = l.text
    return [_join_text(p) for p in paras]


def _entries(body: list[Line]) -> tuple[list[str], list[dict]]:
    entries: list[str] = []
    higher: list[dict] = []
    for para in _paragraphs(body):
        para = _ORD.sub("1", para)  # OCR "Ist" for "1st"
        m = _HIGHER.search(para)
        if m and not higher and (m.start() == 0 or para[m.start() - 1] == " "):
            if para[:m.start()].strip():
                entries.append(para[:m.start()].strip())
            higher.append({"type": "entries", "name": "At Higher Levels", "entries": [para[m.end():].strip()]})
        else:
            entries.append(para)
    return entries, higher


def _name(raw: str) -> str:
    """Printed spell name, repairing the OCR's lost spaces ('ControlWeatheR', 'Eviland Good')."""
    t = raw if " " in raw.strip() else re.sub(r"(?<=[a-z]{4})(?=[A-Z][a-z])", " ", raw)  # only a lone glued token
    t = _title(t)
    return re.sub(r"\b([A-Z][a-z]{3,})(and|of)\b(?= [A-Z])", lambda m: f"{m.group(1)} {m.group(2)}", t)


def _section_break(l: Line, med: float) -> bool:
    """A caps heading (e.g. 'PARALYZED') or an oversized heading line ends the spell text."""
    return _is_heading_caps(l.text) or l.height > 1.6 * med


def _parse_block(L: list[Line], med: float, i: int, end: int) -> Parsed:
    level, school, ritual = _level_line(L[i].text) or (0, None, False)
    vals, nxt = _header(L, i, end)
    body: list[Line] = []
    stop = end
    for x in range(nxt, end):
        if L[x].height > 1.6 * med and len(L[x].text) < 8:
            continue  # decorative art / drop-cap glyphs inside the text flow
        if _section_break(L[x], med):
            stop = x
            break
        body.append(L[x])
    duration = _duration_field(vals["Duration"])
    entries, higher = _entries(body)
    fields: dict = {
        "level": level, "school": school, "time": _time_field(vals["Casting Time"]),
        "range": _range_field(vals["Range"]), "components": _components_field(vals["Components"]),
        "duration": duration, "ritual": ritual,
        "concentration": bool(duration and any(d.get("concentration") for d in duration)),
        "entries": entries, "entriesHigherLevel": higher,
    }
    name = _name(L[i - 1].text)
    block = [L[i - 1]] + L[i:nxt] + body
    text = "\n".join(l.text for l in block)
    # the structured duration is checked against the printed line, not just against itself
    if vals["Duration"] is not None:
        text = re.sub(r"(?m)^Duration.*$", "Duration: " + vals["Duration"], text, count=1) \
            if re.search(r"(?m)^Duration", text) else text
    return Parsed(type="Spell", name=name, fields=fields, text=text, lines=block)


def parse_spells(lines: list[Line]) -> list[Parsed]:
    """Parse every spell found in `lines` (reading order, pages concatenated)."""
    L = [l for l in lines if not _PAGE_FURNITURE.match(l.text)]
    if not L:
        return []
    med = statistics.median(l.height for l in L) or 1.0
    st = _starts(L)
    out: list[Parsed] = []
    for k, i in enumerate(st):
        end = st[k + 1] - 1 if k + 1 < len(st) else min(len(L), i + 80)
        p = _parse_block(L, med, i, end)
        p.checks = spell_checks(p)
        out.append(p)
    return out


# ---------- checks ----------
_HIGHER_MARK = re.compile(r"at higher level", re.I)


def _printed(p: Parsed, label: str, until: str) -> str:
    m = re.search(rf"(?ims)^{label}\w*\s*[:;]?\s*(.*?)^{until}", p.text)
    return re.sub(r"\s+", " ", m.group(1)).strip() if m else ""


def _range_agrees(p: Parsed) -> bool:
    printed = _printed(p, "Range", "Component")
    rng = p.fields.get("range") or {}
    if not printed:
        return False
    nums = sorted(_num(n) for n in re.findall(rf"(?<![A-Za-z]){_NUM}(?![A-Za-z])", printed) if re.search(r"\d|^[IlO]$", n))
    amount = (rng.get("distance") or {}).get("amount")
    got = sorted(([amount] if amount is not None else []))
    if nums != got:
        return False
    low = printed.lower()
    dtype = (rng.get("distance") or {}).get("type")
    return all((w not in low) or dtype == w or rng.get("type") == w for w in ("touch", "sight", "unlimited", "special"))


def _components_agree(p: Parsed) -> bool:
    printed = _printed(p, "Component", "Duration")
    comp = p.fields.get("components") or {}
    head = printed.split("(")[0]
    letters = set(re.findall(r"\b[VSM]\b", head))
    have = {k.upper() for k in comp}
    return bool(letters) and letters == have
def spell_checks(p: Parsed) -> list[Check]:
    f = p.fields
    time = f.get("time") or []
    comp = f.get("components")
    dur = f.get("duration")

    def chk(name: str, ok: bool, why: str) -> Check:
        return Check(name, ok, "" if ok else why)

    level_ok = isinstance(f.get("level"), int) and 0 <= f["level"] <= 9 and f.get("school") in SCHOOLS.values()
    time_ok = bool(time) and all(isinstance(t.get("number"), int) and t["number"] > 0
                                 and t.get("unit") in _TIME_UNITS.values() for t in time)
    if time_ok and any(t["unit"] == "reaction" for t in time):
        time_ok = all(t.get("condition") for t in time if t["unit"] == "reaction")
    rng = f.get("range")
    range_ok = bool(rng) and (rng["type"] == "special" or bool(rng.get("distance")))
    comp_ok = bool(comp) and ("m" not in comp or bool(comp["m"]))
    printed = re.search(r"(?mi)^Duration:\s*(.*)$", p.text)
    shows_conc = bool(printed and "concentration" in printed.group(1).lower())
    has_conc = bool(dur) and any(d.get("concentration") for d in dur)
    conc_ok = f.get("concentration") is has_conc and (printed is None or shows_conc == has_conc)
    ents = f.get("entries") or []
    last = ents[-1] if ents else ""
    complete = bool(ents) and (not isinstance(last, str) or "\u2022" in last
                               or bool(re.search(r"[.!?:)\"'\u201d]\s*$", last)))  # a bullet list may end unpunctuated
    split_ok = not (not f.get("entriesHigherLevel") and any(_HIGHER_MARK.search(str(e)) for e in ents))
    return [
        chk("level-school", level_ok, f"level {f.get('level')!r} / school {f.get('school')!r} not recognised"),
        chk("casting-time", time_ok, "casting time missing or unparseable"),
        chk("range", range_ok and _range_agrees(p), "range missing, unparseable, or disagrees with the printed line"),
        chk("components", comp_ok and _components_agree(p),
            "components missing, unparseable, or disagree with the printed line"),
        chk("duration", bool(dur), "duration missing or unparseable"),
        chk("concentration-agrees", conc_ok, "concentration flag disagrees with the printed duration"),
        chk("entries-complete", complete, f"description does not end in terminal punctuation: ...{last[-40:]!r}"),
        chk("higher-level-split", split_ok, "'At Higher Levels' still inline in entries"),
    ]
