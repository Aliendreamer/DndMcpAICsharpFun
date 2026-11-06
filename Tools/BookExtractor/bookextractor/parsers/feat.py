"""Feat parser and its verification check.

A feats section opens at a short heading ending in "Feats" ("Feats", "Racial Feats", "Draconic Feats") and ends at
the next title about as tall (a heading, "Chapter 7: ...", "PART 2"). Each heading inside it is a feat when it
stands over a "Prerequisite:" line, or when it stands on its own: the line before it ends a sentence (so a table
row "Dwarven Fortitude" under "Feat" is not a feat) and the line after it is capitalised prose (so a table caption
is not one). Headings naming feats ("Feat Descriptions") are never feats. Feats printed outside such a section
are a "Feat: Revenant Blade" heading, or the one feat under an "Optional Deep Gnome Feat" heading; each ends at
the next heading over prose.

Fields follow the hand-authored 5etools-aligned `FeatFields` schema:

- `prerequisite`: 5etools objects from the optional "Prerequisite:" line. A list of objects is OR, the keys of one
  object are AND: "Elf or half-elf" -> [{"race": [{"name": "elf"}, {"name": "half-elf"}]}],
  "4th level, Strixhaven Initiate feat" -> [{"level": 4, "feat": ["strixhaven initiate"]}]. A part the parser
  cannot read is kept as {"other": "<printed part>"} and fails `prerequisite-parsed`. Absent when none is printed.
- `ability`: the "Increase your <Ability> score by 1" grant ([{"str": 1}], or a choice), only when fully read.
- `entries`: the feat's prose paragraphs, with its bullet benefits (the grants) as a {"type": "list"} entry.

Names are the printed headings (OCR small caps undone); races and abilities are matched exactly, never fuzzily.
"""
from __future__ import annotations

import re

from ..checks import Check
from ..layout import fix_digits
from ..model import Line
from . import ParseContext, Parsed
from .charcommon import ABILITIES, SENTENCE_END, heading_like, prose_entries, squash, title_case
from .monster import _join_text
from .race import CORE_RACES, tidy_case

_MAX_SECTION = 1500  # lines a feats section may span
_MAX_SINGLE = 120  # lines a feat printed on its own may span
_ONE_REACH = 15  # a single feat's heading follows its "... Feat" heading (and introduction) this closely
_TALL = 0.95  # a title this close to the section heading's height (or taller) ends the section
_CHAPTER = re.compile(r"^(?:chapter|part)\s*\d", re.I)
_NAMED = re.compile(r"^feat\s*:\s*(.+)$", re.I)
_PREREQ = re.compile(r"^prerequisites?\s*[:;.]\s*(.*)$", re.I)
_ORDER = list(ABILITIES.values())
_ABILITY_WORD = "|".join(ABILITIES)
_ABILITY_LIST = rf"((?:{_ABILITY_WORD})(?:(?:\s*,\s*(?:or\s+)?|\s+or\s+)(?:{_ABILITY_WORD}))*)"
_ABILITY_CLAUSE = re.compile(_ABILITY_LIST + r"\s+(\d+)\s+or\s+higher", re.I)
_SPELLCASTING_2020 = re.compile(r"spellcasting\s+or\s+pact\s+magic\s+feature", re.I)
_SPLIT = re.compile(r"\s*,\s*(?:or\s+)?|\s+or\s+", re.I)
_ARMOR = {"light": "light", "medium": "medium", "heavy": "heavy", "shields": "shield", "a shield": "shield"}
_INCREASE = re.compile(r"increase your " + _ABILITY_LIST + r"(?: score)? by 1\b", re.I)
_ANY_INCREASE = re.compile(r"increase (?:the chosen ability score|one ability score of your choice|"
                           r"an ability score of your choice) by 1\b", re.I)


# ---------- prerequisite ----------
def _ability_code(word: str) -> str:
    return ABILITIES[next(k for k in ABILITIES if k.lower() == word.lower())]


def _race(token: str) -> dict | None:
    """'Dwarf' -> {"name": "dwarf"}, 'Elf (drow)' -> {"name": "elf", "subrace": "drow"}; exact names only."""
    if squash(token) == "asmallrace":
        return {"name": "small race", "displayEntry": "a Small race"}
    m = re.fullmatch(r"(.+?)(?:\s*\(([^)]+)\))?", token.strip())
    race = next((r for r in CORE_RACES if squash(r) == squash(m.group(1))), None)
    if race is None:
        return None
    out = {"name": race.lower()}
    if m.group(2):
        out["subrace"] = m.group(2).strip().lower()
    return out


