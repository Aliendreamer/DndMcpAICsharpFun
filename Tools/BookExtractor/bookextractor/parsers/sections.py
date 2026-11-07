"""Shared section reader: the book's printed heading tree, for the world-type parsers (gods, rules, planes,
factions, locations, lore).

`read_sections(body, ctx)` returns one `Section` per printed heading, in reading order. A section is its heading
line and every text line under it up to its end, its sub-sections included.

- **Text.** Art credits and sidebars are out (`character_body`), as are running heads and feet, page numbers, art
  text and captions (`text_lines`). A chapter title is kept whatever its size.
- **Chapter titles** ("Chapter 2: Guilds of Ravnica", "Appendix A: Conditions", in any OCR casing, or a "CHAPTER 1"
  line over its title) are set at least 1.4 times the median line height (`chapter_titles`). They are no section
  of their own: each is the outermost parent of the sections under it, and every section ends at the next one.
- **Headings** are short title-case or all-caps lines set larger or wider than body text, after a finished
  sentence, a map's caption or set apart, over prose (or over a smaller heading that is over prose). A heading
  wrapped onto a second line ("Cantrips for Sorcerers," / "Warlocks, and Wizards"; "NATioNS OF" / "K HORVAIRE") is
  read as one. An ordinal is a numeral, its OCR letter-digits undone ("Beyond lst Level" -> "Beyond 1st Level").
- **Levels** are told apart per chapter, relative to each other and never by an absolute size, as
  `trap._section_end` does. A heading's size is its height times its average character width (OCR casing noise
  moves the one and the other in opposite directions). A heading's sub-headings are set clearly smaller than it
  (`_SUB_STEP`); with sub-headings, it stops at the first later heading nearer its own size than its widest
  sub-heading's so far (only headings clearly smaller than its own count as sub-headings there), without, at the
  next heading. Its level is one more than the number of headings enclosing it.
- **Ends.** A section ends at the next heading of equal or greater level (`end_reason` "heading") or at a chapter
  title ("chapter"). It is not `complete` when it runs to the end of the text read ("end of text"), when the
  heading it stops at is barely larger than its widest sub-heading or barely over the midpoint it had to reach (and
  clearly smaller than its own heading), so that the stop may be one more sub-heading ("close: ...", naming the
  sizes), or when it holds a heading that nearly
  ended it: not clearly smaller than its own, or barely under that midpoint ("holds ...", ruling W24: a sibling it
  ran on into).
- **Chapter gaps.** Printed chapter numbers run 1, 2, 3 (appendices A, B, C); a jump means a chapter title the OCR
  did not read, somewhere between the two titles read. Every section between them is not `complete` ("chapter gap:
  Chapter 5 title not found (...)") and its outermost parent is "Chapter ? (title not found)", never the chapter
  before the lost title. A tall "CHAPTER" with no number read (MTF chapter 6) is still a chapter title.
- **Stripped text.** `Section.stripped` holds the printed lines inside the section's span that are not its text
  (sidebars, running heads and feet, page numbers, captions, art text), so a check can see what was taken out.
- **The structural net** (`foreign_lines`, ruling W25): the lines of an entity's text the reader would classify as
  a title, a caption or display text that do not name the entity, for the God, Rule and world parsers'
  `text-complete` checks.

Read by the God, Rule and world parsers (Phase 2c).
"""
from __future__ import annotations

import re
from dataclasses import dataclass, field

from ..model import Line
from . import ParseContext
from .charcommon import (_LARGE, SENTENCE_END, caps_heading, char_width, character_body, heading_like,
                         median_height, name_case, set_apart, text_lines, title_case, vertical)

# "Chapter 2: Guilds of Ravnica", "CHAPTER 1:", "CHAPTER1", "APPENDIX A: CONDITIONS" (never "chapter 3." in a
# sentence: the word is capitalised and a bare number is not followed by a full stop or bracket)
_CHAPTER = re.compile(r"^(?P<word>C(?i:hapt[a-z]{2,3})|A(?i:ppendi[a-z]{1,2}))\s*"
                      r"(?P<n>[0-9lIi|O]{1,2}|[A-H])(?![A-Za-z])\s*(?P<p>[:;.)]?)\s*(?P<rest>.*)$")
