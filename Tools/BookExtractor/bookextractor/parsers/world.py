"""World section parser: one Plane, Faction, Location or Lore entity per heading-bounded section of the book's
setting and lore chapters, at each chapter's entry level.

**Which sections.** Every section the section reader (`read_sections`) finds, except:

- the sections of a rules chapter (`rule.rules_chapter`): those are the Rule parser's;
- a section whose heading line an earlier type's entity owns, or most of whose lines it owns
  (`ctx.cache["claimed_lines"]`, line keys as `assemble._line_keys`; required, the assembler fills it), or whose
  heading is an earlier entity's name, a plural "s" and a trailing parenthetical aside (`ctx.cache["claimed_names"]`,
  the names as printed; required): a race, a class, a monster, a magic item, a god, a rule, ... is that type's,
  never a world section (ERLW "Changelings" is the race Changeling's; MM "Barbed Devil" the monster "Barbed Devil
  (Hamatula)"'s);
- a section with no prose of its own (under `_MIN_PROSE` prose lines: a table's caption over its rows).

A section with prose is never dropped in silence (ruling W24): a stat block's part (a heading "Actions",
"Reactions", "Bonus Actions" or "Legendary Actions", the Monster's), a heading that is OCR debris or a table row
("N O P Q R S T", "Drow (+1) Human (+1)"; `garbage_heading`), a table's title (`table_title`) or a heading with no
prose straight under it (a table's header, a caption, a display title's "Domain of ..." block) is emitted with a
failing `entry-level` check naming why. A section whose text holds a stat block's lines is kept and flagged
(`_stat_block`).

**Entry level**, per chapter, from the heading-size evidence the reader leaves and the sibling counts, never page
numbers or section names: a chapter's top sections (the reader's level 1) are its entries, unless one is a
*container* of entries (`_container`): a section with three sub-sections or more that lists entries by its own
plural type word ("Other Domains of Dread", "Nations of Khorvaire"), whose span is over `_CONTAINER` times its
sibling sections' median ("Nations of Khorvaire" among the gazetteer's shorter sections), or that belongs to a
sibling group of long listings (decided once per group, ruling W24: the group's multi-part members span over
`_LISTING_PAGES` pages by median, as EGW's gazetteer regions, each as long as the next; VRGR's six-page domains are
entries, the long ones alike), or that is its chapter's only top heading (PHB Appendix B's "D&D Pantheons"); or a
top section with no chapter title over it ("Welcome to Ravnica", "Introduction": the book's unnumbered opening
chapters). A container's sub-sections are the entries there, the same
rule applying again one level down. A container's own lead text (before its first sub-section) is an entity of its
own when it holds prose. A top section with no chapter title over it, or such a container's lead, has no heading
to type or categorise it by: it is emitted all the same, and `type-from-context` fails.

**Type** (ruling W12), only from the section's immediate printed parent heading (the chapter title for a top
section, without "Chapter N:"): Plane, Faction or Location when the parent's head noun is a plural type word
(`_PLURAL_TYPES`: "Nations of Khorvaire" -> Location, "Guilds of Ravnica" -> Faction, "Domains of Ravenloft" and
"The Outer Planes" -> Plane; never after a gerund, "Creating Domains of Dread"), the section is printed as a proper
name in its own text (`proper_name`: "Breland", not "Currency"), it is set among the parent's main entries
(`_main_size`: VRGR's Darkon over "Currency"), and no heading repeats among the parent's children (their own headings
were not all read). Anything else is Lore: singular words, distant ancestors and grandchildren type nothing. The
`type-from-context` check passes naming the parent and the evidence; it fails when the parent heading holds words of
two types ("Guild Locations": 'Guild' (Faction), 'Locations' (Location); the head noun's type is taken), or when no
printed parent heading encloses the section (only a chapter title the OCR did not read).

**Fields**, only as printed: `category` is the parent heading, absent when none is printed (`LoreFields` does not
require it); `setting` (Location) and `settingContext` (Lore) are the book's setting when the book is a setting book
(`SETTINGS`); `entries` the section's text. Faction prints no category field (`FactionFields`).

**Text** is the section's lines, sub-sections included, without lines an earlier type owns, the text of another
world entity printed inside it (a container's sub-sections), art text and a dangling table caption at its end
(`drop_art`, `drop_dangling`). The section reader already ends a section at the next heading of equal or greater
level, or at a chapter title; nothing is cut beyond that. When the evidence says the end may be wrong, the text is
kept whole and `text-complete` fails, naming every doubtful heading or the measure (ruling W13):

- a heading inside it may be a sibling it runs on into, or a float printed inside it (`_doubt`: a positive end signal
  or the 0.65-0.97 height band; never a single ratio's silent cut);
- its end is in doubt (`_doubtful_tail`): it ends on a heading-like line, or its last lines sit on a page holding
  none of its prose (map labels);
- the reader is not sure of the end (`Section.complete`: end of text, a chapter gap, a stop barely larger than a
  sub-heading);
- the section runs over more than `_MAX_PAGES` pages and over `_PAGE_FACTOR` times its sibling entries' median page
  count (a guild chapter's six-page guilds are alike; a section that ran on over its siblings is not);
- it runs over `_SIBLING_FACTOR` times its sibling entries' median line count.
- it holds a stat block's lines (two of its printed forms within a few lines: `_stat_block`);
- it holds a sub-section set like its own siblings (`held_siblings`: at least (1 - `_SUB_STEP`) times the median
  size of its sibling group), a sibling the reader nested (ruling W25);
- it holds a line the reader would classify as a title, a caption or display text that does not name it
  (`sections.foreign_lines`: an unbounded sidebar's title, a caption read into the text; ruling W25).

`entry-level` (ruling W16) fails for a section the entry-level reading picked that is a sub-heading of an entry: its
container is an entry typed Plane, Faction or Location, or its heading recurs in its chapter (`_entry_check`).

A container's lead text ends at its first sub-section's heading, a heading the reader read: only the first two
checks apply to it (DMG "Fun Combat Encounters" holds the unread, taller "Random Encounters").

`entry-level` also fails (rulings W21, W22, W25) when the section's place in the reader's tree, from which its
parent heading, category, type and W11 id come, is not verified (`placement_doubts`): its page prints another chapter's
title, it follows a section whose end is in doubt and is set like that section's sub-headings, or its parent heading
is OCR debris, repeats the section's heading, prints it more than once (or the section comes after a repeated
sub-heading's second print), or holds no sentence of its own (a table caption or title); a doubted parent doubts
every section under it. It fails too for a section whose own text holds no sentence (a table read as a section).
"""
from __future__ import annotations

import re
from collections import Counter
from dataclasses import dataclass
from statistics import median

from ..checks import Check
from ..model import Line
from . import ParseContext, Parsed
from .boundary_trim import trim_own
from .charcommon import SENTENCE_END, SMALL_WORDS, character_body, prose_entries, set_apart, squash
from .rule import rules_chapter
from .sections import (_SUB_STEP, UNKNOWN_CHAPTER, Section, chapter_titles, display_lines, drop_art, drop_dangling,
                       fix_acronyms, foreign_lines, heading_words, line_key, name_words, odd_opening, prose,
                       read_sections)

# the book's setting, for the setting books (a dev-time constant, by 5etools source key)
SETTINGS = {"ERLW": "Eberron", "EGW": "Exandria", "GGR": "Ravnica", "SCAG": "Forgotten Realms", "VRGR": "Ravenloft",
            "SCC": "Strixhaven"}

# a parent heading types its direct children only by its plural head noun ("Nations of Khorvaire", "The Outer
# Planes", "Guilds of Ravnica", "Domains of Ravenloft"); "Realms" names the Forgotten Realms or kingdoms in the corpus
_PLURAL_TYPES = {
    "planes": "Plane", "layers": "Plane", "domains": "Plane",
    "factions": "Faction", "guilds": "Faction", "organizations": "Faction", "organisations": "Faction",
    "houses": "Faction", "orders": "Faction", "cults": "Faction", "companies": "Faction",
    "cities": "Location", "regions": "Location", "kingdoms": "Location", "nations": "Location",
    "settlements": "Location", "locations": "Location", "places": "Location", "districts": "Location",
    "islands": "Location",
}
# any type word, singular or plural, for the conflict check ("Guild Locations")
_TYPE_WORDS = (
    ("Plane", re.compile(r"(?i)\b(?:planes?|realm|layers?|(?<!divine )domains?)\b")),
    ("Faction", re.compile(r"(?i)\b(?:factions?|guilds?|organi[sz]ations?|houses?|orders?|cults?|company|companies)\b")),
    ("Location", re.compile(r"(?i)\b(?:city|cities|regions?|kingdoms?|nations?|settlements?|locations?|places|"
                            r"districts?|islands?)\b")),
)
_GAP = 1.25  # a sibling group whose heading sizes jump by this factor sets its main entries larger than the rest
# a monster's stat block or its parts are the Monster's, never a world section
_STAT_HEADINGS = {"actions", "reactions", "legendaryactions", "bonusactions"}
# a stat block's own forms, case as printed (prose "a melee weapon attack" is none); two within `_STAT_SPAN` lines
# make a stat block printed inside the section's text
_STAT_LINE = re.compile(r"\b(?:Melee|Ranged) (?:Weapon|Spell) Attack:|\bHit: \d+ \(|^Armou?r Class \d+|"
                        r"^Challenge \d+(?:/\d+)? \([\d,]+ XP\)")