def _part(token: str) -> tuple[str, object] | None:
    """(5etools key, value) of one prerequisite part, or None when it is not understood."""
    t = token.strip().rstrip(".").strip()
    low = t.lower()
    if m := re.fullmatch(r"(\d+)(?:st|nd|rd|th)\s+level", fix_digits(low)):
        return "level", int(m.group(1))
    if m := re.fullmatch(r"proficiency with (light|medium|heavy) armor|proficiency with (shields|a shield)", low):
        return "proficiency", {"armor": _ARMOR[m.group(1) or m.group(2)]}
    if re.fullmatch(r"proficiency with (?:a )?martial weapons?|martial weapon proficiency", low):
        return "proficiency", {"weapon": "martial"}
    if low == "the ability to cast at least one spell":
        return "spellcasting", True
    if low == "spellcasting feature":
        return "spellcastingFeature", True
    if m := re.fullmatch(r"(.+?)\s+feat", t, re.I):
        return "feat", m.group(1).strip().lower()
    if m := re.fullmatch(r"(.+?)\s+background", t, re.I):
        return "background", {"name": m.group(1).strip()}
    if (r := _race(t)) is not None:
        return "race", r
    return None


def parse_prerequisite(text: str) -> tuple[list[dict], list[str]]:
    """5etools `prerequisite` objects of a printed prerequisite, and the parts that could not be read (each
    kept as {"other": part} in the objects)."""
    t = re.sub(r"\s+", " ", text).strip().rstrip(".").strip()
    if not t:
        return [], []
    held: dict[str, tuple[str, object]] = {}

    def hold(value: tuple[str, object]) -> str:
        key = f"\x00{len(held)}"
        held[key] = value
        return key

    t = _ABILITY_CLAUSE.sub(lambda m: hold(("ability", [{_ability_code(w): int(m.group(2))} for w in
                                                         re.findall(_ABILITY_WORD, m.group(1), re.I)])),
                            fix_digits(t))
    t = _SPELLCASTING_2020.sub(lambda m: hold(("spellcasting2020", True)), t)
    either = bool(re.search(r"\bor\b", t, re.I))
    parts: list[tuple[str, object]] = []
    unread: list[str] = []
    for token in (x for x in _SPLIT.split(t) if x.strip()):
        token = token.strip()
        got = held.get(token) or _part(token)
        if got is None:
            unread.append(token)
            got = ("other", token)
        parts.append(got)
    races = [v for k, v in parts if k == "race"]
    rest = [(k, v) for k, v in parts if k != "race"]
    if either and "," in t and rest:
        return _mixed(t, held, text)
    if either:  # alternatives: one object each, the races one list
        out = [{"race": races}] if races else []
        out += [{k: _wrap(k, v)} for k, v in rest]
        return out, unread
    obj: dict = {}
    if races:
        obj["race"] = races
    for k, v in rest:
        if k in ("proficiency", "feat", "background") and k in obj:
            obj[k] += _wrap(k, v)
        else:
            obj[k] = _wrap(k, v)
    return [obj], unread


def _mixed(t: str, held: dict, printed: str) -> tuple[list[dict], list[str]]:
    """A line with both commas and "or" whose parts are not all races ("4th level, elf or half-elf"): the
    comma-separated groups are AND, the "or" inside a group are alternatives. Only alternatives that are all
    races can sit inside one AND object; anything else is not encoded and fails the check."""
    obj: dict = {}
    for group in (g.strip() for g in re.split(r"\s*,\s*(?!or\b)", t, flags=re.I) if g.strip()):
        alts = [a.strip() for a in re.split(r"\s*,?\s*\bor\s+", group, flags=re.I) if a.strip()]
        got = [held.get(a) or _part(a) for a in alts]
        if any(g is None for g in got) or (len(got) > 1 and any(k != "race" for k, _ in got)):
            why = f"mixed and/or not understood: {printed.strip()}"
            return [{"other": why}], [why]
        for k, v in got:
            if k == "race":
                obj.setdefault("race", []).append(v)
            elif k in obj and k in ("proficiency", "feat", "background"):
                obj[k] += _wrap(k, v)
            elif k in obj:
                why = f"mixed and/or not understood: {printed.strip()}"
                return [{"other": why}], [why]
            else:
                obj[k] = _wrap(k, v)
    return [obj], []