_BARE_CHAPTER = re.compile(r"^C(?i:hapt[a-z]{2,3})$")  # "CHAPTER" whose number the OCR lost (MTF chapter 6)
_CHAPTER_HEIGHT = 1.4  # a chapter title is set at least this many median line heights tall
_OCR_DIGITS = str.maketrans({"l": "1", "I": "1", "i": "1", "|": "1", "O": "0"})
_MAP_CAPTION = re.compile(r"(?i)^map\s*\d+(?:\.\d+)?\s*:")  # "MAP 3.4: DEMENTLIEU", "MAP3.4: DEMeNTLiEU"
_ABILITY_ROW = re.compile(r"(?i)^str\s+dex\s+con\b")  # a stat block's ability header, not a heading
_ORDINAL = re.compile(r"(?<![A-Za-z])[0-9lIi|O]{1,2}(?:st|nd|rd|th)(?![A-Za-z])")  # "1st", the OCR's "lst", "20th"
_HEAD_SIZE = 1.25  # a heading's size (height x character width) is at least this many times body text's
_SUB_STEP = 0.12  # a sub-heading is set at least this much (relative) smaller than its section's heading
_OPEN_END = {"of", "the", "and", "a", "an", "to", "in", "on", "for", "with", "at", "by", "&"}
_OPEN_DROP = 0.35  # an unfinished heading line's second line may be set this much (relative) smaller or larger
_DISPLAY = 3.0  # a heading set this many times body text's size is a display title (a domain's, a chapter part's)
_DISPLAY_LINES = 4  # ... whose prose may start this many lines below it, after a block of short lines
_CLOSE = 0.06  # a stopping heading within this (relative) of the widest sub-heading leaves the end in doubt


@dataclass
class Section:
    title: str
    level: int
    parents: list[str]  # the printed enclosing headings, outermost first (the chapter title first, when any)
    lines: list[Line]  # the heading line(s), then the text up to the section's end, sub-sections included
    page: int
    complete: bool
    end_reason: str
    size: float = field(default=0.0, repr=False)  # height x character width over the body's, for diagnostics
    # the printed lines inside the section's span that are not its text: sidebars, running heads and feet, page
    # numbers, captions, art text (art credits are not kept); a check can see what the stripping took out
    stripped: list[Line] = field(default_factory=list, repr=False)


def chapter_titles(B: list[Line]) -> list[tuple[list[int], str]]:
    """(the indices of its lines, its printed title) of each chapter title in the lines B, in reading order.

    The title is one line ("Chapter 2: Guilds of Ravnica", set at least 1.4 median line heights tall, or the
    OCR's "CHaPter 4: Personality And Background"), or a "CHAPTER 1" line, of any size, with the title set that
    tall in the lines just under it ("CHARACTER" / "CREATION"; ERLW's reading order puts part of it first). A
    running foot ("CHAPTER 7 | TREASURE", "APPENDIX A: MISCELLANEOUS CREATURES" at the page foot) is set small."""
    med = median_height(B)
    tall = _CHAPTER_HEIGHT * med
    out: list[tuple[list[int], str]] = []
    on_page: dict[int, list[int]] = {}
    for i, l in enumerate(B):
        on_page.setdefault(l.page, []).append(i)
    for i, l in enumerate(B):
        t = l.text.strip()
        m = _CHAPTER.match(t)
        bare = not m and bool(_BARE_CHAPTER.match(t))
        if not (m or bare) or "|" in t or vertical(l.box):
            continue
        if bare:
            rest, word, n, punct = "", "Chapter", "?", ""
        else:
            rest, punct = m.group("rest"), m.group("p")
            word = "Chapter" if m.group("word")[0] == "C" else "Appendix"
            n = m.group("n")
            n = n if word == "Appendix" and n.isalpha() and n.isupper() and n != "I" else n.translate(_OCR_DIGITS)
        if rest:
            if l.height < tall or not (title_case(rest) or caps_heading(rest) or rest.isupper()):
                continue
            out.append(([i], f"{word} {n}: {name_case(rest)}"))
            continue
        if punct in (".", ")"):
            continue
        # the title in the tall lines printed with it: just below it, or beside it and read first (ERLW), wrapped
        # onto lines of its own size ("NATioNS OF" / "K HORVAIRE"), never the first heading under it
        page = [k for k in on_page[l.page] if k != i and B[k].height >= tall and not vertical(B[k].box)
                and re.search(r"[A-Za-z]{3}", B[k].text)]
        near = [k for k in page if l.box[1] - l.height <= B[k].box[1] <= l.box[3] + 2 * l.height]
        if near:
            h0 = max(B[k].height for k in near)
            grow = True
            while grow:
                bottom = max(B[k].box[3] for k in near)
                more = [k for k in page if k not in near and abs(B[k].height - h0) <= 0.1 * h0
                        and B[k].box[1] >= min(B[j].box[1] for j in near) and B[k].box[1] <= bottom + 0.6 * h0]
                near += more
                grow = bool(more)
        if not near and l.height < tall or bare and (not near or l.height < tall):
            continue
        words = " ".join(B[k].text.strip() for k in sorted(near, key=lambda k: (round(B[k].box[1] / l.height),
                                                                                 B[k].box[0])))
        out.append(([i] + near, f"{word} {n}: {name_case(words)}" if words else f"{word} {n}"))
    return out


