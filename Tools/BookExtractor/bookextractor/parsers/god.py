"""God parser: one God per printed deity-table row, with the deity's own prose section as its text.

**Deity tables** are found on any page by their header row, never by page number. The row ends in a symbol column
("Symbol", "Common Symbol") and holds a domains column ("Suggested Domains", "Domains", "Suggested Cleric
Domains"); between them sit "Alignment" and, in later books, "Province" (or EGW's "Warlock Patron"). The first header
cell is "Deity", or the name of the group of gods the rows list ("The Sovereign Host", "The Gods of Good"). The OCR
sometimes reads two header cells as one line ("Alignment Province"): the line is cut at the word boundary by its
character offsets before the table is read (`tables.read_row_tables`, one Row per label).

- **Name** is the label cell up to ", god of ..." / ", goddess of ..." (PHB, SCAG print "Anubis, god of judgment and
  death"); the rest is the deity's `province` as printed. A table with a Province (or Warlock Patron) column takes
  `province` from that column.
- **Pantheon** comes from the table's title, the line set just above the header row: "Deities of the Forgotten
  Realms" -> Forgotten Realms, "The Dwarven Pantheon" -> Dwarven, "Celtic Deities" -> Celtic, "Gods of the
  Ordning" -> Ordning, "Elf Deities (The Seldarine)" -> Elf (category The Seldarine; the book prints "Elf", 5etools
  stores "Elven"). A title naming a rank of gods, not a people or a world ("Prime Deities", "Betrayer Gods", "Lesser
  Idols"), is the `category`; the pantheon then comes from the heading the table is printed under ("Pantheon of
  Exandria"), when there is one. A table printed straight under another (ERLW "The Dark Six" under "The Sovereign
  Host") has the title of the table above it. The group-name first header cell, or a sub-heading row ("The Gods of
  Neutrality"), is the `category`.
- **Fields** are read from the cells as printed: `alignment` in 5etools letters ("LN" -> ["L", "N"], "N" -> ["N"],
  "Unaligned" -> ["U"]); `domains` the comma-separated domains, footnote markers ("Arcana,**") dropped; `symbol` the
  symbol cell. A column the table does not print leaves its field absent (ERLW prints no alignment: `alignment` is
  []).
- **Text**: the row as printed, then the prose of the section whose heading names the deity ("Avandra, the Change
  Bringer"), as `entries`. Only a section in the table's own chapter counts (EGW's magic item "Arms of the
  Betrayers" in chapter 6 is not the lesser idol's text). The deity's text stops at another deity's section nested
  inside it (the reader's levels are noisy: SCAG Jergal under Hoar) and at a magic item or stat block printed inside
  it (the hosted entity's own heading): the god never absorbs another entity's text. Deity-table lines inside the
  section, with their titles and footnotes, and display text (pull quotes) are no part of it. A sidebar title or
  other heading inside the section that does not name the deity ends its text.
- **Rows**: a table ends at the next header row, and at the first line laid across a column it does not start in
  (prose under the table taken into the last row). A label wrapped onto a line the table reader took for a row ("Arms
  of the" / "Betrayers") or for a sub-heading ("Cults of the Dragon" / "Below") is joined back.

Checks (each names the values involved):

- `alignment-known`: the alignment cell is one of the nine L/N/C x G/N/E alignments or "Unaligned";
- `domains-known`: every domain is one the 16 books print a cleric domain for (`DOMAINS`), and the cell is not
  blank; "no clerics" passes with no domains;
- `symbol-present`: the symbol cell is not blank and opens in capitals (a lower-case start is a wrapped symbol whose
  first line the OCR lost);
- `name-capitalised`: the label opens in capitals; the OCR reads a capital I as l the same way every time ("luz",
  "loun"), which no name repair sees, so a lower-case label fails, named, and is never repaired;
- `pantheon-repaired` (passing, only when it changed): an accented letter of the pantheon spelled as the book most
  often spells the words sharing its stem (SCAG "Faerúnian" -> "Faerûnian"), both spellings and counts named;
- `table-rows-read` / `row-complete`: as for the equipment tables (`equipment._table_checks`);
- `text-complete` (when the deity has a prose section): the section ends at a heading or chapter title the reader is
  sure of (`Section.complete`), and is not far longer than its sibling sections (a heading the OCR misread as larger
  than it is runs on over its siblings), and its text did not stop at a heading inside it that does not name the
  deity (a sidebar) with prose naming the deity again after it.

Not registered in the assembler yet (Phase 2c ruling W1): Task 6 does that.
"""
from __future__ import annotations

