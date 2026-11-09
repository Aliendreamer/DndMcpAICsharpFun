"""Rule parser: the named rules of the rules chapters, every "Variant:" / "Optional Rule:" section or sidebar, and
every downtime activity, each with its heading-bounded text.

**Rules chapters** are found by their printed chapter titles (`_RULES_CHAPTERS`), never by page number: the PHB's
"Using Ability Scores", "Adventuring", "Combat" and "Spellcasting", the DMG's "Running the Game" and "Dungeon Master's
Workshop", and XGE's and TCE's "Dungeon Master's Tools". A chapter's entry level is its top heading level (the
section reader's level 1). A top heading that names a group of options ("Ability Options", "Adventuring Options",
"Combat Options") holds the rules one level down: its sub-sections are the entry level there.

**Labelled rules**, anywhere in the book:

- a section whose heading opens with "Variant:" or "Optional Rule:" ("Variant: Customizing Ability Scores",
  "OPtional Rule: Mad Winds"); the name is the heading after the label;
- a sidebar whose title opens the same way ("Variant: Equipment Sizes"). The section reader strips sidebars, so
  these are read from the unstripped lines (`ctx.cache["character_lines"]`) and end with the sidebar's box.

`ruleType` is "V" for a printed "Variant" label and "O" for "Optional Rule"; a rule with no printed label has none.

**Downtime activities**, anywhere in the book: the sections one level under a "Downtime Activities" heading ("More
Downtime Activities", "Example Downtime Activities"). A sub-heading naming an activity's complications ("Crime
Complications"), or a table caption read as a heading ("Loot Value" over its rows), stays inside the activity
before it. A heading set like its sibling activities' that the section
reader did not read (PHB "Practicing a Profession", printed in mixed case beside "CRafTInG") starts an activity too:
a short title-case line after a finished sentence, over prose.

**Text.** A rule's text is its section, without the text of any other rule printed inside it (a variant nested in an
entry-level rule, an activity inside "Between Adventures"). It ends before a heading the section reader did not read
when that heading is set at least as tall as the rule's own ("4. Describe Your Character" after the variant), and at
a magic item or stat block printed inside it (the hosted entity's heading). A bold lead-in ("Push, Drag, or Lift.")
is no heading: it stays inside its rule. Lines an earlier type's entity owns (`ctx.cache["claimed_lines"]`, line
keys as `assemble._line_keys`, filled by the assembler) are never a rule's, and a rule whose heading is owned is no
rule.

Check (naming the values involved): `text-complete`: the section ends at a heading or chapter title the reader is
sure of (`Section.complete`; a chapter gap counts as unsure), an activity's end is its container's when it is the
last one, and an entry-level rule is not far longer than its sibling rules (a heading the OCR read as larger than it
is runs on over its siblings). A sidebar ends with its box. Ruling W25: the rule holds no sub-section set like its
own siblings (`world.held_siblings`: PHB "Cover" holding "Damage and Healing") and no line the reader would
classify as a title, a caption or display text that does not name it (`sections.foreign_lines`).
"""
from __future__ import annotations

import re
from dataclasses import dataclass
from statistics import median

from ..checks import Check
from ..model import Line
from . import ParseContext, Parsed
from .boundary_trim import trim_own
from .charcommon import (PAGE_NO, RUNNING_HEAD, SENTENCE_END, caps_heading, character_body, heading_like, like,
                         name_case, prose_entries, set_apart, squash, title_case)
from .item import _is_type_line
from .monster import _is_size_line
from .sections import (Section, drop_art, drop_dangling, fix_acronyms, foreign_lines, heading_words, line_key,
                       name_words, odd_opening, page_column, prose, read_sections)

_RULES_CHAPTERS = ("Using Ability Scores", "Adventuring", "Combat", "Spellcasting", "Running the Game",
                   "Dungeon Master's Workshop", "Dungeon Master's Tools")
