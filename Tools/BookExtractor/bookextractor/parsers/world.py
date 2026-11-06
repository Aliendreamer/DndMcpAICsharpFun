"""World section parser: one Plane, Faction, Location or Lore entity per heading-bounded section of the book's
setting and lore chapters, at each chapter's entry level.

**Which sections.** Every section the section reader (`read_sections`) finds, except:

- the sections of a rules chapter (`rule.rules_chapter`): those are the Rule parser's;
- a section whose heading line an earlier type's entity owns, or most of whose lines it owns
  (`ctx.cache["claimed_lines"]`, line keys as `assemble._line_keys`; required, the assembler fills it), or whose
  heading is an earlier entity's name, a plural "s" aside (`ctx.cache["claimed_names"]`, `charcommon.squash`ed;
  required): a race, a class, a monster, a magic item, a god, a rule, ... is that type's, never a world section
  (ERLW "Changelings" is the race Changeling's section);
- a heading that is OCR debris or a table read as a heading ("N O P Q R S T", "Gp Pp": no word of three letters), or
  that has no prose under it.

**Entry level**, per chapter, from the heading-size evidence the reader leaves and the sibling counts, never page
numbers or section names: a chapter's top sections (the reader's level 1) are its entries, unless one is a
*container* of entries: a section with three sub-sections or more whose span is over `_CONTAINER` times its sibling
sections' median ("Nations of Khorvaire" over its nations, among the gazetteer's shorter sections), or a top section
with no chapter title over it ("Welcome to Ravnica", "Introduction": the book's unnumbered opening chapters). A
container's sub-sections are the entries there, the same rule applying again one level down. A container's own lead
text (before its first sub-section) is an entity of its own when it holds prose; the lead of a top section with no
chapter title over it is not (it has no enclosing heading to type or categorise it by).

**Type**, from the nearest printed enclosing heading (the chapter title included, without "Chapter N:") that holds a
type word (`_TYPE_WORDS`): Plane (plane, realm, layer, domain), Faction (faction, guild, organization, house, order,
cult, company), Location (city, region, kingdom, nation, settlement, location, places, district, island), else Lore.
The `type-from-context` check passes naming the heading and its word; when that heading holds words of two types
("Guild Locations") it fails naming both, and the entity takes the type of the last word (a heading's head noun).

**Fields**, only as printed: `category` is that enclosing heading (Lore with no type word: the nearest enclosing
heading; none printed, when only a chapter title the OCR did not read encloses it: then `type-from-context` fails); `setting` (Location) and `settingContext` (Lore) are the book's setting when the book is a setting book
(`SETTINGS`); `entries` the section's text. Faction prints no category field (`FactionFields`).

**Text** is the section's lines, sub-sections included, without lines an earlier type owns, the text of another
world entity printed inside it (a container's sub-sections), art text and a dangling table caption at its end
(`drop_art`, `drop_dangling`). The section reader already ends a section at the next heading of equal or greater
level, or at a chapter title; nothing is cut beyond that. When the evidence says the end may be wrong, the text is
kept whole and `text-complete` fails, naming the heading or the measure:

- the reader is not sure of the end (`Section.complete`: end of text, a chapter gap, a stop barely larger than a
  sub-heading);
- a heading inside it, over prose, may be a sibling it runs on into (`_tall_inside`): a sub-heading the reader nests
  under it but set nearly as large as its own heading or its siblings' (`_SUB_SIZE`), or a heading the reader did
  not read, set nearly as tall (`_HEAD_HEIGHT`);
- the section runs over more than `_MAX_PAGES` pages and over `_PAGE_FACTOR` times its sibling entries' median page
  count (a guild chapter's six-page guilds are alike; a section that ran on over its siblings is not);
- it runs over `_SIBLING_FACTOR` times its sibling entries' median line count.

A container's lead text ends at its first sub-section's heading, a heading the reader read: only the `_tall_inside`
check applies to it (DMG "Fun Combat Encounters" holds the unread, taller "Random Encounters").

Not registered in the assembler yet (Phase 2c ruling W1): Task 6 does that.
"""
from __future__ import annotations

import re
from dataclasses import dataclass
from statistics import median

from ..checks import Check
from ..model import Line
from . import ParseContext, Parsed
from .charcommon import prose_entries, squash
from .rule import rules_chapter
from .sections import (UNKNOWN_CHAPTER, Section, drop_art, drop_dangling, heading_words, line_key, prose,
                       read_sections)

# the book's setting, for the setting books (a dev-time constant, by 5etools source key)
SETTINGS = {"ERLW": "Eberron", "EGW": "Exandria", "GGR": "Ravnica", "SCAG": "Forgotten Realms", "VRGR": "Ravenloft",
            "SCC": "Strixhaven"}

