"""Background parser and its verification checks.

A background is anchored on its printed "Skill Proficiencies:" line; its name is the nearest heading above it
(names stay as printed, OCR small caps undone). The anchor opens a run of "Label: text" lines (Skill Proficiencies,
Tool Proficiencies, Languages, Equipment); a label's text continues onto the following lines (indented, or after a
line ending in a comma or "or"/"and") until the next "Label:" line, a heading, or a line back at the column edge.
The background's feature is the first "Feature: <Name>" line after the anchor and before the next background; its
text runs to the next heading.

Fields follow the hand-authored 5etools-aligned `BackgroundFields` schema:

- `skillProficiencies`: [{"insight": true, "religion": true}], with {"choose": {"from": [...], "count": n}} for
  "plus one from among ..." / "Choose two from among ..." and {"any": n} for "any two". A printed skill word is
  never corrected: one that is not one of the 18 skills is kept as printed (lower case) and fails `skills-valid`.
- `toolProficiencies`, `languageProficiencies`: 5etools objects ({"disguise kit": true}, {"anyGamingSet": 1},
  {"anyStandard": 2}, {"choose": {"from": [...]}}) only when every part of the printed text is understood;
  otherwise absent (the printed text stays in `entries`), never guessed. A language choice ("Choose one of
  Celestial, Draconic, Goblin, or Minotaur") keeps its words as printed; `languages-valid` fails on any word that
  is not a known language.
- `entries`: the label lines as a 5etools "list-hang-notitle" list ({"type": "item", "name": "Equipment:",
  "entry": "..."}), then the feature as {"type": "entries", "name": "Feature: Shelter of the Faithful",
  "entries": [...], "data": {"isFeature": true}}. Equipment is carried only here (`startingEquipment` is absent).
"""
from __future__ import annotations

import re

from ..checks import Check
from ..model import Line
from . import ParseContext, Parsed
from .charcommon import SKILLS, heading_like, indented, prose_entries, squash, title_case
from .monster import _join_text
from .race import tidy_case

_LABELS = {"skillproficiencies": "Skill Proficiencies:", "skillproficiency": "Skill Proficiencies:",
           "toolproficiencies": "Tool Proficiencies:", "toolproficiency": "Tool Proficiencies:",
           "languages": "Languages:", "language": "Languages:", "equipment": "Equipment:"}
_LABEL = re.compile(r"^([A-Za-z][A-Za-z ]{2,24}?)\s*:\s*(.*)$")
_AS = re.compile(r",\s*as\s+[^,]*$", re.I)  # "..., as appropriate for your order"
_FEATURE = re.compile(r"^feature\s*:\s*(.+)$", re.I)
_NAME_REACH = 80  # the name heading is printed this close above the skill line (the introduction between)
_FEATURE_REACH = 250  # the feature follows the skill line this closely
_FEATURE_LINES = 60  # a feature's text spans at most this many lines
_JOINER = re.compile(r"(?:,|\bor|\band|\bof|\bfrom|\bwith|\ba|-)$", re.I)
_NUMBERS = {"one": 1, "two": 2, "three": 3, "four": 4}
_SKILL_KEYS = {s.lower() for s in SKILLS}
_SPLIT = re.compile(r"\s*,\s*(?:and\s+|or\s+)?|\s+and\s+|\s+or\s+", re.I)
_ANY_TOOL = {"gaming set": "anyGamingSet", "gaming sets": "anyGamingSet", "artisan's tools": "anyArtisansTool",
             "musical instrument": "anyMusicalInstrument", "musical instruments": "anyMusicalInstrument"}