# a section that calls ITSELF a rule in its opening lines ("Renown is an optional rule you can use", "With this
# rule, you have the option", "Inspiration is a rule the Dungeon Master can use"): a Rule by the book's own words,
# in any chapter, at any level (Task 7 round 2; the signal is the print, not the 5etools list). A mention of rules
# in general ("In these rules, the dice are ...", "the optional rule from chapter 5", "use these rules of thumb")
# is none: the first corpus pass read PHB "Game Dice", "Size" and "Equipment" as Rules on those
_DECLARES = re.compile(r"(?i)\bthis (?:optional |variant )?rule\b|\bis an? (?:optional |variant )?rule\b")
_DECLARE_LINES = 6  # ... within this many lines under its heading
_CHAPTER_TITLE = re.compile(r"^(?:Chapter|Appendix) [^:]+: (?P<title>.+)$")
# "Variant: Encumbrance", "Variants: Vampire Warriors and Spellcasters", "OPtional Rule: Mad Winds",
# "Optionalrule: Bloodlust" (the OCR joined the words), "Optional Rules: Feywild Magic"
_LABEL = re.compile(r"^(?P<label>variants?|optional\s*rules?)\s*:\s*(?P<name>\S.*)$", re.I)
_DOWNTIME = re.compile(r"downtime\s*activities$", re.I)
_OPTIONS = re.compile(r"options$", re.I)  # "Combat Options", "Abilityoptions" (OCR-joined)
_COMPLICATIONS = re.compile(r"complications$", re.I)
_SIBLING_FACTOR = 3
_STAT_PARTS = {"actions", "reactions", "legendaryactions", "bonusactions"}  # a stat block's parts, never a Rule
_UNREAD_HEIGHT = 0.97  # a heading the reader did not read ends a rule when set at least this tall, relative to
# the rule's own heading (OCR heading heights vary by about 10%: a sub-heading may be set 0.9x as tall)
_TALL = 1.15  # a line this many times the rule's median line height is set clearly taller than its body text
_MAX_PAGES = 3  # a rule printed over more pages than this has no sure end
_ACTIVITY_HEIGHT = 0.9  # an unread activity heading is set at least this tall, relative to its siblings' smallest


def rules_chapter(s: Section) -> bool:
    """The section is in a rules chapter (by its printed chapter title, `_RULES_CHAPTERS`)."""
    m = _CHAPTER_TITLE.match(s.parents[0]) if s.parents else None
    return bool(m) and any(squash(m.group("title")) == squash(c) or like(m.group("title"), c, 90)
                           for c in _RULES_CHAPTERS)


def _named(title: str) -> bool:
    """A heading of words, not OCR debris ("I G 4 N C"): one word of three letters at least."""
    return bool(re.search(r"[A-Za-z]{3}", title))


def _unread_heading(L: list[Line], k: int, min_h: float, starts: set[int], tall: float) -> bool:
    """L[k] is a heading the section reader did not read: heading words set at least `min_h` tall, over prose, after
    a finished sentence, a table's footnote ("*Might involve a rival") or a space; a line set clearly taller than
    body text (`tall`) needs no sentence before it ("SkIll VarIants" under a table's last row). A bold lead-in ends
    in a full stop and runs on into its paragraph: never one."""
    if k == 0 or k + 1 >= len(L) or id(L[k]) in starts:
        return False
    if L[k].height < min_h or not heading_words(L[k].text):
        return False
    prev = L[k - 1]
    if not (L[k].height >= tall or prev.text.rstrip().endswith(SENTENCE_END) or set_apart(prev, L[k])
            or prev.text.lstrip().startswith("*")):
        return False
    return prose(L[k + 1].text)


def _tall(L: list[Line]) -> float:
    return _TALL * median(l.height for l in L)


def _hosted_stop(lines: list[Line]) -> int:
    """Index of the heading of the first magic item or stat block printed inside a rule, else len(lines): a short
    heading over its type line ("Wondrous item, common"), or a name over a size line with Armor Class just below."""
    for i in range(1, len(lines)):
        t = lines[i].text.strip()
        if not heading_like(lines[i - 1].text.strip()):
            continue
        if _is_type_line(t) or (_is_size_line(t) and any(re.match(r"(?i)^armou?r\s*class", l.text.strip())
                                                          for l in lines[i + 1:i + 5])):
            return i - 1
    return len(lines)