def display_lines(B: list[Line], title_of: dict) -> list[Line]:
    """The lines set taller than body text's art-text cut (`charcommon._LARGE` times the median line height) that
    read as a section heading the reader never reads: heading words, no part of a chapter title, no headline's
    question or exclamation mark, straight over prose on their page set clearly smaller (ERLW "RACES", p18, 2.2x
    the median, over "Humanoids of all kinds ..."). The text stripper drops them as art text; the world rules doubt
    the top sections after one that are set clearly smaller than it (Task 7 round 1). Reading them as headings was
    measured and rejected: ERLW's newspaper headlines ("UNDER SIEGE", "VAMPIRE KING") read the same way."""
    med = median_height(B)
    out: list[Line] = []
    for i, l in enumerate(B):
        t = l.text.strip()
        # a drop cap ("A", set 6x) is no heading: a word of three letters at least
        if l.height <= _LARGE * med or id(l) in title_of or vertical(l.box) or not _heading_text(t) \
                or t.endswith(("?", "!")) or not re.search(r"[A-Za-z]{3}", t):
            continue
        nxt = B[i + 1] if i + 1 < len(B) else None
        if nxt is not None and nxt.page == l.page and id(nxt) not in title_of and prose(nxt.text) \
                and nxt.height < l.height / _HEAD_SIZE:
            out.append(l)
    return out


def _size(l: Line, med: float) -> float:
    return l.height / med * char_width(l, med)


def _heading_text(t: str) -> bool:
    """A printed heading's words: short, capitalised, title-case or all caps; digits only after the first word
    ("Rank 1: Official"), never a table row ("3 Crime"). An ordinal ("Beyond 1st Level", the OCR's "Beyond lst
    Level") is a numeral, not a lower-case word."""
    t = t.strip()
    if not (0 < len(t.split()) <= 8 and t[:1].isalpha() and t[:1].isupper()) or _ABILITY_ROW.match(t):
        return False
    if t.endswith((".", ",", ";", ":", "-")) or re.match(r"^\S+\d", t):
        return False
    words = _ORDINAL.sub("", t)
    return bool(words.split()) and (title_case(words) or caps_heading(words))


def _fix_ordinals(t: str) -> str:
    """An ordinal's OCR letter-digits undone ("Beyond lst Level" -> "Beyond 1st Level"), as a chapter number's."""
    return _ORDINAL.sub(lambda m: m.group(0)[:-2].translate(_OCR_DIGITS) + m.group(0)[-2:], t)


def prose(t: str) -> bool:
    """Prose, not a heading or a table row: three lower-case words at least."""
    return sum(w[:1].islower() for w in t.split()) >= 3


def _joined(L: list[Line], i: int) -> int:
    """How many lines the heading at L[i] takes: 2 when it wraps ("Cantrips for Sorcerers," / "Warlocks, and
    Wizards"; "The Lady of" / "Pain"), else 1."""
    if i + 1 >= len(L):
        return 1
    a, b = L[i], L[i + 1]
    # a first line ending in a function word or a comma is unfinished: its second line may be set smaller ("The
    # Raven Queen and" / "THE SHADAR-KAI", "Church of the" / "SILVER FLAME")
    open_end = a.text.rstrip().endswith(",") or a.text.split()[-1].lower() in _OPEN_END
    if b.page != a.page or abs(b.height - a.height) > (_OPEN_DROP if open_end else 0.12) * a.height \
            or not 0 < b.box[1] - a.box[1] < 1.8 * a.height:
        return 1
    if abs(b.box[0] - a.box[0]) > 2 * a.height:
        return 1
    tb = b.text.strip()
    if open_end:
        return 2 if tb[:1].isupper() and not prose(tb) else 1
    return 1


