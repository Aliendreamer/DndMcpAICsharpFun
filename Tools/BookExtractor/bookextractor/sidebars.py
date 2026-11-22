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
from .parsers.sections import _table_row, drop_art, heading_words, prose

PAGES_AFTER = 3  # a sidebar is looked for on the pages of a stat block and the ones after it (the block ran on)
MIN_LINE = 30  # a printed line this long is content, not a label or a page number
_CHAPTER_TITLE = re.compile(r"(?i)^(?:chapter|appendix|part)\s+\w+\s*:")  # a title, not the running head "CHAPTER 6 | ..."
_FRAGMENT = 120  # a run this short beside a margin note is a stray line of the note
_QUOTE_START = ("\"", "“", "‘", "—", "–")


@dataclass
class _Run:
    lines: list[Line]


def quote_box(lines: list[Line]) -> bool:
    """A headingless quote box or epigraph: opens with a quotation mark or a dash, ends in a closing quote, or is set
    in capitals (small-caps OCR: "ADVENTURERS LOYAL ..."), with no heading over it. The capitals test never fires on
    a run that goes on into lower-case prose: a chapter's drop-cap opening is set in small caps too."""
    text = " ".join(l.text.strip() for l in lines)
    if text.startswith(_QUOTE_START) or text.endswith(("\u201d", "\"")):
        return True
    if any(prose(l.text) for l in lines):
        return False
    letters = [c for c in text if c.isalpha()]
    return len(letters) > 8 and sum(c.isupper() for c in letters) >= 0.35 * len(letters)


def _name(L: list[Line], heads: int, previous_name) -> str:
    """The box's own heading, else its first line when that opens a sentence, else "<previous entity> sidebar"."""
    first = L[0].text.strip()
    if heads:
        return first
    if first[:1].isupper():
        return " ".join(re.sub(r"[.,;:!?\"]+$", "", first).split()[:8])
    return f"{previous_name(L[0])} sidebar"


def left_out_art(lines: list[Line], ctx: ParseContext) -> list[Line]:
    """The lines `drop_art` takes out that are art or a margin note: set taller than the text (a handwritten note), or
    no prose (a map label, a picture's words). A prose line set in from the edge after a gap is a chapter's drop-cap
    opening, not art: it is no exemption."""
    kept = {id(l) for l in drop_art(lines, ctx)}
    openers = {n for n in {l.page for l in lines}  # a chapter's first page: its text is the chapter's, not art
               if any(_CHAPTER_TITLE.match(x.text.strip()) for x in getattr(ctx.pages.get(n), "lines", ()))}
    dropped = [i for i, l in enumerate(lines) if id(l) not in kept and l.page not in openers]
    out: list[Line] = []
    i = 0
    while i < len(dropped):
        j = i
        while j + 1 < len(dropped) and dropped[j + 1] == dropped[j] + 1:
            j += 1
        run = [lines[k] for k in dropped[i:j + 1]]
        nxt = lines[dropped[j] + 1] if dropped[j] + 1 < len(lines) else None
        # a run set in from the edge whose last line stops mid-sentence and which the kept text carries on in lower
        # case, directly beneath and out at the margin, is a paragraph's drop-cap opening (it flows on), not a note
        flows = (nxt is not None and id(nxt) in kept and nxt.page == run[-1].page and nxt.text.strip()[:1].islower()
                 and not run[-1].text.rstrip().endswith((".", "!", "?", "\"", "\u201d")) and any(prose(l.text) for l in run)
                 and 0 <= nxt.box[1] - run[-1].box[3] <= 1.2 * nxt.height and nxt.box[0] < run[-1].box[0] - 1)
        if not flows:
            out += run
        i = j + 1
    return out


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
        near = any(number - k in pages_with_blocks for k in range(PAGES_AFTER + 1))
        art = left_out_art(lines, ctx)  # art text and margin notes: left out on purpose
        recognised = {id(l) for l in lines} - {id(l) for l in art}
        if left_out is not None:
            left_out += [(number, l.text.strip()) for l in art]
        eligible = {id(l) for l in lines
                    if id(l) in recognised and key(l) not in claimed and len(l.text.strip()) >= 3
                    and not PAGE_NO.fullmatch(l.text.strip()) and not RUNNING_HEAD.match(l.text.strip())}
        at = {id(l): i for i, l in enumerate(lines)}
        table_page = is_table_page(pages_by_number[number])
        for run in _runs(lines, eligible):
            L = run.lines
            if not any(len(l.text.strip()) >= MIN_LINE and prose(l.text) for l in L):
                if left_out is not None and (quote_box(L) or not table_page and not any(_table_row(l.text) for l in L)):
                    left_out += [(l.page, l.text.strip()) for l in L]  # a label, a caption: not a table's cells
                continue
            before, after = at[id(L[0])] - 1, at[id(L[-1])] + 1
            beside_note = any(0 <= j < len(lines) and id(lines[j]) not in recognised for j in (before, after))
            if beside_note and sum(len(l.text.strip()) for l in L) < _FRAGMENT and not (len(L) > 1 and heading_words(L[0].text) and not prose(L[0].text)):
                if left_out is not None:
                    left_out += [(l.page, l.text.strip()) for l in L]  # a stray line of the margin note beside it
                continue
            heads = 1 if len(L) > 1 and heading_words(L[0].text) and not prose(L[0].text) else 0
            if not heads and quote_box(L):
                if left_out is not None:
                    left_out += [(l.page, l.text.strip()) for l in L]
                continue
            if not near:
                continue  # (only runs after a stat block are emitted)
            name = _name(L, heads, previous_name)
            entries = prose_entries(L, heads, len(L))
            flat = [x if isinstance(x, str) else "\n".join(x.get("items", [])) for x in entries]
            text = "\n".join(([L[0].text.strip()] if heads else []) + flat)
            why = ("text printed after a stat block that no entity holds (a boxed sidebar or lore the block's end "
                   f"rules cut off, p{L[0].page}): kept for review, " + ("under its own heading" if heads
                                                                        else "with no heading of its own"))
            out.append(Parsed(type="Lore", name=name, fields={"entries": entries}, text=text, lines=L,
                              checks=[Check("sidebar-detached", False, why)]))
    return out