@dataclass
class _Cand:
    name: str
    rule_type: str | None
    raw: list[Line]  # the printed span, nested rules included
    heads: int  # how many lines the heading takes
    kind: str  # "label", "entry", "activity", "sidebar"
    complete: bool
    end_reason: str
    group: str = ""  # entry rules: the chapter (and options heading) they are siblings in
    conflict: str = ""  # a heading the rule may end at but the reader nests under it (`_cut_unread`)
    sub: bool = False  # a printed sub-heading of a rules-chapter section: a rule of its own, its parent's text ends at it


def _rule_type(label: str) -> str:
    return "V" if label.lower().startswith("variant") else "O"


def _heads(lines: list[Line], title: str) -> int:
    """How many of the first lines are the heading (a heading may wrap onto a second line)."""
    want, k, got = squash(title), 0, ""
    while k < min(3, len(lines)):
        got += squash(lines[k].text)
        if not got or got not in want and not want.endswith(got):
            break
        k += 1
    return max(k, 1)


def _cut_unread(s: Section, starts: dict[int, int], rule_starts: set[int]) -> tuple[list[Line], bool, str, str]:
    """(the section's lines up to the first heading it may end at; whether its end is sure; the end's reason; a
    conflict: a heading that may end it but that the reader nests under it, "" when none).

    A rule ends at (a) a heading the section reader placed at the rule's own level or shallower, or (b) a heading the
    reader did not read, set at least `_UNREAD_HEIGHT` as tall as the rule's own, over prose ("4. Describe Your
    Character" after the variant, "SkIll VarIants" under PROFICIENCY DICE's table). A heading the reader nests
    under the rule is a sub-heading and stays in, however tall (OCR heights vary: "HORROR" under FEAR AND HORROR);
    when one is set as tall as that, the evidence conflicts and the text is kept whole, never silently cut: the
    conflict fails `text-complete` naming it. Another rule's heading inside the section is no end: that rule's lines
    are taken out of this one."""
    L = s.lines
    h = _heads(L, s.title)
    min_h = _UNREAD_HEIGHT * max(l.height for l in L[:h])
    tall = _tall(L)
    conflict = ""
    for k in range(h, len(L)):
        if id(L[k]) in rule_starts:
            continue
        level = starts.get(id(L[k]))
        if level is not None:
            if level <= s.level:
                return L[:k], True, f"heading {L[k].text.strip()!r}", conflict
            if not conflict and L[k].height >= min_h and heading_words(L[k].text) and k + 1 < len(L) \
                    and prose(L[k + 1].text):
                conflict = (f"{L[k].text.strip()!r} (p{L[k].page}) is set as tall as the rule's own heading "
                            f"({L[k].height:.1f} vs {min_h / _UNREAD_HEIGHT:.1f}) but the reader nests it under it")
            continue
        if _unread_heading(L, k, min_h, set(starts), tall):
            return L[:k], True, f"heading {L[k].text.strip()!r}", conflict
    return L, s.complete, s.end_reason, conflict


def _caption(s: Section) -> bool:
    """The section is a table's caption read as a heading ("Loot Value" over "DC Value" / "10 50 gp, ..."): no
    prose in the two lines under it."""
    h = _heads(s.lines, s.title)
    return not any(prose(l.text) for l in s.lines[h:h + 2])