def _head_at(L: list[Line], i: int, med: float, body_size: float, titles: dict) -> int:
    """How many lines the heading starting at L[i] takes; 0 when L[i] opens no heading."""
    a = L[i]
    if vertical(a.box):
        return 0
    n = _joined(L, i)
    text = " ".join(L[k].text.strip() for k in range(i, i + n))
    text = re.sub(r"\s*,$", "", text)
    if not _heading_text(text) or _size(a, med) < _HEAD_SIZE * body_size:
        return 0
    # after a finished sentence, a space, a chapter title, or a map's caption (the caption closes the map; the
    # region heading under it is set apart from the map, not from the text: EGW "MAP 3.6: GREYING WILDLANDS" /
    # "GREYING WILDLANDS")
    if i > 0 and not (L[i - 1].text.rstrip().endswith(SENTENCE_END) or set_apart(L[i - 1], a)
                      or id(L[i - 1]) in titles or _MAP_CAPTION.match(L[i - 1].text.strip())):
        return 0
    k = i + n
    if k >= len(L):
        return 0
    # a map's label over its caption ("DEMENTLIEU" / "MAP 3.4: DEMENTLIEU"): art text, no heading
    if _MAP_CAPTION.match(L[k].text.strip()):
        return 0
    # prose under it (an epigraph may open with a line in caps: "OfficeR LaVinia stood Before tHe ENorMous")
    if prose(L[k].text) or k + 1 < len(L) and prose(L[k + 1].text) and not _heading_text(L[k].text):
        return n
    # straight over a smaller heading that is over prose ("Azorius Senate" / "Inside the Senate" / ...)
    nb = _joined(L, k)
    tb = re.sub(r"\s*,$", "", " ".join(L[j].text.strip() for j in range(k, k + nb)))
    if _heading_text(tb) and _size(L[k], med) < _size(a, med) and k + nb < len(L) and prose(L[k + nb].text):
        return n
    # a display title over a block of short lines set far smaller (by `_DISPLAY`), then prose (VRGR "FALKOVNIA" / "Domain
    # Besieged by the Dead" / "Darklord: Vladeska Drakov" / "Genre: Disaster horror" / "Hallmarks: ...")
    if _size(a, med) >= _DISPLAY * body_size:
        block = L[k:k + _DISPLAY_LINES]
        at = next((j for j, l in enumerate(block) if prose(l.text)), None)
        run = [a] + block[:at + 1] if at is not None else []
        # the block is set straight under the title, line after line (a map's label sits over scattered labels)
        if run and all(l.page == a.page and _size(l, med) < _size(a, med) / _DISPLAY for l in run[1:]) and all(
                0 <= y.box[1] - x.box[3] < a.height and not _MAP_CAPTION.match(y.text.strip())
                for x, y in zip(run, run[1:])):
            return n
    return 0


def _ends(sizes: list[float]) -> list[tuple[int | None, float | None, int | None]]:
    """For each of a chapter's headings, (the heading it stops at, or None to run to the chapter's end; the size of
    its widest sub-heading, or None without one; the first heading it holds that nearly ended it, or None).

    As `trap._section_end`: a heading's sub-headings are set clearly smaller than it (by `_SUB_STEP` at least).
    With sub-headings, it stops at the first later heading nearer its own size than the widest sub-heading before
    it; without, at the next heading.

    The widest sub-heading grows only with headings set clearly smaller than the section's own (by `_SUB_STEP`): a
    swallowed sibling must not raise the midpoint and let the next sibling in too (ruling W24). A heading the
    section holds that is not clearly smaller than its own, or that comes within `_CLOSE` under the midpoint it had
    to reach, nearly ended the section: the first such is returned, and the section's end is in doubt (DMG "Healing"
    at 0.97 over sub-headings at 0.61 holds "Rest Variants" at 0.79, the midpoint)."""
    out: list[tuple[int | None, float | None, int | None]] = []
    for k, s in enumerate(sizes):
        if k + 1 == len(sizes):
            out.append((None, None, None))
            continue
        first = sizes[k + 1]
        if (s - first) / s < _SUB_STEP:
            out.append((k + 1, None, None))
            continue
        # the stop is the first later heading nearer its own size than the widest sub-heading so far (a region's
        # first sub-heading may be a small one: EGW "Menagerie Coast" over "Barren Shores", then the larger
        # city headings "Brokenbank" and "Darktow")
        stop, widest, near = None, first, None
        for j in range(k + 1, len(sizes)):
            mid = (s + widest) / 2
            if sizes[j] >= mid:
                stop = j
                break
            if near is None and (sizes[j] >= (1 - _CLOSE) * mid or sizes[j] > (1 - _SUB_STEP) * s):
                near = j
            if sizes[j] <= (1 - _SUB_STEP) * s:  # only a clear sub-heading widens the midpoint (ruling W24)
                widest = max(widest, sizes[j])
        out.append((stop, widest, near))
    return out


