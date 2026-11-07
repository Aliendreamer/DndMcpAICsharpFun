"""Residual doubts for the world types (God, Rule, Plane, Faction, Location, Lore).

The section reader and the text-complete net catch the faults they were written for. These checks flag the classes
that still got through a review of the Accepted entities (rr12): each one only ever DOUBTS (a failing check with a
named reason), never cuts or repairs text, so an entity it hits becomes NeedsReview.

- `neighbour-absorbed`: the text holds a heading set like its own (small caps / capitals) over prose, after a table
  or a finished sentence: the next same-level section ran on into this one (XGE Alchemist's / Brewer's Supplies).
- `quote-leak`: a line printed apart from the text flow sits inside it: the end of a sentence interrupted by a
  detached line (a pull-quote's last line), or an epigraph block set apart over the opening paragraph.
- `figure-label-tail`: the text ends on a block set apart from the text above it that is no prose (a figure's
  caption and labels: DMG Tactical Maps).
- `table-cut`: a table with lines missing (a gap of whole lines between its rows), a table running off the bottom
  of its page, or a text that is only a lead-in or only a table.
- `stray-number-tail`: the text ends on a bare number printed away from the text (a page folio or a figure's tag).
"""
from __future__ import annotations

import re
import statistics

from .checks import Check
from .model import Line
from .parsers.charcommon import SENTENCE_END
from .parsers.sections import _OWN_PART_LABELS, _table_row, heading_words, prose, squash_text

_GAP = 2.0  # lines printed apart from the text above them by this many median line heights are set apart
_HOLE = 2.0  # consecutive table lines this many (x1.25) median line heights apart (top to top) have a line missing
_BOTTOM = 0.86  # a table whose last line ends this far down its page may run on to the next
_THIN = 70  # a text of one short sentence is only a lead-in
_CUE = re.compile(r"(?i)\b(?:following|below|table|shown|these|variants?|options?|presents?)\b")
_NUMBER = re.compile(r"^[\d][\d.,:/]{0,7}$")


def _flips(t: str) -> int:
    """Case flips inside words ("COveR", "BrEWer'S"): the OCR of small capitals."""
    return len(re.findall(r"[a-z][A-Z]|[A-Z][a-z][A-Z]", t))


def _odd_style(t: str) -> bool:
    """Set in small capitals or capitals: OCR mixed case ("BrEWer'S SUPpLIes") or all capitals, 6 letters or more."""
    letters = re.sub(r"[^A-Za-z]", "", t)
    return _flips(t) >= 2 or (t.isupper() and len(letters) >= 6)


def _top_gap(prev: Line, l: Line) -> float:
    return l.box[1] - prev.box[3]


def _med(lines: list[Line]) -> float:
    return statistics.median(l.height for l in lines) or 1.0


def recurring_labels(body: list[Line]) -> set[str]:
    """Short heading-like lines the book prints three times or more ("GOVERNMENT", "STORY OVERVIEW"): a label of every
    entry's own parts, never the next section's heading."""
    count: dict[str, int] = {}
    for l in body:
        t = l.text.strip()
        if len(t.split()) <= 4 and not prose(t) and _odd_style(t):
            k = squash_text(t)
            count[k] = count.get(k, 0) + 1
    return {k for k, n in count.items() if n >= 3}


def neighbour_absorbed(name: str, lines: list[Line], recurring: frozenset | set = frozenset()) -> str:
    if len(lines) < 5:
        return ""
    own = squash_text(name)
    for k in range(2, len(lines) - 2):
        l = lines[k]
        t = l.text.strip()
        if len(t.split()) > 7 or re.search(r"\d", t) or prose(t) or t.endswith(SENTENCE_END) or not _odd_style(t):
            continue
        if not heading_words(t) or squash_text(t) == own or own in squash_text(t):
            continue
        if squash_text(t) in _OWN_PART_LABELS or squash_text(t) in recurring:
            continue  # a label every entry of the book prints (STORY OVERVIEW, GOVERNMENT): a part of the entry
        prev = lines[k - 1].text.strip()
        if not (prev.endswith(SENTENCE_END) or not prose(prev) or _table_row(prev)):
            continue
        if any(prose(x.text) for x in lines[k + 1:k + 3]):
            return (f"the text holds {t!r} (p{l.page}), set like its own heading, over prose: the next section "
                    "of the same level ran on into it")
    return ""


