"""Race and subrace parser (PHB-style race chapters, MPMM-style race pages) and their verification checks.

A race is anchored on its printed "<Name> Traits" heading ("Bugbear Racial Traits" too) followed by its trait
run (bold "Label." lines indented under the column) holding a "Size." or "Speed." trait. The name is the
heading minus "(Racial) Traits"; an "Ability Score Increase." trait run with a Size or Speed trait but no such
heading is a race with no name (the assembler fails its `name-found` check). The run ends at the race's first
subrace heading, at the next race, or at a heading with no trait under it (a sidebar, the next race's chapter
heading). A heading before the run's first trait is a sidebar printed inside it ("Grudging Acceptance").

A subrace is a heading followed, within a few lines and with no other heading in between, by its own "Ability
Score Increase." trait ("Hill Dwarf"). A lore heading ("Dwarf Names") has no trait of its own, so it is never a
subrace. A subrace between its race's traits and the next race (or the next chapter-size heading) belongs to
that race. One printed elsewhere names its race itself ("Pallid Elf", "Dragonborn Variant: Draconblood", a
"Variant Half-Elf:" line over "Mark of Detection"); without such a name its `raceName` is null and
`parent-race-known` fails.

Field shapes follow 5etools: `size` ["M"] (smallest first); `speed` a plain number when only a walking speed is
printed, else {"walk": 30, "fly": 50} (true for a speed "equal to your walking speed"); `ability` [{"con": 2}]
or [{"cha": 2, "choose": {"from": [...], "count": 2}}]. An ability text that is not fully understood gives
`ability` null and a failed `ability-increase-parsed` check naming the text. MPMM-style races print no
increase of their own: the book's "Ability Score Increases" rule says the player chooses them, so `ability` is
null and the check passes; so it does for a race whose subraces carry the increases (ERLW Shifter).
"""
from __future__ import annotations

import re
import statistics
from dataclasses import dataclass

from ..checks import Check
from ..layout import fix_digits
from ..model import Line
from . import ParseContext, Parsed
from .charcommon import ABILITIES, SIZES, heading_like, like, squash, title_case
from .charcommon import indented as _indented
from .charcommon import tidy_case as _tidy_words
from .monster import _join_text

_TRAIT = re.compile(r"^([A-Z][A-Za-z'’()\- ]{1,48}?)\s*\.\s*(.*)$")
_ANCHOR_REACH = 20  # an unnamed race's Size or Speed trait follows its Ability Score Increase this closely
_FIRST_TRAIT = 25  # the first trait follows the Traits heading (its introduction, a sidebar) this closely
_SUB_REACH = 25  # a subrace's Ability Score Increase trait follows its heading (and introduction) this closely
_LOOKAHEAD = 45  # a heading inside a trait run (a table caption, a sidebar) has a trait after it this closely
_SUB_WINDOW = 400  # lines after a race's traits searched for its subraces when no next race bounds them
_CHAPTER = 1.3  # a heading this much taller than the race's Traits heading (a race or chapter title) ends it
_FOOTER = 0.85  # a line set this much below its page's median height is a running header or footer
_CHOSEN = re.compile(r"increase\s+one\s+(?:of\s+those\s+|ability\s+)?scores?\s+by\s+2", re.I)
_BY_SUBRACE = "the increases are printed in each subrace"
# a race variant that replaces a trait other than Ability Score Increase leaves the race's increases as they are
VARIANT_KEEPS_ASI = "the variant replaces another trait; the race's Ability Score Increase is unchanged"
_REPLACES = re.compile(r"\breplaces the\s+([^.]{1,80}?)\s+trait\b", re.I)
_SECTION = 1.3  # a race's section heading ("DWARVES") is set this much taller than a subrace heading inside it
_SECTION_CLOSE = 0.95  # a non-race heading this tall relative to that section heading lies between the two
_NUMBER_WORDS = {"one": 1, "two": 2, "three": 3}
_ORDER = list(ABILITIES.values())
_MODES = {"flying": "fly", "swimming": "swim", "climbing": "climb", "burrowing": "burrow"}
_CONDITIONAL = re.compile(r"\b(?:until|while|when|whenever|if|for \d+ (?:minute|hour)s?)\b", re.I)
_OPTIONS = re.compile(r"\bof the following(?: options)?\b", re.I)
_ENCLOSE = 1.2  # a "<Race> Subraces" heading this much taller than the headings under it encloses them
_ENCLOSE_REACH = 700  # lines searched back from a subrace heading for its enclosing "<Race> Subraces" heading
_PROSE_REACH = 15  # lines before a subrace heading searched for "an elf subrace: the eladrin"
_EXAMPLE_RACE = re.compile(r"^.*?\brace\s*:\s*(.+)$", re.I)  # DMG "Example Race: Aasimar"
_ART_CREDIT = re.compile(r"^(?:[A-Z][A-Z'.]+\s+){2,}(?=[a-z])")  # "RON LEMEN creases by 1." after "in-"
# the races a subrace printed outside its race's chapter may name (the PHB races)
CORE_RACES = ("Dragonborn", "Dwarf", "Elf", "Gnome", "Half-Elf", "Half-Orc", "Halfling", "Human", "Tiefling")


