"""Page-type classifier: separates rules pages from art, TOC, index, credits and blanks."""
from __future__ import annotations

import re
from typing import Literal

from .model import Page

PageKind = Literal["rules", "art", "toc", "index", "credits", "blank"]

_ENTRY = re.compile(r"[,.\s]\s*\d{1,3}[.,]?$")  # "Goblin, 166" / "Aboleth.... 13" / bare page number
# Index entries are "Name, 123" (comma then page ref); a thousands separator ("1,500 gp") or a bare
# trailing number ("Level 5") in a rules table must not look like one.
_INDEX_REF = re.compile(r"[A-Za-z)'.], ?\d{1,3}[.,]?$")
# Looser index-line shapes, for index pages without an "Index" heading or dot leaders: page-range lists
# ("25-26,73,106112,"), "(cont.)" sub-headings and "See ..." cross references. A comma is required between
# the numbers so a table cell like "1,500" or a bare "12" does not count.
_INDEX_RANGE = re.compile(r"[A-Za-z)'.], ?\d{1,3}[\d,\-\u2013]*[.,]?$|^\d[\d\-\u2013]*(?:, ?\d[\d\-\u2013]*)+,?$"
                          r"|\(cont\.\)$|\bSee\b")
_LOOSE_INDEX_RATIO = 0.6
_MAP = re.compile(r"\bMAPS?\b")
_ART_MAX_WORDS = 30
_ART_MAX_LINES = 15
_TABLE_MIN_LINES = 60
_TABLE_SHORT_RATIO = 0.4
_TABLE_NUMERIC_RATIO = 0.3
_TABLE_CELL = re.compile(r"^[\dOolIdD/,\-\u2013+ ]*\d?[\dOolI]*$|^d\d+|^OOLP|^[\d,]+ ?[gscep]p$", re.I)
_MIN_LISTING_LINES = 20
_LISTING_RATIO = 0.5


def _heading(page: Page) -> str:
    """Text of the top-most line, lower-cased."""
    top = min(page.lines, key=lambda ln: (ln.box[1], ln.box[0]))
    return top.text.lower()


def classify_page(page: Page) -> PageKind:
    if not page.lines:
        return "blank"
    if len(page.lines) <= 3 and sum(len(ln.text) for ln in page.lines) < 60:
        return "art"
    words = sum(len(ln.text.split()) for ln in page.lines)
    if len(page.lines) <= _ART_MAX_LINES and words < _ART_MAX_WORDS:
        return "art"  # a few short labels: map legend, tarot-card art labels
    if len(page.lines) <= 20 and words < 120 and any(_MAP.search(ln.text) for ln in page.lines):
        return "art"  # "Appendix C: Maps" page with a short intro and a map
    heading = _heading(page)
    if heading.startswith("credits"):
        return "credits"
    listing = sum(1 for ln in page.lines if _ENTRY.search(ln.text)) / len(page.lines)
    many = len(page.lines) >= _MIN_LISTING_LINES
    if heading.startswith("contents") and (listing >= _LISTING_RATIO or many):
        return "toc"
    refs = sum(1 for ln in page.lines if _INDEX_REF.search(ln.text)) / len(page.lines)
    loose = sum(1 for ln in page.lines if _INDEX_RANGE.search(ln.text)) / len(page.lines)
    if heading.startswith("index") or (many and (refs >= 0.7 or loose >= _LOOSE_INDEX_RATIO)):
        return "index"
    return "rules"


def is_table_page(page: Page) -> bool:
    """A rules page dominated by table cells (short or numeric/dice cells): counted as covered for later phases."""
    n = len(page.lines)
    if n < _TABLE_MIN_LINES:
        return False
    short = sum(1 for ln in page.lines if len(ln.text.split()) <= 2) / n
    numeric = sum(1 for ln in page.lines if _TABLE_CELL.match(ln.text.strip())) / n
    return short >= _TABLE_SHORT_RATIO or numeric >= _TABLE_NUMERIC_RATIO