import re
import unicodedata
from statistics import median

from rapidfuzz import fuzz

from ..checks import Check
from ..model import Line, Page
from ..tables import Row, RowTable, read_row_tables
from . import ParseContext, Parsed
from .charcommon import SENTENCE_END, caps_heading, heading_like, name_case, prose_entries, squash, title_case
from .equipment import _table_checks
from .item import _is_type_line
from .monster import _is_size_line
from .sections import Section, read_sections

# Every cleric domain the 16 books print (5etools class-cleric.json subclasses of these sources: PHB Knowledge,
# Life, Light, Nature, Tempest, Trickery, War; DMG Death; SCAG Arcana; XGE Forge, Grave; GGR/TCE Order; TCE Peace,
# Twilight). A dev-time constant: a deity cell naming anything else is a misread.
DOMAINS = ("Arcana", "Death", "Forge", "Grave", "Knowledge", "Life", "Light", "Nature", "Order", "Peace", "Tempest",
           "Trickery", "Twilight", "War")
_ALIGNMENTS = {"LG", "NG", "CG", "LN", "N", "CN", "LE", "NE", "CE"}
_NO_CLERICS = re.compile(r"^no clerics?$", re.I)
_SYMBOL_HEADS = ("Symbol", "Common Symbol")
_DOMAIN_HEADS = ("Suggested Domains", "Domains", "Suggested Cleric Domains")
_MIDDLE_HEADS = ("Alignment", "Province", "Warlock Patron")
_HEAD_MATCH = 80
_PLAIN_FIRST = ("Deity", "Name", "God")  # a first header naming the column, not a group of gods
# "Alignment Province", "Alignment Domains", "Alignment Warlock Patron", "Alignment t Province": two header cells
# the OCR read as one line
_MERGED = re.compile(r"^(Alignment)\s+(.+)$", re.I)
# "Anubis, god of judgment and death", "The Undying Court, elven ancestors", "Valkur, Northlander god of sailors"
_EPITHET = re.compile(r"^(?P<name>[^,]+?),\s+(?P<title>[a-z].*|(?:\S+\s+){0,3}(?:god|goddess|deity|deities)\b.*)$")
# a title naming a rank or kind of gods, not a people or a world: the category, never the pantheon
_RANKS = {"prime", "lesser", "greater", "intermediate", "minor", "major", "betrayer", "elder", "old", "new"}
_DISPLAY = 1.4  # a line this many times taller than the section's median is display text (a pull quote)
_SIBLING_FACTOR = 3  # a deity section this many times longer than its siblings' median ran on over them


# ---------- header rows ----------
def _like(a: str, b: str) -> int:
    return fuzz.ratio(re.sub(r"\s+", " ", a.strip().lower()), b.lower())


def _best(text: str, names: tuple[str, ...]) -> str | None:
    hit = max(names, key=lambda n: _like(text, n))
    return hit if _like(text, hit) >= _HEAD_MATCH else None


def _yc(l: Line) -> float:
    return (l.box[1] + l.box[3]) / 2


def _cut(l: Line, at: int) -> list[Line]:
    """Two lines from one OCR line read across two header cells, cut before character `at` (by character offset)."""
    per = (l.box[2] - l.box[0]) / max(len(l.text), 1)
    a, b = l.text[:at].strip(), l.text[at:].strip()
    x = l.box[0] + at * per
    return [Line(text=a, box=(l.box[0], l.box[1], x - per, l.box[3]), height=l.height, page=l.page, frags=l.frags),
            Line(text=b, box=(x, l.box[1], l.box[2], l.box[3]), height=l.height, page=l.page, frags=l.frags)]


def _split_page(page: Page) -> Page:
    """The page with each header line holding two header cells ("Alignment Province") cut into two."""
    lines: list[Line] = []
    changed = False
    for l in page.lines:
        m = _MERGED.match(l.text.strip())
        if m and (_best(m.group(2), _MIDDLE_HEADS + _DOMAIN_HEADS) or _like(m.group(2), "t Province") >= 80):
            lines += _cut(l, l.text.index(m.group(2)))
            changed = True
        else:
            lines.append(l)
    return Page(number=page.number, width=page.width, height=page.height, lines=lines, source=page.source) \
        if changed else page