_TOOLS = {"alchemist's supplies", "brewer's supplies", "calligrapher's supplies", "carpenter's tools",
          "cartographer's tools", "cobbler's tools", "cook's utensils", "glassblower's tools", "jeweler's tools",
          "leatherworker's tools", "mason's tools", "painter's supplies", "potter's tools", "smith's tools",
          "tinker's tools", "weaver's tools", "woodcarver's tools", "disguise kit", "forgery kit", "herbalism kit",
          "navigator's tools", "poisoner's kit", "thieves' tools", "vehicles (land)", "vehicles (water)",
          "vehicles (air)", "vehicles (space)", "dice set", "playing card set", "dragonchess set",
          "three-dragon ante set", "bagpipes", "drum", "dulcimer", "flute", "lute", "lyre", "horn", "pan flute",
          "shawm", "viol"}
_LANGUAGES = {"common", "dwarvish", "elvish", "giant", "gnomish", "goblin", "halfling", "orc", "abyssal",
              "celestial", "draconic", "deep speech", "infernal", "primordial", "sylvan", "undercommon"}
# languages of a setting that backgrounds print by name (GGR guild choices); a word in neither set fails
_SETTING_LANGUAGES = {"minotaur", "kraul", "vedalken", "loxodon"}
_LANGUAGE_KEYS = _LANGUAGES | _SETTING_LANGUAGES


# ---------- layout ----------
def _label(t: str) -> tuple[str, str] | None:
    """('Skill Proficiencies:', 'Insight, Religion') for a label line, else None."""
    m = _LABEL.match(t.strip())
    if not m:
        return None
    lab = _LABELS.get(squash(m.group(1)))
    return (lab, m.group(2).strip()) if lab else None


def _upright(l: Line) -> bool:
    return l.box[2] - l.box[0] >= l.box[3] - l.box[1]


def _head(l: Line) -> bool:
    t = l.text.strip()
    return heading_like(t) and title_case(t) and _upright(l) and _label(t) is None and not _FEATURE.match(t)


def _anchors(L: list[Line]) -> list[int]:
    return [i for i, l in enumerate(L) if (lab := _label(l.text)) and lab[0] == "Skill Proficiencies:"]


def _name(L: list[Line], a: int, floor: int) -> tuple[str, int | None]:
    """The nearest heading above the skill line (not above the previous background's lines). A heading set on two
    lines ("BACkgrounD: Azorius" over "FUNCTIONARY") is joined; a "Background:" prefix is not part of the name."""
    for j in range(a - 1, max(floor, a - _NAME_REACH) - 1, -1):
        if _head(L[j]):
            start = j
            if j - 1 >= floor and _head(L[j - 1]) and L[j - 1].page == L[j].page and \
                    abs(L[j - 1].height - L[j].height) <= 0.15 * L[j].height:
                start = j - 1
            name = " ".join(L[k].text.strip() for k in range(start, j + 1))
            name = re.sub(r"^background\s*:\s*", "", name, flags=re.I)
            return tidy_case(name), start
    return "", None


def _labels(L: list[Line], a: int, stop: int) -> tuple[list[tuple[str, str]], int]:
    """The label run opened at the skill line: [(label, text)] and the index after it."""
    out: list[tuple[str, list[str]]] = []
    j = a
    while j < stop:
        t = L[j].text.strip()
        lab = _label(t)
        if lab:
            out.append((lab[0], [lab[1]] if lab[1] else []))
        elif out and not _FEATURE.match(t) and (_JOINER.search(" ".join(out[-1][1])) or
                                                (indented(L, j) and not _head(L[j]))):
            out[-1][1].append(t)  # a wrapped label text ("Insight," over "Religion")
        else:
            break
        j += 1
    return [(lab, _join_text(parts)) for lab, parts in out], j


def _feature(L: list[Line], start: int, stop: int) -> tuple[str, list, int, int] | None:
    """(name, entries, heading index, end index) of the first "Feature:" line in [start, stop)."""
    for j in range(start, stop):
        m = _FEATURE.match(L[j].text.strip())
        if m:
            end = next((k for k in range(j + 1, min(stop, j + 1 + _FEATURE_LINES)) if _head(L[k])),
                       min(stop, j + 1 + _FEATURE_LINES))
            return tidy_case(m.group(1).strip()), prose_entries(L, j + 1, end), j, end
    return None