def _activities(c: Section, sections: list[Section], starts: set[int]) -> list[_Cand]:
    """The downtime activities under the container section `c`: its sub-sections one level down, and the unread
    headings set like them. A complications sub-heading stays in the activity before it; a nested container
    ("More Downtime Activities") is a boundary, its own activities read from it."""
    L = c.lines
    at = {id(l): k for k, l in enumerate(L)}
    kids = [s for s in sections if s.level == c.level + 1 and s.parents[-1:] == [c.title] and id(s.lines[0]) in at]
    bounds: list[tuple[int, str | None]] = []  # (line index in L, activity name or None for a nested container)
    for s in kids:
        if _COMPLICATIONS.search(s.title) or _caption(s):
            continue
        bounds.append((at[id(s.lines[0])], None if _DOWNTIME.search(s.title) else s.title))
    heights = [s.lines[0].height for s in kids if not (_COMPLICATIONS.search(s.title) or _caption(s))]
    min_h = _ACTIVITY_HEIGHT * (min(heights) if heights else L[0].height)
    # a heading set like its sibling activities that the reader nested under one of them (PHB "Practicing a
    # Profession" under "CRafTInG": OCR casing changed the sizes)
    depth = len(c.parents) + 1
    for s in sections:
        if (s.level > c.level + 1 and s.parents[:depth] == c.parents + [c.title] and id(s.lines[0]) in at
                and s.lines[0].height >= min_h and not _COMPLICATIONS.search(s.title) and not _caption(s)
                and not _DOWNTIME.search(s.title) and all(id(s.lines[0]) != id(o.lines[0]) for o in kids)
                and not any(_DOWNTIME.search(p) for p in s.parents[depth:])):
            bounds.append((at[id(s.lines[0])], s.title))
    taken = {k for k, _ in bounds}
    tall = _tall(L)
    for k in range(1, len(L)):
        if k not in taken and _unread_heading(L, k, min_h, starts, tall):
            bounds.append((k, name_case(L[k].text.strip())))
    bounds.sort()
    out: list[_Cand] = []
    for n, (k, name) in enumerate(bounds):
        end = bounds[n + 1][0] if n + 1 < len(bounds) else len(L)
        if name is None or not _named(name):
            continue
        last = n + 1 == len(bounds)
        out.append(_Cand(name=name, rule_type=None, raw=L[k:end], heads=_heads(L[k:end], name), kind="activity",
                         complete=c.complete if last else True,
                         end_reason=c.end_reason if last else "heading"))
    return out


# a running head or foot the OCR joined ("APPENDIXB:NONPLAYER CHARACTERS", "CHAPTER 7 | TREASURE") and a page number
# with OCR digit noise ("3I1")
_RUNNING = re.compile(r"(?i)^(\d+\s*)?(chapter|part|appendix)\s*[0-9A-Za-z]{1,2}\s*[:|]")
_PAGE_NOISE = re.compile(r"^(?=.*\d)[0-9Il|O]{1,4}\W?$")
_BOX_GAP = 1.8  # a box ends at a vertical gap this many line heights tall (line tops) after a finished sentence
_BOX_HEIGHT = 1.4  # ... or at a line set this many times taller than the box's text (a quote or art below it)


def _noise(l: Line) -> bool:
    t = l.text.strip()
    return bool(RUNNING_HEAD.match(t) or _RUNNING.match(t) or PAGE_NO.fullmatch(t) or _PAGE_NOISE.fullmatch(t))