def _header_rows(page: Page) -> list[list[Line]]:
    """Each deity-table header row on the page, its cells left to right, top to bottom. The row ends in the symbol
    header, holds a domains header and at least two cells before that."""
    out: list[list[Line]] = []
    for s in page.lines:
        if not _best(s.text, _SYMBOL_HEADS):
            continue
        h = s.height
        row = sorted((l for l in page.lines if abs(_yc(l) - _yc(s)) <= 0.6 * h and l.box[2] <= s.box[2] + h
                      and l.box[0] > s.box[0] - 0.75 * page.width), key=lambda l: l.box[0])
        k = row.index(s)
        row = row[:k + 1]
        # the table's cells: from the first cell after a column-sized gap (prose beside it) to the symbol
        doms = [i for i, l in enumerate(row) if _best(l.text, _DOMAIN_HEADS)]
        if not doms or len(row) < 4:
            continue
        mids = [l for l in row[1:doms[-1]] if _best(l.text, _MIDDLE_HEADS) or _like(l.text, "t Province") >= 80]
        if not mids:
            continue
        first = row.index(mids[0]) - 1
        if first < 0:
            continue
        out.append(row[first:])
    return sorted(out, key=lambda r: (r[0].box[1], r[0].box[0]))


def _role(header: str) -> str | None:
    for role, names in (("symbol", _SYMBOL_HEADS), ("domains", _DOMAIN_HEADS)):
        if _best(header, names):
            return role
    if _like(header, "Alignment") >= _HEAD_MATCH:
        return "alignment"
    if _best(header, ("Province", "Warlock Patron")) or _like(header, "t Province") >= 80:
        return "province"
    return None


# ---------- titles and pantheons ----------
def _title_line(page: Page, head: list[Line], tables: list[tuple[list[Line], RowTable]]) -> Line | None:
    """The table's title: the nearest title-like line set just above the header row, at the table's left edge."""
    h = head[0].height
    top = min(l.box[1] for l in head)
    x0, x1 = head[0].box[0], head[-1].box[2]
    cands = [l for l in page.lines if top - 3 * h <= l.box[1] < top - 0.5 * h and abs(l.box[0] - x0) <= 3 * h
             and l.box[2] < x1]
    for l in sorted(cands, key=lambda l: -l.box[1]):
        t = l.text.strip()
        if any(l in r.lines for _, tb in tables for r in tb.rows):
            return None  # straight under the rows of the table above
        if 0 < len(t.split()) <= 8 and "," not in t and not t.endswith(".") and \
                (title_case(_ascii(t)) or caps_heading(t) or t[:1].isupper() and re.search(r"\(.*\)", t)):
            return l
        return None
    return None


def _ascii(t: str) -> str:
    """Accented letters as plain ones, so "Faerûnian" reads as one word to `title_case`."""
    return unicodedata.normalize("NFKD", t).encode("ascii", "ignore").decode()


def _pantheon(title: str) -> tuple[str | None, str | None]:
    """(pantheon, category) a table title names."""
    t = name_case(title.strip())
    if m := re.match(r"^(?:Deities|Gods) of (?:the )?(.+)$", t, re.I):
        return m.group(1), None
    if m := re.match(r"^The (.+?) Pantheon$", t, re.I):
        return m.group(1), None
    if m := re.match(r"^(.+?) Deities(?:\s*\((.+)\))?$", t, re.I):
        if m.group(1).lower() in _RANKS:
            return None, t
        # the group inside the brackets keeps its printed casing ("(The Dark Seldarine)")
        inner = re.search(r"\((.+)\)\s*$", title.strip())
        return m.group(1), inner.group(1).strip() if m.group(2) and inner else m.group(2)
    return None, t


_WORD = re.compile(r"[^\W\d_]+")


