"""Subclass parser (the subclass groups of PHB / XGE / TCE class chapters) and its verification checks.

A subclass group starts at its plural group heading ("Primal Paths"; XGE prints a singular "Arcane Tradition"
when it offers one option) and runs to the next heading of the group heading's rank, the next class chapter
heading or the next "Hit Dice:" line. Inside it, a feature heading is a heading line whose text opens with a
level cue ("At 3rd level", "Starting when you choose this path at 3rd level"); a subclass heading is a heading
line that is not a feature heading, is set taller than the group's feature headings (or carries the group's
naming pattern, "Path of ..."), and owns at least one feature heading.

Which candidate heading is the subclass is decided by evidence first (an XGE "Divine Soul Features" table
caption, a TCE cue "3rd-level Alchemist feature", the group's naming pattern), height last; a heading picked
only for its height whose features merely continue the previous subclass's levels (a stat block, a terrain
list) does not start a subclass.

Feature refs follow the 5etools convention: the subclass's first-level features are all held by one ref
named after the subclass itself (`Path of the Zealot|Barbarian||Zealot|XGE|3`); each later feature heading
gets its own ref at the level of its cue. The levels are never guessed: a subclass whose read levels differ
from its class's subclass levels, or run backwards in print order, fails `feature-levels-match-class`.
"""
from __future__ import annotations

import re
import statistics
from dataclasses import dataclass

from rapidfuzz import fuzz

from ..checks import Check
from ..layout import fix_digits
from ..model import Line
from . import ParseContext, Parsed
from .charcommon import SMALL_WORDS as _SMALL
from .charcommon import SUBCLASS_GROUPS, SUBCLASS_LEVELS, class_source, ordinal, squash
from .charcommon import heading_like as _heading_like
from .charcommon import like as _like
from .charcommon import tidy_case as _tidy_case
from .charcommon import title_case as _title_case
from .spell import _paragraphs

# the phrase a group's subclass names carry around their short name ("Path of the Zealot" -> "Zealot"); the
# 5etools short names drop the sorcerer origin's and the wizard tradition's last word too ("Wild Magic" -> "Wild")
_PREFIXES = ("Path of the ", "Path of ", "College of the ", "College of ", "Circle of the ", "Circle of ",
             "Way of the ", "Way of ", "Oath of the ", "Oath of ", "School of ", "The ")
_CLASS_PREFIXES = {"Wizard": ("Order of ",)}
_CLASS_SUFFIXES = {"Cleric": (" Domain",), "Sorcerer": (" Bloodline", " Magic", " Sorcery"), "Wizard": (" Magic",)}
_NAMING = {"Barbarian": r"^path of\b", "Bard": r"^college of\b", "Cleric": r"\bdomain$", "Druid": r"^circle of\b",
           "Monk": r"^way of\b", "Paladin": r"^oath of\b", "Warlock": r"^the\b", "Wizard": r"^school of\b"}
# headings inside a subclass that are never a subclass themselves
_NOT_SUBCLASS = re.compile(r"(?:spells?|spell list|features?|^tenets of\b.*|\blevel\b.*)$", re.I)
_FEATURES_TABLE = re.compile(r"^(.+?)\s+features$", re.I)  # XGE: "Divine Soul Features" table caption
_TALLER = 1.08  # a subclass heading with no other evidence is set this much taller than its feature headings
_CUE_LINES = 2  # a feature's level cue sits in its first lines
_LEVEL_CUE = re.compile(r"\b(\S{2,5})\s+level\b", re.I)
# TCE prints the cue as its own line: "3rd-level Alchemist feature"
_TAGGED_CUE = re.compile(r"^(\S{2,5})-level\s+([^.]{1,40}?)\s+feature\b", re.I)
_GROUP_HEIGHT = 1.15  # a group heading is set at least this much above its page's median line height
_RANK = 1.0  # a heading this tall relative to the group heading ends the group


def _vertical(l: Line) -> bool:
    """Text set sideways in the margin (an art credit): taller than it is wide."""
    return l.box[2] - l.box[0] < l.box[3] - l.box[1]