# type words, as whole words of a printed heading; "divine domain" is a cleric feature, not a plane
_TYPE_WORDS = (
    # "realm", never "Realms": the plural names the Forgotten Realms ("Welcome to the Realms") or kingdoms
    # ("Independent Realms") in the corpus, not planes
    ("Plane", re.compile(r"(?i)\b(?:planes?|realm|layers?|(?<!divine )domains?)\b")),
    ("Faction", re.compile(r"(?i)\b(?:factions?|guilds?|organi[sz]ations?|houses?|orders?|cults?|company|companies)\b")),
    ("Location", re.compile(r"(?i)\b(?:city|cities|regions?|kingdoms?|nations?|settlements?|locations?|places|"
                            r"districts?|islands?)\b")),
)
_CHAPTER_PREFIX = re.compile(r"^(?:Chapter|Appendix) [^:]+: ")
_CONTAINER = 3  # a section over this many times its siblings' median span holds the entries
_CONTAINER_KIDS = 3  # ... when it has this many sub-sections at least
_CLAIMED = 0.5  # a section with more than this share of its lines owned by earlier types is theirs
_MAX_PAGES = 3
_PAGE_FACTOR = 2
_SIBLING_FACTOR = 3
# an unread heading this tall, relative to the section's own heading or its siblings', is set like a sibling's: OCR
# heading heights vary by about 10% (ERLW "Demon Wastes" 0.93x inside "Darguun")
_HEAD_HEIGHT = 0.9
# a sub-heading the reader nests under the section, set this large (height x character width, `Section.size`)
# relative to the section's own heading or its siblings', may be a sibling: the reader's own sub-heading step is
# 12% (`sections._SUB_STEP`); between 12% and 20% smaller the evidence is in doubt (EGW "Daily Life in Wildemount"
# 0.84x under "Moons of Exandria"; GGR "Golgari Characters" 0.72x under "Golgari Swarm" is a sub-heading)
_SUB_SIZE = 0.8
_MIN_PROSE = 2  # a section holds this many prose lines at least


@dataclass
class _Entry:
    section: Section
    lines: list[Line]  # the section's own printed lines (before claimed lines and nested entities are taken out)
    lead: bool = False  # a container's lead text, up to its first sub-section


def _bare(heading: str) -> str:
    return _CHAPTER_PREFIX.sub("", heading)


def section_type(parents: list[str]) -> tuple[str, str | None, Check]:
    """(type, category, the `type-from-context` check) from the printed enclosing headings, outermost first. A chapter
    title the OCR did not read (`sections.UNKNOWN_CHAPTER`) is no printed heading."""
    printed = [p for p in parents if p != UNKNOWN_CHAPTER]
    for p in reversed(printed):
        title = _bare(p)
        hits = sorted((m.start(), t, m.group(0)) for t, rx in _TYPE_WORDS for m in rx.finditer(title))
        if not hits:
            continue
        etype, word = hits[-1][1], hits[-1][2]
        if len({t for _, t, _ in hits}) > 1:
            named = ", ".join(f"{w!r} ({t})" for _, t, w in hits)
            return etype, title, Check("type-from-context", False,
                                       f"enclosing heading {p!r} holds words of different types: {named}; "
                                       f"took {etype} from the last")
        return etype, title, Check("type-from-context", True, f"{etype}: {word!r} in enclosing heading {p!r}")
    if not printed:
        return "Lore", None, Check("type-from-context", False,
                                   "no printed enclosing heading to type the section by: its chapter title was not "
                                   "read")
    return "Lore", _bare(printed[-1]), Check("type-from-context", True,
                                             "Lore: no plane, faction or location word in the enclosing headings "
                                             f"{[_bare(p) for p in printed]}")


def _plain(name: str) -> str:
    """A name squashed, a plural "s" off: the section "Changelings" is the race "Changeling"'s."""
    s = squash(name)
    return s[:-1] if s.endswith("s") and len(s) > 3 else s


def _named(title: str) -> bool:
    """Heading words, not OCR debris or a table header read as a heading ("N O P Q R S T", "Gp Pp")."""
    return bool(re.search(r"[A-Za-z]{3}", title))


def _kids(s: Section, sections: list[Section]) -> list[Section]:
    """The section's direct sub-sections (one level down, inside its span)."""
    span = {id(l) for l in s.lines}
    return [c for c in sections if c.level == s.level + 1 and c.parents[:len(s.parents) + 1] == s.parents + [s.title]
            and id(c.lines[0]) in span and c is not s]