def repair_accents(pantheon: str, body: list[Line]) -> tuple[str, Check | None]:
    """The pantheon with each accented letter spelled as the book most often spells it in the words sharing its stem
    (the OCR reads SCAG's "Faerûnian" as "Faerúnian", but prints "Faerûn" 125 times against "Faerún" 48), and a
    passing `pantheon-repaired` check naming both spellings when it changed; (pantheon, None) otherwise."""
    words = [w for l in body for w in _WORD.findall(l.text)]
    out: list[str] = []
    notes: list[str] = []
    for w in _WORD.findall(pantheon):
        pos = [k for k, c in enumerate(w) if not c.isascii()]
        if not pos:
            out.append(w)
            continue
        fw = _ascii(w).lower()
        votes: dict[str, int] = {}
        for t in words:
            ft = _ascii(t).lower()
            if len(t) > pos[-1] and len(ft) == len(t) and fw.startswith(ft) and len(ft) >= pos[-1] + 2:
                key = "".join(t[k] for k in pos)
                votes[key] = votes.get(key, 0) + 1
        mine = "".join(w[k] for k in pos)
        best = max(votes, key=lambda k: (votes[k], k == mine)) if votes else mine
        if best != mine and votes[best] > votes.get(mine, 0):
            fixed = "".join(best[pos.index(k)] if k in pos else c for k, c in enumerate(w))
            notes.append(f"{w!r} -> {fixed!r} (the book spells the stem with {best!r} {votes[best]} times, with "
                         f"{mine!r} {votes.get(mine, 0)})")
            out.append(fixed)
        else:
            out.append(w)
    if not notes:
        return pantheon, None
    fixed = pantheon
    for a, b in zip(_WORD.findall(pantheon), out):
        fixed = fixed.replace(a, b)
    return fixed, Check("pantheon-repaired", True, "; ".join(notes))


def _section_pantheon(sections: list[Section], at: Line) -> str | None:
    """The pantheon a heading the table is printed under names ("Pantheon of Exandria")."""
    key = (at.page, round(at.box[1]))
    for s in sections:
        m = re.match(r"^(?:The )?Pantheon of (?:the )?(.+)$|^The (.+) Pantheon$", s.title, re.I)
        if not m:
            continue
        span = s.lines + s.stripped
        pages = {l.page for l in span}
        if at.page not in pages:
            continue
        first = min(((l.page, l.box[1]) for l in span))
        last = max(((l.page, l.box[3]) for l in span))
        if first <= key <= last:
            return m.group(1) or m.group(2)
    return None


# ---------- cells ----------
def alignment_value(cell: str) -> list[str] | None:
    t = re.sub(r"\s+", "", cell).upper()
    if t in _ALIGNMENTS:
        return list(t)
    if t == "UNALIGNED":
        return ["U"]
    return None


def alignment_check(cell: str, name: str) -> Check:
    if alignment_value(cell) is not None:
        return Check("alignment-known", True)
    if not cell.strip():
        return Check("alignment-known", False, f"alignment cell of {name!r} is blank")
    return Check("alignment-known", False, f"alignment cell of {name!r} {cell.strip()!r} is not one of LG, NG, CG, "
                                           "LN, N, CN, LE, NE, CE or Unaligned")


def split_domains(cell: str) -> list[str]:
    """'Life, Order,* War' -> ['Life', 'Order', 'War'] (footnote markers dropped)."""
    t = re.sub(r"[*†‡]+", "", cell)
    return [d.strip() for d in t.split(",") if d.strip()]


def domains_check(domains: list[str], cell: str, name: str) -> Check:
    if _NO_CLERICS.match(cell.strip()):
        return Check("domains-known", True, f"{name!r} prints {cell.strip()!r}: no domains")
    if not domains:
        return Check("domains-known", False, f"domains cell of {name!r} is blank")
    bad = [d for d in domains if d not in DOMAINS]
    if bad:
        return Check("domains-known", False, f"unknown domain{'s' if len(bad) > 1 else ''} of {name!r}: "
                     + ", ".join(repr(b) for b in bad))
    return Check("domains-known", True)


def symbol_check(cell: str, name: str) -> Check:
    """A symbol is printed capitalised: a cell opening in lower case is the wrapped end of a symbol whose first
    line the OCR lost (EGW Vecna's "palm")."""
    t = cell.strip()
    if not t:
        return Check("symbol-present", False, f"symbol cell of {name!r} is blank")
    if t[:1].islower():
        return Check("symbol-present", False, f"symbol cell of {name!r} {t!r} opens in lower case: its first line "
                                              "may be lost")
    return Check("symbol-present", True)


def name_check(name: str) -> Check:
    """`name-capitalised`: a deity's name is printed capitalised. A label opening in lower case is a misread the
    book repeats ("luz" for Iuz, "loun" for Ioun: the OCR reads a capital I as l), so the name-word checks cannot
    see it; it is named, never repaired."""
    if name[:1].islower():
        return Check("name-capitalised", False, f"deity label {name!r} opens in lower case (a misread capital?)")
    return Check("name-capitalised", True)


def _name(label: str) -> tuple[str, str | None]:
    """('Anubis', 'god of judgment and death') from the label cell; (label, None) when it prints no epithet."""
    m = _EPITHET.match(label.strip())
    return (m.group("name").strip(), m.group("title").strip()) if m else (label.strip(), None)


