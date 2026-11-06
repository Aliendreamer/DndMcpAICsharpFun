"""DiseasePoison and Condition parsers: printed poisons and diseases (5etools mundane `items.json` poisons and
`conditionsdiseases.json` `disease`), and the conditions appendix (`condition`).

Poisons are the rows of a poison table ("Item | Type | Price per Dose", DMG) and the rows of an item table whose
label names a poison ("Poison, basic (vial)", PHB Adventuring Gear; the Item parser leaves those rows here). A row
carries `value` (copper) from its price cell, and `poisonTypes` from its Type cell. Its description is the
paragraph whose lead-in names the row, inside the row's own chapter ("Burnt Othur Fumes (Inhaled). A creature
..."), and the lead-in's printed name (title case) names the entity; the lead-in's bracketed type must agree with
the table's. A table row with no description fails `entries-read`.

Diseases are found three ways, each by print evidence:

- a heading or lead-in whose first sentence names it ("Sewer plague is ...", "The Gnawing Plague, also known as
  ...") or calls it a disease ("This disease targets ...", "This painful infection ..."), and whose text speaks of
  infection: its `entries` run to the next heading (a heading's own lead-in paragraphs, "Transmission." and
  "Symptoms.", are part of it);
- the lead-in paragraphs after a sentence that offers "the diseases described below" (PHB Contagion: "Blinding
  Sickness. ..."), up to the next heading.

A disease name that holds a disease word ("Sewer Plague") is no evidence by itself: the first sentence's disease word
must lie outside the name, or the entity's own text (its name taken out) must speak of disease and infection.
`disease-evidence` fails when the entity's own text - not its name, not the sentence that offered it - prints no
disease, infection or symptom vocabulary (a non-disease lead-in after an offer, "At Higher Levels.", goes to
review; so do the Contagion diseases, which describe only their effects).

Conditions are the headings of the conditions appendix ("Appendix A: Conditions"). The appendix ends at the next
appendix or chapter title (so "Unconscious" stops before "Appendix B"), read by its words or, when the OCR garbles
them, by its size; or at the first capitalised heading that names no condition (a misread "STUNNEX" still resembles
one). A heading is a short capitalised line with its text directly below it; the same names set alone around the
art ("BLINDED" beside a picture) are captions and are not text. The Exhaustion levels table is a table entry; a
"Level Effect" header with no rows under it is read as text and fails `table-rows-read`. A condition with no text
fails `entries-read`; `condition-known` passes for one of the 15 PHB conditions.

A poison row whose lead-in matches more than one description is not joined, and fails `entries-read` naming them.

Not registered in assemble yet (Phase 2b ruling P1).
"""
from __future__ import annotations

import re
import statistics
from dataclasses import replace

from rapidfuzz import fuzz

from ..checks import Check
from ..model import Line
from ..tables import Row, RowTable, read_row_tables
from . import ParseContext, Parsed
from .charcommon import SENTENCE_END, character_body, heading_like, prose_entries, title_case
from .equipment import _rows, _table_checks, cost_check, cost_value
from .gear import (_OTHER_TYPE, _chapters, _entries, _key, _outside_tables, _page_chapters, _paragraphs, _row_tables,
                   _same_section, _table_boxes)
from .monster import _is_heading_caps, _join_text, _title
from .trap import _clean, _is_head, _median

POISON_TYPES = ("contact", "ingested", "inhaled", "injury")
CONDITIONS = ("blinded", "charmed", "deafened", "exhaustion", "frightened", "grappled", "incapacitated", "invisible",
              "paralyzed", "petrified", "poisoned", "prone", "restrained", "stunned", "unconscious")
POISON_HEADERS = ["Item", "Type", "Price per Dose"]


# ---------- poisons ----------
def _poison_type_check(cell: str, name: str) -> tuple[list[str] | None, Check]:
    t = cell.strip().lower()
    if t in POISON_TYPES:
        return [t], Check("poison-type-known", True)
    if not t:
        return None, Check("poison-type-known", False, f"type cell of {name!r} is blank")
    return None, Check("poison-type-known", False, f"type cell of {name!r} {cell.strip()!r} is not one of "
                                                   f"{', '.join(POISON_TYPES)}")