def _group_class(t: str) -> str | None:
    """The class whose subclass-group heading `t` is (plural, or the singular title XGE prints for a single
    option), case-insensitive and fuzzy (>= 85)."""
    s = squash(t)
    # the group heading may lose a drop-cap first letter ("therworldly Patrons"), so a lowercase start is fine
    if not (_heading_like(t[:1].upper() + t[1:])) or len(t.split()) > 4 or not s:
        return None
    for cls, (single, plural) in SUBCLASS_GROUPS.items():
        if s.endswith("s") and fuzz.ratio(s, squash(plural)) >= 85:
            return cls
    for cls, (single, _) in SUBCLASS_GROUPS.items():
        if fuzz.ratio(s, squash(single)) >= 90:
            return cls
    return None


def _cue(L: list[Line], i: int, end: int) -> tuple[int | None, str | None]:
    """(level, subject) of the cue opening the text under heading i: "Beginning at 6th level" gives (6, None),
    TCE's "3rd-level Alchemist feature" gives (3, "Alchemist"). (None, None) when there is no cue."""
    text = fix_digits(" ".join(L[j].text for j in range(i + 1, min(i + 1 + _CUE_LINES, end))).strip())
    text = re.sub(r"\b1 ([0-9lI|!]th)\b", r"1\1", text)  # OCR "1 1th-level" / "1 lth level" for 11th
    m = _TAGGED_CUE.match(text)
    if m:
        return ordinal(m.group(1)), m.group(2).strip()
    if not text[:1].isupper():
        return None, None
    for m in _LEVEL_CUE.finditer(text):
        lvl = ordinal(m.group(1).rstrip(",."))
        if lvl is not None:
            return lvl, None
    return None, None


def _groups(L: list[Line], med: dict[int, float]) -> list[tuple[int, str]]:
    out = []
    for i, l in enumerate(L):
        cls = _group_class(l.text)
        # a group heading is set well above body text; a same-named feature heading in the class text is not
        if cls and l.height >= _GROUP_HEIGHT * med[l.page]:
            out.append((i, cls))
    return out


def _named(t: str, cls: str) -> bool:
    naming = _NAMING.get(cls)
    return bool(naming and re.search(naming, _tidy_case(t).lower()))


def _group_end(L: list[Line], g: int, stop: int, cls: str, med: dict[int, float]) -> int:
    gh = L[g].height
    for i in range(g + 1, stop):
        t = L[i].text.strip()
        if re.match(r"^\s*Hit Dice\s*[:;]", t, re.I):
            return i
        if not _heading_like(t) or not _title_case(t) or _vertical(L[i]):
            continue
        if L[i].height >= _RANK * gh and not _named(t, cls):
            return i
        if L[i].height >= 1.3 * med[L[i].page] and squash(t) in {squash(c) for c in SUBCLASS_GROUPS}:
            return i
    return stop


def _short_name(name: str, cls: str) -> str:
    low = name.lower()
    for p in _PREFIXES + _CLASS_PREFIXES.get(cls, ()):
        if low.startswith(p.lower()) and len(name) > len(p):
            return name[len(p):].strip()
    for x in _CLASS_SUFFIXES.get(cls, ()):
        if low.endswith(x.lower()) and len(name) > len(x):
            return name[: -len(x)].strip()
    return name


@dataclass
class _Feature:
    level: int
    name: str
    head: int  # index of the feature's first heading line
    body: int  # index of the feature's first body line
    subject: str | None  # the subclass a TCE cue names


def _stacked(t: str) -> bool:
    """A heading-like line that is a heading of its own (not a wrapped heading's tail, 'AND PLanTS')."""
    return _heading_like(t) and _title_case(t) and t.split()[0].lower() not in _SMALL