# ---------- the deity's prose section ----------
def _hosted_stop(lines: list[Line]) -> int:
    """Index of the heading of the first magic item or stat block printed inside a deity section, else
    len(lines). A magic item is a short heading over its type line ("Wondrous item, rare (requires attunement)");
    a stat block is a name over a size/type/alignment line with "Armor Class" just below."""
    for i in range(1, len(lines)):
        t = lines[i].text.strip()
        head = lines[i - 1].text.strip()
        if not heading_like(head):
            continue
        if _is_type_line(t) or _is_size_line(t) and any(re.match(r"(?i)^armou?r\s*class", l.text.strip())
                                                         for l in lines[i + 1:i + 5]):
            return i - 1
    return len(lines)


def _chapter(sections: list[Section], at: Line) -> str | None:
    """The chapter title the line is printed under: that of the last section starting at or before it."""
    before = [s for s in sections if (s.page, s.lines[0].box[1]) <= (at.page, at.box[1])]
    return before[-1].parents[0] if before and before[-1].parents else None


def _section_for(name: str, sections: list[Section], chapter: str | None) -> Section | None:
    """The shallowest section of the deity table's chapter whose heading names the deity ("Avandra, the Change
    Bringer" names Avandra); never a same-named section elsewhere (EGW's magic item "Arms of the Betrayers")."""
    want = squash(name)
    hits = [s for s in sections if squash(s.title.split(",")[0]) == want
            and (s.parents[0] if s.parents else None) == chapter]
    return min(hits, key=lambda s: s.level) if hits else None


def _names_a_god(L: list[Line], k: int, names: set[str]) -> bool:
    """L[k] is a heading naming another deity of the book ("ILMATER", "Uk'otoa, the Leviathan Lord"), set after a
    finished sentence (or at a column or page top), whether or not the section reader read it as a heading."""
    t = L[k].text.strip()
    if not (0 < len(t.split()) <= 8 and not t.endswith((".", ",", ";")) and squash(t.split(",")[0]) in names):
        return False
    prev = L[k - 1]
    return prev.page != L[k].page or prev.box[1] > L[k].box[1] or prev.text.rstrip().endswith((".", "!", "?", '"'))


def _inner_heading(L: list[Line], k: int, own: str) -> bool:
    """L[k] is a heading the reader may have missed, inside a deity's section and not naming the deity (`own`,
    squashed): a sidebar title ("Drow Trance: Entering the Void"). Short, title case or capitals, after a finished
    sentence or at a column or page top, over prose."""
    t = L[k].text.strip()
    if not (0 < len(t.split()) <= 8 and t[:1].isupper() and not t.endswith((".", ",", ";", "-"))):
        return False
    if own in squash(t) or not (title_case(_ascii(t.replace(":", " "))) or caps_heading(t)):
        return False
    prev, nxt = L[k - 1], L[k + 1] if k + 1 < len(L) else None
    after = prev.page != L[k].page or prev.box[1] > L[k].box[1] or prev.text.rstrip().endswith(SENTENCE_END)
    return after and nxt is not None and sum(w[:1].islower() for w in nxt.text.split()) >= 3


def own_span(s: Section, others: list[Section], names: set[str] = frozenset(),
             boxes: dict[int, list[tuple]] | None = None,
             sections: list[Section] = ()) -> tuple[list[Line], list[Line], str | None]:
    """(the deity section's own lines, the lines after them, the heading of a section or sidebar that does not name
    the deity where they stop, or None when they stop at another deity, a hosted entity or the section's end).

    The own lines leave out the deity-table lines printed inside the section (`boxes`: rows, title, footnotes) and
    display text set far taller than its body (a pull quote: MTF beside Laduguer). They run up to the first of:

    - a section inside it that is another deity's (the reader may nest a sibling under it: SCAG Jergal under Hoar);
    - a heading naming another deity (`names`, squashed), read as one or not (SCAG "ILMATER");
    - a section, or a heading the reader missed, inside it that does not name the deity: a sidebar or another topic
      ("Drow Trance: Entering the Void" in Lolth's column, "The Gods of Mulhorand" under Waukeen);
      "Commandments of Avandra" names its deity and stays;
    - a hosted magic item or stat block."""
    own_name = squash(s.title.split(",")[0])
    deity = {id(o.lines[0]) for o in others if o is not s}
    other = {id(o.lines[0]): o.title for o in sections if o is not s and own_name not in squash(o.title)}
    lines = [l for l in s.lines if not boxes or not _in_box(l, boxes)]
    if len(lines) > 2:
        med = median(l.height for l in lines[1:])
        lines = lines[:1] + [l for l in lines[1:] if l.height <= _DISPLAY * med]
    end, why = len(lines), None
    for k in range(1, len(lines)):
        l = lines[k]
        if id(l) in deity or _names_a_god(lines, k, names):
            end = k
            break
        if id(l) in other or _inner_heading(lines, k, own_name):
            end, why = k, other.get(id(l)) or l.text.strip()
            break
    own = lines[:end]
    cut = _hosted_stop(own)
    if cut < len(own):
        return own[:cut], lines[cut:], None
    return own, lines[end:], why


