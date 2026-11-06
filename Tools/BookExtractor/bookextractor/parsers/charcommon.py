"""Shared vocabulary for the character-data parsers (classes, races, feats, backgrounds)."""
from __future__ import annotations

import re
import statistics
from dataclasses import replace

from rapidfuzz import fuzz

from ..model import Line
from . import ParseContext
from .monster import _is_heading_caps, _join_text, _title

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


# ---------- sidebars ----------
# A sidebar ("Breaking Your Oath", "Druids and the Gods", "Mage Sigils") is printed in a shaded box inside a
# column. The OCR keeps no shading, but the box shows in the geometry: its title and every line under it start
# inset from the column's text edge (by about half a line height in PHB/DMG, a full one in TCE/SCAG/VRGR), while
# a body paragraph only indents its first line. Measured on the class and subclass sections of all 16 books.
_SIDEBAR_INSET = 0.25  # x inset from the column's text edge, in page-median line heights
_SIDEBAR_MAX_INSET = 1.5  # a title set further in is centred (a table caption, a spell-save formula)
_SIDEBAR_MIN_LINES = 3
_PROSE_WORDS = 3
_ROW_START = re.compile(r"^\S{0,2}\d")  # a table row opens with its number or level ("3rd", "1 Crime.", "d6")
_CUE_START = re.compile(r"(?i)^\S{2,5}-level\b|\b\S{2,5} level\b")  # a feature heading's level cue


def _prose_line(t: str, min_len: int) -> bool:
    return len(t) >= min_len and sum(w[:1].islower() for w in t.split()) >= _PROSE_WORDS