def _features(L: list[Line], g: int, end: int) -> dict[int, _Feature]:
    """Feature headings in a group by heading index. A heading directly above another heading (an art caption,
    'PALADiN OF / REdeMpTion') is not one; a wrapped tail ('Channel Divinity: Charm Animals' / 'AND PLanTS')
    and a split label ('Channel Divinity:' / 'Knowledge of the Ages') join the heading."""
    out: dict[int, _Feature] = {}
    i = g + 1
    while i < end:
        t = L[i].text.strip()
        nxt = L[i + 1].text.strip() if i + 1 < end else ""
        if not _heading_like(t) or _stacked(nxt):
            i += 1
            continue
        lvl, subj = _cue(L, i, end)
        if lvl is None:
            i += 1
            continue
        name, body = t, i + 1
        if nxt and _heading_like(nxt) and len(nxt.split()) <= 3 and _cue(L, i + 1, end)[0] == lvl:
            name, body = f"{t} {nxt}", i + 2
        head, prev = i, (L[i - 1].text.strip() if i - 1 > g else "")
        if re.fullmatch(r"[A-Z][A-Za-z' ]{2,30}:", prev):
            name, head = f"{prev} {name}", i - 1
        out[i] = _Feature(lvl, _tidy_case(name), head, body, subj)
        i = body
    return out


def _subclasses(L: list[Line], g: int, end: int, cls: str) -> list[tuple[int, int, list[_Feature]]]:
    """(heading index, section end, features) for each subclass in a group."""
    feats = _features(L, g, end)
    subjects = {i: f.subject for i, f in feats.items() if f.subject}
    if not feats:
        return []
    fh = statistics.median(L[i].height for i in feats)
    # a subclass heading is followed by its introduction, never directly by another heading (a table row)
    cands = [i for i in range(g + 1, end)
             if i not in feats and _heading_like(L[i].text) and _title_case(L[i].text) and not _vertical(L[i])
             and not (i + 1 < end and i + 1 not in feats and _stacked(L[i + 1].text.strip()))]
    # consecutive candidates with no feature heading between them are one slot: a subclass heading among the
    # sidebar, table and margin-note headings around it
    slots: list[list[int]] = []
    for i in cands:
        if slots and not any(slots[-1][-1] < j < i for j in feats):
            slots[-1].append(i)
        else:
            slots.append([i])
    heads: list[tuple[int, bool]] = []  # (heading index, chosen by height alone)
    order = sorted(feats)
    for k, sl in enumerate(slots):
        nxt_slot = slots[k + 1][0] if k + 1 < len(slots) else end
        after = [j for j in order if sl[-1] < j < nxt_slot]
        # evidence naming the subclass: an "X Features" table caption in the slot, or TCE cues "Xth-level X feature"
        upto = after[0] if after else nxt_slot
        named_by = [m.group(1) for i in range(sl[0], upto)
                    if _heading_like(L[i].text) and (m := _FEATURES_TABLE.match(L[i].text.strip()))]
        named_by += [subjects[j] for j in after if j in subjects]
        pool = [i for i in sl if not _NOT_SUBCLASS.search(L[i].text.strip())]
        # evidence naming the subclass already open ("PHANTOM" over a 17th-level Phantom feature) continues it
        if heads and any(_like(L[heads[-1][0]].text, n, 90) for n in named_by):
            continue
        pick = next((i for i in pool if any(_like(L[i].text, n) for n in named_by)), None)
        if pick is None:
            pick = next((i for i in pool if _named(L[i].text, cls)), None)
        if pick is not None:
            if not (heads and _like(L[heads[-1][0]].text, L[pick].text, 90)):  # the same name again continues it
                heads.append((pick, False))
            continue
        tall = [i for i in pool if L[i].height >= _TALLER * fh]
        if tall:
            heads.append((max(tall, key=lambda i: (L[i].height, -i)), True))
    out: list[tuple[int, int, list[_Feature]]] = []
    first_level = SUBCLASS_LEVELS.get(cls, [None])[0]
    for k, (i, by_height) in enumerate(heads):
        nxt = heads[k + 1][0] if k + 1 < len(heads) else end
        own = [feats[j] for j in order if i < j < nxt]
        if not own:
            continue
        # a heading picked only for its height (a stat block, a terrain list) whose features all come after the
        # previous subclass's last feature level continues that subclass instead of starting one
        if by_height and out and own[0].level != first_level and \
                min(f.level for f in own) > max(f.level for f in out[-1][2]):
            pi, _, pown = out[-1]
            out[-1] = (pi, nxt, pown + own)
            continue
        out.append((i, nxt, own))
    return out


