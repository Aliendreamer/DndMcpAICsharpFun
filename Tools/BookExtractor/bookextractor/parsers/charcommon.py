"""Shared vocabulary for the character-data parsers (classes, races, feats, backgrounds)."""
from __future__ import annotations

import re

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
