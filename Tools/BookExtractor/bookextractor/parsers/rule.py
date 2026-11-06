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
is runs on over its siblings). A sidebar ends with its box.

Not registered in the assembler yet (Phase 2c ruling W1): Task 6 does that.
"""
from __future__ import annotations

import re
from dataclasses import dataclass
from statistics import median

from ..checks import Check
from ..model import Line
from . import ParseContext, Parsed
from .charcommon import (PAGE_NO, RUNNING_HEAD, SENTENCE_END, caps_heading, character_body, heading_like, like,
                         name_case, prose_entries, set_apart, squash, title_case)
from .item import _is_type_line
from .monster import _is_size_line
from .sections import Section, read_sections

_RULES_CHAPTERS = ("Using Ability Scores", "Adventuring", "Combat", "Spellcasting", "Running the Game",
                   "Dungeon Master's Workshop", "Dungeon Master's Tools")
_CHAPTER_TITLE = re.compile(r"^(?:Chapter|Appendix) [^:]+: (?P<title>.+)$")
# "Variant: Encumbrance", "Variants: Vampire Warriors and Spellcasters", "OPtional Rule: Mad Winds",
# "Optionalrule: Bloodlust" (the OCR joined the words), "Optional Rules: Feywild Magic"
_LABEL = re.compile(r"^(?P<label>variants?|optional\s*rules?)\s*:\s*(?P<name>\S.*)$", re.I)
_DOWNTIME = re.compile(r"downtime\s*activities$", re.I)
_OPTIONS = re.compile(r"options$", re.I)  # "Combat Options", "Abilityoptions" (OCR-joined)
_COMPLICATIONS = re.compile(r"complications$", re.I)
_STEP = re.compile(r"^\d{1,2}\.\s*")  # a numbered step heading: "4. Describe Your Character"
_SIBLING_FACTOR = 3
_UNREAD_HEIGHT = 0.95  # an unread heading that ends a rule is set at least this tall, relative to its own heading
_ACTIVITY_HEIGHT = 0.9  # an unread activity heading is set at least this tall, relative to its siblings' smallest


def _key(l: Line) -> tuple:
    """Printed-line identity, as `assemble._line_keys`."""
    return l.page, tuple(round(x, 1) for x in l.box)


def _prose(t: str) -> bool:
    return sum(w[:1].islower() for w in t.split()) >= 3


def _rules_chapter(s: Section) -> bool:
    m = _CHAPTER_TITLE.match(s.parents[0]) if s.parents else None
    return bool(m) and any(squash(m.group("title")) == squash(c) or like(m.group("title"), c, 90)
                           for c in _RULES_CHAPTERS)


def _named(title: str) -> bool:
    """A heading of words, not OCR debris ("I G 4 N C"): one word of three letters at least."""
    return bool(re.search(r"[A-Za-z]{3}", title))


def _unread_heading(L: list[Line], k: int, min_h: float, starts: set[int]) -> bool:
    """L[k] is a heading the section reader did not read: a short title-case (or caps) line, a numbered step's
    included ("4. Describe Your Character"), set at least `min_h` tall, after a finished sentence (or set apart),
    over prose. A bold lead-in ends in a full stop and runs on into its paragraph: never one."""
    if k == 0 or k + 1 >= len(L) or id(L[k]) in starts:
        return False
    t = _STEP.sub("", L[k].text.strip())
    if L[k].height < min_h or not (heading_like(t) and (title_case(t) or caps_heading(t))):
        return False
    prev = L[k - 1]
    if not (prev.text.rstrip().endswith(SENTENCE_END) or set_apart(prev, L[k])):
        return False
    return _prose(L[k + 1].text)


def _hosted_stop(lines: list[Line]) -> int:
    """Index of the heading of the first magic item or stat block printed inside a rule, else len(lines): a short
    heading over its type line ("Wondrous item, common"), or a name over a size line with Armor Class just below."""
    for i in range(1, len(lines)):
        t = lines[i].text.strip()
        if not heading_like(lines[i - 1].text.strip()):
            continue
        if _is_type_line(t) or _is_size_line(t) and any(re.match(r"(?i)^armou?r\s*class", l.text.strip())
                                                         for l in lines[i + 1:i + 5]):
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


def _cut_unread(s: Section, starts: set[int]) -> tuple[list[Line], bool, str]:
    """(the section's lines up to a heading the reader did not read, set at least as tall as its own; whether its
    end is sure; the end's reason). Ending at such a heading is a sure end."""
    L = s.lines
    h = _heads(L, s.title)
    min_h = _UNREAD_HEIGHT * max(l.height for l in L[:h])
    end = next((k for k in range(h, len(L)) if _unread_heading(L, k, min_h, starts)), None)
    if end is None:
        return L, s.complete, s.end_reason
    return L[:end], True, f"unread heading {L[end].text.strip()!r}"


def _caption(s: Section) -> bool:
    """The section is a table's caption read as a heading ("Loot Value" over "DC Value" / "10 50 gp, ..."): no
    prose in the two lines under it."""
    h = _heads(s.lines, s.title)
    return not any(_prose(l.text) for l in s.lines[h:h + 2])


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
    for k in range(1, len(L)):
        if k not in taken and _unread_heading(L, k, min_h, starts):
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


def _sidebars(ctx: ParseContext) -> list[_Cand]:
    """The "Variant:" / "Optional Rule:" sidebars: a stripped line opening with the label, and the stripped lines
    after it on its page (the box), running heads and page numbers out."""
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
        box = [x for x in full[i:k] if not (RUNNING_HEAD.match(x.text.strip()) or PAGE_NO.fullmatch(x.text.strip()))]
        out.append(_Cand(name=name_case(m.group("name").strip()), rule_type=_rule_type(m.group("label")), raw=box,
                         heads=1, kind="sidebar", complete=True, end_reason="sidebar box"))
        i = k
    return out


def _candidates(sections: list[Section], ctx: ParseContext) -> list[_Cand]:
    starts = {id(s.lines[0]) for s in sections}
    out: list[_Cand] = []
    options = {id(s) for s in sections if s.level == 1 and _rules_chapter(s) and _OPTIONS.search(s.title)}
    for s in sections:
        m = _LABEL.match(s.title)
        if m:
            name = name_case(m.group("name").strip())
            raw, complete, reason = _cut_unread(s, starts)
            out.append(_Cand(name=name, rule_type=_rule_type(m.group("label")), raw=raw, heads=_heads(raw, s.title),
                             kind="label", complete=complete, end_reason=reason))
            continue
        if _DOWNTIME.search(s.title):
            out += _activities(s, sections, starts)
        if not _rules_chapter(s) or not _named(s.title) or id(s) in options:
            continue
        group = None
        if s.level == 1:
            group = s.parents[0]
        elif s.level == 2:
            parent = next((p for p in sections if id(p) in options and p.parents == s.parents[:1]
                           and p.title == s.parents[-1] and id(s.lines[0]) in {id(x) for x in p.lines}), None)
            group = f"{s.parents[0]} > {parent.title}" if parent else None
        if group is None:
            continue
        raw, complete, reason = _cut_unread(s, starts)
        out.append(_Cand(name=s.title, rule_type=None, raw=raw, heads=_heads(raw, s.title), kind="entry",
                         complete=complete, end_reason=reason, group=group))
    out += _sidebars(ctx)
    # one rule per printed heading: a labelled section beats an activity beats an entry-level section
    rank = {"label": 0, "sidebar": 0, "activity": 1, "entry": 2}
    best: dict[int, _Cand] = {}
    for c in out:
        k = id(c.raw[0])
        if k not in best or rank[c.kind] < rank[best[k].kind]:
            best[k] = c
    return [c for c in out if best[id(c.raw[0])] is c]


def _text_check(c: _Cand, own: list[Line], sizes: dict[str, list[int]]) -> Check:
    if not c.complete:
        return Check("text-complete", False, f"rule {c.name!r} (p{c.raw[0].page}) has no sure end: {c.end_reason}")
    sib = sizes.get(c.group, []) if c.kind == "entry" else []
    others = list(sib)
    if len(own) in others:
        others.remove(len(own))
    if len(others) >= 2 and len(own) > _SIBLING_FACTOR * max(median(others), 10):
        return Check("text-complete", False, f"rule {c.name!r} (p{c.raw[0].page}) runs {len(own)} lines, over "
                     f"{_SIBLING_FACTOR}x its {len(others)} sibling rules' median of {median(others):g}")
    return Check("text-complete", True, "ends with its sidebar box" if c.kind == "sidebar" else "")


def parse_rules(body: list[Line], ctx: ParseContext) -> list[Parsed]:
    """One Rule per rules-chapter entry-level section, labelled variant or optional rule (section or sidebar), and
    downtime activity, in reading order."""
    sections = read_sections(body, ctx)
    if not sections and not body:
        return []
    character_body(body, ctx)  # the unstripped and stripped lines, for the sidebars
    cands = _candidates(sections, ctx)
    claimed: set = ctx.cache.get("claimed_lines") or set()
    full = ctx.cache["character_lines"]
    order = {id(l): k for k, l in enumerate(full)}
    cands.sort(key=lambda c: order.get(id(c.raw[0]), 0))
    spans = [{id(l) for l in c.raw} for c in cands]
    owned: list[tuple[_Cand, list[Line]]] = []
    for i, c in enumerate(cands):
        # the lines of every other rule that starts inside this one are that rule's
        nested = {x for j, d in enumerate(cands) if j != i and id(d.raw[0]) in spans[i] for x in spans[j]}
        own = [l for l in c.raw if id(l) not in nested]
        own = own[:_hosted_stop(own)]
        if not own or _key(own[0]) in claimed:
            continue
        own = [l for l in own if _key(l) not in claimed]
        if len(own) <= c.heads:
            continue
        owned.append((c, own))
    sizes: dict[str, list[int]] = {}
    for c, own in owned:
        if c.kind == "entry":
            sizes.setdefault(c.group, []).append(len(own))
    out: list[Parsed] = []
    for c, own in owned:
        heads = min(c.heads, len(own))
        entries = prose_entries(own, heads, len(own))
        fields: dict = {"entries": entries}
        if c.rule_type:
            fields["ruleType"] = c.rule_type
        head = " ".join(l.text.strip() for l in own[:heads])
        flat = [x if isinstance(x, str) else "\n".join(x.get("items", [])) for x in entries]
        p = Parsed(type="Rule", name=c.name, fields=fields, text="\n".join([head] + flat), lines=own)
        p.checks = [_text_check(c, own, sizes)]
        out.append(p)
    return out