def _sidebars(ctx: ParseContext) -> list[_Cand]:
    """The "Variant:" / "Optional Rule:" sidebars: a stripped line opening with the label, and the stripped lines
    after it in its column and on its page (the box), up to a vertical gap, a line set much taller (a quote box or
    art text under it: MM "Sonnet of a Naughty Satyr") or a column jump; running heads and feet and page numbers
    out. A box that runs to its page's last line without finishing a sentence may go on overleaf: its end is not
    sure."""
    full: list[Line] = ctx.cache["character_lines"]
    kept = {id(l) for l in ctx.cache["character_body"]}
    out: list[_Cand] = []
    i = 0
    while i < len(full):
        l = full[i]
        m = _LABEL.match(l.text.strip())
        if id(l) in kept or not m or len(l.text.split()) > 10 or (i and id(full[i - 1]) not in kept
                                                                   and full[i - 1].page == l.page):
            i += 1
            continue
        k = i + 1
        while k < len(full) and id(full[k]) not in kept and full[k].page == l.page:
            k += 1
        run = [x for x in full[i:k] if not _noise(x)]
        box = run[:1]
        body_h = median(x.height for x in run[1:]) if len(run) > 1 else l.height
        reason = "sidebar box"
        for x in run[1:]:
            prev = box[-1]
            if page_column(x, ctx) != page_column(l, ctx):
                reason = "sidebar box (column jump)"
                break
            gap = (x.box[1] - prev.box[1]) / max(prev.height, body_h)
            # a line the OCR lost inside a sentence leaves a gap too: only a finished sentence, or a wide gap, ends
            if x.box[1] < prev.box[1] or gap > _BOX_GAP and prev.text.rstrip().endswith(SENTENCE_END) \
                    or gap > 2 * _BOX_GAP:
                reason = f"sidebar box (gap before {x.text.strip()[:30]!r})"
                break
            if x.height > _BOX_HEIGHT * body_h:
                reason = f"sidebar box (taller line {x.text.strip()[:30]!r})"
                break
            box.append(x)
        complete = True
        page_lines = [x for x in full if x.page == l.page and page_column(x, ctx) == page_column(l, ctx) and not _noise(x)]
        if len(box) == len(run) and page_lines and page_lines[-1] is box[-1] \
                and not box[-1].text.rstrip().endswith(SENTENCE_END):
            complete, reason = False, (f"the box runs to the foot of p{l.page} mid-sentence "
                                       f"({box[-1].text.strip()[-30:]!r}): it may go on overleaf")
        out.append(_Cand(name=name_case(m.group("name").strip()), rule_type=_rule_type(m.group("label")), raw=box,
                         heads=1, kind="sidebar", complete=complete, end_reason=reason))
        i = k
    return out


def _candidates(sections: list[Section], ctx: ParseContext) -> list[_Cand]:
    starts = {id(s.lines[0]) for s in sections}
    levels = {id(s.lines[0]): s.level for s in sections}
    out: list[_Cand] = []
    options = {id(s) for s in sections if s.level == 1 and rules_chapter(s) and _OPTIONS.search(s.title)}
    # the headings that open a rule of their own: labelled sections and downtime containers' activities
    rule_starts = {id(s.lines[0]) for s in sections if _LABEL.match(s.title)}
    for s in sections:
        if _DOWNTIME.search(s.title):
            rule_starts |= {id(c.raw[0]) for c in _activities(s, sections, starts)} | {id(s.lines[0])}
    for s in sections:
        m = _LABEL.match(s.title)
        if m:
            name = name_case(m.group("name").strip())
            raw, complete, reason, conflict = _cut_unread(s, levels, rule_starts - {id(s.lines[0])})
            out.append(_Cand(name=name, rule_type=_rule_type(m.group("label")), raw=raw, heads=_heads(raw, s.title),
                             kind="label", complete=complete, end_reason=reason, conflict=conflict))
            continue
        if _DOWNTIME.search(s.title):
            out += _activities(s, sections, starts)
        if not _named(s.title) or id(s) in options:
            continue
        group = None
        if rules_chapter(s) and s.level == 1:
            group = s.parents[0]
        elif rules_chapter(s) and s.level == 2:
            parent = next((p for p in sections if id(p) in options and p.parents == s.parents[:1]
                           and p.title == s.parents[-1] and id(s.lines[0]) in {id(x) for x in p.lines}), None)
            group = f"{s.parents[0]} > {parent.title}" if parent else None
        if group is None:
            h = _heads(s.lines, s.title)
            if any(_DECLARES.search(l.text) for l in s.lines[h:h + _DECLARE_LINES]):
                group = (s.parents[0] if s.parents else "") + " (declared)"
        sub = False
        if group is None and rules_chapter(s) and not _COMPLICATIONS.search(s.title) and not _caption(s) \
                and squash(s.title) not in _STAT_PARTS:
            sub = True
            # a printed sub-heading in a rules chapter: a Rule of its own (Task 7 round 4); the rule above it holds
            # only its lead. Its siblings are the sub-headings printed under the same parent heading
            group = " > ".join(s.parents)
        if group is None:
            continue
        raw, complete, reason, conflict = _cut_unread(s, levels, rule_starts - {id(s.lines[0])})
        out.append(_Cand(name=s.title, rule_type=None, raw=raw, heads=_heads(raw, s.title), kind="entry",
                         complete=complete, end_reason=reason, group=group, conflict=conflict, sub=sub))
    out += _sidebars(ctx)
    # one rule per printed heading: a labelled section beats an activity beats an entry-level section
    rank = {"label": 0, "sidebar": 0, "activity": 1, "entry": 2}
    best: dict[int, _Cand] = {}
    for c in out:
        k = id(c.raw[0])
        if k not in best or rank[c.kind] < rank[best[k].kind]:
            best[k] = c
    return [c for c in out if best[id(c.raw[0])] is c]