def tidy_case(name: str) -> str:
    """Undo OCR small-caps noise in a printed name, part by part: 'Half-ORC' -> 'Half-Orc',
    'Dark Elf (DROW)' -> 'Dark Elf (Drow)'."""
    return re.sub(r"[A-Za-z']+", lambda m: _tidy_words(m.group(0)) if m.start() == 0 else
                  _tidy_words("x " + m.group(0))[2:], name)


@dataclass
class _Book:
    L: list[Line]
    labels: list[str | None]  # the trait label a line opens, if any
    heads: list[bool]  # the line is a heading (not a trait, not a running header or footer)
    hanging: set[int]  # trait lines set with hanging indents (TCE Custom Lineage)


def _book(L: list[Line]) -> _Book:
    by_page: dict[int, list[float]] = {}
    for l in L:
        by_page.setdefault(l.page, []).append(l.height)
    med = {p: statistics.median(h) for p, h in by_page.items()}
    labels: list[str | None] = []
    heads: list[bool] = []
    cand = [_label(l.text) for l in L]
    hanging = _hanging_run(L, cand)
    for i, l in enumerate(L):
        t = l.text.strip()
        lab = cand[i]
        ok = bool(lab) and (_indented(L, i) or i in hanging)
        labels.append(tidy_case(lab) if ok else None)
        vertical = l.box[2] - l.box[0] < l.box[3] - l.box[1]  # an art credit set sideways in the margin
        heads.append(not ok and heading_like(t) and title_case(t) and "|" not in t and not vertical
                     and l.height >= _FOOTER * med[l.page])
    return _Book(L, labels, heads, hanging)


def _label(t: str) -> str | None:
    """The label a line opens if it reads as a trait ("Size. Your size is Medium."), before layout checks."""
    m = _TRAIT.match(t.strip())
    lab = m.group(1).strip() if m else ""
    return lab if m and 0 < len(lab.split()) <= 6 and title_case(lab) else None


def _hanging(L: list[Line], i: int) -> bool:
    """The trait is set with a hanging indent (TCE "Custom Lineage"): its label line starts left of the line it
    wraps onto, which is indented."""
    l, n = L[i], L[i + 1] if i + 1 < len(L) else None
    return n is not None and n.page == l.page and n.box[0] - l.box[0] > 0.4 * l.height and \
        abs(n.box[0] - l.box[0]) < 3 * l.height


def _hanging_run(L: list[Line], cand: list[str | None]) -> set[int]:
    """Trait lines of a race's trait block set with hanging indents (TCE "Custom Lineage"): runs of trait lines
    flush with each other, a few lines apart, at least one wrapping onto an indented line. A one-line trait
    ("Size.", "Speed.") is in the run by being flush with it. A run counts only when it holds a Size or Speed
    trait and an Ability Score Increase trait (a feat's hanging "Ability Score Increase." list item does not)."""
    out: set[int] = set()
    run: list[int] = []

    def flush() -> None:
        labs = [cand[j] for j in run]
        if any(_hanging(L, j) for j in run) and any(_is_asi(x) for x in labs) and \
                any(_size_speed(x) for x in labs):
            out.update(run)
        run.clear()

    for i, lab in enumerate(cand):
        if not lab:
            continue
        if run and (i - run[-1] > 4 or L[i].page != L[run[-1]].page or
                    abs(L[i].box[0] - L[run[-1]].box[0]) >= 0.4 * L[i].height):
            flush()
        run.append(i)
    flush()
    return out


def _hanging_end(b: _Book, start: int, end: int) -> int:
    """A hanging-indent trait run ends at the first line after its last trait that is set flush with that trait's
    label on the same page (TCE Custom Lineage's last trait is followed by the column's next paragraph, "For
    example, high elf ..."). A line on another page or in another column (a trait wrapping onto the next column)
    never ends it."""
    last = max((j for j in range(start, end) if j in b.hanging), default=None)
    if last is None:
        return end
    lab = b.L[last]
    x, h = lab.box[0], lab.height
    return next((j for j in range(last + 1, end) if b.L[j].page == lab.page and abs(b.L[j].box[0] - x) <= 0.4 * h),
                end)


def _is(label: str | None, want: str) -> bool:
    return bool(label) and like(label, want, 88)


def _is_asi(label: str | None) -> bool:
    return bool(label) and squash(label).startswith("abilityscoreincrease")


def _size_speed(label: str | None) -> bool:
    return _is(label, "Size") or _is(label, "Speed")


def _traits_name(t: str) -> str | None:
    """'Dwarf TraIts' -> 'Dwarf', 'Bugbear Racial Traits' -> 'Bugbear', SCAG 'Duergar Subrace Traits' -> 'Duergar';
    None unless a '<Name> Traits' heading."""
    m = re.match(r"^(.+?)(?:\s+racial|\s+subrace)?\s+traits$", t.strip(), re.I)
    return tidy_case(m.group(1)) if m and len(t.split()) <= 5 and squash(m.group(1)) != "racial" else None


def _book_rule(L: list[Line]) -> str | None:
    """The text of a book-wide 'Ability Score Increases' rule letting the player choose the increases (MPMM)."""
    for i, l in enumerate(L):
        if squash(l.text) == "abilityscoreincreases":
            text = _join_text([x.text.strip() for x in L[i + 1:i + 8]])
            if _CHOSEN.search(text):
                return text
    return None


