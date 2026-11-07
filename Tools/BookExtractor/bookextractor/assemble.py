"""Assembler: pages -> parsed entities -> ids, common checks, provenance, report."""
from __future__ import annotations

import re
from collections import Counter
from collections.abc import Callable
from dataclasses import dataclass

from .checks import Check
from .layout import fix_digits, reading_order
from .model import Line, Page
from .names import (_PLAIN, Vocabulary, build_vocabulary, glued_words, own_heading_counts, repair_name,
                    spaced_pair)
from .pages import classify_page, is_table_page
from .parsers import ParseContext, Parsed
from .parsers.background import parse_backgrounds
from .parsers.charclass import parse_classes
from .parsers.equipment import parse_armor, parse_weapons
from .parsers.feat import parse_feats
from .parsers.gear import parse_items_mundane
from .parsers.god import parse_gods
from .parsers.hazard_rules import parse_conditions, parse_diseases_poisons
from .parsers.item import parse_items
from .parsers.monster import parse_monsters
from .parsers.race import parse_races
from .parsers.rule import parse_rules
from .parsers.sections import Section, read_sections
from .parsers.spell import parse_spells
from .parsers.subclass import _short_name, parse_subclasses
from .parsers.trap import parse_traps
from .parsers.vehicle_object import parse_objects, parse_vehicles
from .parsers.world import bare_heading, chapter_pages, has_sentence, parent_section, parse_world, placement_doubts
from .report import build_report

ALL_TYPES = ("Monster", "Spell", "MagicItem", "Class", "Subclass", "Race", "Subrace", "Feat",
             "Background", "Weapon", "Armor", "Item", "VehicleMount", "Object", "Trap", "DiseasePoison", "Condition",
             "God", "Rule", "Plane", "Faction", "Location", "Lore")
PHASE1_TYPES = ("Monster", "Spell", "MagicItem")
# stat blocks a host entity may print inside its own text (Drakewarden's Drake Companion, the Apparatus of Kwalish
# inside its magic item): exempt from text-not-shared when the host names them before the block
HOSTED_TYPES = ("Monster", "VehicleMount", "Object")
PROSE_LINES_PER_PAGE = 10
# printed lines two entities of different types may share before one is taken to hold the other's text
# (v19: unrelated character entities share at most a few lines of repeated trait boilerplate)
SHARED_LINES_MAX = 9
# the Phase 2b types (an equipment row is 1 to 3 lines, never 10): a pair with one of them is bleed once it shares
# half the smaller entity's lines, at least 2 and at most SHARED_LINES_MAX
PHASE2B_TYPES = ("Weapon", "Armor", "Item", "VehicleMount", "Object", "Trap", "DiseasePoison", "Condition")
# the Phase 2c types. Each sees the printed lines and names of every entity emitted before it
# (`ctx.cache["claimed_lines"]`, `ctx.cache["claimed_names"]`), so God and Rule run before the world types.
# Their text-not-shared gate is one-sided against the earlier types (only the new entity fails; the earlier
# entity's disposition never changes) and two-sided among themselves.
WORLD_TYPES = ("God", "Rule", "Plane", "Faction", "Location", "Lore")
WORLD_SECTION_TYPES = ("Plane", "Faction", "Location", "Lore")
SOURCE_BY_SLUG = {
    "phb14": "PHB", "mm14": "MM", "dmg14": "DMG", "mpmm": "MPMM", "mtf": "MTF", "ftd": "FTD", "bgg": "BGG",
    "vrgr": "VRGR", "egw": "EGW", "erlw": "ERLW", "ggr": "GGR", "scc": "SCC", "xge": "XGE", "tce": "TCE",
    "scag": "SCAG", "elemental-evil-player-s-companion": "EEPC",
}
_PARSERS: dict[str, Callable[[list[Line], ParseContext], list[Parsed]]] = {
    "Monster": lambda body, ctx: parse_monsters(body),
    "Spell": lambda body, ctx: parse_spells(body),
    "MagicItem": lambda body, ctx: parse_items(body),
    "Class": parse_classes,
    "Subclass": parse_subclasses,
    "Race": lambda body, ctx: [p for p in _races(body, ctx) if p.type == "Race"],
    "Subrace": lambda body, ctx: [p for p in _races(body, ctx) if p.type == "Subrace"],
    "Feat": parse_feats,
    "Background": parse_backgrounds,
    "Weapon": parse_weapons,
    "Armor": parse_armor,
    "Item": parse_items_mundane,
    "VehicleMount": parse_vehicles,
    "Object": parse_objects,
    "Trap": parse_traps,
    "DiseasePoison": parse_diseases_poisons,
    "Condition": parse_conditions,
    "God": parse_gods,
    "Rule": parse_rules,
    "Plane": lambda body, ctx: [p for p in _world(body, ctx) if p.type == "Plane"],
    "Faction": lambda body, ctx: [p for p in _world(body, ctx) if p.type == "Faction"],
    "Location": lambda body, ctx: [p for p in _world(body, ctx) if p.type == "Location"],
    "Lore": lambda body, ctx: [p for p in _world(body, ctx) if p.type == "Lore"],
}
# feature-ref lists whose part 0 (the feature name) is checked against the book vocabulary
_FEATURE_REFS = {"Class": ("classFeatures", "classFeature"), "Subclass": ("subclassFeatures", "subclassFeature")}
_DIE_NOTE = re.compile(r"\s+\(\d*d\d+\)$")
_DICE_LIKE = re.compile(r"\b[0-9SOolIZB]+d[0-9SOolIZB]+\b")
_MIXED = re.compile(r"\b(?:[0-9]+[SOolIZB]+|[SOolIZB]+[0-9]+)[0-9SOolIZB]*\b")
_NUMERIC_KEYS = {"formula": re.compile(r"\d+d\d+(?:\s*[+-]\s*\d+)?"), "cr": re.compile(r"[\d/]+")}
# Numeric fields are checked directly by key (any string value there must be a clean number).
_PLAIN_NUMBER = re.compile(r"[+-]?\d+(?:[./,]\d+)*")
_NUMBER_KEYS = {"ac", "average", "passive", "str", "dex", "con", "int", "wis", "cha", "level", "number",
                "amount", "walk", "fly", "swim", "climb", "burrow", "hover"}