def _own_lines(s: Section, others: list[Section], names: set[str] = frozenset(),
               boxes: dict[int, list[tuple]] | None = None, sections: list[Section] = ()) -> list[Line]:
    return own_span(s, others, names, boxes, sections)[0]


def _size_check(s: Section, own: list[Line], sections: list[Section]) -> Check:
    if not s.complete:
        return Check("text-complete", False, f"section {s.title!r} (p{s.page}) has no sure end: {s.end_reason}")
    sib = [len(o.lines) for o in sections if o.parents == s.parents and o is not s]
    if len(sib) >= 2 and len(own) > _SIBLING_FACTOR * max(median(sib), 10):
        return Check("text-complete", False, f"section {s.title!r} (p{s.page}) runs {len(own)} lines, over "
                     f"{_SIBLING_FACTOR}x its {len(sib)} siblings' median of {median(sib):g}")
    return Check("text-complete", True)


# ---------- entities ----------
def _row_text(label: str, cells: dict[str, str], group: str | None) -> str:
    return "\n".join([label] + [f"{h}: {c}" for h, c in cells.items() if c.strip()] + ([group] if group else []))


def _god(r: Row, t: RowTable, roles: dict[str, str], pantheon: str | None, category: str | None) -> Parsed:
    c = {h: v.strip() for h, v in r.raw.items()}
    label = r.label
    name, epithet = _name(label)
    f: dict = {}
    checks: list[Check] = []
    by = {role: c.get(h, "") for h, role in roles.items()}
    if "alignment" in roles.values():
        al = alignment_value(by["alignment"])
        f["alignment"] = al or []
        checks.append(alignment_check(by["alignment"], name))
    else:
        f["alignment"] = []
        checks.append(Check("alignment-known", True, "the table prints no alignment column"))
    doms = [] if _NO_CLERICS.match(by.get("domains", "")) else split_domains(by.get("domains", ""))
    f["domains"] = doms
    checks.append(domains_check(doms, by.get("domains", ""), name))
    if by.get("symbol"):
        f["symbol"] = by["symbol"]
    checks.append(symbol_check(by.get("symbol", ""), name))
    if pantheon:
        f["pantheon"] = pantheon
    province = by.get("province") or epithet
    if province:
        f["province"] = province
    if category:
        f["category"] = category
    checks.append(name_check(name))
    checks += _table_checks(r, t, None)
    f["entries"] = []
    p = Parsed(type="God", name=name, fields=f, text=_row_text(label, {h: c[h] for h in roles if h in c}, r.group),
               lines=list(r.lines), table_row=True)
    p.checks = checks
    return p


def _in_box(l: Line, boxes: dict[int, list[tuple]]) -> bool:
    x, y = (l.box[0] + l.box[2]) / 2, (l.box[1] + l.box[3]) / 2
    return any(b[0] <= x <= b[2] and b[1] <= y <= b[3] for b in boxes.get(l.page, []))


def _attach_prose(gods: list[Parsed], sections: list[Section], boxes: dict[int, list[tuple]]) -> None:
    """Each deity's prose section (in its table's chapter) as its `entries` and the rest of its text and lines. The
    deity tables printed inside a section (`boxes`, per page: header row to last row) are no part of its prose."""
    found = [(g, _section_for(g.name, sections, _chapter(sections, g.lines[0]))) for g in gods]
    god_sections = [s for _, s in found if s is not None]
    for g, s in found:
        if s is None:
            continue
        names = {squash(o.name) for o in gods} - {squash(g.name)}
        own, rest, why = own_span(s, god_sections, names, boxes, sections)
        # the heading may wrap onto a second line ("Avandra, the" / "Change Bringer")
        heads = next((k for k in range(1, len(own) + 1) if k == len(own) or own[k].text.strip() not in s.title),
                     1)
        g.fields["entries"] = prose_entries(own, heads, len(own))
        g.lines = g.lines + own
        g.text += "\n" + "\n".join(x for x in g.fields["entries"] if isinstance(x, str))
        check = _size_check(s, own, sections)
        g.checks.append(check if not check.passed else _resumes(g.name, rest, why))