_STAT_SPAN = 6
_CHAPTER_PREFIX = re.compile(r"^(?:Chapter|Appendix) [^:]+: ")
_CONTAINER = 3  # a section over this many times its siblings' median span holds the entries
_CONTAINER_KIDS = 3  # ... when it has this many sub-sections at least
_LISTING_PAGES = 9  # a sibling group whose multi-part members span this many pages (median) lists entries
_CLAIMED = 0.5  # a section with more than this share of its lines owned by earlier types is theirs
_MAX_PAGES = 3
_PAGE_FACTOR = 2
_SIBLING_FACTOR = 3
# a heading over prose inside a section's text is a doubtful end, never a silent one (`_doubt`): set at least this
# tall relative to the section's own heading it may be a sibling the reader nested or did not read; under it, it
# still is one when it is styled like the section's own heading or set larger than the section's own sub-headings
_DOUBT = 0.65
_RECURRING = 3  # a heading printed this many times in one chapter is a sub-heading label ("Symbol", "Crime")
_RUN = 3  # a run of this many same-styled peer sub-headings inside a section is doubted (`_peer_runs`)
_PEER = 1.05  # a band heading set over this many times its tallest peer sub-heading stands out from them
# ... and peer sub-headings set this large (reader size, height x character width) relative to the section's own
# heading are no sub-headings but siblings the reader nested (FTD "Young Dragons" 0.83x under "Wyrmlings", SCAG
# "Helm's Hold" 0.747x under "Hartsvale"; GGR "Golgari Characters" 0.716x under "Golgari Swarm" is a sub-heading)
_PEER_SIZE = 0.73
_NAMED = 3  # a text-complete reason details this many doubtful headings, and names the rest
_TALL_LINE = 1.15  # a line this many times the section's median line height is set as a heading, not body text
_MIN_PROSE = 2  # a section holds this many prose lines at least


@dataclass
class _Entry:
    section: Section
    lines: list[Line]  # the section's own printed lines (before claimed lines and nested entities are taken out)
    lead: bool = False  # a container's lead text, up to its first sub-section
    container: Section | None = None  # the container whose sub-section this entry is
    under_owned: bool = False  # its container is another entity's section (a sibling the reader nested there)
    # a section with printed sub-headings that are entries of their own: `lines` is its lead (the text before its
    # first sub-section), `full` its whole span, so the doubts about a heading inside it are still raised (round 4)
    full: list[Line] | None = None


def bare_heading(heading: str) -> str:
    """The printed heading without its "Chapter N:" / "Appendix X:" prefix."""
    return _CHAPTER_PREFIX.sub("", heading)


def _head_noun(title: str) -> tuple[str, bool]:
    """(the heading's head noun, lower case; whether a gerund comes before it: "Creating Domains of Dread" names an
    activity, not a group). The head noun is the last word before "of" ("Nations of Khorvaire"), or the last word
    ("The Outer Planes")."""
    pre = re.split(r"(?i)\s+of\s+", bare_heading(title), maxsplit=1)[0]
    words = re.findall(r"[A-Za-z][A-Za-z'’-]*", pre)
    if not words:
        return "", False
    return words[-1].lower(), any(x.lower().endswith("ing") and len(x) > 4 for x in words[:-1])


def parent_type(parent: str | None) -> tuple[str | None, str, Check | None]:
    """(the type a parent heading gives its direct children, or None; why; a failing `type-from-context` check when the
    heading holds type words of different types, else None)."""
    if parent is None:
        return None, "no printed parent heading", None
    noun, gerund = _head_noun(parent)
    etype = _PLURAL_TYPES.get(noun)
    if noun == "domains" and re.search(r"(?i)\bdivine\s+domains\b", parent):
        etype = None  # a cleric's divine domains, not domains of dread
    if etype is None or gerund:
        return None, (f"the head noun {noun!r} of its parent heading {parent!r} is no plural plane, faction or location "
                      "word" + (" (an activity heading)" if etype and gerund else "")), None
    hits = sorted((m.start(), t, m.group(0)) for t, rx in _TYPE_WORDS for m in rx.finditer(bare_heading(parent)))
    if len({t for _, t, _ in hits}) > 1:
        named = ", ".join(f"{x!r} ({t})" for _, t, x in hits)
        return etype, "", Check("type-from-context", False, f"parent heading {parent!r} holds words of different "
                                f"types: {named}; took {etype} from its head noun")
    return etype, f"{etype}: {noun!r}, the plural head noun of its parent heading {parent!r}", None


def proper_name(name: str, text: str) -> bool:
    """The section's name is printed as a proper name in its own text: the whole name (small words and a trailing
    parenthetical aside) capitalised mid-sentence at least once and never in lower case ("the House Dimir", "Breland",
    "the Mount Celestia"; "Currency", "Healing", "Size", "Guild Membership", "Chamber Adventure Hooks" are
    topics)."""
    words = [x for x in re.findall(r"[A-Za-z][A-Za-z'’-]*", re.sub(r"\s*\([^)]*\)", "", name))
             if x.lower() not in SMALL_WORDS]
    if not words or len(words[0]) < 3:
        return False

    def printed(phrase: str) -> bool:
        cap = low = 0
        for m in re.finditer(r"(?i)\b" + r"\s+(?:\w+\s+)?".join(map(re.escape, phrase.split())) + r"\b", text):
            before = text[:m.start()].rstrip()
            if m.group(0)[0].islower():
                low += 1
            elif before and before[-1] not in ".!?:\"'\n•":
                cap += 1
        return cap > 0 and low == 0

    return printed(" ".join(words))


def section_type(parents: list[str], proper: bool = True, main: bool = True,
                 repeated: str = "") -> tuple[str, str | None, Check]:
    """(type, category, the `type-from-context` check) of a section under the printed headings `parents` (outermost
    first; a chapter title the OCR did not read, `sections.UNKNOWN_CHAPTER`, is no printed heading).

    Plane, Faction or Location only from the immediate parent's plural head noun (`parent_type`), and only for a
    section printed as a proper name in its text (`proper`) and set among the parent's main entries (`main`), and only
    when no heading repeats among the parent's children (`repeated`: VRGR "Featured Domains" holds "Noteworthy
    Features" again and again because the reader lost the domains' own headings, so its children are not its list);
    anything else is Lore. The category is the parent heading; none when nothing printed encloses the section."""
    printed = [p for p in parents if p != UNKNOWN_CHAPTER]
    if not printed or parents[-1] == UNKNOWN_CHAPTER:
        return "Lore", None, Check("type-from-context", False, "no printed parent heading to type the section by: "
                                   "its chapter title was not read")
    parent = printed[-1]
    etype, why, conflict = parent_type(parent)
    category = bare_heading(parent)
    if etype and conflict:
        return etype, category, conflict
    if etype and repeated:
        why, etype = (f"Lore: its parent {parent!r} lists {etype.lower()}s, but the heading {repeated!r} repeats "
                      "among the parent's children: their own headings were not all read"), None
    elif etype and not proper:
        why, etype = (f"Lore: its parent {parent!r} lists {etype.lower()}s, but its name is not printed as a proper "
                      "name in its text (a topic of the parent)"), None
    elif etype and not main:
        why, etype = (f"Lore: its parent {parent!r} lists {etype.lower()}s, but its heading is set smaller than the "
                      "parent's main entries"), None
    elif etype is None:
        why = f"Lore: {why}"
    return etype or "Lore", category, Check("type-from-context", True, why)


def _plain(name: str) -> str:
    """A name squashed, a trailing parenthetical and a plural "s" off: the section "Changelings" is the race
    "Changeling"'s, the section "Barbed Devil" the monster "Barbed Devil (Hamatula)"'s."""
    s = squash(re.sub(r"\s*\([^)]*\)\s*$", "", name))
    return s[:-1] if len(s) > 3 and s.endswith("s") else s


def _kids(s: Section, sections: list[Section]) -> list[Section]:
    """The section's direct sub-sections (one level down, inside its span)."""
    span = {id(l) for l in s.lines}
    return [c for c in sections if c.level == s.level + 1 and c.parents[:len(s.parents) + 1] == s.parents + [s.title]
            and id(c.lines[0]) in span and c is not s]


def _pages(s: Section) -> int:
    return s.lines[-1].page - s.lines[0].page + 1


def _listing_group(group: list[Section], kids_of: dict[int, list[Section]]) -> bool:
    """The sibling group's multi-part members (those with `_CONTAINER_KIDS` sub-sections or more) are long listings
    of entries, decided once for the group (ruling W24): their median span is over `_LISTING_PAGES` pages (EGW's
    gazetteer regions, each a run of alphabetical places over 10 to 25 pages; SCAG's realms). A group whose typical
    multi-part member is shorter (VRGR's domains, six pages each) holds entries, long or short alike."""
    spans = [_pages(x) for x in group if len(kids_of.get(id(x), ())) >= _CONTAINER_KIDS]
    return bool(spans) and median(spans) > _LISTING_PAGES