def _races(body: list[Line], ctx: ParseContext) -> list[Parsed]:
    """One parse_races call per extraction gives both the Race and the Subrace records."""
    if "races" not in ctx.cache:
        ctx.cache["races"] = parse_races(body, ctx)
    return ctx.cache["races"]


def _world(body: list[Line], ctx: ParseContext) -> list[Parsed]:
    """One parse_world call per extraction gives the Plane, Faction, Location and Lore records; it sees the claims
    of every type before the first world type (the world types never claim from each other)."""
    if "world" not in ctx.cache:
        ctx.cache["world"] = parse_world(body, ctx)
    return ctx.cache["world"]


@dataclass
class ExtractionResult:
    entities: list[dict]
    report: dict


def slugify(name: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", name.lower()).strip("-")


def _is_prose(text: str) -> bool:
    letters = sum(c.isalpha() for c in text)
    return len(text) >= 25 and len(text.split()) >= 5 and letters / len(text) >= 0.75


def _provenance(lines: list[Line], pages: dict[int, Page]) -> dict:
    """Per page, one box per column (union of that column's lines), in page-image pixels."""
    by_page: dict[int, dict[int, list[tuple[float, float, float, float]]]] = {}
    for l in lines:
        pg = pages.get(l.page)
        mid = (pg.width / 2) if pg else 0
        col = 1 if pg and (l.box[0] + l.box[2]) / 2 > mid else 0
        by_page.setdefault(l.page, {}).setdefault(col, []).append(l.box)
    out = []
    for number in sorted(by_page):
        boxes = []
        for col in sorted(by_page[number]):
            bs = by_page[number][col]
            boxes.append({"x0": min(b[0] for b in bs), "y0": min(b[1] for b in bs),
                          "x1": max(b[2] for b in bs), "y1": max(b[3] for b in bs)})
        out.append({"page": number, "boxes": boxes})
    return {"pages": out}


def _repair_digits(v):
    """Apply the OCR digit repair (ld6 -> 1d6, I00 -> 100) to every string, in place for containers."""
    if isinstance(v, str):
        return fix_digits(v)
    if isinstance(v, list):
        return [_repair_digits(x) for x in v]
    if isinstance(v, dict):
        return {k: _repair_digits(x) for k, x in v.items()}
    return v


def _numeric_problems(fields: dict) -> list[str]:
    """Letters left inside numbers after repair. Deliberately also scans prose entries (damage dice live in
    descriptions, not only in structured fields), so a digit-noise token the repair could not fix is flagged."""
    bad: list[str] = []

    def walk(v, path: str) -> None:
        if isinstance(v, dict):
            for k, x in v.items():
                if k in _NUMERIC_KEYS and isinstance(x, str) and not _NUMERIC_KEYS[k].fullmatch(x.strip()):
                    bad.append(f"{path}.{k}={x!r}")
                if k in _NUMBER_KEYS and isinstance(x, str) and not _PLAIN_NUMBER.fullmatch(x.strip()):
                    bad.append(f"{path}.{k}={x!r}")
                walk(x, f"{path}.{k}")
        elif isinstance(v, list):
            for i, x in enumerate(v):
                walk(x, f"{path}[{i}]")
        elif isinstance(v, str):
            for m in _DICE_LIKE.finditer(v):
                if re.search(r"\d", m.group(0)) and not re.fullmatch(r"\d+d\d+", m.group(0)):
                    bad.append(f"{path}: {m.group(0)!r}")
            for m in _MIXED.finditer(v):
                bad.append(f"{path}: {m.group(0)!r}")

    walk(fields, "fields")
    return bad


def _replace_heading(text: str, old_name: str, new_name: str) -> str:
    """Put the repaired name on the first line of canonicalText when that line is the printed heading."""
    first, sep, rest = text.partition("\n")
    squash = lambda s: re.sub(r"[^a-z]", "", s.lower())  # noqa: E731
    if squash(first) == squash(old_name):
        return new_name + sep + rest
    return text


def _repair_feature_name(name: str, vocab: Vocabulary):
    """(repaired name, repaired?, unknown note or '', unattested words) for one printed feature name."""
    res = repair_name(name, vocab, Vocabulary())
    # a glued word the book prints spaced more often than glued is split, however often the glued form occurs
    # (TCE prints its small-caps "Extra Attack" heading as "ExtRAATtaCk" three times)
    words = res.name.split()
    split = [" ".join(w[:1].upper() + w[1:] for w in sp) if (sp := spaced_pair(t, vocab)) else t for t in words]
    if split != words:
        res = repair_name(" ".join(split), vocab, Vocabulary())
        res.repaired = True
    # a feature heading printed once that reads as two known words glued (OCR lost the space) is a misread,
    # not a coined word: a real coined name is rarely two ordinary words run together
    glued = [f"{w} = {'+'.join(g)}" for w in res.unattested if (g := glued_words(w, vocab))]
    unknown = f"{res.name!r} ({', '.join(res.unknown + glued)})" if res.unknown or glued else ""
    return res.name, res.repaired, unknown, [w for w in res.unattested if not glued_words(w, vocab)]


def _feature_name_checks(p: Parsed, vocab: Vocabulary) -> list[Check]:
    """Repair each feature ref's name (part 0) and each feature entry's heading (`entries[].name`) from the book
    vocabulary, as entity names are, and flag the names that still hold misread words. Only the name changes;
    the rest of the ref is kept as printed. Refs and entry headings go through the same repair, so a feature's
    ref and its entry heading agree (the first-level features are folded into the subclass-named ref, so their
    headings are checked only as entries).

    Unlike an entity name, the feature's own heading is not subtracted from the vocabulary: a word is known when
    the book prints it at least twice in all, heading included. A misread is one-off, so it still fails, while a
    real word a book prints only in its feature heading and once more (XGE "Hearth", "Duelist") is not flagged."""
    key, obj_key = _FEATURE_REFS[p.type]
    refs = p.fields.get(key)
    if not isinstance(refs, list):
        return []
    repaired: list[str] = []
    unknown: list[str] = []
    unattested: list[str] = []

    def fix(raw: str) -> str:
        # a printed die annotation ("Song of Rest (d6)", the 5etools ref keeps it) is not a word to check
        m = _DIE_NOTE.search(raw)
        word_part, note = (raw[:m.start()], raw[m.start():]) if m else (raw, "")
        name, rep, unk, unatt = _repair_feature_name(word_part, vocab)
        if rep and (msg := f"{word_part!r} -> {name!r}") not in repaired:
            repaired.append(msg)
        if unk and unk not in unknown:
            unknown.append(unk)
        unattested.extend(w for w in unatt if w not in unattested)
        return (name if rep else word_part) + note

    out = []
    for r in refs:
        ref = r.get(obj_key) if isinstance(r, dict) else r
        if not isinstance(ref, str) or not ref.split("|")[0].strip():
            out.append(r)
            continue
        raw, sep, rest = ref.partition("|")
        ref = fix(raw) + sep + rest
        out.append({**r, obj_key: ref} if isinstance(r, dict) else ref)
    p.fields[key] = out
    entries = p.fields.get("entries")
    if isinstance(entries, list):
        p.fields["entries"] = [{**e, "name": fix(e["name"])} if isinstance(e, dict) and isinstance(e.get("name"), str)
                               and e["name"].strip() else e for e in entries]
    checks = []
    if repaired:
        checks.append(Check("feature-name-repaired", True, "; ".join(repaired)))
    if unknown:
        checks.append(Check("feature-names-known", False, "unknown words in feature names: " + "; ".join(unknown)))
    else:
        checks.append(Check("feature-names-known", True, "" if not unattested
                            else "printed only once in the book: " + ", ".join(unattested)))
    return checks


def _repair_subclass_short(p: Parsed, old: str, new: str) -> str:
    """A repaired subclass name re-derives its shortName and rewrites every subclassFeatures ref's ShortName
    (part 3), and the subclass-named ref's part 0, so no field keeps the misread. Returns what changed, for the
    `name-repaired` check ('' when nothing did)."""
    f = p.fields
    cls = f.get("className") or ""
    old_short = f.get("shortName")
    new_short = _short_name(new, cls)
    if not isinstance(old_short, str) or old_short == new_short:
        return ""
    f["shortName"] = new_short
    refs, n = [], 0
    for r in f.get("subclassFeatures") or []:
        ref = r.get("subclassFeature") if isinstance(r, dict) else r
        parts = ref.split("|") if isinstance(ref, str) else []
        if len(parts) == 6 and parts[3] == old_short:
            parts[3] = new_short
            if parts[0] == old:
                parts[0] = new
            ref, n = "|".join(parts), n + 1
            r = {**r, "subclassFeature": ref} if isinstance(r, dict) else ref
        refs.append(r)
    if "subclassFeatures" in f:
        f["subclassFeatures"] = refs
    return f"shortName {old_short!r} -> {new_short!r} in the shortName and {n} subclassFeatures refs"


def _name_words_check(res, p: Parsed, vocab: Vocabulary) -> Check:
    """`name-words-known`. A table row's label cell is attested by the printed table itself when the row's own
    (cell) checks pass (P4): a weapon printed once, as a row label ("Greatclub", "Flail", "Maul"), is not a
    misread for being one letter from a common word or two words run together. Only plain-word tokens are
    attested: a token holding any other character ("Gr€ataxe") is a sure misread and still fails (P17). A glued
    label whose words the book prints spaced at least as often ("Unarmedstrike" for "unarmed strike") is still a
    lost space, and fails. A table-row label holding no letter at all ("7") is no name."""
    bare = lambda ts: [t.strip(".,;:") for t in ts]  # noqa: E731  ("Bread," reads "Bread" in a reason)
    if p.table_row and not re.search(r"[A-Za-z]", res.name):
        return Check("name-words-known", False, f"no word in name {res.name!r}")
    unknown, why = bare(res.unknown), ""
    if unknown and p.table_row and all(c.passed for c in p.checks):
        glued = {t: sp for t in unknown if _PLAIN.match(t) and (sp := spaced_pair(t, vocab))}
        attested = [t for t in unknown if _PLAIN.match(t) and t not in glued]
        unknown = [f"{t} (printed spaced as {' '.join(glued[t])!r})" if t in glued else t
                   for t in unknown if t not in attested]
        if attested:
            why = "printed only as its table-row label (the table attests it): " + ", ".join(attested)
    if unknown:
        return Check("name-words-known", False, "unknown words in name: " + ", ".join(unknown))
    if res.unattested:
        why = "; ".join(x for x in (why, "not printed elsewhere in the book: " + ", ".join(bare(res.unattested)))
                        if x)
    return Check("name-words-known", True, why)


def _text_present(p: Parsed) -> Check:
    """`text-present`: at least 30 characters. A short table row ("Bell | 1 gp | -") has no other text than its
    printed cells, so it passes when the row's own cell checks all pass; unreadable cells still fail."""
    if len(p.text.strip()) >= 30:
        return Check("text-present", True, "")
    if p.table_row and p.text.strip() and p.checks and all(c.passed for c in p.checks):
        return Check("text-present", True, "short table row: its printed cells are its whole text")
    return Check("text-present", False, "entity text is empty or too short")


def _id_and_checks(p: Parsed, book_slug: str, seen: Counter, vocab: Vocabulary) -> tuple[str, list[Check]]:
    p.fields = _repair_digits(p.fields)
    p.text = fix_digits(p.text)
    raw = p.name
    res = repair_name(raw, vocab, own_heading_counts(raw)) if raw.strip() else None
    name = res.name if res else ""
    checks = list(p.checks)
    if res and res.repaired:
        why = f"{res.original!r} -> {res.name!r}"
        if p.type == "Subclass" and (short := _repair_subclass_short(p, raw, name)):
            why += f"; {short}"
        # a parser repair already recorded (a subclass named by its cues) keeps one check telling both steps
        k = next((k for k, c in enumerate(checks) if c.name == "name-repaired"), None)
        if k is None:
            checks.append(Check("name-repaired", True, why))
        else:
            checks[k] = Check("name-repaired", True, f"{checks[k].reason}; then {why}")
    # a table row's label cell may open with a number ("10-foot pole" is printed so) (P15), but needs a letter
    first_ok = (lambda c: c.isalnum()) if p.table_row else (lambda c: c.isalpha())
    if not name.strip():
        checks.append(Check("name-found", False, "no entity name found"))
    elif p.table_row and not re.search(r"[A-Za-z]", name):
        checks.append(Check("name-found", False, f"table-row label {name!r} holds no letter"))
    elif not first_ok(name.strip()[0]):
        checks.append(Check("name-found", False, f"name {name!r} does not start with a letter"))
    else:
        checks.append(Check("name-found", True, ""))
    if res:
        checks.append(_name_words_check(res, p, vocab))
    else:
        checks.append(Check("name-words-known", False, "no name to check"))
    if p.type in _FEATURE_REFS:
        checks += _feature_name_checks(p, vocab)
    checks.append(_text_present(p))
    bad = _numeric_problems(p.fields)
    checks.append(Check("numeric-clean", not bad, "" if not bad else "letters in numeric values: " + "; ".join(bad[:4])))
    page = p.lines[0].page if p.lines else 0
    # a subrace's id names its race too: "Hill Dwarf" of "Dwarf" -> phb14.subrace.dwarf-hill-dwarf
    race = p.fields.get("raceName") if p.type == "Subrace" and name.strip() else None
    id_name = f"{race} {name}" if isinstance(race, str) and race.strip() else name
    base = f"{book_slug}.{p.type.lower()}.{slugify(id_name) or f'unnamed-{page}'}"
    seen[base] += 1
    eid = base if seen[base] == 1 else f"{base}-{seen[base]}"
    checks.append(Check("unique-id", seen[base] == 1,
                        "" if seen[base] == 1 else f"id {base} already used; suffixed -{seen[base]}"))
    p.name = name
    if res and res.repaired:
        p.text = _replace_heading(p.text, raw, name)
    return eid, checks


def _parent_of(p: Parsed, owners: dict, sections: list) -> tuple[str | None, object]:
    """The printed heading enclosing the entity and its section (None for a chapter title): the parent of the
    section its first line opens, else the innermost section holding that line (a sidebar rule inside it)."""
    if not p.lines:
        return None, None
    hit = owners.get(next(iter(_line_keys(p.lines[:1]))))
    if hit is None:
        return None, None
    level, sec = hit
    if level >= 0:
        return bare_heading(sec.title), sec
    return (bare_heading(sec.parents[-1]), parent_section(sec, sections)) if sec.parents else (None, None)


def _world_ids(parsed: list[Parsed], checks: list[list[Check]], book_slug: str, ctx: ParseContext,
               body: list[Line]) -> list[tuple[str, list[Check]]]:
    """Ids for one God, Rule or world type, unique and deterministic over the book, with a passing `unique-id`
    check naming how a repeated name was told apart. A name printed once is `<name>`. A name that repeats is,
    for every occurrence (none is "first wins"): a god's `<name>-<pantheon>` (ruling W4), any other's
    `<parent-heading>-<name>` (ruling W11, as subraces); still repeated, `-p<page>` is appended (ruling W19);
    still repeated, an ordinal in print order (and `unique-id` fails: one heading printed twice on one page
    under one parent is probably read twice)."""
    if not parsed:
        return []
    owners: dict[LineKey, tuple[int, Section]] = {}
    sections = read_sections(body, ctx) if parsed[0].type != "God" else []
    if sections:
        for sec in sections:
            for k, l in enumerate(sec.lines):
                key = next(iter(_line_keys([l])))
                # the section's own heading line: (-1, ..) names its parent; a deeper section's line wins
                level = -1 if k == 0 else sec.level
                old = owners.get(key)
                if old is None or old[0] != -1 and (level == -1 or level >= old[0]):
                    owners[key] = (level, sec)
    pages = [p.lines[0].page if p.lines else 0 for p in parsed]
    stems = [slugify(p.name) or f"unnamed-{pg}" for p, pg in zip(parsed, pages)]
    why = ["" for _ in parsed]
    parent_used: dict[int, object] = {}  # entity index -> the parent section its id names (W11)

    def spread(qualify) -> None:
        counts = Counter(stems)
        for i, p in enumerate(parsed):
            if counts[stems[i]] > 1 and (q := qualify(i, p)):
                stems[i], why[i] = q

    def by_parent(i: int, p: Parsed):
        if p.type == "God":
            pan = p.fields.get("pantheon")
            return (f"{stems[i]}-{slugify(pan)}", f"name repeats in the book: pantheon {pan!r} added") \
                if isinstance(pan, str) and slugify(pan) else None
        par, sec = _parent_of(p, owners, sections)
        parent_used[i] = sec
        return (f"{slugify(par)}-{stems[i]}", f"name repeats in the book: parent heading {par!r} added") \
            if par and slugify(par) else None

    spread(by_parent)
    before = list(stems)
    spread(lambda i, p: (f"{stems[i]}-p{pages[i]}", "; ".join(x for x in (why[i], f"page {pages[i]} added") if x)))
    memo: list[dict[int, str]] = []

    def placed() -> dict[int, str]:  # the reader-tree doubts, computed once and only when a rule needs them
        if not memo:
            memo.append(placement_doubts(sections, chapter_pages(body, ctx), ctx.cache.get("claimed_lines", set())))
        return memo[0]
    counts, nth = Counter(stems), Counter()
    out = []
    for i, p in enumerate(parsed):
        cs = [c for c in checks[i] if c.name != "unique-id"]
        stem = stems[i]
        if counts[stem] > 1:
            nth[stem] += 1
            stem = f"{stem}-{nth[stem]}"
            cs.append(Check("unique-id", False, f"id {book_slug}.{p.type.lower()}.{stems[i]} printed "
                                                f"{counts[stems[i]]} times; ordinal -{nth[stems[i]]} appended"))
        else:
            cs.append(Check("unique-id", True, why[i]))
        # a rule's W11 parent is verified here (a world section's by its own `entry-level`, ruling W21)
        if p.type == "Rule" and i in parent_used and (doubt := _rule_parent_doubt(p, parent_used[i], before[i] !=
                                                                                    stems[i], placed())):
            cs.append(Check("entry-level", False, doubt))
        out.append((f"{book_slug}.{p.type.lower()}.{stem}", cs))
    return out


def _rule_parent_doubt(p: Parsed, sec, repeated: bool, placed: dict[int, str]) -> str:
    """Why the parent heading a rule's id names is not verified (rulings W21, W23; '' when it is): the parent's own
    place in the reader's tree is doubted (`placement_doubts`), the parent prints the rule's heading more than once
    (entry headings between them were not read), or it holds no sentence outside the rule (a table caption or title
    read as a heading)."""
    if sec is None:
        return ""
    if placed.get(id(sec)):
        return f"its parent heading {sec.title!r} (p{sec.page}) is doubted: {placed[id(sec)]}"
    if repeated:
        return (f"the heading {p.name!r} is printed more than once under its parent heading {sec.title!r} "
                f"(p{sec.page}): the parent is not its nearest enclosing heading")
    mine = _line_keys(p.lines)
    if not has_sentence([l for l in sec.lines if not _line_keys([l]) & mine]):
        return (f"its parent heading {sec.title!r} (p{sec.page}) holds no sentence of its own outside {p.name!r}: "
                "a table caption or title read as a heading")
    return ""


LineKey = tuple[int, tuple[float, ...]]


def _line_keys(lines: list[Line]) -> set[LineKey]:
    """Printed-line identity: page and box (reading order rebuilds Line objects, so not the object)."""
    return {(l.page, tuple(round(x, 1) for x in l.box)) for l in lines}


def _names_before(host: list[tuple[LineKey, str]], first: LineKey | None, name: str) -> bool:
    """The host's printed text before the stat block's first line names the monster (Drakewarden's "your drake
    companion" before the Drake Companion stat block), compared as whole words, case-insensitive: "Ape" is not
    named by "shape", nor "Owl" by "knowledge"."""
    at = next((k for k, (key, _) in enumerate(host) if key == first), None)
    want = re.findall(r"[a-z]+", name.lower())
    if at is None or not want:
        return False
    words = re.findall(r"[a-z]+", " ".join(t for _, t in host[:at]).lower())
    n = len(want)
    return any(words[i:i + n] == want for i in range(len(words) - n + 1))


def shared_text_checks(entities: list[dict], keys: list[set[LineKey]],
                       firsts: list[LineKey | None] | None = None,
                       texts: list[list[tuple[LineKey, str]]] | None = None) -> None:
    """Text bleed gate: a character entity (Class ... Background) that shares more than SHARED_LINES_MAX printed
    lines with an entity of another type holds that entity's text (a subclass feature running on into the
    Backgrounds section), so both get a failing `text-not-shared` check naming the other. A stat block the other
    entity prints is not bleed: its first line is one of the host's lines and the host's text before that line
    names the monster (Drakewarden's Drake Companion, Battle Smith's Steel Defender); a vehicle or object stat block
    the same way (the Apparatus of Kwalish inside its magic item, P10). A stat block parse that runs
    on past its host is then the monster's own problem, which its own checks report; a stat block the host never
    names is another section's, swallowed by a run-on parse. Character entities record a passing check; a
    Monster/Spell/MagicItem gets the check only when it fails, and two of those are never compared (their own
    checks gate them). An equipment or hazard entity (an equipment row is 1 to 3 lines) fails from fewer shared
    lines: half the smaller entity's lines, at least 2 and at most SHARED_LINES_MAX; its partner of an older type
    keeps its own gate. A God, Rule or world entity fails from SHARED_LINES_MAX + 1 shared lines; its partner of an
    earlier type never fails for it, and a magic item it names before the item's first line is hosted. `texts` holds each entity's printed lines in print order (key, text)."""
    owner: dict[LineKey, list[int]] = {}
    for i, ks in enumerate(keys):
        for k in ks:
            owner.setdefault(k, []).append(i)
    shared: Counter = Counter()
    for ids in owner.values():
        for x in range(len(ids)):
            for y in range(x + 1, len(ids)):
                shared[(ids[x], ids[y])] += 1
    bad: dict[int, list[str]] = {}
    for (i, j), n in shared.items():
        a, b = entities[i], entities[j]
        if a["type"] == b["type"] or (a["type"] in PHASE1_TYPES and b["type"] in PHASE1_TYPES):
            continue
        # the Phase 1/2a entity of a pair keeps the old gate; a Phase 2b entity fails from the relative one
        relative = min(SHARED_LINES_MAX, max(2, -(-min(len(keys[i]), len(keys[j])) // 2)))
        fail_a, fail_b = (n >= (relative if e["type"] in PHASE2B_TYPES else SHARED_LINES_MAX + 1) for e in (a, b))
        # a world-type entity paired with an earlier type's: only the world entity fails (one-sided), so no earlier
        # disposition changes; two world-type entities both fail
        new_a, new_b = a["type"] in WORLD_TYPES, b["type"] in WORLD_TYPES
        fail_a, fail_b = fail_a and (new_a or not new_b), fail_b and (new_b or not new_a)
        if not (fail_a or fail_b):
            continue
        # a magic item printed inside a god, rule or world section is hosted as a stat block is (the host names it)
        if firsts is not None and texts is not None and any(
                (e["type"] in HOSTED_TYPES or e["type"] == "MagicItem" and entities[o]["type"] in WORLD_TYPES)
                and firsts[k] in keys[o]
                and _names_before(texts[o], firsts[k], e.get("name", ""))
                for e, k, o in ((a, i, j), (b, j, i))):
            continue
        if fail_a:
            bad.setdefault(i, []).append(f"{n} printed lines are also {b['id']} ({b['type']})")
        if fail_b:
            bad.setdefault(j, []).append(f"{n} printed lines are also {a['id']} ({a['type']})")
    for i, e in enumerate(entities):
        if i in bad:
            e["checks"].append({"name": "text-not-shared", "passed": False,
                                "reason": "text bleed: " + "; ".join(bad[i])})
        elif e["type"] not in PHASE1_TYPES:
            e["checks"].append({"name": "text-not-shared", "passed": True, "reason": ""})


def extract_book(pages: list[Page], book_slug: str, types: set[str]) -> ExtractionResult:
    by_number = {p.number: p for p in pages}
    ctx = ParseContext(slug=book_slug, source=SOURCE_BY_SLUG.get(book_slug, book_slug.upper()), pages=by_number)
    kinds = {p.number: classify_page(p) for p in pages}
    rules = [p for p in pages if kinds[p.number] == "rules"]
    ordered = {p.number: reading_order(p) for p in pages if kinds[p.number] in ("rules", "art")}

    listed = [l.text for p in pages if kinds[p.number] in ("index", "toc") for l in p.lines]
    vocab = build_vocabulary((l.text for p in rules for l in ordered[p.number]), listed)

    body: list[Line] = [l for p in pages if p.number in ordered for l in ordered[p.number]]
    entities: list[dict] = []
    covered: set[int] = set()
    seen: Counter = Counter()
    keys: list[set[LineKey]] = []
    firsts: list[LineKey | None] = []
    texts: list[list[tuple[LineKey, str]]] = []
    claimed_lines: set[LineKey] = set()
    claimed_names: set[str] = set()
    god_names: set[str] = set()  # the gods' printed names: a world section naming one is a part of its text
    for t in ALL_TYPES:
        if t not in types:
            continue
        ctx.cache["claimed_lines"], ctx.cache["claimed_names"] = set(claimed_lines), set(claimed_names)
        ctx.cache["god_names"] = set(god_names)
        parsed = _PARSERS[t](body, ctx)
        claimed_names.update(p.name for p in parsed if p.name.strip())  # as printed, before the name repair
        if t == "God":
            god_names.update(p.name for p in parsed if p.name.strip())
        # a world type's ids are assigned over the whole type at once (rulings W4, W11, W19)
        named = ([_id_and_checks(p, book_slug, seen, vocab) for p in parsed] if t not in WORLD_TYPES else
                 _world_ids(parsed, [_id_and_checks(p, book_slug, Counter(), vocab)[1] for p in parsed],
                            book_slug, ctx, body))
        for p, (eid, checks) in zip(parsed, named):
            claimed_lines |= _line_keys(p.lines)
            covered.update(l.page for l in p.lines)
            keys.append(_line_keys(p.lines))
            firsts.append(next(iter(_line_keys(p.lines[:1])), None))
            texts.append([(next(iter(_line_keys([l]))), l.text) for l in p.lines])
            entities.append({
                "id": eid, "type": p.type, "name": p.name,
                "page": p.lines[0].page if p.lines else 0,
                "canonicalText": p.text, "fields": p.fields,
                "checks": [{"name": c.name, "passed": c.passed, "reason": c.reason} for c in checks],
                "provenance": _provenance(p.lines, by_number),
            })

    shared_text_checks(entities, keys, firsts, texts)
    for p in rules:
        if sum(_is_prose(l.text) for l in ordered[p.number]) >= PROSE_LINES_PER_PAGE or is_table_page(p):
            covered.add(p.number)
    rules_numbers = {p.number for p in rules}
    report = build_report(book_slug, len(pages), len(rules_numbers), len(covered & rules_numbers), entities)
    return ExtractionResult(entities=entities, report=report)