def _poison_row(r: Row, t: RowTable, suspect: str | None) -> Parsed:
    raw = {h: c.strip() for h, c in r.raw.items()}
    name = r.label
    f: dict = {}
    checks: list[Check] = []
    if "Type" in raw:
        types, c = _poison_type_check(raw["Type"], name)
        if types:
            f["poisonTypes"] = types
        checks.append(c)
    price = raw.get("Price per Dose", raw.get("Cost", ""))
    v = cost_value(price)
    if v is not None:
        f["value"] = v
    checks.append(cost_check(price, name))
    checks += _table_checks(r, t, suspect)
    cells = [f"{h}: {c}" for h, c in r.cells.items() if c.strip() and h != "Weight"]
    p = Parsed(type="DiseasePoison", name=name, fields=f, text="\n".join([name] + cells), lines=list(r.lines))
    p.checks = checks
    return p


def _poison_rows(ctx: ParseContext) -> list[tuple[Parsed, int]]:
    out: list[tuple[Parsed, int]] = []
    seen: set = set()
    for n in sorted(ctx.pages):
        page = ctx.pages[n]
        tables = read_row_tables(page, POISON_HEADERS) + [t for t, _ in _row_tables(page)]
        for tb in tables:
            poison_table = tb.headers == POISON_HEADERS
            for r, t, suspect, _ in _rows([tb], seen):
                if poison_table or re.match(r"^\s*poison\b", r.label, re.I) and _OTHER_TYPE.match(r.label):
                    out.append((_poison_row(r, t, suspect), n))
    return out


def _join_poisons(items: list[tuple[Parsed, int]], body: list[Line], ctx: ParseContext) -> None:
    L = _outside_tables(body, _table_boxes(ctx))
    chapters, page_chapter = _chapters(L), _page_chapters(body)
    paras = _paragraphs(L)
    for p, pg in items:
        k = _key(p.name)
        hits = [(m, s, e) for m, s, e in paras if _key(m.group("name")) == k and
                _same_section((L[s].page, chapters[s]), (pg, page_chapter(pg)))]
        if not hits:
            p.checks.append(Check("entries-read", False, f"no description whose lead-in names {p.name!r} was found"))
            continue
        if len(hits) > 1:
            where = ", ".join(f"{m.group('name')!r} on page {L[s].page}" for m, s, _ in hits)
            p.checks.append(Check("entries-read", False, f"several descriptions could be the one for {p.name!r}: "
                                  f"{where}; none was joined"))
            continue
        m, s, e = hits[0]
        ents = _entries(L, s, e, m.group("rest"))
        p.fields["entries"] = ents
        p.checks.append(Check("entries-read", bool(ents), "" if ents else f"description of {p.name!r} is empty"))
        lead = m.group("name")
        if lead.lower() == p.name.lower() and lead != p.name:
            p.name = lead  # the description's printed (title-case) name
        paren = (m.group("paren") or "").strip().lower()
        if paren and p.fields.get("poisonTypes") and [paren] != p.fields["poisonTypes"]:
            p.checks.append(Check("poison-type-known", False, f"the description of {p.name!r} prints type "
                                  f"{paren!r}, the table {p.fields['poisonTypes'][0]!r}"))
        p.lines = p.lines + L[s:e]
        head = f"{lead} ({m.group('paren')})" if m.group("paren") else lead
        p.text = "\n".join([p.text, head + ". " + _flat(ents)])


# ---------- diseases ----------
_DISEASE_WORD = r"(?:disease|infection|illness|plague|sickness|affliction)"
_SELF = re.compile(rf"(?i)^this(?:\s+[a-z-]+){{0,2}}\s+{_DISEASE_WORD}\b")
_INFECT = re.compile(r"(?i)\binfect")
_EVIDENCE = re.compile(r"(?i)\b(disease|infect\w*|illness\w*|plague\w*|sickness\w*|symptom\w*|contagio\w*)")
_DISEASE = re.compile(rf"(?i)\b{_DISEASE_WORD}")
_OFFER = re.compile(r"(?i)\bdiseases?\b[^.]*\b(?:described|listed|detailed|presented) below\b|"
                    r"\bthe following diseases\b")