def _container(s: Section, siblings: list[Section], kids: list[Section], listing: bool = False) -> bool:
    """The section holds the entries one level down: a top section with no chapter title over it; or, with
    `_CONTAINER_KIDS` sub-sections at least, a heading that lists entries by its own plural type word ("Other
    Domains of Dread", "Nations of Khorvaire"; `parent_type`), a span over `_CONTAINER` times its siblings' median
    ("Nations of Khorvaire" among the gazetteer's shorter sections), a member of a group of long listings
    (`listing`, `_listing_group`), or its chapter's only top heading (PHB Appendix B's "D&D Pantheons": the
    chapter's body, whose parts are the entries)."""
    if not kids:
        return False
    if not s.parents:  # a top section with no chapter title over it: an unnumbered opening chapter
        return True
    if len(kids) < _CONTAINER_KIDS:
        return False
    if parent_type(s.title)[0] is not None:
        return True
    others = [len(o.lines) for o in siblings if o is not s]
    if not others and s.level == 1:  # its chapter's only top heading: the chapter's body, its parts the entries
        return True
    return bool(others) and len(s.lines) > _CONTAINER * median(others) or listing


def _contiguous_lead(lead: list[Line], lines: list[Line], kids: list[Section]) -> list[Line]:
    """A container's lead: the text before its first entry and the folded parts (a recurring sub-heading's text)
    that follow in reading order. A folded part joins its parent only while it is contiguous with the lead: an
    entry that carries on to a later page than the lead has reached stands between them, so the parts printed after
    it belong to the run they sit in, never to the parent's text (ruling W17, Task 7 round 3: EGW Blightshore
    never takes ROTTHOLD's Government and Crime from p157-158). An entry that ends on the page the lead has
    reached (Darktow's "By Land or Sea" before its Government) does not break the run."""
    if not kids:
        return lead
    starts = {id(c.lines[0]): c for c in kids}
    keep = {id(l) for l in lead}
    out: list[Line] = []
    broken = False
    for l in lines:
        c = starts.get(id(l))
        if c is not None and out and c.lines[-1].page > out[-1].page:
            broken = True
        if id(l) in keep and not broken:
            out.append(l)
    return out


def _entries(sections: list[Section], owned) -> list[_Entry]:
    """The entry-level sections of every chapter, and the containers' leads, in reading order. A container an
    earlier type owns (`owned`: its name, or most of its lines) is not opened: its sub-sections are that entity's
    ("Far Traveler"'s "Ideals"). A sub-heading printed `_RECURRING` times or more in its chapter ("Crime",
    "Government", "Noteworthy Features") is a part of every entry, never an entry or a container's child: it folds
    into its parent's text (ruling W17, Task 7 round 2)."""
    out: list[_Entry] = []
    recurring = Counter((x.parents[0] if x.parents else "", squash(x.title)) for x in sections)

    def part(s: Section) -> bool:
        return recurring[(s.parents[0] if s.parents else "", squash(s.title))] >= _RECURRING

    def visit(group: list[Section], parent: Section | None = None, under_owned: bool = False) -> None:
        kids_of = {id(s): [c for c in _kids(s, sections) if not part(c)] for s in group}
        listing = _listing_group(group, kids_of)
        for s in group:
            kids = kids_of[id(s)]
            # a section an earlier entity owns, container or not: its sub-sections that entity owns too are its
            # text ("Far Traveler"'s "Ideals"); one it does not own is a sibling the reader nested under the
            # entity's heading (DMG "Gehenna" under the Larva stat block) and is opened all the same, never dropped
            # in silence: its place is unverified (`_Entry.under_owned`)
            if kids and owned(s.title, s.lines, heading=False):
                if not owned(s.title, s.lines[:1]):
                    # owned by its lines' share only (DMG "Downtime Activities" over its activity Rules): its own
                    # unclaimed prose outside the sub-sections is its lead, kept
                    inside = {id(l) for c in kids for l in c.lines}
                    lead = [l for l in s.lines if id(l) not in inside]
                    if len(lead) > 1:
                        out.append(_Entry(s, lead, lead=True))
                visit([c for c in kids if not owned(c.title, c.lines)], s, under_owned=True)
                continue
            if _container(s, group, kids, listing):
                # the lead: every line of the container outside its entries' spans: the text before the first
                # entry, and the folded parts (a recurring sub-heading's text printed among the entries)
                inside = {id(l) for c in kids for l in c.lines}
                lead = _contiguous_lead([l for l in s.lines if id(l) not in inside], s.lines, kids)
                if len(lead) > 1:
                    out.append(_Entry(s, lead, lead=True))
                visit(kids, s)
            elif kids:
                # an entry with printed sub-headings: each sub-heading is an entry of its own (Task 7 round 4), and
                # the entry holds only its lead, the text before its first sub-section (and the folded parts that
                # stay contiguous with it); the entry and its sub-sections never share text
                inside = {id(l) for c in kids for l in c.lines}
                lead = _contiguous_lead([l for l in s.lines if id(l) not in inside], s.lines, kids)
                if len(lead) > 1:
                    out.append(_Entry(s, lead, lead=True, container=parent, under_owned=under_owned, full=s.lines))
                visit(kids, s, under_owned=under_owned)
            else:  # a top section with no chapter title over it has no category: emitted all the same, never
                # dropped in silence (`type-from-context` fails naming the missing heading; ruling W24)
                out.append(_Entry(s, s.lines, container=parent, under_owned=under_owned))

    tops = [s for s in sections if s.level == 1]
    chapters: dict[str, list[Section]] = {}
    for s in tops:
        chapters.setdefault(s.parents[0] if s.parents else "", []).append(s)
    for group in chapters.values():
        visit(group)
    order = {id(s.lines[0]): k for k, s in enumerate(sections)}
    out.sort(key=lambda e: (order[id(e.section.lines[0])], not e.lead))
    return out


def _style(t: str) -> str:
    """"caps" ("LHAZAAR PRINCIPALITIES"), "title" ("Demon Wastes") or "" when OCR casing noise leaves it in doubt
    ("EldeEn ReAches")."""
    words = [x for x in re.findall(r"[A-Za-z]+", t) if len(x) > 1]
    if not words:
        return ""
    if all(x.isupper() for x in words):
        return "caps"
    if all(_title_word(x) for x in words):
        return "title"
    return ""


def _title_word(x: str) -> bool:
    """A title-case word through OCR casing noise: a capital first (or a small word in lower case), and lower case
    for most of the rest ("CarnIval" in "The Cog CarnIval"; "RANgeR" is none: three of six capitals)."""
    rest = x[1:]
    return (x[:1].isupper() or x.islower()) and (not rest or sum(c.islower() for c in rest) > len(rest) / 2)


def _heading_line(t: str) -> bool:
    """Heading words in any OCR casing ("GolGARI CREATuREs", "Dual SOcieTies, Dual SKiLls", "What Is a Monster?"):
    up to eight words, each capitalised but the small ones, one of three letters at least, no closing full stop,
    comma or semicolon (a bold lead-in ends in one), no colon (a lead-in "Capital: Flamekeep", a caption "MAP 3.4:
    ..."), no leading number (a table row)."""
    t = t.strip()
    words = re.findall(r"[A-Za-z][A-Za-z'’-]*", t)
    if not words or len(t.split()) > 8 or not t[:1].isalpha() or t.endswith((".", ",", ";", "-")) or ":" in t:
        return False
    if not any(len(x) >= 3 for x in words) or prose(t):
        return False
    return heading_words(t) or all(x[0].isupper() or x.lower() in SMALL_WORDS or re.match(r"[a-z]['’][A-Z]", x)
                                   for x in words)