def _subrace_head(b: _Book, i: int) -> bool:
    if not b.heads[i]:
        return False
    for j in range(i + 1, min(len(b.L), i + 1 + _SUB_REACH)):
        if _is_asi(b.labels[j]):
            return True
        if b.labels[j] is not None or b.heads[j]:
            return False
    return False


def _run_end(b: _Book, start: int, stop: int) -> int:
    """End of the trait run opened at `start`: a subrace heading after the run's first trait, or a heading with
    no trait after it. Headings with a trait after them (a table caption, a sidebar: "Draconic Ancestry",
    "DRACONIANS") are inside the run, unless set as tall as a race title or naming a race's traits."""
    seen = False
    tall = _CHAPTER * b.L[start].height
    for j in range(start + 1, stop):
        if b.labels[j] is not None:
            seen = True
        if not b.heads[j]:
            continue
        if seen and _subrace_head(b, j):
            return j
        for k in range(j, min(stop, j + 1 + _LOOKAHEAD)):
            if b.labels[k] is not None:
                break
            if b.heads[k] and (b.L[k].height >= tall or _traits_name(b.L[k].text) or
                               (k > j and _subrace_head(b, k))):
                return j
        else:
            return j
    return stop


def _race_anchors(b: _Book) -> list[tuple[int, str]]:
    """(heading index, name) of every race; the index is that of the first trait when no Traits heading names it.
    A Traits heading anchors a race when its trait run holds a Size or Speed trait ("Variant Human Traits"
    holds neither: it is a subrace)."""
    out: list[tuple[int, str]] = []
    spans: list[tuple[int, int]] = []
    for i, l in enumerate(b.L):
        name = _traits_name(l.text)
        if name is None or b.labels[i] is not None:
            continue
        if not any(b.labels[j] for j in range(i + 1, min(len(b.L), i + 1 + _FIRST_TRAIT))):
            continue
        end = _run_end(b, i, min(len(b.L), i + _SUB_WINDOW))
        if any(_size_speed(b.labels[j]) for j in range(i + 1, end)):
            out.append((i, name))
            spans.append((i, end))
    for i, lab in enumerate(b.labels):
        if not _is_asi(lab) or any(a < i < e for a, e in spans):
            continue
        h = next((j for j in range(i - 1, max(-1, i - 1 - _LOOKAHEAD), -1) if b.heads[j]), None)
        before = range(max(0 if h is None else h + 1, i - _ANCHOR_REACH), i)
        if not (any(_size_speed(x) for x in b.labels[i + 1:i + 1 + _ANCHOR_REACH]) or
                any(_size_speed(b.labels[j]) for j in before)):
            continue
        anchor = _headed_race(b, h, i)
        if anchor is not None:
            if not any(a == anchor[0] for a, _ in out):
                out.append(anchor)
        elif not any(b.heads[j] for j in range(max(0, i - _SUB_REACH), i)):
            out.append((i, ""))
    return sorted(out)


def _headed_race(b: _Book, h: int | None, i: int) -> tuple[int, str] | None:
    """(heading index, name) of a race whose trait run (holding the Ability Score Increase trait at `i`) sits
    under a heading other than "<Name> Traits": TCE "Custom Lineage" directly above its traits, DMG "Example
    Race: Aasimar" above a few paragraphs of design notes. None when no such heading names it."""
    if h is None:
        return None
    first = next(j for j in range(h + 1, i + 1) if b.labels[j] is not None)
    title = b.L[h].text.strip()
    m = _EXAMPLE_RACE.match(title)
    if m:
        return h, tidy_case(m.group(1).strip())
    if first - h <= _FIRST_TRAIT and not re.search(r"\bsubraces?\b", title, re.I):
        return h, tidy_case(title)
    return None


def _traits(b: _Book, start: int, end: int) -> list[tuple[str, str]]:
    out: list[tuple[str, list[str]]] = []
    for j in range(start, end):
        lab = b.labels[j]
        m = _TRAIT.match(b.L[j].text.strip())
        if lab is not None and m:
            out.append((lab, [m.group(2)]))
        elif out:
            t = b.L[j].text.strip()
            if out[-1][1][-1].endswith("-"):  # an art credit OCR'd into the line a hyphenated word wraps onto
                t = _ART_CREDIT.sub("", t)
            out[-1][1].append(t)
    return [(lab, _join_text([p for p in parts if p]).strip()) for lab, parts in out]


# ---------- fields ----------
def _num(s: str) -> int | None:
    s = re.sub(r"^[lI|]$", "1", fix_digits(s))
    return int(s) if s.isdigit() else None


def _ability_key(word: str) -> str | None:
    return next((v for k, v in ABILITIES.items() if like(word, k, 85)), None)