def _drop_floats(own: list[Line], heads: int, ctx: ParseContext) -> list[Line]:
    """Without a table printed in another column across the rule's sentence (PHB's "Ability Scores and Modifiers"
    table at the head of the column the variant's last sentence goes on in): a run of lines that are no prose, in
    another column than the prose before it, with that sentence going on (in lower case) after it."""
    def place(l: Line) -> tuple[int, int]:
        return l.page, page_column(l, ctx)

    drop: set[int] = set()
    k = heads
    while k < len(own):
        if prose(own[k].text):
            k += 1
            continue
        j = k
        while j < len(own) and not prose(own[j].text) and place(own[j]) == place(own[k]):
            j += 1
        before = next((own[i] for i in range(k - 1, heads - 1, -1) if prose(own[i].text)), None)
        after = own[j] if j < len(own) else None
        if before is not None and after is not None and place(before) != place(own[k]) \
                and place(after) == place(own[k]) and after.text.strip()[:1].islower() \
                and not before.text.rstrip().endswith(SENTENCE_END):
            drop.update(range(k, j))
        k = j
    return [l for i, l in enumerate(own) if i not in drop]


def _text_check(c: _Cand, own: list[Line], sizes: dict[str, list[int]], heads: int,
                rule_starts: set[int], held: list = (), starts: set[int] = frozenset(),
                claimed_names: frozenset | set = frozenset()) -> Check:
    if not c.complete:
        return Check("text-complete", False, f"rule {c.name!r} (p{c.raw[0].page}) has no sure end: {c.end_reason}")
    if c.conflict:
        return Check("text-complete", False, f"rule {c.name!r} (p{c.raw[0].page}) may end before {c.conflict}")
    if odd := odd_opening(c.name, own, heads, claimed_names):
        return Check("text-complete", False, f"rule {c.name!r} (p{c.raw[0].page}): {odd}")
    pages = own[-1].page - own[0].page + 1
    if pages > _MAX_PAGES:
        return Check("text-complete", False, f"rule {c.name!r} runs over {pages} pages (p{own[0].page}-"
                     f"p{own[-1].page}), more than {_MAX_PAGES}")
    head_h = max(l.height for l in own[:heads])
    tall = _tall(own)
    for k in range(heads, len(own) - 1):
        l = own[k]
        if id(l) not in rule_starts and l.height >= max(head_h, tall) and heading_words(l.text) \
                and prose(own[k + 1].text):
            return Check("text-complete", False, f"rule {c.name!r} (p{c.raw[0].page}) still holds the heading "
                         f"{l.text.strip()!r} (p{l.page}, height {l.height:.1f} >= its own {head_h:.1f})")
    sib = sizes.get(c.group, []) if c.kind == "entry" else []
    others = list(sib)
    if len(own) in others:
        others.remove(len(own))
    if len(others) >= 2 and len(own) > _SIBLING_FACTOR * max(median(others), 10):
        return Check("text-complete", False, f"rule {c.name!r} (p{c.raw[0].page}) runs {len(own)} lines, over "
                     f"{_SIBLING_FACTOR}x its {len(others)} sibling rules' median of {median(others):g}")
    # ruling W25: a held sub-section set like its parent's siblings (PHB "Cover" holding "Damage and Healing"), or
    # a line the reader classifies as a title, caption or display text not naming the rule
    if held:
        return Check("text-complete", False, f"rule {c.name!r} (p{c.raw[0].page}) holds {held[0].title!r} "
                     f"(p{held[0].page}, size {held[0].size:.2f}), a sub-section set like its parent's siblings: a "
                     "sibling it ran on into")
    if foreign := foreign_lines(own, heads, name_words(c.name), starts | rule_starts):
        return Check("text-complete", False, f"rule {c.name!r} (p{c.raw[0].page}) holds {len(foreign)} line(s) "
                     "that are no body text of its own: " + "; ".join(foreign[:3]))
    return Check("text-complete", True, "ends with its sidebar box" if c.kind == "sidebar" else "")