def _doubt(s: Section, own: list[Line], heads: int, starts: dict[int, int], sizes: dict[int, float],
           parts: set[int] = frozenset()) -> list[str]:
    """Every heading inside the section's text that may be a sibling (or another section's float) it runs on into.
    The text is kept whole either way; this names the doubt, so `text-complete` fails on it (rulings W13, W16).

    A heading here is heading words in any OCR casing (`_heading_line`) that the reader read as a heading, that is set
    `_TALL_LINE` times the section's median line, or that opens after a finished sentence, a space, or a line that is
    no prose (a table). Inside an unfinished sentence, over lines that are no prose, it is a float (a table's title:
    MTF "Halfling Deities" in "Bad Apples"). Otherwise it is in doubt when it is

    - named in parallel with the section's own heading, with as many words ("RAKDoS CREATURES" inside "Orzhov
      Creatures"); a heading of another word count falls through to the checks below;
    - over lines that are no prose (a table) and styled like the section's own heading (all caps / title case, when
      the OCR casing is clear: "SIMIC CREATURES" inside "MERFOLK");
    - over prose (within four lines), and (c) set at least `_DOUBT` as tall as the section's own heading (ERLW
      "LHAZAAR PRINCIPALITIES" 0.89x inside "Karrnath") while it stands out from its peers: no other sub-heading the
      reader nests one level under the section, or `_PEER` times taller than every such peer, or set (reader size)
      `_PEER_SIZE` as large as the section's own heading (its peers are then siblings the reader nested); (a) styled
      like the section's own heading; or (b) set taller than the section's own first sub-headings.

    Peer sub-headings set alike and clearly smaller than the section's heading ("Inside the Senate", "Azorius
    Characters") are the section's own: no doubt."""
    head = own[0].height
    title = " ".join(l.text for l in own[:heads])
    style = _style(title)
    last = squash(title.split()[-1]) if title.split() else ""
    tall = _TALL_LINE * median(l.height for l in own)
    kid_lines = [l for l in own[heads:] if starts.get(id(l)) == s.level + 1 and id(l) not in parts]
    kids = [l.height for l in kid_lines]
    out: list[str] = []
    for k in range(heads, len(own) - 1):
        l, prev = own[k], own[k - 1]
        if not _heading_line(l.text) or id(l) in parts:
            continue  # a folded recurring sub-heading ("Government") is a part of every entry (ruling W17)
        over_prose = any(prose(x.text) for x in own[k + 1:k + 5])
        unfinished = prose(prev.text) and not prev.text.rstrip().endswith(SENTENCE_END)
        if unfinished and not prose(own[k + 1].text):
            out.append(f"{l.text.strip()!r} (p{l.page}) interrupts an unfinished sentence over lines that are no "
                       "prose: a float (a table or sidebar) printed inside its text")
            continue
        if unfinished and not (id(l) in starts or l.height >= tall or set_apart(prev, l)):
            continue  # a wrapped line of title words inside a sentence
        what = (f"{l.text.strip()!r} (p{l.page}, height {l.height:.1f}, {l.height / head:.2f}x its own heading's "
                f"{head:.1f}; {'read' if id(l) in starts else 'not read'} as a heading by the reader)")
        words = l.text.split()
        same_style = bool(style) and _style(l.text) == style
        peers = [x.height for x in kid_lines if x is not l]
        if len(words) == len(title.split()) > 1 and len(last) >= 4 and squash(words[-1]) == last \
                and squash(words[0]) != squash(title.split()[0]):
            out.append(f"{what}: named in parallel with its own heading")
        elif not over_prose:
            if same_style:
                out.append(f"{what}: styled like its own heading ({style}), over lines that are no prose")
        elif l.height >= _DOUBT * head and (not peers or l.height > _PEER * max(peers) or not s.size
                                            or id(l) in sizes and sizes[id(l)] >= _PEER_SIZE * s.size):
            out.append(f"{what}: set at least {_DOUBT}x as tall as its own heading"
                       + (", with no peer sub-heading" if not peers else
                          f" and over {_PEER}x its tallest peer sub-heading ({max(peers):.1f})"
                          if l.height > _PEER * max(peers) else
                          "; the section's own heading has no measured size, so its peers cannot be told from "
                          "siblings" if not s.size else
                          f", its reader size {sizes[id(l)]:.2f} at least {_PEER_SIZE}x its own heading's "
                          f"{s.size:.2f} (its peers are siblings the reader nested)"))
        elif same_style:
            out.append(f"{what}: styled like its own heading ({style})")
        elif kids and starts.get(id(l)) != s.level + 1 and l.height > max(kids):
            out.append(f"{what}: set taller than its own sub-headings ({max(kids):.1f})")
    out += _peer_runs(own, heads, kid_lines, out)
    return out


def _run_style(t: str) -> str:
    """A peer heading's style for `_peer_runs`, through OCR casing noise: "caps" when most of its letters are capitals
    ("RANgeR", "SORCeRER" among PALADIN and ROGUE), "title" when every word is capitalised and the rest lower case
    ("Gnome Ideals"), else ""."""
    letters = [c for c in t if c.isalpha()]
    if not letters:
        return ""
    if sum(c.isupper() for c in letters) >= 0.6 * len(letters):
        return "caps"
    return _style(t)


def _peer_runs(own: list[Line], heads: int, kid_lines: list[Line], named: list[str]) -> list[str]:
    """A run of `_RUN` or more consecutive peer sub-headings (those the reader nests one level under the section) set
    in one style, all caps or all title case, each over prose: several same-styled peers inside one section are
    probably its siblings the reader nested (ruling W18: XGE "Monk" holds PALADIN, RANGER, ROGUE, SORCERER, WARLOCK,
    WIZARD, each over its "d6 I became a ... because ..." table). Fires whatever their sizes; a heading already
    named by another doubt is not named again."""
    at = {id(l): k for k, l in enumerate(own)}
    peers = [l for l in kid_lines if at[id(l)] >= heads
             and any(prose(x.text) for x in own[at[id(l)] + 1:at[id(l)] + 5])]
    out: list[str] = []
    run: list[Line] = []
    for l in peers + [None]:
        st = _run_style(l.text) if l is not None else ""
        if l is not None and st and run and _run_style(run[-1].text) == st:
            run.append(l)
            continue
        if len(run) >= _RUN:
            fresh = [x for x in run if not any(d.startswith(repr(x.text.strip())) for d in named)]
            if fresh:
                out.append(f"{run[0].text.strip()!r} (p{run[0].page}) opens a run of {len(run)} peer sub-headings "
                           f"in one style ({_run_style(run[0].text)}: "
                           + ", ".join(repr(x.text.strip()) for x in run) + "): siblings the reader nested?")
        run = [l] if l is not None and st else []
    return out


def _stat_block(own: list[Line]) -> list[tuple[str, int]]:
    """The stat block lines printed inside the text (as printed: "Melee Weapon Attack:", "Hit: 7 (", "Armor Class
    15", "Challenge 2 (450 XP)"), two at least within `_STAT_SPAN` lines of each other; [] when none. The text keeps
    them and `text-complete` names them (EGW "Story Overview" holds an NPC's block), never a silent drop."""
    hits = [k for k, l in enumerate(own) if _STAT_LINE.search(l.text.strip())]
    near = [k for k in hits if any(j != k and abs(j - k) <= _STAT_SPAN for j in hits)]
    return [(own[k].text.strip()[:40], own[k].page) for k in near]


def _doubtful_tail(own: list[Line], heads: int) -> str:
    """The text's end in doubt: "" when not. It ends on a heading-like line (the next section's heading the reader
    did not read: EGW "ASariuS, The CiTy OF BeAStS" after "A Martial Pillar"), or its last lines sit on a page that
    holds none of its prose (map labels or another section's table: EGW "Everplume" / "Darktow" after "Bwualli")."""
    body = own[heads:]
    if not body:
        return ""
    end = body[-1].text.strip()
    if len(body) > 1 and _heading_line(end) and not end.endswith(("?", "!", ")", '"', "'")):
        return f"it ends on the heading-like line {end!r} (p{body[-1].page})"
    prose_pages = {l.page for l in body if prose(l.text)}
    stray = [l for l in body if l.page not in prose_pages]
    if len(stray) >= 2 and prose_pages and min(l.page for l in stray) > max(prose_pages):
        return (f"its last {len(stray)} lines (p{stray[0].page}: {stray[0].text.strip()[:30]!r} ...) sit on a page "
                "holding none of its prose")
    return ""


def _text_check(e: _Entry, own: list[Line], heads: int, sib_lines: list[int], sib_pages: list[int],
                starts: dict[int, int], sizes: dict[int, float] | None = None,
                sections: list[Section] = (), parts: set[int] = frozenset(),
                claimed_names: frozenset | set = frozenset()) -> Check:
    sizes = sizes or {}
    s = e.section
    name = f"section {s.title!r} (p{s.page})"
    stat = _stat_block(own)
    if stat:
        return Check("text-complete", False, f"{name} holds a stat block's lines, a Monster's not its own: "
                     + "; ".join(f"{t!r} (p{p})" for t, p in stat[:4]))
    # a section with sub-sections of their own is doubted over its whole span, as before they were entries: the
    # lead may end at a heading that is no sub-heading, and a doubted heading inside it is never silent
    doubts = _doubt(s, e.full if e.full else own, heads, starts, sizes, parts)
    if doubts:
        rest = [d.split(" (p", 1)[0] for d in doubts[_NAMED:]]
        return Check("text-complete", False, f"{name} may run on: it holds {len(doubts)} doubtful heading(s): "
                     + "; ".join(doubts[:_NAMED]) + (f"; also {', '.join(rest)}" if rest else ""))
    tail = _doubtful_tail(own, heads)
    if tail:
        return Check("text-complete", False, f"{name} may run on: {tail}")
    if odd := odd_opening(s.title, own, heads, claimed_names):
        return Check("text-complete", False, f"{name}: {odd}")
    if e.full:  # a section with sub-sections of its own: its lead ends at the first one, a heading the reader read;
        # a sub-section set like its parent's siblings is still a sibling it may have run on into (ruling W25)
        if held := held_siblings(s, sections, e.full):
            return Check("text-complete", False, f"{name} holds {held[0].title!r} (p{held[0].page}, size "
                         f"{held[0].size:.2f}), a sub-section set like its parent's siblings: a sibling it ran on "
                         "into")
        if foreign := foreign_lines(own, heads, name_words(s.title), set(starts)):
            return Check("text-complete", False, f"{name} holds {len(foreign)} line(s) that are no body text of "
                         "its own: " + "; ".join(foreign[:_NAMED]))
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
    # ruling W25: a held sub-section set like its parent's siblings (a sibling it ran on into), or a line the
    # reader classifies as a title, caption or display text that does not name the section
    if held := held_siblings(s, sections, own):
        return Check("text-complete", False, f"{name} holds {held[0].title!r} (p{held[0].page}, size "
                     f"{held[0].size:.2f}), a sub-section set like its parent's siblings: a sibling it ran on into")
    if foreign := foreign_lines(own, heads, name_words(s.title), set(starts)):
        return Check("text-complete", False, f"{name} holds {len(foreign)} line(s) that are no body text of its "
                     "own: " + "; ".join(foreign[:_NAMED]))
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
    f = {"entries": entries}
    if category:
        f["category"] = category
    if etype == "Lore" and setting:
        f["settingContext"] = setting
    return f


