"""Shared vocabulary for the character-data parsers (classes, races, feats, backgrounds)."""
from __future__ import annotations

import re
from dataclasses import replace

from rapidfuzz import fuzz

from ..model import Line
from . import ParseContext
from .monster import _join_text

SKILLS = ("Acrobatics", "Animal Handling", "Arcana", "Athletics", "Deception", "History", "Insight",
          "Intimidation", "Investigation", "Medicine", "Nature", "Perception", "Performance", "Persuasion",
          "Religion", "Sleight of Hand", "Stealth", "Survival")

SIZES = {"Tiny": "T", "Small": "S", "Medium": "M", "Large": "L", "Huge": "H", "Gargantuan": "G"}

ABILITIES = {"Strength": "str", "Dexterity": "dex", "Constitution": "con",
             "Intelligence": "int", "Wisdom": "wis", "Charisma": "cha"}

# class -> (singular title, plural heading)
SUBCLASS_GROUPS = {cls: (title, title + "s") for cls, title in {
    "Barbarian": "Primal Path", "Bard": "Bard College", "Cleric": "Divine Domain", "Druid": "Druid Circle",
    "Fighter": "Martial Archetype", "Monk": "Monastic Tradition", "Paladin": "Sacred Oath",
    "Ranger": "Ranger Archetype", "Rogue": "Roguish Archetype", "Sorcerer": "Sorcerous Origin",
    "Warlock": "Otherworldly Patron", "Wizard": "Arcane Tradition", "Artificer": "Artificer Specialist",
}.items()}

SUBCLASS_LEVELS = {
    "Barbarian": [3, 6, 10, 14], "Bard": [3, 6, 14], "Cleric": [1, 2, 6, 8, 17], "Druid": [2, 6, 10, 14],
    "Fighter": [3, 7, 10, 15, 18], "Monk": [3, 6, 11, 17], "Paladin": [3, 7, 15, 20],
    "Ranger": [3, 7, 11, 15], "Rogue": [3, 9, 13, 17], "Sorcerer": [1, 6, 14, 18],
    "Warlock": [1, 6, 10, 14], "Wizard": [2, 6, 10, 14], "Artificer": [3, 5, 9, 15],
}

# 5etools ClassSource: empty when the class is defined in PHB.
CLASS_SOURCE = {**{c: "" for c in SUBCLASS_LEVELS if c != "Artificer"}, "Artificer": "TCE"}


def class_source(cls: str, ctx_source: str) -> str:
    """ClassSource for a feature ref. A class defined in the book being parsed uses that book's source
    (unless it is PHB, which is the empty source)."""
    if ctx_source != "PHB" and CLASS_SOURCE.get(cls) == ctx_source:
        return ctx_source
    return CLASS_SOURCE.get(cls, "")


_ORD_DIGIT = str.maketrans({"l": "1", "I": "1", "i": "1", "|": "1", "!": "1", "O": "0", "o": "0",
                            "S": "5", "s": "5", "Z": "2", "z": "2", "B": "8"})
_ORDINAL = re.compile(r"([0-9lIi|!OoSsZzB]{1,2})\s*(st|nd|rd|th)", re.IGNORECASE)


def ordinal(text: str) -> int | None:
    """'3rd' -> 3, tolerant of OCR digit noise ('lst' -> 1). None when not a level ordinal (1..20)."""
    m = _ORDINAL.fullmatch(text.strip())
    if not m:
        return None
    digits = m.group(1).translate(_ORD_DIGIT)
    if not digits.isdigit():
        return None
    n = int(digits)
    return n if 1 <= n <= 20 else None


def squash(s: str) -> str:
    """Letters only, lower case: the form OCR casing and spacing noise cannot change."""
    return re.sub(r"[^a-z]", "", s.lower())


def like(a: str, b: str, cutoff: int = 85) -> bool:
    """Fuzzy equality of two printed names (on their squashed form)."""
    x, y = squash(a), squash(b)
    return bool(x) and bool(y) and fuzz.ratio(x, y) >= cutoff


def vertical(box: tuple[float, float, float, float]) -> bool:
    """Text set sideways in the margin (an art credit): taller than it is wide."""
    return box[2] - box[0] < box[3] - box[1]


def _credit(text: str, box: tuple[float, float, float, float]) -> bool:
    """A sideways art credit ("BRIAN VALEZA"). Three letters at least: a single upright glyph ("I", a drop cap) can
    be taller than it is wide too."""
    return vertical(box) and sum(c.isalpha() for c in text) >= 3


