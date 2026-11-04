"""Verification checks shared by all entity parsers (type-specific checks live next to each parser)."""
from __future__ import annotations

import re
from dataclasses import dataclass


@dataclass
class Check:
    name: str
    passed: bool
    reason: str = ""


XP_BY_CR: dict[str, int] = {
    "0": 0, "1/8": 25, "1/4": 50, "1/2": 100, "1": 200, "2": 450, "3": 700, "4": 1100, "5": 1800,
    "6": 2300, "7": 2900, "8": 3900, "9": 5000, "10": 5900, "11": 7200, "12": 8400, "13": 10000,
    "14": 11500, "15": 13000, "16": 15000, "17": 18000, "18": 20000, "19": 22000, "20": 25000,
    "21": 33000, "22": 41000, "23": 50000, "24": 62000, "25": 75000, "26": 90000, "27": 105000,
    "28": 120000, "29": 135000, "30": 155000,
}

_DICE = re.compile(r"(\d+)d(\d+)(?:([+-])(\d+))?")


def hp_dice_check(average: int | None, formula: str | None) -> Check:
    """Average hit points must equal floor(n*(s+1)/2) + k for the printed dice formula."""
    name = "hp-matches-dice"
    if average is None or not formula:
        return Check(name, False, "hit points or dice formula missing")
    m = _DICE.fullmatch(re.sub(r"\s+", "", formula))
    if not m:
        return Check(name, False, f"unparseable dice formula {formula!r}")
    n, s = int(m.group(1)), int(m.group(2))
    k = int(m.group(4) or 0) * (-1 if m.group(3) == "-" else 1)
    expected = n * (s + 1) // 2 + k
    if expected == average:
        return Check(name, True)
    return Check(name, False, f"average {average} != {expected} expected from {formula}")


def hp_con_check(formula: str | None, con: int | None) -> Check:
    """The hit-dice bonus is the dice count times the CON modifier ("7d10 + 35" needs CON 20 or 21).

    Catches a CON score and modifier misread together ("20 (+5)" read as "14 (+2)"), which the
    score/modifier check can't see. Passes when either input is missing: other checks fail those.
    """
    name = "hp-bonus-matches-con"
    m = _DICE.fullmatch(re.sub(r"\s+", "", formula or ""))
    if not m or not isinstance(con, int):
        return Check(name, True)
    n = int(m.group(1))
    k = int(m.group(4) or 0) * (-1 if m.group(3) == "-" else 1)
    mod = (con - 10) // 2
    if k == n * mod:
        return Check(name, True)
    return Check(name, False, f"{formula} needs a CON modifier of {k / n:+g}, but CON is {con} ({mod:+d})")