def parse_world(body: list[Line], ctx: ParseContext) -> list[Parsed]:
    """One Plane, Faction, Location or Lore per entry-level world section, in reading order."""
    claimed: set = ctx.cache["claimed_lines"]  # required: the assembler fills it (an empty set when none)
    names: set = ctx.cache["claimed_names"]  # required: earlier entities' names as printed
    sections = [s for s in read_sections(body, ctx) if not rules_chapter(s)]
    starts = {id(s.lines[0]): s.level for s in sections}
    sizes = {id(s.lines[0]): s.size for s in sections}
    picked: list[tuple[_Entry, list[Line]]] = []

    plain_names = {_plain(n) for n in names}

    def owned(title: str, lines: list[Line], heading: bool = True) -> bool:
        return (heading and line_key(lines[0]) in claimed or _plain(title) in plain_names
                or sum(line_key(l) in claimed for l in lines) > _CLAIMED * len(lines))

    entries = _entries(sections, owned)
    entry_titles = {x.section.title for x in entries if len(x.section.title) >= 4}  # other entries' names too
    spans = [{id(l) for l in e.lines} for e in entries]
    for i, e in enumerate(entries):
        s = e.section
        # a section whose heading line, or whose name, an earlier entity owns is that entity's; one most of whose
        # lines are owned keeps its own unclaimed prose below (DMG "Downtime Activities": its intro over the
        # activity Rules), never dropped in silence (Task 7 round 2)
        if line_key(e.lines[0]) in claimed or _plain(s.title) in plain_names:
            continue
        # the lines of another entry that starts inside this one are that entry's
        nested = {x for j, d in enumerate(entries) if j != i and id(d.lines[0]) in spans[i]
                  and id(d.lines[0]) != id(e.lines[0]) for x in spans[j]}
        own = [l for l in e.lines if id(l) not in nested and line_key(l) not in claimed]
        heads = _heads(own, s.title)
        own = drop_dangling(drop_art(own, ctx), e.lines, heads, set(starts))
        own = trim_own(own, s.title, heads, set(starts), {x.title for x in sections if x is not s}, ctx)
        if e.full:  # the whole span, without what the lead's own reading takes out (for the doubts about it)
            e.full = drop_art([l for l in e.full if line_key(l) not in claimed], ctx)
        n_prose = sum(prose(l.text) for l in own[heads:])
        if n_prose < _MIN_PROSE and not (e.lead and n_prose):
            continue  # no prose of its own: nothing to read (a table's caption over its rows)
        # a section with prose is never dropped in silence (ruling W24): one the reading cannot vouch for is emitted
        # with a failing `entry-level` check naming why
        why = ""
        # over the whole section, not the entry's own lines: an owned block's lead is built from the unclaimed
        # lines, and it is the block that is theirs (PHB "Martial Archetypes" over its claimed subclasses)
        claimed_share = sum(line_key(l) in claimed for l in s.lines) / max(1, len(s.lines))
        if squash(s.title) in _STAT_HEADINGS:
            why = f"the heading {s.title!r} (p{s.page}) is a stat block's part, a Monster's not a section of its own"
        elif e.under_owned:
            why = (f"the heading {s.title!r} (p{s.page}) sits under {e.container.title!r} (p{e.container.page}), "
                   "another entity's section: a sibling the reader nested there, its place unverified")
        elif claimed_share > _CLAIMED:
            # most of its lines are earlier entities' (a class's subclasses, a chapter's feats): only its own
            # unclaimed prose is kept, and its place among them is unverified (Task 7 round 2)
            why = (f"{s.title!r} (p{s.page}) holds other entities' text for {100 * claimed_share:.0f}% of its lines: "
                   "its own prose is the rest, its place unverified")
        elif n_prose < _MIN_PROSE:  # a container's one-sentence lead (DMG "Other Planes"; ruling W25)
            why = (f"the lead of {s.title!r} (p{s.page}) holds one prose line only, under {_MIN_PROSE}: too short to "
                   "vouch for")
        elif not e.lead and not any(prose(l.text) for l in own[heads:heads + 2]) \
                and not _sub_heading_over_prose(own, heads, starts):
            # a table's header or caption read as a heading has no prose straight under it ("Suggested Domains
            # Symbol"); a display title's block ("Domain of ..." / "Darklord: ...") is no prose either
            why = (f"no prose straight under the heading {s.title!r} (p{s.page}): a table's header, a caption or a "
                   "display block read as a heading")
        picked.append((e, own, why))
    setting = SETTINGS.get(ctx.source.upper())
    groups: dict[tuple, list[float]] = {}
    for e, _, _ in picked:
        groups.setdefault(tuple(e.section.parents), []).append(e.section.size)
    # a heading printed twice among one parent's children (read or not as an entry)
    counts = Counter((tuple(x.parents), squash(x.title)) for x in sections)
    repeats: dict[tuple, str] = {}
    for x in sections:
        if counts[(tuple(x.parents), squash(x.title))] > 1:
            repeats.setdefault(tuple(x.parents), x.title)
    in_chapter = Counter((x.parents[0] if x.parents else "", squash(x.title)) for x in sections)
    # the heading lines of the recurring sub-headings (ruling W17): parts of every entry, no doubt of their own
    parts = {id(x.lines[0]) for x in sections if in_chapter[(x.parents[0] if x.parents else "", squash(x.title))]
             >= _RECURRING}
    placed = placement_doubts(sections, chapter_pages(body, ctx), claimed, ctx.cache.get("god_names", set()), body)
    out: list[Parsed] = []
    lead_type: dict[int, str] = {}  # a container's emitted lead: its type
    for e, own, why in picked:
        s = e.section
        sibs = [o for x, o, _ in picked if x is not e and not x.lead and x.section.parents == s.parents]
        heads = _heads(own, s.title)
        entries_ = prose_entries(own, heads, len(own))
        flat = [x if isinstance(x, str) else "\n".join(x.get("items", [])) for x in entries_]
        body_text = "\n".join(flat)
        etype, category, typed = section_type(s.parents, proper_name(s.title, body_text),
                                              s.size >= _main_size(groups[tuple(s.parents)]),
                                              repeats.get(tuple(s.parents), ""))
        if e.lead:
            lead_type[id(s)] = etype
        head = " ".join(l.text.strip() for l in own[:heads])
        p = Parsed(type=etype, name=fix_acronyms(s.title), fields=_fields(etype, category, entries_, setting),
                   text="\n".join([head] + flat), lines=own)
        par_sec = parent_section(s, sections)
        p.category_raw = par_sec.lines[0].text if par_sec is not None and par_sec.lines else ""
        p.checks = [_text_check(e, own, heads, [len(o) for o in sibs], [o[-1].page - o[0].page + 1 for o in sibs],
                                starts, sizes, sections, parts, entry_titles), typed,
                    _entry_check(e, lead_type, in_chapter[(s.parents[0] if s.parents else "", squash(s.title))],
                                 placed[id(s)] or why
                                 or ("" if has_sentence(own[heads:]) else
                                     f"the text of {s.title!r} (p{s.page}) holds no sentence: a table read as a "
                                     "section"))]
        out.append(p)
    return out


def _entry_check(e: _Entry, lead_type: dict[int, str], recurs: int, doubt: str = "") -> Check:
    """`entry-level`: the section is an entry of its chapter, not a sub-heading of one that the entry-level reading
    picked (ruling W16). It fails when its container is itself an entry typed Plane, Faction or Location (VRGR
    Darkon's "Noteworthy Features" is a part of the domain), or when its heading recurs in its chapter
    (`_RECURRING` times or more: ERLW "Symbol", "Rites", "Temples" under each faith; EGW "Crime" under each city), a
    sub-heading label every entry prints, never an entry of its own."""
    s = e.section
    if e.container is not None and lead_type.get(id(e.container), "Lore") != "Lore":
        return Check("entry-level", False, f"section {s.title!r} (p{s.page}) is a sub-section of the "
                     f"{lead_type[id(e.container)]} entry {e.container.title!r}, not an entry of its own")
    if not e.lead and recurs >= _RECURRING:
        return Check("entry-level", False, f"the heading {s.title!r} (p{s.page}) recurs {recurs} times in its "
                     "chapter: a sub-heading label of its entries, not an entry of its own")
    if doubt:
        return Check("entry-level", False, doubt)
    return Check("entry-level", True, "")


# a finished sentence: a lower-case word ending in a full stop, question or exclamation mark ("... the coast.");
# a table row ("Abbey 50,000 gp 400 days") prints none
_SENTENCE = re.compile(r"[a-z][.!?](?:[\"')\]]|\s|$)")


def has_sentence(lines: list[Line]) -> bool:
    return any(_SENTENCE.search(l.text) for l in lines)


def parent_section(s: Section, sections: list[Section]) -> Section | None:
    """The section of the printed heading enclosing `s` (`s.parents[-1]`); None when that is a chapter title.

    Lines are matched by `id()`: `read_sections` hands the same `Line` objects to a section and to the sections it
    encloses (every span is a slice of one list), so identity is the printed line's identity here."""
    if len(s.parents) < 2 and s.level <= 1:
        return None
    first = id(s.lines[0])
    for c in sections:
        if (c.level == s.level - 1 and c.parents == s.parents[:-1] and c.title == s.parents[-1]
                and any(id(l) == first for l in c.lines)):
            return c
    return None