# ---------- fields ----------
def _tokens(text: str) -> list[str]:
    t = re.sub(r"\s+", " ", text).strip().rstrip(".").strip()
    return [x.strip() for x in _SPLIT.split(t) if x and x.strip()]


def _skill(token: str) -> str:
    """The skill a token names, lower case, exactly as printed (never corrected)."""
    return re.sub(r"\s+", " ", token).strip().lower()


def _skills(text: str) -> list[dict] | None:
    t = re.sub(r"\s+", " ", text).strip().rstrip(".")
    t = _AS.sub("", t)
    if not t:
        return None
    if m := re.fullmatch(r"any (one|two|three|four)(?: skills?)?(?: of your choice)?", t, re.I):
        return [{"any": _NUMBERS[m.group(1).lower()]}]
    out: dict = {}
    m = re.search(r"(?:,\s*)?(?:plus\s+)?(?:your choice of\s+)?(?:choose\s+)?\b(one|two|three|four)\s+"
                  r"(?:skills?\s+)?(?:from among|of the following(?: skills)?(?: of your choice)?:?)\s+(.+)$", t, re.I)
    fixed = t[:m.start()] if m else t
    for token in _tokens(fixed):
        out[_skill(token)] = True
    if m:
        choose: dict = {"from": [_skill(x) for x in _tokens(m.group(2))]}
        n = _NUMBERS[m.group(1).lower()]
        if n != 1:
            choose["count"] = n
        out["choose"] = choose
    return [out] if out else None


def _tool(token: str, in_choice: bool) -> tuple[str, object] | None:
    low = re.sub(r"\s+", " ", token).strip().lower()
    m = re.fullmatch(r"(?:any |your choice of )?(?:one|a)(?: type of| set of)? (gaming sets?|artisan's tools|"
                     r"musical instruments?)(?: of your choice)?", low) or \
        re.fullmatch(r"(gaming set|artisan's tools|musical instrument)", low)
    if m:
        kind = m.group(1)
        if in_choice:  # 5etools names the alternatives of a choice plainly, artisan's tools as "anyArtisansTool"
            return ("anyArtisansTool" if kind == "artisan's tools" else kind.rstrip("s")), True
        return _ANY_TOOL[kind], 1
    if low in _TOOLS:
        return low, True
    return None


def _tools(text: str) -> list[dict] | None:
    t = re.sub(r"\s+", " ", text).strip().rstrip(".")
    t = re.sub(r"(?<=s')(?=[a-z])", " ", t)  # "thieves'tools": OCR drops the space after the apostrophe
    if not t:
        return None
    m = re.fullmatch(r"(?:your choice of )?(?:(?:one|a) type of |a |one )?(.+?) or (?:a |one )?(.+)", t, re.I)
    if m and "," not in t:
        a, b = _tool(m.group(1), True), _tool(m.group(2), True)
        if a and b:
            return [{"choose": {"from": [a[0], b[0]]}}]
    out: dict = {}
    for token in _tokens(t):
        got = _tool(token, False)
        if got is None:
            return None
        out[got[0]] = got[1]
    return [out] if out else None


def _languages(text: str) -> list[dict] | None:
    t = re.sub(r"\s+", " ", text).strip().rstrip(".")
    if not t:
        return None
    if m := re.fullmatch(r"choose (?:one of|either) (.+)", t, re.I):
        names = [x.lower() for x in _tokens(m.group(1))]
        return [{"choose": {"from": names}}] if names and all(re.fullmatch(r"[a-z][a-z' ]*", x) for x in names) \
            else None
    out: dict = {}
    for part in re.split(r"\s+and\s+", t):
        part = part.strip()
        if m := re.fullmatch(r"(?:any )?(one|two|three)(?: other)?(?: languages?)? of your choice", part, re.I):
            out["anyStandard"] = _NUMBERS[m.group(1).lower()]
        elif part.lower() in _LANGUAGES:
            out[part.lower()] = True
        else:
            return None
    return [out] if out else None