def _container(s: Section, siblings: list[Section], kids: list[Section]) -> bool:
    if not kids:
        return False
    if not s.parents:  # a top section with no chapter title over it: an unnumbered opening chapter
        return True
    if len(kids) < _CONTAINER_KIDS:
        return False
    others = [len(o.lines) for o in siblings if o is not s]
    if not others:
        return s.lines[-1].page - s.lines[0].page + 1 > _MAX_PAGES
    return len(s.lines) > _CONTAINER * median(others)


def _entries(sections: list[Section], owned) -> list[_Entry]:
    """The entry-level sections of every chapter, and the containers' leads, in reading order. A container an
    earlier type owns (`owned`: its name, or most of its lines) is not opened: its sub-sections are that entity's
    ("Far Traveler"'s "Ideals")."""
    out: list[_Entry] = []

    def visit(group: list[Section]) -> None:
        for s in group:
            kids = _kids(s, sections)
            if _container(s, group, kids):
                # a heading line owned by an earlier type over a span it mostly does not own is a run-on read
                # ("The Mark of" over the dragonmarks, then the group patrons): its sub-sections are opened
                if owned(s.title, s.lines, heading=False):
                    continue
                first = next(k for k, l in enumerate(s.lines) if id(l) == id(kids[0].lines[0]))
                if s.parents and first > 1:
                    out.append(_Entry(s, s.lines[:first], lead=True))
                visit(kids)
            elif s.parents:  # a top section with no chapter title over it and no sub-sections: no category
                out.append(_Entry(s, s.lines))

    tops = [s for s in sections if s.level == 1]
    chapters: dict[str, list[Section]] = {}
    for s in tops:
        chapters.setdefault(s.parents[0] if s.parents else "", []).append(s)
    for group in chapters.values():
        visit(group)
    order = {id(s.lines[0]): k for k, s in enumerate(sections)}
    out.sort(key=lambda e: (order[id(e.section.lines[0])], not e.lead))
    return out


def _tall_inside(s: Section, own: list[Line], sizes: dict[int, float], sibs: list[Section]) -> str:
    """A heading inside the section's text that may be a sibling it runs on into: "" when none.

    - a sub-heading the reader nests under it, set at least `_SUB_SIZE` as large as the smaller of the section's own
      heading and its sibling sections' median heading (EGW "ICEHAVEN" under "Erdeloch");
    - a heading the reader did not read, set at least `_HEAD_HEIGHT` as tall as the smaller of the two heights (ERLW
      "EldeEn ReAches" inside "Droaam").

    Either is over prose within four lines ("Capital: Greenheart" / "Hallmarks: ..." may come first)."""
    head = own[0].height
    yard_h = min(head, median(o.lines[0].height for o in sibs)) if sibs else head
    yard_s = min(s.size, median(o.size for o in sibs)) if sibs else s.size
    for k in range(1, len(own) - 1):
        l = own[k]
        if not (heading_words(l.text) and any(prose(x.text) for x in own[k + 1:k + 5])):
            continue
        if id(l) in sizes:
            if sizes[id(l)] >= _SUB_SIZE * yard_s:
                return (f"{l.text.strip()!r} (p{l.page}), which the reader nests under it, set {sizes[id(l)]:.2f} "
                        f"large; its own heading {s.size:.2f}"
                        + (f", its siblings' median {median(o.size for o in sibs):.2f}" if sibs else ""))
        elif l.height >= _HEAD_HEIGHT * yard_h:
            return (f"{l.text.strip()!r} (p{l.page}), which the reader did not read, set {l.height:.1f} tall; its own "
                    f"heading {head:.1f}" + (f", its siblings' median {median(o.lines[0].height for o in sibs):.1f}"
                                             if sibs else ""))
    return ""


def _text_check(e: _Entry, own: list[Line], sib_lines: list[int], sib_pages: list[int], sib_secs: list[Section],
                sizes: dict[int, float]) -> Check:
    s = e.section
    name = f"section {s.title!r} (p{s.page})"
    tall = _tall_inside(s, own, sizes, sib_secs)
    if tall:
        return Check("text-complete", False, f"{name} may run on: it holds the heading {tall}")
    if e.lead:  # it ends at its first sub-section's heading, a heading the reader read
        return Check("text-complete", True, "the lead text up to its first sub-section")
    if not s.complete:
        return Check("text-complete", False, f"{name} has no sure end: {s.end_reason}")
    pages = own[-1].page - own[0].page + 1
    if pages > _MAX_PAGES and (len(sib_pages) < 2 or pages > _PAGE_FACTOR * median(sib_pages)):
        return Check("text-complete", False, f"{name} runs over {pages} pages (p{own[0].page}-p{own[-1].page}), "
                     f"more than {_MAX_PAGES}" + (f" and over {_PAGE_FACTOR}x its {len(sib_pages)} sibling "
                                                  f"sections' median of {median(sib_pages):g}" if sib_pages else ""))
    if len(sib_lines) >= 2 and len(own) > _SIBLING_FACTOR * max(median(sib_lines), 10):
        return Check("text-complete", False, f"{name} runs {len(own)} lines, over {_SIBLING_FACTOR}x its "
                     f"{len(sib_lines)} sibling sections' median of {median(sib_lines):g}")
    return Check("text-complete", True, f"ends at {s.end_reason}")


