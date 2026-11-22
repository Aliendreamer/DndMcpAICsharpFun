"""Text no entity holds, printed after a stat block: a boxed sidebar the section reader strips, lore a stop rule
(`_starts_next_entity`, `_running_foot`, the interleave cut, the second-header break) cut off the block's tail.

The stat block used to swallow it by accident. Kept now as a Lore entity of its own, failing `sidebar-detached` so it
goes to review: never dropped in silence, never glued back into the block. A headingless quote box or epigraph (the
design's D3 #5: left out) and the art text `drop_art` recognises stay out."""
from __future__ import annotations

import re
from dataclasses import dataclass

from .checks import Check
from .model import Line
from .pages import is_table_page
from .parsers import ParseContext, Parsed
from .parsers.charcommon import PAGE_NO, RUNNING_HEAD, prose_entries
from .parsers.sections import drop_art, heading_words, prose

PAGES_AFTER = 3  # a sidebar is looked for on the pages of a stat block and the ones after it (the block ran on)
MIN_LINE = 30  # a printed line this long is content, not a label or a page number
_QUOTE_START = ("\"", "“", "‘", "—", "–")


@dataclass
class _Run:
    lines: list[Line]


def quote_box(lines: list[Line]) -> bool:
    """A headingless quote box or epigraph: opens with a quotation mark or a dash, ends in a closing quote, or is set
    in capitals (small-caps OCR: "ADVENTURERS LOYAL ..."), with no heading over it."""
    text = " ".join(l.text.strip() for l in lines)
    if text.startswith(_QUOTE_START) or text.endswith(("”", "\"")):
        return True
    letters = [c for c in text if c.isalpha()]
    return len(letters) > 8 and sum(c.isupper() for c in letters) >= 0.35 * len(letters)


def _runs(lines: list[Line], eligible: set[int]) -> list[_Run]:
    runs: list[_Run] = []
    cur: list[Line] = []
    for l in lines:
        if id(l) in eligible:
            cur.append(l)
        elif cur:
            runs.append(_Run(cur))
            cur = []
    if cur:
        runs.append(_Run(cur))
    return runs


def detached_sidebars(ordered: dict[int, list[Line]], pages_with_blocks: set[int], pages_by_number: dict,
                      ctx: ParseContext, claimed: set, key, previous_name,
                      left_out: list | None = None) -> list[Parsed]:
    """One Lore per run of unclaimed content lines on a page at or after a stat block (same page or the next).

    `key(line)` is the printed-line identity the entities claim by; `previous_name(line)` names the entity printed
    before a line, for a sidebar with no heading of its own ("<name> sidebar")."""
    out: list[Parsed] = []
    for number, lines in ordered.items():
        if not any(number - k in pages_with_blocks for k in range(PAGES_AFTER + 1)):
            continue
        page = pages_by_number[number]
        if is_table_page(page):
            continue
        recognised = {id(l) for l in drop_art(lines, ctx)}  # art text and margin pull-quotes: left out on purpose
        if left_out is not None:
            left_out += [(number, l.text.strip()) for l in lines if id(l) not in recognised]
        eligible = {id(l) for l in lines
                    if id(l) in recognised and key(l) not in claimed and len(l.text.strip()) >= 3
                    and not PAGE_NO.fullmatch(l.text.strip()) and not RUNNING_HEAD.match(l.text.strip())}
        for run in _runs(lines, eligible):
            L = run.lines
            if not any(len(l.text.strip()) >= MIN_LINE and re.search(r"[a-z]{2}", l.text) for l in L):
                continue  # a label, a figure caption, a table's cell
            heads = 1 if len(L) > 1 and heading_words(L[0].text) and not prose(L[0].text) else 0
            if not heads and quote_box(L):
                if left_out is not None:
                    left_out += [(l.page, l.text.strip()) for l in L]
                continue
            name = L[0].text.strip() if heads else f"{previous_name(L[0])} sidebar"
            entries = prose_entries(L, heads, len(L))
            flat = [x if isinstance(x, str) else "\n".join(x.get("items", [])) for x in entries]
            text = "\n".join(([L[0].text.strip()] if heads else []) + flat)
            why = ("text printed after a stat block that no entity holds (a boxed sidebar or lore the block's end "
                   f"rules cut off, p{L[0].page}): kept for review, " + ("under its own heading" if heads
                                                                        else "with no heading of its own"))
            out.append(Parsed(type="Lore", name=name, fields={"entries": entries}, text=text, lines=L,
                              checks=[Check("sidebar-detached", False, why)]))
    return out