_LEAD = re.compile(r"^(?P<name>[A-Z][A-Za-z'’-]*(?: (?:[A-Z][A-Za-z'’-]*|of|and|the)){0,4})\.\s+(?P<rest>\S.*)$")


def _flat(e) -> str:
    if isinstance(e, str):
        return e
    if isinstance(e, list):
        return " ".join(_flat(x) for x in e)
    if isinstance(e, dict):
        return " ".join(_flat(e.get(k)) for k in ("name", "entry", "entries", "items") if e.get(k))
    return ""


def _first_sentence(text: str) -> str:
    m = re.search(r"[.!?](?=\s+[A-Z\"“]|\s*$)", text)
    return text[:m.end()] if m else text


def _names_itself(name: str, text: str) -> bool:
    """The first sentence names the disease and says what it is ("Sewer plague is a generic term for ... illnesses",
    "The Gnawing Plague, also known as ...") or calls itself one ("This disease targets ...", "This painful
    infection ...")."""
    first = _first_sentence(text.strip())
    bare = re.sub(r"(?i)^the\s+", "", name).lower()
    named = re.sub(r"(?i)^the\s+", "", first).lower().startswith(bare)
    if _SELF.match(first):
        return True
    if not named:
        return False
    # the disease word must be the sentence's own, not the name's ("Sewer plague is ... illnesses"); a name that
    # is itself a disease's ("The Gnawing Plague, also known as ...") needs the evidence of its own text
    return bool(_DISEASE.search(_without_name(name, first))) or \
        bool(_DISEASE.search(name)) and _evidence(name, text)


def _without_name(name: str, text: str) -> str:
    bare = re.sub(r"(?i)^the\s+", "", name)
    return re.sub(re.escape(bare), " ", text, flags=re.I) if bare else text


def _evidence(name: str, text: str) -> bool:
    """The entity's own text (its name taken out) speaks of disease and of infection."""
    own = _without_name(name, text)
    return bool(_DISEASE.search(own)) and bool(_INFECT.search(own))


def _evidence_check(name: str, text: str) -> Check:
    """`disease-evidence`: the entity's own text (not its name, not the sentence that offered it) prints disease,
    infection or symptom vocabulary."""
    hit = _EVIDENCE.search(_without_name(name, text))
    return Check("disease-evidence", bool(hit), f"its own text prints {hit.group(0)!r}" if hit else
                 f"the text of {name!r} prints no disease, infection or symptom vocabulary")


def _indented(L: list[Line], j: int) -> bool:
    a, b = L[j - 1], L[j]
    return a.page == b.page and abs(a.box[1] - b.box[1]) < 3 * b.height and b.box[0] - a.box[0] > 0.4 * b.height


def _lead_starts(L: list[Line], s: int, e: int) -> list[int]:
    return [j for j in range(max(s, 1), e) if L[j - 1].text.rstrip().endswith(SENTENCE_END)
            and _LEAD.match(L[j].text.strip()) and _indented(L, j)]


def _short_heading(L: list[Line], j: int) -> bool:
    """A short capitalised line after a finished sentence (a spell's name over its level line, "Contingency")."""
    t = L[j].text.strip()
    return heading_like(t) and (title_case(t) or _is_heading_caps(t)) and \
        L[j - 1].text.rstrip().endswith(SENTENCE_END)


def _disease(name: str, L: list[Line], s: int, e: int, rest: str | None, lines: list[Line], how: str) -> Parsed:
    sub = L[s:e]
    if rest is not None:
        sub = [replace(sub[0], text=rest)] + sub[1:]
    ents = prose_entries(sub, 0, len(sub))
    f: dict = {}
    if ents:
        f["entries"] = ents
    text = f"{name}. " + _join_text([l.text.strip() for l in sub])
    p = Parsed(type="DiseasePoison", name=name, fields=f, text=text, lines=lines)
    p.checks = [Check("entries-read", bool(ents), how if ents else f"no text read for {name!r}"),
                _evidence_check(name, _join_text([l.text.strip() for l in sub]))]
    return p