def garbage_heading(title: str) -> bool:
    """A heading that holds more noise tokens (no letter, or a lone letter) than words of three letters or more, or
    a signed number ("(+1)", "+2", "-1": an ability-score or modifier column): OCR debris or a table row read as a
    heading ("Q Yes 83", "Drow (+1) Human (+1)"), never a heading that encloses sections."""
    toks = bare_heading(title).split()
    words = sum(bool(re.fullmatch(r"[^A-Za-z]*[A-Za-z][A-Za-z'’-]{2,}[^A-Za-z]*", t)) for t in toks)
    noise = sum(not re.search(r"[A-Za-z]{2}", t) for t in toks)
    return words == 0 or noise > words or bool(_SIGNED.search(title))


_SIGNED = re.compile(r"(?:^|[\s(])[+\-−–]\d")  # a signed number: a table's modifier, never a heading's word
_DIE_HEADER = re.compile(r"^d\d{1,3}(?![0-9A-Za-z])")  # "d6 Villain", "d100 Result": a table's die column header
_TABLE_ROW = re.compile(r"^\d{1,3}(?:[-–]\d{1,3})?\s+[A-Z(]")  # "1 The villain ...", "01-05 Aberration"
_ROWS = 2  # this many table rows among the first lines under a heading make it a table's title


def table_title(lines: list[Line], title: str) -> str:
    """Why the heading over `lines` is a table's title read as a heading ('' when it is not): the line under it is a
    die column header ("d6 Villain"), or `_ROWS` of the first four lines under it are numbered rows ("1 The villain
    is targeting ..."). A table's rows read as prose pass the prose tests; its title is no section (ERLW "Villain
    You Know", the d6 table under "The Villain You Know")."""
    body = lines[_heads(lines, title):]
    if not body:
        return ""
    first = body[0].text.strip()
    if _DIE_HEADER.match(first):
        return f"the line under it is a die-table header ({first!r})"
    rows = [l.text.strip() for l in body[:4] if _TABLE_ROW.match(l.text.strip())]
    if len(rows) >= _ROWS:
        return f"its text opens with numbered table rows ({rows[0][:40]!r}, ...)"
    return ""


_CLOSE_MID = 0.06  # a stop within this (relative) of the midpoint it had to reach leaves the section's end in doubt
_CLIPPED = {"of", "to", "the", "and", "in", "a", "an", "for", "with", "on", "at", "by", "from"}


def clipped_heading(title: str) -> bool:
    """A heading that ends in a function word lost its last line ("Fizban's Treasury of", "Welcome to")."""
    words = re.findall(r"[A-Za-z']+", bare_heading(title))
    return bool(words) and words[-1].lower() in _CLIPPED


def chapter_pages(body: list[Line], ctx: ParseContext) -> dict[int, set[str]]:
    """The squashed chapter titles printed on each page (`chapter_titles`)."""
    if not body:
        return {}
    B = character_body(body, ctx)
    out: dict[int, set[str]] = {}
    for ks, title in chapter_titles(B):
        out.setdefault(B[ks[0]].page, set()).add(squash(title))
    return out


def _names_a_god(title: str, gods: dict[str, list[str]]) -> str | None:
    """The god a heading names by the god's own name, as whole words, beside other words ("Commandments of Melora"
    names Melora; "Melora" alone is the god's own heading, and the God parser owns it)."""
    words = re.findall(r"[a-z]+", title.lower())
    for g, want in gods.items():
        n = len(want)
        if want and len(words) > n and any(words[i:i + n] == want for i in range(len(words) - n + 1)):
            return g
    return None


HELD_SIBLING = "set like its parent's siblings"  # the reason of a section holding one: its own, never inherited


def held_siblings(s: Section, sections: list[Section], lines: list[Line] | None = None) -> list[Section]:
    """The sections opening inside the section `s` (or inside the entity's own `lines`, when given) that are set like
    `s`'s own siblings: at least (1 - `_SUB_STEP`) times the median size of `s`'s sibling group (the sections sharing
    its parents and level, two at least besides it). A section holding one holds a sibling the reader nested (PHB
    "Cover" holding "Damage and Healing", SCAG "Bards" holding "Clerics") or sits a level too deep (PHB
    "Fantasy-Historical Pantheons" under "D&D Pantheons", its parts set like the pantheons); ruling W25. A
    sub-section set like the parts of its own level (GGR "Rank and Renown" under "Azorius Characters") is none."""
    sizes = [o.size for o in sections if o is not s and o.level == s.level and o.parents == s.parents and o.size]
    if len(sizes) < 2:
        return []
    bound = (1 - _SUB_STEP) * median(sizes)
    inside = {id(l) for l in (lines if lines is not None else s.lines)[1:]}
    return [c for c in sections if c is not s and id(c.lines[0]) in inside and c.size and c.size >= bound]


