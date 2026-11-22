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
from .parsers import ParseContext, Parsed
from .parsers.charcommon import PAGE_NO, RUNNING_HEAD, prose_entries
from .parsers.sections import drop_art, drop_pull_quotes, heading_words, prose

PAGES_AFTER = 3  # a sidebar is looked for on the pages of a stat block and the ones after it (the block ran on)
MIN_LINE = 30  # a printed line this long is content, not a label or a page number
_CHAPTER_TITLE = re.compile(r"(?i)^(?:ch[a-z]{4,7}|appendix|part)\s+\w+\s*:")  # a title, not the running head "CHAPTER 6 | ..."
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


_PERSON = re.compile(r"[A-Z][a-z']+(?: [A-Z][a-z']+)?")  # a commentator's name; an epigraph's attribution is not
_SIGNATURE = re.compile(r"^[-\u2014\u2013]+\s*(\S.*)$")
_NOTE_SHARE = 0.4  # a page whose "notes" are more than this share of its text is a letter or a foreword, not a margin


@dataclass
class Note:
    lines: list[Line]
    by: str | None
    text: str
    signature: Line | None = None  # the dash line (kept out of `text`)

    @property
    def page(self) -> int:
        return self.lines[0].page

    def as_dict(self) -> dict:
        return {"by": self.by, "text": self.text, "page": self.page}


def _join(texts: list[str]) -> str:
    out = ""
    for t in texts:
        out = out[:-1] + t if out.endswith("-") and t[:1].islower() else (f"{out} {t}" if out else t)
    return out


def _epigraph(run: list[Line]) -> bool:
    """An epigraph or quote box, which is no aside: a quote box, or text set mostly in capitals even through OCR
    casing noise ("ThE teMple IVAS strewn WiTH BODy pARTS"), where an aside's own letters are almost all lower case."""
    if quote_box(run):
        return True
    letters = [c for c in " ".join(l.text for l in run) if c.isalpha()]
    return len(letters) > 8 and sum(c.isupper() for c in letters) >= 0.3 * len(letters)


def margin_notes(lines: list[Line], ctx: ParseContext, held=lambda line: False,
                 left_lines: list[Line] | None = None) -> list[Note]:
    """The writers' margin notes of a page (Bigby's and Diancastra's asides, Mordenkainen's notes): the blocks of tall,
    hand-set prose that the pull-quote rule takes out of the column, each a box of its own. A dash line right under a
    box is its signature (`by`) and is not part of the text, whether or not an entity claimed or glued it in. Epigraphs
    and quote boxes, captions (no prose), and a page that is mostly such text are no notes: the lines of an epigraph or a
    caption are appended to `left_lines`. Inset prose (a chapter's opening, rules text, table cells) is no note."""
    openers = {n for n in {l.page for l in lines}
               if any(_CHAPTER_TITLE.match(x.text.strip()) for x in getattr(ctx.pages.get(n), "lines", ()))}
    # the first line of a page is read as a heading by the pull-quote rule: re-read the page with none, so a note
    # that opens the page (bgg p127) is the whole box
    tall = {id(l) for l in lines} - {id(l) for l in drop_pull_quotes(lines, 0)} - {id(l) for l in lines if l.page in openers}
    art = [l for l in lines if id(l) in tall]
    ids = {id(l) for l in art}
    gone = {id(l) for l in art if held(l)}  # glued into an entity's text: it keeps them, the box's rest is the note
    page_chars = sum(len(l.text) for l in lines)
    if not art or sum(len(l.text) for l in art) > _NOTE_SHARE * max(page_chars, 1):
        return []
    big = sorted(l.height for l in art)[len(art) // 2]
    if sum(l.height >= 0.75 * big for l in lines) > _NOTE_SHARE * len(lines):
        return []  # a page set in large type throughout (a foreword's letter) has no margin
    at = {id(l): i for i, l in enumerate(lines)}
    notes: list[Note] = []
    run: list[Line] = []
    for l in [*lines, None]:
        if l is not None and id(l) in ids and (not run or l.page == run[-1].page):
            run.append(l)
            continue
        full = run
        run = [x for x in run if id(x) not in gone]
        if run:
            if any(prose(x.text) for x in run) and not _epigraph(run):
                k = at[id(full[-1])] + 1  # a signature line set below the box, too small to be read as part of it
                tail: list[Line] = []  # the box's last word, a short fragment the OCR set apart ("kettte")
                while k < len(lines) and len(tail) < 2 and id(lines[k]) not in ids and lines[k].page == run[-1].page \
                        and len(lines[k].text.strip()) <= 12 and not _SIGNATURE.match(lines[k].text.strip()):
                    tail.append(lines[k])
                    k += 1
                sig = None
                if k < len(lines) and id(lines[k]) not in ids and _SIGNATURE.match(lines[k].text.strip()) \
                        and lines[k].page == run[-1].page and len(lines[k].text.strip()) <= 40:
                    sig = lines[k]
                else:
                    tail = []
                run = [*run, *tail]
                body = run
                m = _SIGNATURE.match(run[-1].text.strip())
                by = None
                if sig is not None:
                    by = _SIGNATURE.match(sig.text.strip()).group(1).strip()
                elif m and len(run) > 1:
                    by, body = m.group(1).strip(), run[:-1]
                if by is not None and not _PERSON.fullmatch(by):
                    if left_lines is not None:  # an epigraph's attribution ("Rakshasa maxim", "... of the Emerald Enclave")
                        left_lines += [*run, *([sig] if sig is not None else [])]
                    run = [l] if l is not None and id(l) in ids else []
                    continue
                all_lines = [*run, sig] if sig is not None else run
                notes.append(Note(lines=all_lines, by=by, text=_join([x.text.strip() for x in body]),
                                  signature=(sig or (run[-1] if m and len(run) > 1 else None))))
            elif left_lines is not None:
                left_lines += run
        run = [l] if l is not None and id(l) in ids else []
    return notes


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
        for run in _runs(lines, eligible):
            L = run.lines
            if not any(len(l.text.strip()) >= MIN_LINE and prose(l.text) for l in L):
                if left_out is not None and (quote_box(L) or all(heading_words(l.text) for l in L)):
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