def _entries(L: list[Line], feats: list[_Feature], end: int) -> list[dict]:
    out = []
    for k, f in enumerate(feats):
        stop = feats[k + 1].head if k + 1 < len(feats) else end
        body = L[f.body:stop]
        out.append({"type": "entries", "name": f.name, "entries": _paragraphs(body) if body else []})
    return out


def parse_subclasses(body: list[Line], ctx: ParseContext) -> list[Parsed]:
    """Parse every subclass in `body` (reading-ordered lines of the whole book)."""
    L = body
    if not L:
        return []
    by_page: dict[int, list[float]] = {}
    for l in L:
        by_page.setdefault(l.page, []).append(l.height)
    med = {p: statistics.median(h) for p, h in by_page.items()}
    groups = _groups(L, med)
    out: list[Parsed] = []
    for k, (g, cls) in enumerate(groups):
        stop = groups[k + 1][0] if k + 1 < len(groups) else len(L)
        end = _group_end(L, g, stop, cls, med)
        csrc = class_source(cls, ctx.source)
        ssrc = "" if ctx.source == "PHB" else ctx.source
        for i, sec_end, feats in _subclasses(L, g, end, cls):
            name = _tidy_case(L[i].text.strip())
            short = _short_name(name, cls)
            first = min(f.level for f in feats)
            refs = [f"{name}|{cls}|{csrc}|{short}|{ssrc}|{first}"]
            # the first level's features are held by the subclass ref; one printed after a later level's feature
            # is kept as its own ref, so the out-of-order levels show (two subclasses read as one)
            top = first
            for f in feats:
                if f.level != first or top > first:
                    refs.append(f"{f.name}|{cls}|{csrc}|{short}|{ssrc}|{f.level}")
                top = max(top, f.level)
            lines = L[i:sec_end]
            fields = {
                "className": cls, "classSource": csrc or "PHB", "shortName": short,
                "subclassFeatures": refs,
                "entries": _entries(L, feats, sec_end) or None,
            }
            p = Parsed(type="Subclass", name=name, fields=fields, text="\n".join(l.text for l in lines),
                       lines=lines)
            p.checks = subclass_checks(fields)
            out.append(p)
    return out


# ---------- checks ----------
def subclass_checks(fields: dict) -> list[Check]:
    return [_parent_known(fields), _levels_match(fields)]


def _parent_known(fields: dict) -> Check:
    name = "parent-class-known"
    cls = fields.get("className")
    if cls in SUBCLASS_GROUPS:
        return Check(name, True)
    return Check(name, False, f"parent class {cls!r} is not a known class ({', '.join(SUBCLASS_GROUPS)})")


def _ref_level(r) -> int | None:
    s = r.get("subclassFeature") if isinstance(r, dict) else r
    last = str(s or "").split("|")[-1]
    return int(last) if last.isdigit() else None


def _ref_name(r) -> str:
    s = r.get("subclassFeature") if isinstance(r, dict) else r
    return str(s or "").split("|")[0]


def _levels_match(fields: dict) -> Check:
    name = "feature-levels-match-class"
    cls = fields.get("className")
    want = SUBCLASS_LEVELS.get(cls or "")
    refs = fields.get("subclassFeatures") or []
    if want is None:
        return Check(name, False, f"no subclass feature levels known for class {cls!r}")
    if not refs:
        return Check(name, False, f"no subclass features read; {cls} gains them at levels {want}")
    seq = [(r, lv) for r in refs if (lv := _ref_level(r)) is not None]
    back = next(((a, b) for a, b in zip(seq, seq[1:]) if b[1] < a[1]), None)
    if back:
        (ra, la), (rb, lb) = back
        return Check(name, False, f"feature levels go backwards: {_ref_name(rb)} (level {lb}) is printed after "
                                  f"{_ref_name(ra)} (level {la}); two subclasses may have been read as one")
    got = sorted({lv for _, lv in seq})
    if got != want:
        extra = [lv for lv in got if lv not in want]
        missing = [lv for lv in want if lv not in got]
        parts = []
        if extra:
            parts.append(f"features at levels {extra} where {cls} gains none")
        if missing:
            parts.append(f"no feature at levels {missing}")
        return Check(name, False, f"subclass feature levels {got} but {cls} gains them at {want}: "
                                  + "; ".join(parts))
    return Check(name, True)