def _resumes(name: str, rest: list[Line], why: str | None) -> Check:
    """`text-complete` fails when the deity's text stopped at a heading inside its section that does not name it (a
    sidebar: "Drow Trance: Entering the Void" in Lolth's column) and prose after that heading names the deity
    again: its text may resume after the inserted part, and the reader cannot tell where."""
    word = squash(name)
    again = [l for l in rest[1:] if word in squash(l.text) and len(l.text) >= 30
             and sum(w[:1].islower() for w in l.text.split()) >= 3]
    if why is None or not again:
        return Check("text-complete", True)
    return Check("text-complete", False, f"the text of {name!r} stops at {why!r} (p{rest[0].page}), a heading "
                 f"inside its section that does not name it, but a later line names {name!r} again "
                 f"(p{again[0].page}: {again[0].text.strip()!r}): its text may resume after it")


def _crosses(l: Line, head: list[Line]) -> bool:
    """The line lies across a whole header cell of a column it does not start in, from at least a line height
    before it: prose, not a cell (a cell may start a little left of its own header)."""
    bounds = [(a.box[2] + b.box[0]) / 2 for a, b in zip(head, head[1:])]
    col = sum(l.box[0] > b for b in bounds)
    return any(k != col and l.box[0] < h.box[0] - l.height and l.box[2] > h.box[2] for k, h in enumerate(head))


def _trim(r: Row, head: list[Line], cut: float) -> Row | None:
    """The row without its lines from `cut` down (the prose under the table, taken into the last row's span), its
    label and cells re-read from the lines kept; None when no label line is kept."""
    bounds = [(a.box[2] + b.box[0]) / 2 for a, b in zip(head, head[1:])]
    kept = [l for l in r.lines if l.box[1] < cut]
    col = {id(l): sum((l.box[0] + l.box[2]) / 2 > b for b in bounds) for l in kept}
    label = " ".join(l.text.strip() for l in kept if col[id(l)] == 0)
    if not label:
        return None
    raw = {h.text.strip(): " ".join(l.text.strip() for l in kept if col[id(l)] == k)
           for k, h in enumerate(head) if k}
    return Row(label=label, cells=dict(raw), group=r.group, marker=r.marker, lines=kept, raw=raw)


def _rows(page: Page, head: list[Line], stop: float | None) -> RowTable | None:
    """The deity table under the header row `head`, cut at `stop` (the next header row's top) and at the first line
    laid across a column it does not start in (the prose under the table, read into a row: SCAG "AMAUNATOR" under
    the orc gods); a row whose own lines lie above that line keeps them."""
    headers = [l.text.strip() for l in head]
    top = min(l.box[1] for l in head)
    tables = [t for t in read_row_tables(page, headers) if abs(t.box[1] - top) < 2 * head[0].height]
    if not tables:
        return None
    t = tables[0]
    rows: list[Row] = []
    for r in t.rows:
        if stop is not None and r.lines[0].box[1] >= stop:
            break
        across = [l for l in r.lines if _crosses(l, head)]
        if across:
            cut = min(l.box[1] for l in across)
            if (kept := _trim(r, head, cut)) is not None:
                rows.append(kept)
            break
        rows.append(r)
    t.rows = rows
    return t if rows else None


_OPEN_END = re.compile(r"(?:,|-|\b(?:of|the|and|a|an|to|in|on|for|with|at|by))$", re.I)


def _wrapped_rows(t: RowTable, roles: dict[str, str]) -> None:
    """A deity label wrapped onto a line the table reader took for a row of its own, because a wrapped cell sits
    beside it ("Arms of the" / "Betrayers" beside "eight-eyed skull"): the label above stops mid-phrase and the
    row has no alignment or domains cell of its own. Joined onto the row above."""
    keys = [h for h, r in roles.items() if r in ("alignment", "domains")]
    rows: list[Row] = []
    for r in t.rows:
        if rows and _OPEN_END.search(rows[-1].label.strip()) and not any(r.raw.get(h, "").strip() for h in keys):
            p = rows[-1]
            p.label = f"{p.label} {r.label}"
            p.raw = {h: " ".join(x for x in (p.raw.get(h, ""), r.raw.get(h, "")) if x.strip()) for h in p.raw}
            p.lines = sorted(p.lines + r.lines, key=lambda l: (l.box[1], l.box[0]))
            continue
        rows.append(r)
    t.rows = rows