def read_sections(body: list[Line], ctx: ParseContext) -> list[Section]:
    if "sections" in ctx.cache:
        return ctx.cache["sections"]
    B = character_body(body, ctx)
    out: list[Section] = []
    if B:
        out = _read(B, ctx)
    ctx.cache["sections"] = out
    return out


def _read(B: list[Line], ctx: ParseContext) -> list[Section]:
    titles = chapter_titles(B)
    title_of = {id(B[k]): t for ks, t in titles for k in ks}
    L = text_lines(B, ctx, keep=lambda l, m: id(l) in title_of)
    if not L:
        return []
    med = median_height(L)
    # body text's size: the median size of the lines long enough to be prose
    sized = sorted(_size(l, med) for l in L if len(l.text) >= 30 and prose(l.text))
    body_size = sized[len(sized) // 2] if sized else 0.4
    # a chapter starts at the first of its title's lines; its title lines are no heading
    starts: dict[str, int] = {}
    for i, l in enumerate(L):
        if id(l) in title_of:
            starts.setdefault(title_of[id(l)] + f"@{l.page}", i)
    chapters = sorted(starts.values())
    chapter_name = {i: k.rsplit("@", 1)[0] for k, i in starts.items()}
    heads: list[tuple[int, int]] = []  # (first line, line count)
    i = 0
    while i < len(L):
        if id(L[i]) in title_of:
            i += 1
            continue
        n = _head_at(L, i, med, body_size, title_of)
        if n:
            heads.append((i, n))
            i += n
        else:
            i += 1
    full: list[Line] = ctx.cache["character_lines"]
    at = {id(l): k for k, l in enumerate(full)}
    kept = {id(l) for l in L}
    gaps = _chapter_gaps([chapter_name[c] for c in chapters])
    out: list[Section] = []
    bounds = [0] + chapters + [len(L)]
    for g, (c0, c1) in enumerate(zip(bounds, bounds[1:])):
        chapter = chapter_name.get(c0) if c0 in chapters else None
        # g - 1: the chapter this group opens with (-1: the text before the first chapter title)
        gap = gaps.get(g - 1)
        if gap:
            chapter = UNKNOWN_CHAPTER
        hs = [(i, n) for i, n in heads if c0 <= i < c1]
        if not hs:
            continue
        sizes = [_size(L[i], med) for i, _ in hs]
        ends = _ends(sizes)
        titles_here = [name_case(_fix_ordinals(re.sub(r"\s*,$", "", " ".join(L[j].text.strip()
                                                                             for j in range(i, i + n)))))
                       for i, n in hs]
        open_: list[int] = []  # the enclosing headings (indices into hs), outermost first
        for k, (i, n) in enumerate(hs):
            open_ = [o for o in open_ if ends[o][0] is None or ends[o][0] > k]
            stop, widest, near = ends[k]
            if stop is not None:
                end, reason, complete = hs[stop][0], "heading", True
                so = sizes[stop]
                mid = (sizes[k] + widest) / 2 if widest is not None else None
                # a stopping heading barely larger than the widest sub-heading, or barely over the midpoint it had
                # to reach while set clearly smaller than the section's own heading, may be one more sub-heading (one
                # set like the section's own heading, within `_SUB_STEP`, is a sibling: SCAG's gods)
                if widest is not None and (so - widest) / so < _CLOSE:
                    complete, reason = False, (f"close: stops at {L[end].text.strip()!r} (size {so:.2f}), barely "
                                               f"larger than its widest sub-heading ({widest:.2f}); its own heading "
                                               f"is {sizes[k]:.2f}")
                elif mid is not None and so < (1 + _CLOSE) * mid and so < (1 - _SUB_STEP) * sizes[k]:
                    complete, reason = False, (f"close: stops at {L[end].text.strip()!r} (size {so:.2f}), barely "
                                               f"over the midpoint ({mid:.2f}) between its own heading "
                                               f"({sizes[k]:.2f}) and its widest sub-heading ({widest:.2f})")
            elif c1 < len(L):
                end, reason, complete = c1, "chapter", True
            else:
                end, reason, complete = len(L), "end of text", False
            if near is not None:
                # a heading it holds that nearly ended it: not clearly smaller than its own heading, or barely under
                # the midpoint it had to reach (ruling W24: DMG "Healing" holding "Rest Variants")
                j, nl = near, L[hs[near][0]]
                complete, reason = False, (f"holds {nl.text.strip()!r} (p{nl.page}, size {sizes[j]:.2f}), not "
                                           f"clearly a sub-heading of its own ({sizes[k]:.2f}) over its widest "
                                           f"sub-heading ({widest:.2f}): a sibling it ran on into? ({reason})")
            if gap:  # a chapter title the OCR lost lies somewhere in this group: no end in it is sure
                complete, reason = False, f"chapter gap: {gap} title not found ({reason})"
            parents = ([chapter] if chapter else []) + [titles_here[o] for o in open_]
            span = full[at[id(L[i])]:at[id(L[end])] if end < len(L) else len(full)]
            out.append(Section(title=titles_here[k], level=len(open_) + 1, parents=parents, lines=L[i:end],
                               page=L[i].page, complete=complete, end_reason=reason, size=sizes[k],
                               stripped=[l for l in span if id(l) not in kept]))
            open_.append(k)
    return out


UNKNOWN_CHAPTER = "Chapter ? (title not found)"


def _number(title: str) -> tuple[str, int | None]:
    """("Chapter" or "Appendix", its number: an appendix letter counts A = 1; None when unprinted or unread)."""
    m = re.match(r"^(Chapter|Appendix) ([^:\s]+)", title)
    if not m:
        return "", None
    n = m.group(2)
    if n.isdigit():
        return m.group(1), int(n)
    if len(n) == 1 and n.isalpha() and n.isupper():
        return m.group(1), ord(n) - ord("A") + 1
    return m.group(1), None


def _chapter_gaps(names: list[str]) -> dict[int, str]:
    """For each chapter group that a lost chapter title may lie in (index into `names`; -1 for the text before the
    first title), the lost title's label ("Chapter 5"). Printed numbers run 1, 2, 3 within chapters and within
    appendices; a jump (4 -> 6, or a first chapter numbered 2) means a title the OCR did not read, somewhere between
    the two titles read. An unnumbered "CHAPTER" counts as the next number."""
    out: dict[int, str] = {}
    last: dict[str, tuple[int, int]] = {}  # word -> (index, number) of the last title of that word
    for k, name in enumerate(names):
        word, n = _number(name)
        if not word:
            continue
        prev = last.get(word)
        expected = prev[1] + 1 if prev else 1
        n = expected if n is None else n
        if n > expected:
            label = f"{word} {expected}" if word == "Chapter" else f"{word} {chr(ord('A') + expected - 1)}"
            for g in range(prev[0] if prev else -1, k):
                out[g] = label
        last[word] = (k, n)
    return out


# helpers shared by the world-type parsers (rules, world sections)
_STEP = re.compile(r"^\d{1,2}\.\s*")  # a numbered step heading: "4. Describe Your Character"
_ART_INSET = 2.0  # an art caption is set in from its column's text edge by this many line heights


def line_key(l: Line) -> tuple:
    """Printed-line identity, as `assemble._line_keys`."""
    return l.page, tuple(round(x, 1) for x in l.box)


def heading_words(t: str) -> bool:
    """Short title-case or caps words, a numbered step's included ("4. Describe Your Character"), in any OCR casing
    ("SkIll VarIants", "TRAINING")."""
    t = _STEP.sub("", t.strip())
    return heading_like(t) and (title_case(t) or caps_heading(t))


def page_column(l: Line, ctx: ParseContext) -> int:
    pg = ctx.pages.get(l.page)
    return int((l.box[0] + l.box[2]) / 2 > (pg.width / 2 if pg else 800))


def drop_art(own: list[Line], ctx: ParseContext) -> list[Line]:
    """Without the art text printed in the column (a handwritten note, a picture's words: XGE "when minions come
    back from"): short lines set well in from the column's text edge, after a wide gap or another such line."""
    edges: dict[tuple[int, int], float] = {}
    for l in own:
        if len(l.text) >= 30 and prose(l.text):
            k = (l.page, page_column(l, ctx))
            edges[k] = min(edges.get(k, l.box[0]), l.box[0])
    out: list[Line] = []
    art = False
    for i, l in enumerate(own):
        edge = edges.get((l.page, page_column(l, ctx)))
        prev = own[i - 1] if i else None
        gap = prev is not None and prev.page == l.page and l.box[1] - prev.box[1] >= 3 * l.height
        inset = edge is not None and l.box[0] - edge > _ART_INSET * l.height and len(l.text.strip()) < 45
        art = i > 0 and inset and (gap or art)
        if not art:
            out.append(l)
    return out


def drop_dangling(own: list[Line], raw: list[Line], heads: int, starts: set[int]) -> list[Line]:
    """Without a table caption whose rows are not the rule's ("ChelDar's Plans" straight over the "Downtime
    Activities" heading; "BUilding A STrongholD" at an activity's end): heading words the reader did not read as a
    heading, that end the rule's text, are followed by a line that is not the next printed one, or are followed by
    a heading."""
    nxt = {id(a): b for a, b in zip(raw, raw[1:])}
    out: list[Line] = []
    for i, l in enumerate(own):
        if i >= heads and id(l) not in starts and heading_words(l.text) and not prose(l.text) and \
                (i + 1 == len(own) or nxt.get(id(l)) is not own[i + 1] or id(own[i + 1]) in starts):
            continue
        out.append(l)
    return out


# ---------- the structural net (ruling W25) ----------
# An entity's text must hold no line the reader would classify as a heading, a sidebar or box title, a caption or
# display text, unless that line names the entity. `foreign_lines` names every such line, so the entity's
# `text-complete` check can fail on it: an unbounded sidebar spliced into a god's prose (SCAG Jergal's "The Legend
# of Knucklebones, Skull Bowling, And the Empty Throne"), an art caption read across a page break ("SYmBol OF HOAR"
# inside "the hand of Hoar ... is given credit"), a display caption (EGW "HER COURT CHART THE DESTINY OF A
# CONTINENT."), or a heading of any size the section reader did not read.
_NET_WORDS = 12  # a title or caption holds this many words at most
_NET_DISPLAY = 1.4  # a line this many times the text's median line height is display text, not body text
_NET_CAPS = 3  # a line of this many words in capitals and none in lower case is a caption or a display line
_LABELLED = re.compile(r"(?i)^(?:variants?|optional\s*rules?)\s*:\s*")  # a rule's printed label before its name


def _title_like(t: str) -> bool:
    """Title words, commas allowed ("The Legend of Tyche and Her Twin Daughters", "SYmBol OF HOAR"): short,
    opening with a capital, no closing full stop or semicolon, no colon (a labelled field), every word capitalised
    but the small ones, fewer than three words in lower case (prose)."""
    t = _LABELLED.sub("", t.strip())  # "Variant: Encumbrance", "Optional Rule: Mad Winds": the name after the label
    words = re.findall(r"[A-Za-z][A-Za-z'’-]*", t)
    # a colon labels a field ("Skill Proficiencies: Insight, Intimidation"), a digit is a table's ("Huge -5"), a
    # full stop inside is a bold lead-in's ("Conflict Shapes the World's History. Powerful"): never a title here
    if not words or len(t.split()) > _NET_WORDS or not t[:1].isupper() or t.endswith((".", ";")) or ":" in t \
            or re.search(r"\d", t) or ". " in t:
        return False
    if prose(t) or not any(len(w) >= 3 for w in words):
        return False
    return all(w[:1].isupper() or w.lower() in _OPEN_END or w.lower() in ("her", "his", "its", "their", "or")
               for w in words)


# name words that name nothing by themselves: a heading holding one is not the entity's for that
_GENERIC = {"spell", "spells", "item", "items", "magic", "magical", "ability", "abilities", "rule", "rules",
            "variant", "option", "optional", "table", "tables", "action", "actions", "other", "new", "using",
            "creating", "running", "playing", "character", "characters", "adventure", "adventures", "the"}


def _names(t: str, names: set[str]) -> bool:
    """The line names the entity: the whole name is in it (squashed), or one of the name's own words is a whole
    word of it; a generic word ("Magic Item Price" holds "Item", "Magic") names nothing by itself."""
    s = squash_text(t)
    words = {squash_text(w) for w in re.findall(r"[A-Za-z][A-Za-z'’-]*", t)}
    return any(len(n) > 3 and n in s and " " in n for n in names) or bool(words & (names - _GENERIC))


def squash_text(t: str) -> str:
    return re.sub(r"[^a-z0-9]", "", t.lower())


def name_words(name: str) -> set[str]:
    """The words a line must hold to name the entity: its name's words of three letters or more, squashed, and the
    whole name ("Avandra, the Change Bringer" -> avandra, change, bringer, ...)."""
    words = {squash_text(w) for w in re.findall(r"[A-Za-z][A-Za-z'’-]*", name) if len(w) >= 3}
    words -= {squash_text(w) for w in _OPEN_END} | {"the"}
    whole = re.sub(r"\s+", " ", re.sub(r"[^A-Za-z0-9 ]", "", name).lower()).strip()
    return words | ({whole} if " " in whole else set()) - {""}


_ROW_LINE = re.compile(r"^(?:\d|d\d)")  # a table row opens with its number, range or die ("1-5 Character ...")


def _table_row(t: str) -> bool:
    """A table's row or header: opens with a number or a die, or holds two numbers or more ("Dungeon 20 sq./min.
    30 sq./min.")."""
    t = t.strip()
    return bool(_ROW_LINE.match(t)) or len(re.findall(r"(?<![A-Za-z])[+-]?\d+", t)) >= 2


def _body_prose(t: str) -> bool:
    return prose(t) and not _table_row(t)


def foreign_lines(own: list[Line], heads: int, names: set[str], starts: set[int] = frozenset()) -> list[str]:
    """Every line of the entity's text (`own`, after its `heads` heading lines) that the reader would classify as
    something other than the entity's body text and that does not name the entity (`names`, from `name_words`):

    - a title: a title-like line set apart from the text flow (at a page or column top, under a space, or after a
      finished sentence), over prose (one more title line may wrap under it), that the section reader did not read
      as a heading (`starts`: those are the caller's to judge) and that does not name the entity: a sidebar or
      box title the stripper could not bound, a caption, a heading set at body size;
    - a float naming the entity: a title-like line set apart by its place alone (a page or column top, a space:
      never merely after a sentence end) that interrupts an unfinished sentence (the line before is prose with no
      sentence end) over prose: a caption read into the text ("SYmBol OF HOAR" between "the hand of Hoar" and "is
      given credit"); a wrapped line of a sentence in the flow ("... in the" / "Azorius Senate") is none;
    - a caption: a line of `_NET_CAPS` words or more in capitals and none in lower case, not a table's header
      (digits), not naming the entity;
    - display text: a line that is no prose, set `_NET_DISPLAY` times the text's median line height or taller,
      not naming the entity (OCR height noise on a prose line is none).

    A title-like line straight under the heading is the entity's subtitle (a deity's epithets: "The Final Scribe,
    the Pitiless One"), never foreign; nor is the entity's own table or list: a title the text names just before it
    ("roll on the Carousing table" / "CAROUSING"), a header over rows (digits), a list after a colon, or the wrapped
    second line of a heading the reader read. Each reason names the line and its page."""
    body = own[heads:]
    if len(body) < 2:
        return []
    med = median_height(body)
    out: list[str] = []
    for k in range(heads, len(own)):
        l, t = own[k], own[k].text.strip()
        prev = own[k - 1] if k else None
        nxt = own[k + 1] if k + 1 < len(own) else None
        what = f"{t[:80]!r} (p{l.page})"
        named = _names(t, names)
        if id(l) in starts or k == heads and _title_like(t):
            continue  # a subtitle straight under the heading is the entity's own (a deity's epithets)
        if l.height >= _NET_DISPLAY * med and not named and len(t) >= 3 and not prose(t):
            out.append(f"{what} is set {l.height / med:.1f}x the text's line height: display text or a caption")
            continue
        words = t.split()
        if (len(words) >= _NET_CAPS and not named and not re.search(r"\d", t)
                and all(w.isupper() for w in words if re.search(r"[A-Za-z]", w))
                and sum(bool(re.search(r"[A-Za-z]", w)) for w in words) >= _NET_CAPS):
            out.append(f"{what} is set in capitals: a caption or display text")
            continue
        if prev is None or nxt is None or not _title_like(t):
            continue
        # the entity's own table or list: a title the text names just before it ("roll on the Carousing table" /
        # "CAROUSING"), a header over rows, a list after a colon, or the wrapped second line of a heading the
        # reader read ("Moving Around Other" / "CREATURES")
        before = squash_text(" ".join(x.text for x in own[max(heads, k - 4):k]))
        if squash_text(t) in before or _table_row(nxt.text) or prev.text.rstrip().endswith(":") \
                or id(prev) in starts:
            continue
        over = _body_prose(nxt.text) or (_title_like(nxt.text) and k + 2 < len(own)
                                         and _body_prose(own[k + 2].text) and not _table_row(nxt.text))
        if not over:
            continue
        placed = prev.page != l.page or prev.box[1] > l.box[1] or set_apart(prev, l)
        unfinished = prose(prev.text) and not prev.text.rstrip().endswith(SENTENCE_END)
        if named:
            if placed and unfinished:
                out.append(f"{what} interrupts an unfinished sentence, set apart from it: a caption read into the "
                           "text")
        elif placed or not unfinished:
            out.append(f"{what} is a title over prose the reader did not read as a heading: a sidebar or box "
                       "title, a caption or a heading set at body size, not naming the entity"
                       + (" (it interrupts an unfinished sentence)" if unfinished else ""))
    return out
