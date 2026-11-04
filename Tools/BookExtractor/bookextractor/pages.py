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
    heading = _heading(page)
    if heading.startswith("credits"):
        return "credits"
    listing = sum(1 for ln in page.lines if _ENTRY.search(ln.text)) / len(page.lines)
    many = len(page.lines) >= _MIN_LISTING_LINES
    if heading.startswith("contents") and (listing >= _LISTING_RATIO or many):
        return "toc"
    refs = sum(1 for ln in page.lines if _INDEX_REF.search(ln.text)) / len(page.lines)
    if heading.startswith("index") or (many and refs >= 0.7):
        return "index"
    return "rules"