def placement_doubts(sections: list[Section], chapters: dict[int, set[str]],
                     claimed: set = frozenset(), gods: set = frozenset(), body: list[Line] = ()) -> dict[int, str]:
    """For each section (by `id`), why its place in the reader's tree, and so its parent heading, category, type and
    W11 id, is not verified ('' when it is; ruling W21, W22). Every reason names the headings involved.

    - its first page prints a chapter title that is not its own chapter (a chapter title read after the column it
      opens: the section is filed under the previous chapter);
    - its own heading is OCR debris or a table row (`garbage_heading`: PHB "Drow (+1) Human (+1)"), or a table's
      title (`table_title`: a die column header or numbered rows straight under it; ruling W24);
    - it follows a section whose end is in doubt (`close`: the stopping heading is barely larger than that section's
      widest sub-heading) and is set no larger than that section's sub-headings (within `_SUB_STEP` of the widest);
    - its parent heading ends in a function word (`clipped_heading`: its last line was not read);
    - its heading names a god (`gods`, the God entities' names) beside other words: a part of that god's text (EGW
      "Commandments of Melora");
    - it ended the sibling before it barely over the midpoint the reader's end rule needs (`_CLOSE_MID`): it may
      be one more sub-section of that sibling (XGE "Origins" ending "This Is Your Life");
    - it comes after a sibling an earlier entity owns (`claimed`, line keys) or that is a stat block (a monster's
      heading the reader read as a section: DMG "Larva" ending "Outer Planes"), and is set no larger than it (by
      `_SUB_STEP`), as is every sibling between: the intruder cut the section before it, and the siblings after it
      may be that section's sub-sections (EGW "Commandments of Avandra" after the god Avandra's section; DMG
      "Gehenna" ... "Acheron" after "Larva"; ruling W24). The run ends at the first sibling set larger;
    - its heading line has a taller copy on its page (`body`, the printed lines; the same words set at least
      (1 + `_SUB_STEP`) times taller): the reader measured a running foot or a small print of a display title (SCC
      "WELCOME TO STRIXHAVEN" at the page foot under the title "Welcome to Strixhaven"). The siblings after it set
      clearly smaller than that copy may be its sub-sections (ruling W24); the run ends at the first sibling as tall;
    - it is set like the siblings of its parent: at least (1 - `_SUB_STEP`) times the smallest earlier sibling of its
      parent (SCAG "Clerics" under "Bards", after "Barbarians"; ruling W23); and a section with such a sub-section
      is doubted itself: it sits a level too deep, or holds a sibling (PHB "Fantasy-Historical Pantheons" under
      "D&D Pantheons"; ruling W25);
    - it is a top section of a chapter whose title was read late, set clearly smaller (by `_SUB_STEP`) than the
      section before it that is filed under the previous chapter (the chapter opener, doubted for printing another
      chapter's title): it may be that opener's sub-section (SCC "A Strixhaven Campaign" after "RUNNING THE
      ADVENTURES"; ruling W25). The run ends at the first top section as large as the opener;
    - its parent heading is OCR debris (`garbage_heading`), repeats its own heading (a table title read under its
      section), holds no sentence of its own before its first sub-section (a table caption or title read as a
      heading), or prints the section's heading more than once among its sub-sections;
    - it prints one of its own sub-headings more than once (entry headings inside it were not read: XGE "MONK" over
      the class-training rows printed twice; ruling W23), which every section under it inherits;
    - its parent is doubted (inherited: a doubted parent doubts every section under it)."""
    parent_of = {id(s): parent_section(s, sections) for s in sections}
    copies = _taller_copies(sections, body)
    counts = Counter((tuple(x.parents), squash(x.title)) for x in sections)
    kids: dict[int, list[Section]] = {}
    for s in sections:
        if (par := parent_of[id(s)]) is not None:
            kids.setdefault(id(par), []).append(s)
    # a section that prints one of its sub-headings twice holds entries whose headings were not read: its own extent,
    # and so every section under it, is unverified (XGE "MONK", a class-training table row read as a heading over
    # "Paladin" ... "Wizard" twice; VRGR "Dementlieu" over the unread domains)
    repeats_in: dict[int, str] = {}
    for pid, ks in kids.items():
        seen: set[str] = set()
        for c in ks:
            if squash(c.title) in seen:
                repeats_in[pid] = c.title
                break
            seen.add(squash(c.title))
    order = {id(x): k for k, x in enumerate(sections)}
    after_close: dict[int, Section] = {}  # a section set like the sub-headings of a section ended in doubt before it
    for x in sections:
        subs = [c.size for c in kids.get(id(x), []) if c.size]
        if not x.end_reason.startswith("close") or not subs:
            continue
        # set no larger than the closed section's widest sub-heading (by `_SUB_STEP`): the run ends at the first
        # sibling set clearly larger than its sub-headings (MM's monsters after "Chuul" are set like Chuul)
        bound = (1 + _SUB_STEP) * max(subs)
        for o in sections[order[id(x)] + 1:]:
            # the run stays in the closed section's own sibling group (MM's top sections hold no chapter: the
            # appendices' sections, under a title, are another group)
            if o.level < x.level or o.parents[:len(x.parents)] != x.parents \
                    or o.level == x.level and o.parents != x.parents:
                break
            if o.level != x.level:
                continue
            if not (o.size and o.size <= bound):
                break
            after_close.setdefault(id(o), x)
    # a parent's earlier siblings: a sub-section set at least (1 - `_SUB_STEP`) times the smallest of them is set like
    # a sibling of its parent, not like its sub-heading (SCAG "Clerics" under "Bards", after "Barbarians")
    sib_min: dict[int, Section] = {}
    after_claimed: dict[int, Section] = {}  # the intruder before it (owned by an earlier entity, or a stat block)
    near_mid: dict[int, Section] = {}  # the sibling just before, which it ended barely over the midpoint
    under_copy: dict[int, Section] = {}  # the sibling before it whose heading has a taller copy on its page
    groups: dict[tuple, list[Section]] = {}
    intruder: dict[tuple, Section] = {}  # per group: the owned or stat-block sibling whose run is open
    copied: dict[tuple, Section] = {}  # per group: the sibling with a taller copy whose run is open
    for x in sections:
        key = (tuple(x.parents), x.level)
        g = groups.setdefault(key, [])
        sized = [o for o in g if o.size]
        if sized:
            sib_min[id(x)] = min(sized, key=lambda o: o.size)
        # it ended the sibling before it barely over the midpoint between that sibling's heading and its widest
        # sub-heading (`sections._ends`): it may be one more sub-heading of it (XGE "Origins" ending "This Is Your
        # Life")
        if g and (subs := [c.size for c in kids.get(id(g[-1]), []) if c.size]) and g[-1].size and x.size \
                and x.size < (1 + _CLOSE_MID) * (g[-1].size + max(subs)) / 2:
            near_mid[id(x)] = g[-1]
        # set no larger than an intruder before it (a sibling an earlier entity owns, or a stat block: a god, a
        # race, a monster's heading), it and every sibling between may be parts of the section the intruder cut
        # (EGW "Commandments of Avandra" after the god's section; DMG "Gehenna" ... "Acheron" after "Larva"); the
        # run ends at the first sibling set clearly larger than the intruder
        if line_key(x.lines[0]) in claimed or squash(x.title) in _STAT_HEADINGS or _stat_block(x.lines):
            intruder[key] = x
        elif (c := intruder.get(key)) is not None:
            if c.size and x.size < (1 + _SUB_STEP) * c.size:
                after_claimed[id(x)] = c
            else:
                intruder.pop(key)
        # set clearly smaller than the taller copy of a sibling's heading before it: the reader measured that
        # sibling from a running foot or a small print, and this may be its sub-section (SCC "About This Book" after
        # "WELCOME TO STRIXHAVEN"); the run ends at the first sibling as tall as the copy
        if id(x) in copies:
            copied[key] = x
        elif (c := copied.get(key)) is not None:
            if x.lines[0].height * (1 + _SUB_STEP) <= copies[id(c)]:
                under_copy[id(x)] = c
            else:
                copied.pop(key)
        g.append(x)
    # a chapter opener filed under the previous chapter (its page prints the new chapter's title): the new
    # chapter's top sections after it, set clearly smaller than it, may be its sub-sections
    def misfiled(s: Section) -> bool:
        """Its page prints a chapter title that is not its own (none, when it precedes the book's first title)."""
        here = chapters.get(s.page, set())
        return bool(here and (not s.parents or s.parents[0] == UNKNOWN_CHAPTER or squash(s.parents[0]) not in here))
    after_opener: dict[int, Section] = {}
    for x in sections:
        if not x.size or not misfiled(x):
            continue  # any misfiled section, at any level (VRGR "Horror Monsters" under a garbage parent)
        for o in sections[order[id(x)] + 1:]:
            if o.level != 1 or o.parents[:1] == x.parents[:1]:
                continue  # the opener's own chapter group, still before the new title in reading order
            if not o.size or o.size >= (1 - _SUB_STEP) * x.size:
                break
            after_opener.setdefault(id(o), x)
    held_by_parent = {id(c) for p in sections for c in held_siblings(p, sections)}
    all_starts = {id(x.lines[0]) for x in sections}
    # an unread display line (ERLW "RACES") over prose: the top sections after it set clearly smaller may be its
    # sub-sections; the run ends at the first top section as tall, or at another chapter
    under_display: dict[int, Line] = {}
    if body:
        tops = [s for s in sections if s.level == 1]
        at = {line_key(l): k for k, l in enumerate(body)}
        pos = {id(s): at.get(line_key(s.lines[0]), -1) for s in tops}
        for d in display_lines(list(body), {}):
            k0 = at.get(line_key(d))
            # a chapter title's line is no display heading (VRGR "MonsterS Of RavenLoft")
            if k0 is None or any(squash(d.text) in c for c in chapters.get(d.page, ())):
                continue
            after = sorted((o for o in tops if pos[id(o)] > k0), key=lambda o: pos[id(o)])
            # the display line heads the top sections right after it: the first must start on its page or the
            # next (a map label or art text pages before a section governs nothing: SCAG "Shaar" p11, "A Brief
            # History" p16); and the run ends at a top section set like the chapter's top sections printed before
            # the display line (DMG "Outer Planes" after "THE ELEMENTAL PLANES", among Astral Plane and the rest)
            if not after or after[0].page > d.page + 1:
                continue
            chapter = after[0].parents[:1]
            before = [o.size for o in tops if pos[id(o)] < k0 and o.parents[:1] == chapter and o.size]
            floor = (1 - _SUB_STEP) * median(before) if len(before) >= 2 else None
            for o in after:
                if o.parents[:1] != chapter or o.lines[0].height * (1 + _SUB_STEP) > d.height \
                        or floor is not None and o.size >= floor:
                    break
                under_display.setdefault(id(o), d)
    god_words = {g: re.findall(r"[a-z]+", re.split(r",| \(", g)[0].lower()) for g in gods}
    memo: dict[int, tuple[str, str]] = {}

    def own(s: Section) -> str:
        if misfiled(s):
            return (f"the page of {s.title!r} (p{s.page}) prints another chapter's title than its chapter "
                    f"{s.parents[0]!r}: it is filed under the wrong chapter"
                    if s.parents and s.parents[0] != UNKNOWN_CHAPTER else
                    f"the page of {s.title!r} (p{s.page}) prints a chapter title, yet it is filed under none "
                    "read: read before the title it belongs to")
        if (d := under_display.get(id(s))) is not None:
            return (f"the heading {s.title!r} (p{s.page}, height {s.lines[0].height:.1f}) is set clearly smaller "
                    f"than the display line {d.text.strip()!r} (p{d.page}, height {d.height:.1f}) printed before "
                    "it, which the reader did not read as a heading: it may be a sub-section of it"
                    + (f", not of {s.parents[-1]!r}" if s.parents else ""))
        if (x := after_opener.get(id(s))) is not None:
            return (f"the heading {s.title!r} (p{s.page}, size {s.size:.2f}) is set clearly smaller than {x.title!r} "
                    f"(p{x.page}, size {x.size:.2f}), the chapter opener filed under {x.parents[0]!r}: it may be a "
                    f"sub-section of {x.title!r}, not of {s.parents[-1]!r}")
        if garbage_heading(s.title):
            return f"the heading {s.title!r} (p{s.page}) is no heading (OCR debris or a table row)"
        if t := table_title(s.lines, s.title):
            return f"the heading {s.title!r} (p{s.page}) is a table's title read as a heading: {t}"
        if (x := after_close.get(id(s))) is not None:
            return (f"the heading {s.title!r} (p{s.page}) is set like the sub-headings of {x.title!r} (p{x.page}), "
                    f"whose end is in doubt: it may be a sub-section of {x.title!r}, not of {s.parents[-1]!r}"
                    if s.parents else f"the heading {s.title!r} (p{s.page}) may be a sub-section of {x.title!r}")
        if (g := _names_a_god(s.title, god_words)) is not None:
            return (f"the heading {s.title!r} (p{s.page}) names the god {g!r}: a part of that god's text, not a "
                    f"section of {s.parents[-1]!r}" if s.parents else
                    f"the heading {s.title!r} (p{s.page}) names the god {g!r}: a part of that god's text")
        if (c := near_mid.get(id(s))) is not None:
            return (f"the heading {s.title!r} (p{s.page}, size {s.size:.2f}) ends {c.title!r} (p{c.page}, size "
                    f"{c.size:.2f}) barely over the midpoint to its widest sub-heading: it may be a sub-section of "
                    f"{c.title!r}")
        if (c := after_claimed.get(id(s))) is not None:
            what = "another entity's section" if line_key(c.lines[0]) in claimed else "a stat block"
            return (f"the heading {s.title!r} (p{s.page}) comes after {c.title!r} (p{c.page}), {what}, set no "
                    f"larger than it: it may be a part of the section {c.title!r} cut, not of {s.parents[-1]!r}"
                    if s.parents else f"the heading {s.title!r} (p{s.page}) may be a part of the section "
                    f"{c.title!r} ({what}) cut")
        if (c := under_copy.get(id(s))) is not None:
            return (f"the heading {s.title!r} (p{s.page}, height {s.lines[0].height:.1f}) is set clearly smaller "
                    f"than the taller copy ({copies[id(c)]:.1f}) of the heading {c.title!r} printed on p{c.page}, "
                    f"which the reader measured from a smaller print ({c.lines[0].height:.1f}): it may be a "
                    f"sub-section of {c.title!r}" + (f", not of {s.parents[-1]!r}" if s.parents else ""))
        if id(s) in repeats_in:
            return (f"the section {s.title!r} (p{s.page}) prints its sub-heading {repeats_in[id(s)]!r} more than once: "
                    "entry headings inside it were not read, so its extent is unverified")
        if s.parents and clipped_heading(s.parents[-1]):
            return (f"its parent heading {s.parents[-1]!r} over {s.title!r} (p{s.page}) ends in a function word: "
                    "a clipped heading (its last line was not read)")
        par = parent_of[id(s)]
        if par is None:
            # a top section whose heading its chapter prints twice at the same level: one of them is no entry of
            # the chapter (XGE "MONK", a class-training table row, after the Monk class options)
            if s.parents and (n := counts[(tuple(s.parents), squash(s.title))]) > 1:
                return (f"the heading {s.title!r} (p{s.page}) is printed {n} times at the top of its chapter "
                        f"{s.parents[-1]!r}: an entry heading over it was not read")
            return holds(s)
        where = f"its parent heading {par.title!r} (p{par.page})"
        if line_key(par.lines[0]) in claimed:
            return (f"{where} over {s.title!r} (p{s.page}) is another entity's section: a sibling the reader nested "
                    "under that entity's heading, not its sub-section")
        if (least := sib_min.get(id(par))) is not None and s.size and s.size >= (1 - _SUB_STEP) * least.size:
            return (f"the heading {s.title!r} (p{s.page}, size {s.size:.2f}) is set like the siblings of {where} "
                    f"({least.title!r}, size {least.size:.2f}): a sibling of it, not its sub-section")
        if id(s) in held_by_parent:  # ruling W25: by the median of the parent's siblings (SCAG "Trielta Hills")
            return (f"the heading {s.title!r} (p{s.page}, size {s.size:.2f}) is set like the siblings of {where} "
                    "(their median size): a sibling of it, not its sub-section")
        if garbage_heading(par.title):
            return f"{where} over {s.title!r} (p{s.page}) is no heading (OCR debris or a table row)"
        if squash(par.title) == squash(s.title):
            return f"the heading {s.title!r} (p{s.page}) repeats {where}: a table title read as a sub-section"
        if (n := counts[(tuple(s.parents), squash(s.title))]) > 1:
            return (f"the heading {s.title!r} (p{s.page}) is printed {n} times under {where}: the parent is not its "
                    "nearest enclosing heading (an entry heading between them was not read)")
        span = {id(l) for l in s.lines}
        starts = {id(c.lines[0]) for c in sections if c.level == s.level and c.parents == s.parents}
        k = next((i for i, l in enumerate(par.lines) if id(l) in starts or id(l) in span), len(par.lines))
        if not has_sentence(par.lines[_heads(par.lines, par.title):k]):
            return (f"{where} holds no sentence of its own before {s.title!r} (p{s.page}): a table caption or title "
                    "read as a heading, not the heading enclosing it")
        # an unread title between its parent's heading and its own (the structural net's signal: a title-like
        # line set apart, over prose, naming neither): the section may belong to that heading, not to its parent
        # (DMG "Wilderness Hazards" under the OCR-mangled "WIlDErNESS SURVIVaL", read under "Mapping a Wilderness");
        # only a title over prose heads sections: a caption or a map label set large does not
        if h := holds(s):
            return h
        first = next((i for i, l in enumerate(par.lines) if id(l) == id(s.lines[0])), None)
        if first:
            between = par.lines[_heads(par.lines, par.title):first]
            whole = {re.sub(r"\s+", " ", re.sub(r"[^A-Za-z0-9 ]", "", par.title).lower()).strip()}
            if between and (f := [x for x in foreign_lines([par.lines[0]] + between, 1, whole, all_starts)
                                  if "title over prose" in x]):
                return (f"an unread title sits between {where} and {s.title!r} (p{s.page}): {f[0]}; it may be a "
                        f"sub-section of that title, not of {par.title!r}")
        if par.end_reason.startswith("holds") and s.size and par.size and s.size * (1 + 3 * _SUB_STEP) > par.size:
            # the parent held a heading not clearly smaller than its own: its "sub-sections" may be its peers, so it
            # is no sure category (PHB "Age" holding "Size", "Sex" holding "Alignment")
            return (f"{where} over {s.title!r} (p{s.page}) holds a heading it is not clearly larger than "
                    f"({par.end_reason[:90]}): a peer or a lead-in, not a sure category")
        if s.size and par.size and s.size * (1 + 2 * _SUB_STEP) > par.size and any(
                o is not par and o.parents == par.parents and o.level == par.level and order[id(o)] < order[id(par)]
                and not kids.get(id(o)) and o.size and abs(o.size - par.size) <= _SUB_STEP * par.size
                for o in sections):
            # the parent is set like an earlier sibling that holds nothing, and its sub-section barely smaller:
            # a lead-in among lead-ins, not a heading enclosing the next one (DMG "Town" over "City")
            return (f"{where} over {s.title!r} (p{s.page}) is set like an earlier sibling with no sub-sections "
                    f"(size {par.size:.2f}) and barely larger than {s.title!r} ({s.size:.2f}): a peer lead-in, not a "
                    "sure category")
        return ""

    def holds(s: Section) -> str:
        """The section holds a sub-section set like its parent's siblings (ruling W25): it sits a level too deep
        (PHB "Fantasy-Historical Pantheons" under "D&D Pantheons") or holds a sibling (SCAG "Bards" holding
        "Clerics"). Its own doubt, not inherited (`doubt`)."""
        if parent_type(s.title)[0] is not None:
            return ""  # a heading that lists entries by its own words holds parts set like entries
        held = held_siblings(s, sections)
        if not held:
            return ""
        c = held[0]
        return (f"the section {s.title!r} (p{s.page}, size {s.size:.2f}) holds {c.title!r} (p{c.page}, size "
                f"{c.size:.2f}), {HELD_SIBLING}" + (f" under {s.parents[-1]!r}" if s.parents else "")
                + ": it sits a level too deep, or holds a sibling")

    def doubt(s: Section) -> tuple[str, str]:
        """(the reason, the reason the section's descendants inherit). A held-sibling doubt is the section's own
        (its level or its extent): its other sub-sections keep their verified place, so it is not inherited; an
        ancestor's doubt still passes through it."""
        if id(s) in memo:
            return memo[id(s)]
        memo[id(s)] = ("", "")  # no cycles in a tree; a guard all the same
        par = parent_of[id(s)]
        up = doubt(par) if par is not None else ("", "")
        if r := own(s):
            memo[id(s)] = (r, up[1] if HELD_SIBLING in r else r)
        elif up[1]:
            memo[id(s)] = (f"its parent heading {par.title!r} (p{par.page}) over {s.title!r} (p{s.page}) is "
                           f"doubted: {up[1]}", up[1])
        return memo[id(s)]

    return {id(s): doubt(s)[0] for s in sections}