def _ability(text: str) -> list | None:
    """5etools `ability` from an Ability Score Increase trait; None unless every increase in it is read."""
    t = re.sub(r"\s+", " ", text)
    t = re.sub(r"\bin-? ?creases\b", "increases", t)  # "in- creases" wrapped at the line end
    if re.search(r"ability scores each increase by 1\b", t, re.I):
        return [{k: 1 for k in _ORDER}]
    fixed: dict[str, int] = {}
    read = 0
    # "Your Strength score increases by 2", OCR "YourStrength", ERLW "your Dexterity increases by 1"
    for m in re.finditer(r"([A-Z][a-z]{5,12})(?: score)? increases by (\S+?)[.,;]?(?:\s|$)", t):
        key, n = _ability_key(m.group(1)), _num(m.group(2))
        if key is None or n is None:
            return None
        fixed[key] = n
        read += 1
    choose = None
    for m in re.finditer(r"\b(one|two|three)\s+(?:(?:other|different)\s+)*ability scores? of your choice"
                         r"(?:\s+each)?\s+increases? by (\S+?)[.,;]?(?:\s|$)", t, re.I):
        count, n = _NUMBER_WORDS[m.group(1).lower()], _num(m.group(2))
        if n is None or choose is not None:
            return None
        choose = {"from": [k for k in _ORDER if k not in fixed], "count": count}
        if n != 1:
            choose["amount"] = n
        read += 1
    if read == 0 or read != len(re.findall(r"\bincreases? by\b", t, re.I)):
        return None
    out: dict = dict(sorted(fixed.items(), key=lambda kv: _ORDER.index(kv[0])))
    if choose:
        out["choose"] = choose
    return [out]


def _size(text: str) -> list[str] | None:
    """Size codes in 5etools order (smallest first) from the Size trait's "Your size is ..." clause, or from the
    whole trait when it has no such clause ("You are Medium or Small.")."""
    m = re.search(r"your size is ([^.]*)", text, re.I)
    words = re.findall(r"[A-Za-z]+", m.group(1) if m else text)
    codes = {c for w in words for k, c in SIZES.items() if like(w, k, 85)}
    out = [c for c in SIZES.values() if c in codes]
    return out or None


def _walk(text: str) -> int | None:
    t = fix_digits(re.sub(r"\s+", " ", text))
    m = re.search(r"walking speed (?:is|increases to|of) ?(\d+) feet", t, re.I) or \
        re.search(r"\bspeed is ?(\d+) feet", t, re.I)
    return int(m.group(1)) if m else None


def _modes(traits: list[tuple[str, str]]) -> dict:
    """Flying/swimming/climbing/burrowing speeds from unconditional "you have a ... speed" sentences. A
    transformation's "until it ends, you have a flying speed" is not the race's speed, nor is the rest of a trait
    after its "choose one of the following options" sentence."""
    out: dict = {}
    for _, text in traits:
        for sentence in re.split(r"(?<=[.!?:])\s+", fix_digits(re.sub(r"\s+", " ", text))):
            if _OPTIONS.search(sentence):
                break  # the rest of this trait is a choice list; later traits still count
            if _CONDITIONAL.search(sentence):
                continue
            for word, mode in _MODES.items():
                if re.search(rf"\byou have an? {word} speed equal to your walking speed", sentence, re.I):
                    out[mode] = True
                elif m := re.search(rf"\byou have an? {word} speed of (\d+) feet", sentence, re.I):
                    out[mode] = int(m.group(1))
    return out


def _speed(walk: int | None, modes: dict) -> int | dict | None:
    if walk is None:
        return None
    return {"walk": walk, **modes} if modes else walk


def _darkvision(traits: list[tuple[str, str]]) -> int | None:
    for lab, text in traits:
        if "darkvision" in squash(lab):
            m = re.search(r"(?:within|radius of) (\S+) feet", text)  # "Superior Darkvision ... a radius of 120 feet"
            return _num(m.group(1)) if m else None
    return None


def _entries(traits: list[tuple[str, str]]) -> list[dict] | None:
    out = [{"type": "entries", "name": lab, "entries": [text]} for lab, text in traits if not _is_asi(lab)]
    return out or None


def _asi_text(traits: list[tuple[str, str]]) -> str | None:
    return next((text for lab, text in traits if _is_asi(lab)), None)


def _race_fields(traits: list[tuple[str, str]]) -> dict:
    asi = _asi_text(traits)
    speed = _speed(_walk(next((text for lab, text in traits if _is(lab, "Speed")), "")), _modes(traits))
    f: dict = {"size": _size(next((text for lab, text in traits if _is(lab, "Size")), ""))}
    if speed is not None:  # the schemas' speed `oneOf [{}, null]` rejects null, so an unread speed is absent
        f["speed"] = speed
    f["ability"] = _ability(asi) if asi else None
    f["darkvision"] = _darkvision(traits)
    f["entries"] = _entries(traits)
    return f


def _subrace_fields(traits: list[tuple[str, str]], race_walk: int | None) -> dict:
    """A subrace prints a speed only when it changes the race's (Wood Elf "Fleet of Foot. Your base walking speed
    increases to 35 feet.", Water Genasi "Swim. You have a swimming speed of 30 feet."); a new mode keeps the
    race's walking speed, as 5etools does."""
    asi = _asi_text(traits)
    walk = next((w for _, text in traits if (w := _walk(text)) is not None), None)
    modes = _modes(traits)
    f: dict = {}
    speed = _speed(walk if walk is not None else (race_walk if modes else None), modes)
    if speed is not None:
        f["speed"] = speed
    f["ability"] = _ability(asi) if asi else None
    f["darkvision"] = _darkvision(traits)
    f["entries"] = _entries(traits)
    return f


def _walk_of(speed) -> int | None:
    return speed.get("walk") if isinstance(speed, dict) else speed