def _wrap(key: str, value: object) -> object:
    return [value] if key in ("proficiency", "feat", "background") else value


# ---------- ability grant ----------
def _ability(text: str) -> list | None:
    t = re.sub(r"\s+", " ", text)
    if m := _INCREASE.search(t):
        codes = [_ability_code(w) for w in re.findall(_ABILITY_WORD, m.group(1), re.I)]
        if len(codes) == 1:
            return [{codes[0]: 1}]
        return [{"choose": {"from": [c for c in _ORDER if c in codes], "amount": 1}}]
    if _ANY_INCREASE.search(t):
        return [{"choose": {"from": list(_ORDER), "amount": 1}}]
    return None


# ---------- layout ----------
def _is_head(t: str) -> bool:
    return heading_like(t) and title_case(t) and not _PREREQ.match(t)


def _head(l: Line) -> bool:
    """A heading line; an art credit set sideways in the margin is not one."""
    return _is_head(l.text.strip()) and l.box[2] - l.box[0] >= l.box[3] - l.box[1]


def _prose(t: str) -> bool:
    """A line of running text: a few words, some of them lower case (a table's header row has none)."""
    words = re.findall(r"(?<![\w-])[A-Za-z][A-Za-z'-]*", t)
    return len(words) >= 3 and any(w[0].islower() for w in words) and not _is_head(t)


def _is_section(t: str) -> bool:
    """'Feats', 'Racial Feats', 'Draconic Feats': a short heading whose last word is Feats."""
    words = t.split()
    return 0 < len(words) <= 4 and squash(words[-1]) == "feats"


def _is_one_feat(t: str) -> bool:
    """'Optional Deep Gnome Feat': a heading over a single feat."""
    words = t.split()
    return 1 < len(words) <= 5 and squash(words[-1]) == "feat"


def _about_feats(t: str) -> bool:
    """A heading naming feats ("Feat Descriptions") is a section heading, never a feat."""
    return squash(t).startswith("feat") or bool(re.search(r"\bfeats?\b", t, re.I))


def _ends_section(L: list[Line], j: int, height: float) -> bool:
    """A heading (or a "Chapter 7: ..." / "PART 2" title) about as tall as the section heading, other than a
    feat's own heading (which a Prerequisite line follows)."""
    l = L[j]
    upright = l.box[2] - l.box[0] >= l.box[3] - l.box[1]
    title = _head(l) or bool(_CHAPTER.match(l.text.strip()))
    return upright and title and l.height >= _TALL * height and not _is_section(l.text) \
        and not _prerequisite_next(L, j)


def _sections(L: list[Line]) -> list[tuple[int, int]]:
    out: list[tuple[int, int]] = []
    i = 0
    while i < len(L):
        if _head(L[i]) and _is_section(L[i].text.strip()):
            stop = min(len(L), i + _MAX_SECTION)
            end = next((j for j in range(i + 1, stop) if _ends_section(L, j, L[i].height)), stop)
            out.append((i, end))
            i = end
        else:
            i += 1
    return out


def _prerequisite_next(L: list[Line], i: int) -> bool:
    return i + 1 < len(L) and bool(_PREREQ.match(L[i + 1].text.strip()))


def _feat_head(L: list[Line], i: int, start: int) -> bool:
    """A heading over a "Prerequisite:" line, or one standing on its own: after a finished sentence (a table
    row "Dwarven Fortitude" follows another row) and before a capitalised prose line (a table caption heads a
    table). A copy of the heading printed just above it (an OCR ghost of the same words) and art captions
    are passed over."""
    t = L[i].text.strip()
    if not _head(L[i]) or _about_feats(t) or i + 1 >= len(L):
        return False
    if _prerequisite_next(L, i):
        return True
    k = i - 1
    while k > start and (squash(L[k].text) == squash(t) or _caption(L[k].text)):
        k -= 1
    if k != start and not L[k].text.strip().endswith(SENTENCE_END):
        return False
    nxt = L[i + 1].text.strip()
    return _prose(nxt) and nxt[:1].isupper()  # "HE MAGIC WOVEN ..." over "of the Material Plane" is a sentence


def _caption(t: str) -> bool:
    """An art caption ("A GIANT FOUNDLING IS USED / TO A LARGER WORLD."): two or more words, all capitals."""
    return len(t.split()) >= 2 and t.isupper()