def _fields(etype: str, category: str | None, entries: list, setting: str | None) -> dict:
    if etype == "Faction":
        return {"entries": entries}
    if etype == "Location":
        f: dict = {"entries": entries}
        if category:
            f["category"] = category
        if setting:
            f["setting"] = setting
        return f
    f = {"category": category or "", "entries": entries}
    if etype == "Lore" and setting:
        f["settingContext"] = setting
    return f


def parse_world(body: list[Line], ctx: ParseContext) -> list[Parsed]:
    """One Plane, Faction, Location or Lore per entry-level world section, in reading order."""
    claimed: set = ctx.cache["claimed_lines"]  # required: the assembler fills it (an empty set when none)
    names: set = ctx.cache["claimed_names"]  # required: earlier entities' names, squashed (`charcommon.squash`)
    sections = [s for s in read_sections(body, ctx) if not rules_chapter(s)]
    starts = {id(s.lines[0]) for s in sections}
    sizes = {id(s.lines[0]): s.size for s in sections}
    picked: list[tuple[_Entry, list[Line]]] = []

    plain_names = {_plain(n) for n in names}

    def owned(title: str, lines: list[Line], heading: bool = True) -> bool:
        return (heading and line_key(lines[0]) in claimed or _plain(title) in plain_names
                or sum(line_key(l) in claimed for l in lines) > _CLAIMED * len(lines))

    entries = _entries(sections, owned)
    spans = [{id(l) for l in e.lines} for e in entries]
    for i, e in enumerate(entries):
        s = e.section
        if not _named(s.title) or owned(s.title, e.lines):
            continue
        # the lines of another entry that starts inside this one are that entry's
        nested = {x for j, d in enumerate(entries) if j != i and id(d.lines[0]) in spans[i]
                  and id(d.lines[0]) != id(e.lines[0]) for x in spans[j]}
        own = [l for l in e.lines if id(l) not in nested and line_key(l) not in claimed]
        heads = _heads(own, s.title)
        own = drop_dangling(drop_art(own, ctx), e.lines, heads, starts)
        # a table's header or caption read as a heading has no prose straight under it ("Suggested Domains Symbol")
        if sum(prose(l.text) for l in own[heads:]) < _MIN_PROSE or not e.lead and not any(
                prose(l.text) for l in own[heads:heads + 2]) and not _sub_heading_over_prose(own, heads, starts):
            continue
        picked.append((e, own))
    setting = SETTINGS.get(ctx.source.upper())
    out: list[Parsed] = []
    for e, own in picked:
        s = e.section
        sib_entries = [(x, o) for x, o in picked if x is not e and not x.lead and x.section.parents == s.parents]
        sibs = [o for _, o in sib_entries]
        heads = _heads(own, s.title)
        etype, category, typed = section_type(s.parents)
        entries_ = prose_entries(own, heads, len(own))
        flat = [x if isinstance(x, str) else "\n".join(x.get("items", [])) for x in entries_]
        head = " ".join(l.text.strip() for l in own[:heads])
        p = Parsed(type=etype, name=s.title, fields=_fields(etype, category, entries_, setting),
                   text="\n".join([head] + flat), lines=own)
        p.checks = [_text_check(e, own, [len(o) for o in sibs], [o[-1].page - o[0].page + 1 for o in sibs],
                                [x.section for x, _ in sib_entries], sizes), typed]
        out.append(p)
    return out


def _sub_heading_over_prose(own: list[Line], heads: int, starts: set[int]) -> bool:
    """Straight under the heading, a sub-heading the reader read, over prose."""
    return heads + 1 < len(own) and id(own[heads]) in starts and prose(own[heads + 1].text)


def _heads(lines: list[Line], title: str) -> int:
    """How many of the first lines are the heading (a heading may wrap onto a second line)."""
    want, k, got = squash(title), 0, ""
    while k < min(3, len(lines)):
        got += squash(lines[k].text)
        if not got or got not in want and not want.endswith(got):
            break
        k += 1
    return max(k, 1)