# ---------- subraces ----------
def _one(word: str, races: list[str]) -> str | None:
    hit = [r for r in races if squash(r) == squash(word)]
    return hit[0] if len(hit) == 1 else None


def _races_named(text: str, races: list[str]) -> list[str]:
    """The races a heading names: "Half-Elf" -> [Half-Elf]; "Half-Orc Or Human" -> [Half-Orc, Human] (one
    printed trait block for two races). Empty unless every part is a race."""
    parts = re.split(r"\s+(?:or|and)\s+|\s*[,/]\s*", text.strip(), flags=re.I)
    hits = [_one(p, races) for p in parts]
    return [h for h in hits if h] if all(hits) else []


def _named_parent(b: _Book, s: int, races: list[str]) -> tuple[list[str], str]:
    """(raceNames, subrace name) for a subrace heading printed outside its race's section. The races are the
    ones the heading (or a "Variant <Race>:" line over it) names; empty when it names none."""
    title = _traits_name(b.L[s].text) or tidy_case(b.L[s].text.strip())  # MTF "Sea Elf Traits"
    # "Dragonborn Variant: Draconblood", "Variant Human: Mark of Passage", "Elf Subrace: Mark of Shadow"
    m = re.match(r"^(?:variant\s+)?(.+?)(?:\s+(?:variant|subrace))?\s*:\s*(.+)$", title, re.I)
    if m:
        return _races_named(m.group(1), races), tidy_case(m.group(2))
    # a "Variant Half-Elf:", "Variant Half-Orc Or Human:" or "HALFLING SUBRACE:" line over "Mark of Detection"
    prev = b.L[s - 1].text.strip() if s else ""
    m = re.match(r"^(?:variant\s+)?(.+?)(?:\s+(?:variant|subrace))?\s*:$", prev, re.I)
    if m:
        return _races_named(m.group(1), races), title
    words = title.split()
    one = _one(words[-1], races) if len(words) > 1 else None  # "Pallid Elf"
    return ([one] if one else []), title


def _enclosing_parent(b: _Book, s: int, name: str, races: list[str]) -> str | None:
    """The race of an enclosing "<Race> Subraces" heading (MTF "TIEFLING SUBRACES" over "BAALZEBUL", "Elf
    Subraces" over "Eladrin Traits"): the nearest heading above `s` set taller than every heading between them
    (the subrace's own title, "SHADAR-KAI" over "SHADAR-KAI TRAITS", aside). Any other such heading closes the
    search."""
    tallest = b.L[s].height
    for j in range(s - 1, max(-1, s - 1 - _ENCLOSE_REACH), -1):
        if not b.heads[j]:
            continue
        h = b.L[j].height
        if h >= _ENCLOSE * tallest and squash(b.L[j].text) != squash(name):
            m = re.match(r"^(.+?)\s+subraces?$", b.L[j].text.strip(), re.I)
            return _one(m.group(1), races) if m else None
        tallest = max(tallest, h)
    return None


def _prose_parent(b: _Book, s: int, name: str, races: list[str]) -> str | None:
    """The race a sentence just above the subrace heading names it a subrace of: DMG "... walks through the
    creation of an elf subrace: the eladrin." The sentence must name this subrace."""
    lines = []
    for j in range(s - 1, max(-1, s - 1 - _PROSE_REACH), -1):
        if b.heads[j] or b.labels[j] is not None:
            break
        lines.insert(0, b.L[j].text.strip())
    for m in re.finditer(r"\b([A-Za-z][A-Za-z-]*)\s+subrace\s*:\s*the\s+([A-Za-z][A-Za-z' -]*?)[.,;]",
                         _join_text(lines), re.I):
        if like(m.group(2), name, 90):
            return _one(m.group(1), races)
    return None


def _race_word(text: str, races: list[str]) -> str | None:
    """The race a section heading names, singular or plural: "DWARVES" -> Dwarf, "Half-Elves" -> Half-Elf."""
    s = squash(text)
    for r in races:
        q = squash(r)
        if s in (q, q + "s", q + "es") or (q.endswith("f") and s == q[:-1] + "ves"):
            return r
    return None


def _section_parent(b: _Book, s: int, races: list[str]) -> str | None:
    """The race whose section the subrace heading sits in: the nearest heading above it naming a race ("DWARVES"
    over "Gray Dwarves (Duergar)" and its "Duergar Subrace Traits"), set at least `_SECTION` times taller than
    the subrace heading, with no other heading nearly as tall (a chapter title, another section) in between."""
    hs = b.L[s].height
    for j in range(s - 1, max(-1, s - 1 - _ENCLOSE_REACH), -1):
        if not b.heads[j] or b.L[j].height < _SECTION * hs:
            continue
        race = _race_word(b.L[j].text, races)
        if race is None:
            continue
        top = _SECTION_CLOSE * b.L[j].height
        if any(b.heads[k] and b.L[k].height >= top and _race_word(b.L[k].text, races) is None
               for k in range(j + 1, s)):
            return None
        return race
    return None