def _taller_copies(sections: list[Section], body: list[Line]) -> dict[int, float]:
    """For each section (by `id`) whose heading line has a taller copy on its page (the same squashed words, set at
    least (1 + `_SUB_STEP`) times taller: a display title the reader measured from a running foot or a small print),
    the copy's height."""
    if not body:
        return {}
    by_page: dict[int, list[Line]] = {}
    for l in body:
        by_page.setdefault(l.page, []).append(l)
    out: dict[int, float] = {}
    for s in sections:
        own = s.lines[:_heads(s.lines, s.title)]
        keys = {line_key(l) for l in own}
        want = squash(s.title)
        h = max(l.height for l in own)
        tall = [l.height for l in by_page.get(s.page, []) if l.height >= (1 + _SUB_STEP) * h
                and line_key(l) not in keys and squash(l.text) == want]
        if want and tall:
            out[id(s)] = max(tall)
    return out


def _main_size(sizes: list[float]) -> float:
    """The smallest heading size among a sibling group's main entries: above the group's largest jump in size when it
    jumps by `_GAP` (GGR's guilds over "Guild Membership"; VRGR's Darkon and Dementlieu over "Currency"), else all."""
    s = sorted(sizes)
    jumps = [(s[i + 1] / s[i], s[i + 1]) for i in range(len(s) - 1) if s[i] > 0]
    best = max(jumps, default=(1.0, 0.0))
    return best[1] if best[0] >= _GAP else 0.0


def _sub_heading_over_prose(own: list[Line], heads: int, starts: set[int]) -> bool:
    """Straight under the heading, a sub-heading the reader read, over prose."""
    return heads + 1 < len(own) and id(own[heads]) in starts and prose(own[heads + 1].text)


def _heads(lines: list[Line], title: str) -> int:
    """How many of the first lines are the heading (a heading may wrap onto a second line)."""
    want, k, got = squash(title), 0, ""
    while k < min(3, len(lines)):
        got += squash(lines[k].text)
        if not got or got not in want:
            break
        k += 1
    return max(k, 1)