def _page_sidebars(rows: list[tuple[int, Line]], mid: float) -> set[int]:
    """Indices (into the book's lines) of one page's sidebar lines."""
    h = statistics.median(l.height for _, l in rows) or 1.0

    def col(l: Line) -> int:
        return 1 if (l.box[0] + l.box[2]) / 2 > mid else 0

    # the column's text edge: the leftmost x0 shared by at least three prose lines (one stray line is not an edge)
    edge: dict[int, float] = {}
    for c in (0, 1):
        xs = sorted(l.box[0] for _, l in rows if col(l) == c and _prose_line(l.text, 40))
        cl = [x for x in xs if sum(abs(y - x) <= 0.25 * h for y in xs) >= 3]
        if cl:
            edge[c] = cl[0]
    out: set[int] = set()
    i = 0
    while i < len(rows):
        l = rows[i][1]
        c, t = col(l), l.text.strip()
        # a sidebar title may list things ("CAVALIERS, SAMURAI, AND HISTORY")
        if c not in edge or vertical(l.box) or not (heading_like(t) and title_case(t.replace(",", ""))) \
                or "level" in t.lower() \
                or not _SIDEBAR_INSET * h <= l.box[0] - edge[c] <= _SIDEBAR_MAX_INSET * h:
            i += 1
            continue
        # the box: the title and the lines under it in its column, none starting left of the title
        k = i + 1
        while k < len(rows) and col(rows[k][1]) == c and rows[k][1].box[0] >= l.box[0] - _SIDEBAR_INSET * h \
                and rows[k][1].box[1] >= rows[k - 1][1].box[1]:
            k += 1
        body = [x for _, x in rows[i + 1:k]]
        inner = min(x.box[0] for _, x in rows[i:k])
        # the text around the box sits at the column edge (a skewed scan shifts a whole column, not one block)
        around = [[x for _, x in rows[j0:j1] if col(x) == c and _prose_line(x.text, 30)]
                  for j0, j1 in ((max(0, i - 3), i), (k, k + 3))]
        around = [a for a in around if a]
        if (len(body) >= _SIDEBAR_MIN_LINES
                and sum(_prose_line(x.text, 30) for x in body) >= max(_SIDEBAR_MIN_LINES, len(body) // 2)
                and 3 * sum(bool(_ROW_START.match(x.text.strip())) for x in body) < len(body)
                and not _CUE_START.search(" ".join(x.text for x in body[:2])[:80])
                and around and all(inner - min(x.box[0] for x in a) >= _SIDEBAR_INSET * h for a in around)):
            out.update(n for n, _ in rows[i:k])
            i = k
        else:
            i += 1
    return out


def without_sidebars(L: list[Line], ctx: ParseContext) -> list[Line]:
    """The lines with every sidebar box (title and text) taken out, so a sidebar printed beside a feature never
    becomes part of the feature; a sentence the box interrupted ("It also can't take" / "reactions.") reads on."""
    pages: dict[int, list[tuple[int, Line]]] = {}
    for n, l in enumerate(L):
        pages.setdefault(l.page, []).append((n, l))
    drop: set[int] = set()
    for number, rows in pages.items():
        pg = ctx.pages.get(number)
        mid = pg.width / 2 if pg else (min(l.box[0] for _, l in rows) + max(l.box[2] for _, l in rows)) / 2
        drop |= _page_sidebars(rows, mid)
    return [l for n, l in enumerate(L) if n not in drop]


def character_body(body: list[Line], ctx: ParseContext) -> list[Line]:
    """The book's lines as the class and subclass parsers read them (art credits and sidebars out), computed once
    per book."""
    if "character_body" not in ctx.cache:
        ctx.cache["character_lines"] = without_art_credits(body)
        ctx.cache["character_body"] = without_sidebars(ctx.cache["character_lines"], ctx)
    return ctx.cache["character_body"]


def with_sidebars(ctx: ParseContext, first: Line, last: Line) -> list[Line]:
    """Every printed line from `first` to `last` (lines of `character_body`), the sidebars between them included:
    a class's rules printed in a sidebar ("Multiclassing and the Artificer") are still read as fields."""
    full: list[Line] = ctx.cache["character_lines"]
    at = {id(l): k for k, l in enumerate(full)}
    return full[at[id(first)]:at[id(last)] + 1]


# ---------- text lines and headings ----------
# Shared by the trap, hazard/rules and section readers. Geometry is in book-median line heights (measured on DMG,
# XGE, TCE and VRGR).
RUNNING_HEAD = re.compile(r"(?i)^(\d+\s+)?(chapter|part|appendix)\s+\w+\s*\|")  # "CHAPTER 4 | CLASSES"
PAGE_NO = re.compile(r"^\d{1,3}\W?$")
_SMALL, _LARGE = 0.75, 2.0  # a line set this much smaller (caption) or larger (art text) is not body text
_OFF_COLUMN = 6.0  # a line starting this far right of its column's text edge is a caption beside the art
GAP = 1.8  # a heading after a table has this much space above it (line tops; rows are about 1.3 apart)

caps_heading = _is_heading_caps  # an all-caps heading ("WILDERNESS")
name_case = _title  # a printed name with OCR casing noise undone ("GOBLIn BOss" -> "Goblin Boss")


def median_height(L: list[Line]) -> float:
    """The median line height of the lines given (the book's, when given the book): the unit of the geometry."""
    return statistics.median(l.height for l in L)


def char_width(l: Line, med: float) -> float:
    """The line's average character width, in median line heights: a heading is set wider than body text."""
    return (l.box[2] - l.box[0]) / max(len(l.text.strip()), 1) / med


def set_apart(prev: Line, l: Line) -> bool:
    """`l` opens a page or column, or has space above it (a heading after a table)."""
    return prev.page != l.page or prev.box[1] > l.box[1] or l.box[1] - prev.box[1] >= GAP * l.height


def text_lines(L: list[Line], ctx: ParseContext, keep=None) -> list[Line]:
    """The body lines that are text: no running head or foot, page number, art text or off-column caption.
    `keep(line, median)` names lines kept whatever their size (a chapter title set larger than art text)."""
    m = median_height(L)
    edges: dict[tuple[int, int], float] = {}

    def col(l: Line) -> tuple[int, int]:
        pg = ctx.pages.get(l.page)
        mid = pg.width / 2 if pg else 800
        return l.page, int((l.box[0] + l.box[2]) / 2 > mid)

    for l in L:
        if _SMALL * m <= l.height <= _LARGE * m and len(l.text) >= 20:
            k = col(l)
            edges[k] = min(edges.get(k, l.box[0]), l.box[0])
    out = []
    for l in L:
        t = l.text.strip()
        if keep is not None and keep(l, m):
            out.append(l)
            continue
        if not t or RUNNING_HEAD.match(t) or PAGE_NO.fullmatch(t) or l.height > _LARGE * m:
            continue
        if l.height < _SMALL * m and (not re.search(r"[a-z]", t) or heading_like(t) and title_case(t)):
            continue  # a caption set small ("HAUNTED TRAP", "ELDritch StoRm"); a paragraph's short last line
            # ("can escape.") is text
        if l.box[0] - edges.get(col(l), l.box[0]) > _OFF_COLUMN * m:
            continue
        out.append(l)
    return [l for k, l in enumerate(out) if not _caption(out, k) or keep is not None and keep(l, m)]


def _caption(L: list[Line], k: int) -> bool:
    """An art caption read into the middle of a sentence ("... the area in the storm" / "ELDritch StoRm" / "is
    heavily obscured"): a title-case line between a sentence's halves, set well apart from the line after it."""
    t = L[k].text.strip()
    if not (0 < k < len(L) - 1 and heading_like(t) and title_case(t)):
        return False
    prev, nxt = L[k - 1], L[k + 1]
    return not prev.text.rstrip().endswith(SENTENCE_END) and nxt.text.strip()[:1].islower() and \
        nxt.page == L[k].page and nxt.box[1] - L[k].box[1] >= GAP * L[k].height


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