def _variant_ability(text: str) -> tuple[list | None, str]:
    """(ability, asi text) of a variant option. Only an option whose replaced trait is clearly not the Ability
    Score Increase (fuzzy, so an OCR slip "Ability Score lncrease" does not count as another trait) and that prints
    no increase gets the VARIANT_KEEPS_ASI marker; any other goes the normal way, so an unread increase fails
    `ability-increase-parsed`."""
    t = re.sub(r"\s+", " ", text)
    names = [m.group(1) for m in _REPLACES.finditer(t)]
    if names and not any(like(n, "Ability Score Increase", 85) for n in names) and \
            not re.search(r"\bin-? ?creases? by\b", t, re.I):
        return None, VARIANT_KEEPS_ASI
    return _ability(t), t


def _variants(b: _Book, races: list[str], claimed: set[int], source_of) -> list[tuple[Parsed, str | None]]:
    """Subraces printed as a "<Race> Variants" option list (SCAG "Tiefling Variants"): each trait whose text says
    which trait it replaces ("This trait replaces the Infernal Legacy trait.") is a variant named by its label.
    Only what the option prints is a field: an option replacing the Ability Score Increase trait carries its own
    increases; any other option prints none, keeps the race's, and its `ability` stays absent (never inherited).
    An option that replaces nothing (SCAG "Appearance") is not a variant. The list ends at the next heading or at
    the end of the page (the main text resuming after a sidebar)."""
    out: list[tuple[Parsed, str | None]] = []
    L = b.L
    for i, l in enumerate(L):
        m = re.match(r"^(.+?)\s+variants$", l.text.strip(), re.I)
        race = _one(m.group(1), races) if m and b.heads[i] and i not in claimed else None
        if race is None:
            continue
        end = next((j for j in range(i + 1, len(L)) if b.heads[j] or L[j].page != l.page), len(L))
        starts = [j for j in range(i + 1, end) if b.labels[j] is not None]
        for k, j in enumerate(starts):
            stop = starts[k + 1] if k + 1 < len(starts) else end
            traits = _traits(b, j, stop)
            if not traits or not _REPLACES.search(traits[0][1]):
                continue
            lab, text = traits[0]
            fields: dict = {"raceName": race, "raceSource": source_of(race)}
            ability, asi = _variant_ability(text)
            if asi != VARIANT_KEEPS_ASI:
                fields["ability"] = ability
            fields["entries"] = [{"type": "entries", "name": lab, "entries": [text]}]
            lines = L[j:stop]
            out.append((Parsed(type="Subrace", name=lab, fields=fields, text="\n".join(x.text for x in lines),
                               lines=lines), asi))
        claimed.update(range(i, end))
    return out


def _subrace(b: _Book, s: int, stop: int, race: str | None, name: str, source: str, race_walk: int | None) \
        -> tuple[Parsed, str | None, int]:
    end = _run_end(b, s, stop)
    traits = _traits(b, s + 1, end)
    fields = {"raceName": race or None, "raceSource": source, **_subrace_fields(traits, race_walk)}
    lines = b.L[s:end]
    return (Parsed(type="Subrace", name=name, fields=fields, text="\n".join(l.text for l in lines), lines=lines),
            _asi_text(traits), end)


def parse_races(body: list[Line], ctx: ParseContext) -> list[Parsed]:
    """Parse every race and subrace in `body` (reading-ordered lines of the whole book)."""
    if not body:
        return []
    b = _book(body)
    L = b.L
    rule = _book_rule(L)
    anchors = _race_anchors(b)
    races: list[tuple[Parsed, str | None]] = []
    subs: list[tuple[Parsed, str | None]] = []
    claimed: set[int] = set()  # lines inside a race's section (its traits and its subraces)
    for k, (a, name) in enumerate(anchors):
        nxt = anchors[k + 1][0] if k + 1 < len(anchors) else min(len(L), a + _SUB_WINDOW)
        end = _hanging_end(b, a, _run_end(b, a, nxt))
        traits = _traits(b, a, end)
        fields = _race_fields(traits)
        lines = L[a:end]
        p = Parsed(type="Race", name=name, fields=fields, text="\n".join(l.text for l in lines), lines=lines)
        # its subraces: the headings with their own Ability Score Increase up to the next race or chapter title
        tall = _CHAPTER * L[a].height
        stop = next((j for j in range(end, nxt) if b.heads[j] and L[j].height >= tall), nxt)
        claimed.update(range(a, stop))
        own: list[Parsed] = []
        for s in (j for j in range(end, stop) if _subrace_head(b, j)):
            title = L[s].text.strip()
            sp, s_asi, _ = _subrace(b, s, stop, name, _traits_name(title) or tidy_case(title), ctx.source,
                                    _walk_of(fields.get("speed")))
            own.append(sp)
            subs.append((sp, s_asi))
        asi = _asi_text(traits)
        if asi is None and own and all(sp.fields.get("ability") for sp in own):
            asi = _BY_SUBRACE
        races.append((p, asi if asi is not None else rule))
    known = [p.name for p, _ in races if p.name]
    known += [r for r in CORE_RACES if not any(squash(r) == squash(k) for k in known)]
    in_book = {squash(p.name) for p, _ in races if p.name}
    starts = sorted(a for a, _ in anchors)
    # subraces printed apart from their race ("Pallid Elf", the ERLW dragonmarks, MTF "Tiefling Subraces")
    for s in range(len(L)):
        if s in claimed or not _subrace_head(b, s):
            continue
        parents, name = _named_parent(b, s, known)
        if not parents:
            one = _enclosing_parent(b, s, name, known) or _prose_parent(b, s, name, known) or \
                _section_parent(b, s, known)
            parents = [one] if one else []
        stop = next((a for a in starts if a > s), len(L))
        for race in parents or [None]:  # "Variant Half-Orc Or Human:" prints one block for two races
            source = ctx.source if race is None or squash(race) in in_book else "PHB"
            sp, s_asi, end = _subrace(b, s, stop, race, name, source, None)
            claimed.update(range(s, end))
            subs.append((sp, s_asi))
    subs += _variants(b, known, claimed, lambda r: ctx.source if squash(r) in in_book else "PHB")
    known_set = set(known)
    for p, asi in races:
        p.checks = race_checks(p.fields, False, known_set, asi_text=asi)
    for p, asi in subs:
        p.checks = race_checks(p.fields, True, known_set, asi_text=asi)
    return [p for p, _ in races] + npc_table_races(ctx) + [p for p, _ in subs]