def without_art_credits(L: list[Line]) -> list[Line]:
    """The lines with the sideways art credits taken out: a credit line of its own is dropped, and a credit that
    reading order merged into the body row beside it ("whenever BRIAN VALEZA") is cut from that row's text. Upright
    text is never touched, so an all-caps heading ("ARMORER SPELLS") stays. The row keeps its box, which is its
    printed-line identity for the text-bleed gate."""
    out = []
    for l in L:
        if _credit(l.text, l.box):
            continue
        keep = [f for f in l.frags if not _credit(f.text, f.box)]
        if l.frags and keep and len(keep) < len(l.frags):
            l = replace(l, text=" ".join(f.text for f in keep), frags=keep)
        out.append(l)
    return out


def character_body(body: list[Line], ctx: ParseContext) -> list[Line]:
    """The book's lines as the class and subclass parsers read them (art credits out), computed once per book."""
    if "character_body" not in ctx.cache:
        ctx.cache["character_body"] = without_art_credits(body)
    return ctx.cache["character_body"]


BULLET = re.compile(r"^(?:[•·]\s*|\.\s*(?=[A-Z]))")  # OCR reads some bullets as "."
SENTENCE_END = (".", ":", "!", "?", ")", '"', "'")

SMALL_WORDS = {"of", "the", "and", "a", "an", "to", "in", "on", "from", "for", "with", "at", "by"}


def title_case(t: str) -> bool:
    words = re.findall(r"[A-Za-z][A-Za-z'-]*", t)
    return bool(words) and not re.search(r"[,.!?]", t) and all(w[:1].isupper() for k, w in enumerate(words)
                                                if not (k and w.lower() in SMALL_WORDS))


def heading_like(t: str) -> bool:
    t = t.strip()
    return (0 < len(t.split()) <= 6 and t[:1].isalpha() and t[:1].isupper()
            and not t.endswith((".", ",", ";", ":", "-")) and not re.search(r"\d", t))


def tidy_case(name: str) -> str:
    """Undo OCR small-caps noise ('Path Of the ZEALOT', 'FAnaTiCal FOcus') without touching regular words."""
    out = []
    for k, w in enumerate(name.split()):
        if k and w.lower() in SMALL_WORDS:
            out.append(w.lower())
        elif any(c.isupper() for c in w[1:]):
            out.append(w[:1].upper() + w[1:].lower())
        else:
            out.append(w)
    return " ".join(out)


def indented(L: list[Line], i: int) -> bool:
    """The line starts indented from its column's left edge (the lines around it in the column): a trait label,
    a paragraph's first line, a wrapped list item's continuation."""
    l = L[i]
    near = [L[j].box[0] for j in range(max(0, i - 6), min(len(L), i + 7))
            if L[j].page == l.page and abs(L[j].box[0] - l.box[0]) < 3 * l.height]
    return l.box[0] - min(near) > 0.4 * l.height


def prose_entries(L: list[Line], start: int, end: int) -> list:
    """Prose paragraphs and the bullet list, in printed order. A list opens at a bullet or after a line ending
    with ':' ("you gain the following benefits:"); inside it a new item starts at a bullet, or at a capitalised
    line after one ending a sentence (OCR often drops the bullet glyph)."""
    out: list = []
    para: list[str] = []
    items: list[list[str]] = []
    in_list = False

    def flush_para() -> None:
        if para:
            out.append(_join_text(para))
            para.clear()

    def flush_list() -> None:
        if items:
            out.append({"type": "list", "items": [_join_text(it) for it in items]})
            items.clear()

    prev = ""
    for j in range(start, end):
        if vertical(L[j].box):
            continue  # an art credit set sideways in the margin
        raw = L[j].text.strip()
        bullet = bool(BULLET.match(raw))
        text = BULLET.sub("", raw)
        if not text:
            continue
        new_sentence = prev.endswith(SENTENCE_END) and text[:1].isupper()
        if bullet or (in_list and new_sentence):
            if not in_list:
                flush_para()
                in_list = True
            items.append([text])
        elif in_list and items and not new_sentence:
            items[-1].append(text)
        else:
            if in_list:
                flush_list()
                in_list = False
            if para and prev.endswith(SENTENCE_END) and indented(L, j):
                flush_para()
            para.append(text)
            if text.endswith(":"):
                flush_para()
                in_list = True
        prev = text
    flush_para()
    flush_list()
    return out