# ---------- parse ----------
def parse_backgrounds(body: list[Line], ctx: ParseContext) -> list[Parsed]:
    """Parse every background in `body` (reading-ordered lines of the whole book)."""
    out: list[Parsed] = []
    anchors = _anchors(body)
    floor = 0
    for k, a in enumerate(anchors):
        nxt = anchors[k + 1] if k + 1 < len(anchors) else len(body)
        labels, after = _labels(body, a, nxt)
        if len(labels) < 2:
            continue  # a lone "Skill Proficiency:" line (a stat block template's trait) opens no background
        name, head = _name(body, a, floor)
        nxt_head = _name(body, nxt, a)[1] if k + 1 < len(anchors) else None
        feat = _feature(body, after, min(nxt_head if nxt_head is not None else nxt, a + _FEATURE_REACH))
        text = {lab: t for lab, t in labels}
        fields: dict = {"skillProficiencies": _skills(text.get("Skill Proficiencies:", ""))}
        tools = _tools(text.get("Tool Proficiencies:", ""))
        if tools:
            fields["toolProficiencies"] = tools
        languages = _languages(text.get("Languages:", ""))
        if languages:
            fields["languageProficiencies"] = languages
        entries: list = [{"type": "list", "style": "list-hang-notitle",
                          "items": [{"type": "item", "name": lab, "entry": t} for lab, t in labels]}]
        end = after
        if feat:
            fname, fentries, _, end = feat
            entries.append({"type": "entries", "name": f"Feature: {fname}", "entries": fentries,
                            "data": {"isFeature": True}})
        fields["entries"] = entries
        start = head if head is not None else a
        lines = body[start:max(end, after)]
        p = Parsed(type="Background", name=name, fields=fields, text="\n".join(l.text for l in lines), lines=lines)
        p.checks = background_checks(fields)
        out.append(p)
        floor = max(end, after)
    return out


# ---------- checks ----------
def background_checks(fields: dict) -> list[Check]:
    return [_skills_check(fields), _languages_check(fields), _feature_check(fields)]


def _languages_check(fields: dict) -> Check:
    """Every language named in `languageProficiencies` is a known language (none read is not a failure: the
    printed text then stays in the entries only)."""
    name = "languages-valid"
    bad = [x for x in _names(fields.get("languageProficiencies")) if x not in _LANGUAGE_KEYS]
    if bad:
        return Check(name, False, f"{', '.join(repr(b) for b in bad)} not a known language "
                                  f"({', '.join(sorted(_LANGUAGE_KEYS))})")
    return Check(name, True)


def _names(v) -> list[str]:
    out: list[str] = []
    for d in v or []:
        if not isinstance(d, dict):
            out.append(repr(d))
            continue
        for k, on in d.items():
            if k == "choose":
                out += [str(x) for x in (on.get("from") or [])] if isinstance(on, dict) else [repr(on)]
            elif k != "any" and on is True:
                out.append(k)
    return out


def _skills_check(fields: dict) -> Check:
    name = "skills-valid"
    v = fields.get("skillProficiencies")
    if not v:
        return Check(name, False, "no skill proficiencies read from the Skill Proficiencies line")
    bad = [s for s in _names(v) if s not in _SKILL_KEYS]
    if bad:
        return Check(name, False, f"{', '.join(repr(b) for b in bad)} not among the 18 skills")
    return Check(name, True)


def _feature_check(fields: dict) -> Check:
    name = "feature-present"
    for e in fields.get("entries") or []:
        if isinstance(e, dict) and (e.get("data") or {}).get("isFeature"):
            title = re.sub(r"^feature\s*:\s*", "", e.get("name") or "", flags=re.I).strip()
            if not title:
                return Check(name, False, "the feature has no name")
            if not any(isinstance(x, str) and x.strip() or isinstance(x, dict) for x in e.get("entries") or []):
                return Check(name, False, f"the feature {title!r} has no text")
            return Check(name, True)
    return Check(name, False, "no \"Feature:\" heading found after the Skill Proficiencies line")