def _diseases(body: list[Line], ctx: ParseContext) -> list[Parsed]:
    B = character_body(body, ctx)
    L = _clean(B, ctx) if B else []
    if not L:
        return []
    med = _median(L)
    heads = [i for i in range(len(L)) if _is_head(L, i, med)]
    out: list[Parsed] = []
    taken: set[int] = set()

    def end_at(i: int) -> int:
        later = [j for j in heads if j > i]
        return later[0] if later else len(L)

    for i in heads:  # headings
        e = end_at(i)
        text = _join_text([l.text.strip() for l in L[i + 1:e]])
        name = _title(L[i].text.strip())
        if _names_itself(name, text) and _evidence(name, text):
            out.append(_disease(name, L, i + 1, e, None, L[i:e], f"heading {name!r} describes a disease"))
            taken |= set(range(i, e))
    prev_head = 0
    for i in range(1, len(L)):  # lead-ins
        if i in heads or _short_heading(L, i):
            prev_head = i
        if i in taken or not (L[i - 1].text.rstrip().endswith(SENTENCE_END) and _indented(L, i)):
            continue
        m = _LEAD.match(L[i].text.strip())
        if not m:
            continue
        stop = min([j for j in range(i + 1, len(L)) if j in heads or _short_heading(L, j)
                    or (_LEAD.match(L[j].text.strip()) and L[j - 1].text.rstrip().endswith(SENTENCE_END)
                        and _indented(L, j))] or [len(L)])
        name = m.group("name")
        rest = m.group("rest")
        text = _join_text([rest] + [l.text.strip() for l in L[i + 1:stop]])
        intro = _join_text([l.text.strip() for l in L[prev_head:i]])
        offered = bool(_OFFER.search(intro))
        own = _names_itself(name, text) and _evidence(name, text)
        if offered or own:
            how = f"lead-in {name!r} follows {_OFFER.search(intro).group(0)!r}" if offered else \
                f"lead-in {name!r} describes a disease"
            out.append(_disease(name, L, i, stop, rest, L[i:stop], how))
            taken |= set(range(i, stop))
    return out


def parse_diseases_poisons(body: list[Line], ctx: ParseContext) -> list[Parsed]:
    """Poisons from poison-table and item-table rows (joined to their descriptions) and printed diseases."""
    items = _poison_rows(ctx)
    _join_poisons(items, body, ctx)
    return [p for p, _ in items] + _diseases(body, ctx)


# ---------- conditions ----------
_APPENDIX = re.compile(r"(?i)^appendix\s+\w+\s*:\s*conditions$")
_SECTION_END = re.compile(r"(?i)^(appendix\s+\w+\s*:|chapter\s+\d+\s*:)")
_RUNNING = re.compile(r"(?i)^(\d+\s+)?(chapter|part|appendix)\s+\w+\s*\|")
_TITLE = 1.6  # an appendix or chapter title is set at least this many median line heights tall


def _squash(t: str) -> str:
    return re.sub(r"[^a-z]", "", t.lower())


def _directly_below(a: Line, b: Line) -> bool:
    """`b` is the next line of `a`'s column: on the same page, just below it, starting near it."""
    return a.page == b.page and -0.3 * a.height <= b.box[1] - a.box[3] < 0.8 * a.height and \
        abs(b.box[0] - a.box[0]) < 2 * a.height


def _cond_heading(t: str) -> bool:
    return heading_like(t) and (_is_heading_caps(t) or title_case(t)) and len(t.split()) <= 2


def _resembles_condition(t: str) -> bool:
    return max(fuzz.ratio(_squash(t), c) for c in CONDITIONS) >= 80


def _other_heading(t: str) -> bool:
    """A heading set like the appendix's own (capitals) that names no condition, nor resembles one (a misread
    "STUNNEX" is still a condition heading; the title-case "Level Effect" table header is not a heading)."""
    return heading_like(t) and _is_heading_caps(t) and not _resembles_condition(t)


def _level_table(lines: list[Line]) -> tuple[dict, list[Line]] | None:
    """A "Level | Effect" table at the start of `lines`: its rows run to the first gap wider than a row."""
    if not lines or _squash(lines[0].text) != "leveleffect":
        return None
    rows: list[list[str]] = []
    k = 1
    while k < len(lines) and lines[k].box[1] - lines[k - 1].box[3] < 0.6 * lines[k].height:
        m = re.match(r"^(\d+)\s+(.*)$", lines[k].text.strip())
        rows.append([m.group(1), m.group(2)] if m else ["", lines[k].text.strip()])
        k += 1
    if not rows:
        return None
    return {"type": "table", "colLabels": ["Level", "Effect"], "rows": rows}, lines[k:]