def quote_leak(lines: list[Line]) -> str:
    if len(lines) < 4:
        return ""
    med = _med(lines)
    for k in range(2, len(lines)):
        prev, l = lines[k - 1], lines[k]
        if prev.page == l.page and prose(prev.text) and not prev.text.rstrip().endswith(SENTENCE_END) \
                and l.text[:1].islower() and _GAP * med <= _top_gap(prev, l) <= 12 * med:
            return (f"{l.text.strip()[:50]!r} (p{l.page}) is printed {_top_gap(prev, l) / med:.1f} lines below the "
                    f"unfinished sentence it interrupts ({prev.text.strip()[-30:]!r}): a pull-quote's line?")
    # an epigraph: a block of one to three lines in capitals or small capitals under the heading, set apart from
    # the paragraph that follows it
    for end in range(2, min(5, len(lines) - 1)):
        a, b = lines[end - 1], lines[end]
        if a.page == b.page and _top_gap(a, b) >= _GAP * med and prose(b.text):
            block = lines[1:end]
            if any(_odd_style(x.text) and len(x.text.split()) >= 4 for x in block):
                return (f"the opening block {' '.join(x.text for x in block)[:60]!r} (p{a.page}) is set in capitals "
                        "and apart from the text: an epigraph or quotation")
            break
    return ""


def figure_label_tail(lines: list[Line]) -> str:
    if len(lines) < 4:
        return ""
    med = _med(lines)
    k = len(lines) - 1
    while k > 1 and lines[k - 1].page == lines[k].page and _top_gap(lines[k - 1], lines[k]) < _GAP * med:
        k -= 1
        if len(lines) - k > 3:
            return ""
    block = lines[k:]
    prev = lines[k - 1]
    if k <= 1 or prev.page != block[0].page or _top_gap(prev, block[0]) < _GAP * med or len(block) > 3:
        return ""
    if any(prose(x.text) for x in block) or _NUMBER.match(block[-1].text.strip()):
        return ""
    if not prose(prev.text) or any(_table_row(x.text) for x in block):
        return ""
    return (f"the text ends on {' '.join(x.text for x in block)[:70]!r} (p{block[0].page}), set "
            f"{_top_gap(prev, block[0]) / med:.1f} lines below the text above it and no prose: a figure's labels?")


def table_cut(lines: list[Line], page_height: dict[int, float]) -> str:
    if len(lines) < 2:
        return ""
    med = _med(lines)
    body = lines[1:]
    widest = {}
    for l in body:
        widest[l.page] = max(widest.get(l.page, 0), l.box[2] - l.box[0])
    for a, b in zip(body, body[1:]):
        if a.page != b.page or a.text.rstrip().endswith(SENTENCE_END) or (b.box[0] > a.box[2] or a.box[0] > b.box[2]):
            continue
        short = all(x.box[2] - x.box[0] < 0.8 * widest[x.page] for x in (a, b))
        if _odd_style(b.text) or not (short or (_table_row(a.text) and _table_row(b.text))) or abs(a.box[0] - b.box[0]) > 0.1 * widest[a.page]:
            continue
        if b.box[1] - a.box[1] >= _HOLE * med * 1.25:
            return (f"the table rows {a.text.strip()[:30]!r} and {b.text.strip()[:30]!r} (p{a.page}) are "
                    f"{(b.box[1] - a.box[1]) / med:.1f} lines apart: a line of the table is missing")
    last = lines[-1]
    h = page_height.get(last.page, 0)
    if len(lines) >= 8 and h and last.box[3] >= _BOTTOM * h and _table_row(last.text) is not None \
            and not last.text.rstrip().endswith(SENTENCE_END) and not prose(last.text) and not _NUMBER.match(last.text):
        return (f"the text ends on the table row {last.text.strip()[:40]!r} at the foot of p{last.page}: the table "
                "may run on to the next page")
    n_prose = sum(prose(x.text) and not _table_row(x.text) for x in body)
    chars = sum(len(x.text) for x in body)
    if n_prose == 0 and len(body) >= 2:
        return "the text holds no prose, only a table: a table's caption read as a rule?"
    if len(body) <= 2 and n_prose == len(body) and body[-1].text.rstrip().endswith(SENTENCE_END) \
            and (chars <= _THIN or _CUE.search(" ".join(x.text for x in body))):
        return f"the text is a single lead-in sentence ({chars} characters): what it introduces is missing"
    return ""


def stray_number_tail(lines: list[Line]) -> str:
    if len(lines) < 3:
        return ""
    last, prev = lines[-1], lines[-2]
    t = last.text.strip()
    if _NUMBER.match(t) and not _table_row(prev.text) and (prev.page != last.page or not _table_row(prev.text)):
        return f"the text ends on the bare number {t!r} (p{last.page}) away from the text: a folio or a figure's tag"
    return ""


def residual_checks(p, page_height: dict[int, float] | None = None, recurring: frozenset | set = frozenset()) -> list[Check]:
    lines = p.lines
    if not lines or p.type == "God":  # a god is one table row's text: these checks are for sections
        return []
    found = (("neighbour-absorbed", neighbour_absorbed(p.name, lines, recurring)),
             ("quote-leak", quote_leak(lines)),
             ("figure-label-tail", figure_label_tail(lines)),
             ("table-cut", table_cut(lines, page_height or {})),
             ("stray-number-tail", stray_number_tail(lines)))
    return [Check(name, False, why) for name, why in found if why]