def _single_end(L: list[Line], h: int) -> int:
    """End of a feat printed on its own (not in a feats section): the next heading over prose or a
    prerequisite."""
    stop = min(len(L), h + _MAX_SINGLE)
    return next((j for j in range(h + 2, stop) if _head(L[j]) and
                 (_prerequisite_next(L, j) or (j + 1 < len(L) and _prose(L[j + 1].text.strip())))), stop)


def _feat_spans(L: list[Line]) -> list[tuple[int, int, str]]:
    """(heading index, end index, printed name) of every feat."""
    out: list[tuple[int, int, str]] = []
    claimed: set[int] = set()
    for s, e in _sections(L):
        claimed.update(range(s, e))
        heads = [i for i in range(s + 1, e) if _feat_head(L, i, s)]
        for k, h in enumerate(heads):
            end = heads[k + 1] if k + 1 < len(heads) else e
            while end - 1 > h and k + 1 < len(heads) and squash(L[end - 1].text) == squash(L[end].text):
                end -= 1  # an OCR ghost of the next feat's heading belongs to it
            out.append((h, end, L[h].text.strip()))
    for i, l in enumerate(L):
        if i in claimed or not _head(l):
            continue
        t = l.text.strip()
        if m := _NAMED.match(t):  # "Feat: Revenant Blade"
            if _prerequisite_next(L, i) or (i + 1 < len(L) and _prose(L[i + 1].text.strip())):
                out.append((i, _single_end(L, i), m.group(1).strip()))
        elif _is_one_feat(t):  # "Optional Deep Gnome Feat" over its one feat
            h = next((j for j in range(i + 1, min(len(L), i + _ONE_REACH)) if _feat_head(L, j, i)), None)
            if h is not None:
                out.append((h, _single_end(L, h), L[h].text.strip()))
    return sorted(out)


def _prerequisite_line(L: list[Line], i: int, end: int) -> tuple[str | None, int]:
    """The printed prerequisite (joined with its wrapped continuation) and the index after it."""
    m = _PREREQ.match(L[i].text.strip()) if i < end else None
    if not m:
        return None, i
    text = m.group(1).strip()
    j = i + 1
    while j < end and not _PREREQ.match(L[j].text.strip()) and \
            (re.search(r"(?:,|\bor|\band|\bof|\bwith)$", text) or text.count("(") > text.count(")")
             or (len(L[j].text.split()) <= 3 and _is_head(L[j].text.strip()) and not _feat_head(L, j, i - 1))):
        text = _join_text([text, L[j].text.strip()])
        j += 1
    return text, j


def parse_feats(body: list[Line], ctx: ParseContext) -> list[Parsed]:
    """Parse every feat in `body` (reading-ordered lines of the whole book)."""
    out: list[Parsed] = []
    for h, end, name in _feat_spans(body):
        prereq, after = _prerequisite_line(body, h + 1, end)
        fields: dict = {}
        if prereq is not None:
            fields["prerequisite"] = parse_prerequisite(prereq)[0] or None
        entries = prose_entries(body, after, end)
        ability = _ability(" ".join(x if isinstance(x, str) else " ".join(x["items"]) for x in entries))
        if ability:
            fields["ability"] = ability
        fields["entries"] = entries or None
        lines = body[h:end]
        p = Parsed(type="Feat", name=tidy_case(name), fields=fields, text="\n".join(l.text for l in lines),
                   lines=lines)
        p.checks = feat_checks(fields, None if prereq is None else body[h + 1].text.strip())
        out.append(p)
    return out


# ---------- checks ----------
def feat_checks(fields: dict, prerequisite_line: str | None) -> list[Check]:
    """`prerequisite_line` is the printed "Prerequisite:" line, or None when the feat prints none."""
    name = "prerequisite-parsed"
    if prerequisite_line is None:
        return [Check(name, True)]
    m = _PREREQ.match(prerequisite_line.strip())
    if m is not None and not m.group(1).strip():
        return [Check(name, False, f"the prerequisite line {prerequisite_line!r} is empty")]
    pre = fields.get("prerequisite")
    if not pre:
        return [Check(name, False, f"no prerequisite read from {prerequisite_line!r}")]
    unread = [o["other"] for o in pre if isinstance(o, dict) and "other" in o]
    if unread:
        return [Check(name, False, "could not read the prerequisite part(s) "
                                   f"{', '.join(repr(u) for u in unread)} of {prerequisite_line!r}")]
    return [Check(name, True)]