def parse_rules(body: list[Line], ctx: ParseContext) -> list[Parsed]:
    """One Rule per rules-chapter entry-level section, labelled variant or optional rule (section or sidebar), and
    downtime activity, in reading order."""
    sections = read_sections(body, ctx)
    if not sections and not body:
        return []
    character_body(body, ctx)  # the unstripped and stripped lines, for the sidebars
    cands = _candidates(sections, ctx)
    claimed: set = ctx.cache["claimed_lines"]  # required: the assembler fills it (an empty set when none)
    full = ctx.cache["character_lines"]
    order = {id(l): k for k, l in enumerate(full)}
    cands.sort(key=lambda c: order.get(id(c.raw[0]), 0))
    spans = [{id(l) for l in c.raw} for c in cands]
    rule_starts = {id(c.raw[0]) for c in cands}
    starts = {id(x.lines[0]) for x in sections}
    owned: list[tuple[_Cand, list[Line]]] = []
    held_lines: dict[int, list[Line]] = {}
    for i, c in enumerate(cands):
        # the lines of every other rule that starts inside this one are that rule's
        nested = {x for j, d in enumerate(cands) if j != i and id(d.raw[0]) in spans[i] for x in spans[j]}
        own = [l for l in c.raw if id(l) not in nested]
        own = own[:_hosted_stop(own)]
        if not own or line_key(own[0]) in claimed:
            continue
        own = [l for l in own if line_key(l) not in claimed]
        own = drop_dangling(drop_art(_drop_floats(own, c.heads, ctx), ctx), c.raw, c.heads, starts)
        if len(own) <= c.heads:
            continue
        own = trim_own(own, c.name, c.heads, starts,
                       {x.title for x in sections if x.title != c.name} | {d.name for d in cands if d is not c}, ctx)
        # the lines of the printed sub-headings taken out of this rule: the doubts about a heading inside it
        # (a sibling the reader nested) are still raised over them, as before they were rules of their own
        gone = {id(l) for j, d in enumerate(cands) if j != i and d.sub and id(d.raw[0]) in spans[i] for l in d.raw}
        mine = {id(o) for o in own}
        held_lines[id(c)] = [l for l in c.raw if id(l) in gone or id(l) in mine]
        owned.append((c, own))
    sizes: dict[str, list[int]] = {}
    for c, own in owned:
        if c.kind == "entry":
            sizes.setdefault(c.group, []).append(len(own))
    from .world import held_siblings  # the world module imports this one
    section_of = {id(s.lines[0]): s for s in sections}
    out: list[Parsed] = []
    for c, own in owned:
        heads = min(c.heads, len(own))
        entries = prose_entries(own, heads, len(own))
        fields: dict = {"entries": entries}
        if c.rule_type:
            fields["ruleType"] = c.rule_type
        head = " ".join(l.text.strip() for l in own[:heads])
        flat = [x if isinstance(x, str) else "\n".join(x.get("items", [])) for x in entries]
        p = Parsed(type="Rule", name=fix_acronyms(c.name), fields=fields, text="\n".join([head] + flat), lines=own)
        sec = section_of.get(id(c.raw[0]))
        # a complications sub-heading is an activity's own part (`_activities`), however it is set
        held = [h for h in held_siblings(sec, sections, held_lines[id(c)]) if not _COMPLICATIONS.search(h.title)] if sec else []
        p.checks = [_text_check(c, own, sizes, heads, rule_starts, held, starts)]
        out.append(p)
    return out