# ---------- the DMG NPC Features table ----------
_NPC_MOD = re.compile(r"^([+\-−–])\s*(\d)\s+([A-Za-z]{3})$")
_NPC_SPEED = re.compile(r"\bspeed (\d+) ft\.?((?:,? (?:fly|swim|climb|burrow) \d+ ft\.?)*)", re.I)
_NPC_MODE = re.compile(r"\b(fly|swim|climb|burrow) (\d+) ft", re.I)
_NPC_SIZE = re.compile(r"\b(Tiny|Small|Large|Huge)\s+size\b", re.I)


def _npc_modifiers(cell: str) -> list | None:
    """5etools `ability` from an NPC Features "Ability Modifiers" cell ("+2 Str, -4 Int"); None for "None" or
    when any part is not a signed modifier of a named ability."""
    out: dict[str, int] = {}
    for part in (x.strip() for x in cell.split(",")):
        m = _NPC_MOD.match(part)
        key = next((v for v in ABILITIES.values() if m and m.group(3).lower() == v), None)
        if key is None or key in out:
            return None
        out[key] = int(m.group(2)) * (1 if m.group(1) == "+" else -1)
    return [dict(sorted(out.items(), key=lambda kv: _ORDER.index(kv[0])))] if out else None


def _npc_rows(page) -> list[tuple[list[Line], list[Line], list[Line]]]:
    """(name, ability, features) cell lines of each row of the NPC Features table on `page`, or none. Columns
    start at the "Race", "Ability Modifiers" and "Features" header cells; a row starts at an ability cell that
    does not continue the one above it (which ends with a comma); the table ends at its "*" footnote."""
    lines = page.lines
    title = next((l for l in lines if squash(l.text) == "npcfeatures"), None)
    if title is None:
        return []
    below = [l for l in lines if l.box[1] > title.box[1]]
    head = {squash(l.text): l for l in below if abs(l.box[1] - min(x.box[1] for x in below)) < l.height}
    if not {"race", "abilitymodifiers", "features"} <= head.keys():
        return []
    ab, fe = head["abilitymodifiers"].box[0], head["features"].box[0]
    top = max(h.box[3] for h in head.values())
    foot = min((l.box[1] for l in below if l.box[1] > top and l.text.strip().startswith("*") and
                l.box[2] - l.box[0] > fe - ab), default=page.height)
    cells = [l for l in below if top < (l.box[1] + l.box[3]) / 2 < foot and l.box[0] >= title.box[0] - title.height]
    tol = 0.5 * min(l.height for l in cells) if cells else 0

    def col(l: Line) -> int:
        return 0 if l.box[0] < ab - tol else 1 if l.box[0] < fe - tol else 2

    abil = sorted((l for l in cells if col(l) == 1), key=lambda l: l.box[1])
    starts = [l.box[1] for k, l in enumerate(abil) if k == 0 or not abil[k - 1].text.strip().endswith(",")]
    rows = []
    for k, y in enumerate(starts):
        nxt = starts[k + 1] if k + 1 < len(starts) else foot
        band = sorted((l for l in cells if y - tol <= l.box[1] < nxt - tol), key=lambda l: l.box[1])
        rows.append(tuple([l for l in band if col(l) == c] for c in range(3)))
    return rows


def npc_table_races(ctx: ParseContext) -> list[Parsed]:
    """One Race per monster row of the DMG "NPC Features" table: its printed name, ability modifiers, and the
    size, speed and darkvision its Features cell prints. A row marked "*" points at the Player's Handbook race
    and defines nothing here. A row whose name cell the OCR lost is emitted nameless (it fails `name-found`)."""
    out: list[Parsed] = []
    for n in sorted(ctx.pages):
        for names, abil, feats in _npc_rows(ctx.pages[n]):
            name = " ".join(l.text.strip() for l in names)
            if "*" in name:
                continue
            cell = " ".join(l.text.strip() for l in abil)
            text = fix_digits(_join_text([l.text.strip() for l in feats]))
            f: dict = {"size": [SIZES[m.group(1).title()]] if (m := _NPC_SIZE.search(text)) else None}
            if m := _NPC_SPEED.search(text):
                modes = {k.lower(): int(v) for k, v in _NPC_MODE.findall(m.group(2))}
                f["speed"] = {"walk": int(m.group(1)), **modes} if modes else int(m.group(1))
            f["ability"] = None if squash(cell) == "none" else _npc_modifiers(cell)
            f["darkvision"] = int(m.group(1)) if (m := re.search(r"\bdarkvision (\d+) ft", text, re.I)) else None
            f["entries"] = [{"type": "entries", "name": "Features", "entries": [text]}] if text else None
            rows = sorted(names + abil + feats, key=lambda l: (l.box[1], l.box[0]))
            p = Parsed(type="Race", name=tidy_case(name), fields=f, lines=rows,
                       text=f"{name} | Ability Modifiers: {cell} | Features: {text}")
            p.checks = _npc_checks(f, cell)
            out.append(p)
    return out