def _wrapped_groups(t: RowTable, page: Page) -> None:
    """A deity label wrapped onto a second line the table reader took for a sub-heading ("Cults of the Dragon" /
    "Below"): the line sits straight under the label, before the next row. Joined back onto its label; the rows
    after it keep the group they had."""
    for k in range(1, len(t.rows)):
        r, prev = t.rows[k], t.rows[k - 1]
        if r.group == prev.group or r.group is None:
            continue
        lab = [l for l in prev.lines if l.text.strip() == prev.label or prev.label.startswith(l.text.strip())]
        if not lab:
            continue
        under = [l for l in page.lines if l.text.strip() == r.group and abs(l.box[0] - lab[0].box[0]) < lab[0].height
                 and 0 <= l.box[1] - lab[-1].box[1] < 1.2 * lab[-1].height]
        if not under or re.search(r"\b(gods?|deities|faiths?|host|pantheon|powers)\b", r.group, re.I):
            continue
        prev.label = f"{prev.label} {r.group}"
        prev.lines = sorted(prev.lines + under, key=lambda l: (l.box[1], l.box[0]))
        for o in t.rows[k:]:
            if o.group == r.group:
                o.group = prev.group


_FOOTNOTE = re.compile(r"^(?:[*†‡]+|Appears in\b)")


def _footnotes(page: Page, t: RowTable, head: list[Line]) -> list[Line]:
    """The footnote lines printed under the table ("*Appears in Sword Coast Adventurer's Guide"): a run of lines
    opening with a marker or "Appears in", below the last row, in the table's columns."""
    h = head[0].height
    x0, x1 = min(l.box[0] for l in head), max(l.box[2] for l in head)
    bottom = max(l.box[3] for r in t.rows for l in r.lines)
    out: list[Line] = []
    for l in sorted((l for l in page.lines if l.box[1] >= bottom - 0.3 * h and x0 - 2 * h <= l.box[0] <= x1),
                    key=lambda l: l.box[1]):
        if not _FOOTNOTE.match(l.text.strip()):
            break
        out.append(l)
    return out


def parse_gods(body: list[Line], ctx: ParseContext) -> list[Parsed]:
    """One God per printed deity-table row, on any page of the book, with its prose section as its text."""
    sections = read_sections(body, ctx)
    out: list[Parsed] = []
    boxes: dict[int, list[tuple]] = {}
    last: tuple[str | None, str | None] = (None, None)
    for n in sorted(ctx.pages):
        page = _split_page(ctx.pages[n])
        heads = _header_rows(page)
        done: list[tuple[list[Line], RowTable]] = []
        for k, head in enumerate(heads):
            nxt = [h for h in heads[k + 1:] if h[0].box[0] < head[-1].box[2] and h[-1].box[2] > head[0].box[0]]
            t = _rows(page, head, min(l.box[1] for l in nxt[0]) - 0.3 * head[0].height if nxt else None)
            if t is None:
                continue
            _wrapped_groups(t, page)
            roles = {l.text.strip(): _role(l.text) for l in head[1:]}
            roles = {h: r for h, r in roles.items() if r}
            _wrapped_rows(t, roles)
            title = _title_line(page, head, done)
            if title is not None:
                pantheon, category = _pantheon(title.text)
                if pantheon is None:
                    pantheon = _section_pantheon(sections, head[0])
                last = (pantheon, category)
            else:
                pantheon, category = last if done else (_section_pantheon(sections, head[0]), None)
            group = head[0].text.strip()
            first = None if _best(group, _PLAIN_FIRST) else name_case(group)
            fix = repair_accents(pantheon, body) if pantheon else (pantheon, None)
            for r in t.rows:
                g = _god(r, t, roles, fix[0], r.group and name_case(r.group) or first or category)
                if fix[1] is not None:
                    g.checks.append(fix[1])
                out.append(g)
            done.append((head, t))
            # the table's title and its footnotes ("*Appears in ...") are table text too, never a god's prose
            rows = [l for r in t.rows for l in r.lines] + head + ([title] if title is not None else [])
            rows += _footnotes(page, t, head)
            boxes.setdefault(n, []).append((min(l.box[0] for l in rows), min(l.box[1] for l in rows),
                                            max(l.box[2] for l in rows), max(l.box[3] for l in rows)))
    _attach_prose(out, sections, boxes)
    return out