def _condition_entries(lines: list[Line]) -> tuple[list, list[str], list[str]]:
    """(entries, rows whose level cell is blank, table headers no rows could be read under)."""
    out: list = []
    blank: list[str] = []
    bad: list[str] = []
    k = 0
    while k < len(lines):
        j = next((x for x in range(k, len(lines)) if _squash(lines[x].text) == "leveleffect"), len(lines))
        out += prose_entries(lines[k:j], 0, j - k) if j > k else []
        if j == len(lines):
            break
        got = _level_table(lines[j:])
        if got is None:  # a "Level Effect" header with no rows under it: read as text, and say so
            bad.append(lines[j].text.strip())
            nxt = next((x for x in range(j + 1, len(lines)) if _squash(lines[x].text) == "leveleffect"), len(lines))
            out += prose_entries(lines[j:nxt], 0, nxt - j)
            k = nxt
            continue
        tab, rest = got
        out.append(tab)
        blank += [r[1] for r in tab["rows"] if not r[0]]
        k = len(lines) - len(rest)
    return out, blank, bad


def parse_conditions(body: list[Line], ctx: ParseContext) -> list[Parsed]:
    """The conditions appendix: one Condition per heading. The appendix ends at the next appendix or chapter title
    (read by its words, or by its size when the OCR garbles them) or at the first heading that names no
    condition."""
    L = [l for l in body if l.text.strip() and not _RUNNING.match(l.text.strip())
         and not re.fullmatch(r"\d{1,3}\W?", l.text.strip())]
    if not L:
        return []
    med = statistics.median(l.height for l in L)
    starts = [i for i, l in enumerate(L) if _APPENDIX.match(l.text.strip()) and l.height >= 1.4 * med]
    out: list[Parsed] = []
    for s in starts:
        e = len(L)
        seen_head = False
        for j in range(s + 1, len(L)):
            t = L[j].text.strip()
            if (_SECTION_END.match(t) and L[j].height >= 1.4 * med) or L[j].height >= _TITLE * med:
                break  # the next appendix or chapter title, however the OCR read its words
            if seen_head and _other_heading(t):
                break  # a heading naming no condition ("D&D PANTHEONS"): the appendix is over
            seen_head = seen_head or _cond_heading(t)
        else:
            j = len(L)
        e = j
        sec = L[s + 1:e]
        heads: list[int] = []
        captions: set[int] = set()
        for k, l in enumerate(sec):
            t = l.text.strip()
            if not _cond_heading(t):
                continue
            nxt = sec[k + 1] if k + 1 < len(sec) else None
            if nxt is not None and _directly_below(l, nxt):
                heads.append(k)
            elif _squash(t) in CONDITIONS or _is_heading_caps(t):
                captions.add(k)  # a name set alone beside the art
        for n, h in enumerate(heads):
            stop = heads[n + 1] if n + 1 < len(heads) else len(sec)
            lines = [l for k, l in enumerate(sec[h + 1:stop], h + 1) if k not in captions]
            name = _title(sec[h].text.strip())
            ents, blank, bad = _condition_entries(lines)
            known = _squash(name) in CONDITIONS
            checks = [Check("condition-known", known, "" if known else
                            f"{name!r} is not one of the 15 PHB conditions"),
                      Check("entries-read", bool(ents), "" if ents else f"no text read under {name!r}")]
            if bad:
                checks.append(Check("table-rows-read", False, f"no rows read under the table header "
                                    f"{', '.join(repr(b) for b in bad)} of {name!r}; read as text"))
            if blank:
                checks.append(Check("table-rows-read", False, f"level cell of {name!r} table row(s) "
                                    f"{', '.join(repr(b) for b in blank)} is blank"))
            f: dict = {"entries": ents}
            text = f"{name}\n" + _join_text([l.text.strip() for l in lines])
            out.append(Parsed(type="Condition", name=name, fields=f, text=text, lines=[sec[h]] + lines,
                              checks=checks))
    return out