def _npc_checks(f: dict, cell: str) -> list[Check]:
    ability = f.get("ability")
    if ability:
        bad = [f"{k} modifier {v:+d} is not between -6 and +2" for k, v in ability[0].items() if not -6 <= v <= 2 or not v]
        asi = Check("ability-increase-parsed", not bad, "; ".join(bad))
    elif squash(cell) == "none":
        asi = Check("ability-increase-parsed", True)
    else:
        asi = Check("ability-increase-parsed", False, f"could not read the ability modifiers from {cell!r}")
    size = _size_check(f) if f.get("size") else Check(
        "size-valid", False, "the NPC Features table prints no size for this race (it prints only \"Small size\")")
    speed = _speed_check(f) if f.get("speed") is not None else Check(
        "speed-valid", False, "the NPC Features table prints no walking speed for this race")
    return [asi, size, speed]


# ---------- checks ----------
def race_checks(fields: dict, is_subrace: bool, known_races: set[str], asi_text: str | None = None) -> list[Check]:
    """`asi_text` is the printed Ability Score Increase trait, or for a race that prints none the book's rule
    on choosing increases; the check names it when the increases could not be read."""
    out = [_ability_check(fields, asi_text)]
    if not is_subrace:
        out.append(_size_check(fields))
    if not is_subrace or fields.get("speed") is not None:
        out.append(_speed_check(fields))
    if is_subrace:
        out.append(_parent_check(fields, known_races))
    return out


def _ability_problems(ability) -> list[str]:
    """A race increases a score by 1 or 2, and lets the player choose 1 to 3 scores; anything else is a misread
    (an OCR digit merge, "by 7")."""
    out: list[str] = []
    if not isinstance(ability, list):
        return [f"ability {ability!r} is not a list"]
    for entry in ability:
        if not isinstance(entry, dict):
            out.append(f"ability entry {entry!r} is not an object")
            continue
        for key, v in entry.items():
            if key == "choose":
                c = v if isinstance(v, dict) else {}
                if c.get("count") not in (1, 2, 3):
                    out.append(f"choose count {c.get('count')!r} is not 1, 2 or 3")
                if c.get("amount", 1) not in (1, 2):
                    out.append(f"choose amount {c.get('amount')!r} is not 1 or 2")
            elif isinstance(v, bool) or v not in (1, 2):
                out.append(f"{key} increase {v!r} is not 1 or 2")
    return out


def _ability_check(fields: dict, asi_text: str | None) -> Check:
    name = "ability-increase-parsed"
    if fields.get("ability"):
        problems = _ability_problems(fields["ability"])
        return Check(name, not problems, "; ".join(problems))
    if asi_text and (_CHOSEN.search(asi_text) or asi_text in (_BY_SUBRACE, VARIANT_KEEPS_ASI)):
        return Check(name, True)
    if asi_text:
        return Check(name, False, f"could not read the ability score increases from {asi_text!r}")
    return Check(name, False, "no Ability Score Increase trait printed and no book rule letting the player "
                              "choose the increases")


def _size_check(fields: dict) -> Check:
    name = "size-valid"
    size = fields.get("size")
    codes = set(SIZES.values())
    if isinstance(size, list) and size and all(s in codes for s in size):
        return Check(name, True)
    return Check(name, False, f"size {size!r} is not a list of size codes ({', '.join(SIZES.values())})")


def _speed_problem(mode: str, v) -> str | None:
    if mode != "walk" and v is True:
        return None
    hi = 60 if mode == "walk" else 120
    if isinstance(v, bool) or not isinstance(v, int):
        return f"{mode} speed {v!r} is not a number of feet"
    if v % 5 or not 5 <= v <= hi:
        return f"{mode} speed {v} is not a multiple of 5 between 5 and {hi} feet"
    return None


def _speed_check(fields: dict) -> Check:
    name = "speed-valid"
    speed = fields.get("speed")
    if speed is None:
        return Check(name, False, "no walking speed read from the Speed trait")
    modes = speed if isinstance(speed, dict) else {"walk": speed}
    if "walk" not in modes:
        return Check(name, False, f"speed {speed!r} has no walking speed")
    problems = [p for m, v in modes.items() if (p := _speed_problem(m, v))]
    return Check(name, not problems, "; ".join(problems))


def _parent_check(fields: dict, known: set[str]) -> Check:
    name = "parent-race-known"
    parent = fields.get("raceName")
    if parent and any(squash(parent) == squash(k) for k in known):
        return Check(name, True)
    return Check(name, False, f"parent race {parent!r} is not a race read from this book or a PHB race "
                              f"({', '.join(sorted(known)) or 'none'})")
