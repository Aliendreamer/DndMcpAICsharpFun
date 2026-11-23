"""Assembler: pages -> parsed entities -> ids, common checks, provenance, report."""
from __future__ import annotations

import json
import re
from collections import Counter
from collections.abc import Callable
from dataclasses import dataclass, field

from .checks import Check
from .parsers.charcommon import PAGE_NO, RUNNING_HEAD
from .coverage import measure
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
from .parsers.charcommon import DICE_HEADER
from .parsers.sections import Section, drop_art, drop_pull_quotes, heading_words, prose, read_sections
from .parsers.spell import parse_spells
from .parsers.subclass import _short_name, parse_subclasses
from .parsers.trap import parse_traps
from .parsers.vehicle_object import parse_objects, parse_vehicles
from .parsers.world import bare_heading, chapter_pages, garbage_heading, has_sentence, parent_section, parse_world, placement_doubts, table_title
from .report import build_report
from .sidebars import Note, detached_sidebars, left_out_art, margin_notes, quote_box, run_title, _ENTRIES_TYPES, _epigraph, _drop_cap_opening, CHAPTER_TITLE
from .residual import recurring_labels, residual_checks

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
    left_out: list[tuple[int, str]] = field(default_factory=list)  # (page, text) of lines left out on purpose
    coverage: dict = field(default_factory=dict)  # Coverage.to_dict(): content lines and the unheld ones


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


_LONG_UNSEEN = 9  # a heading word of this many letters that the book prints nowhere else
_VERY_LONG_UNSEEN = 15  # ... a word of this many letters needs no capital inside it to be suspect ("Initialinfection")


def _run_together(words: list[str], raw: str, vocab: Vocabulary, own: Vocabulary) -> list[str]:
    """The words of a heading that look like words the OCR ran together: a long word the book prints nowhere outside
    the heading itself (`own` is what the heading added to the vocabulary), that is very long, or printed with a capital
    inside it (`raw`, the heading as read), or that the book prints spaced elsewhere ("citadel adbar")."""
    inner = {re.sub(r"[^a-z]", "", t.lower()) for t in raw.split() if re.search(r"[a-z][A-Z]", t)}
    return [w for w in words
            if (len(w) >= _LONG_UNSEEN and w.lower() in inner or len(w) >= _VERY_LONG_UNSEEN
                or len(w) >= 8 and spaced_pair(w, vocab) is not None)
            and vocab.words.get(w.lower(), 0) - own.words.get(w.lower(), 0) <= 0]


def _unseen_long_word_check(p: Parsed, checks: list[Check], vocab: Vocabulary) -> list[Check]:
    """A Rule, Plane, Faction, Location or Lore name holding a long word that the book prints nowhere outside the
    entity's own heading, printed in the heading with a capital inside it ("KitDependency", "WithoutXP",
    "InitialInfection"), is probably two words the OCR ran together (Task 7 round 4): `name-words-known` fails and the
    entity needs review. A capital inside a word alone is OCR casing noise and a rare word alone is common, so
    both are needed. A God's name is a coined proper name and is left alone. The category (the parent heading) is
    read the same way (task 8: "Demoniccambions", "Aprecarious Peace"): `category-words-known` fails."""
    if p.type == "God" or not p.lines:
        return checks
    raw = p.lines[0].text
    long = _run_together(re.findall(r"[A-Za-z]+", p.name), raw, vocab, own_heading_counts(p.name))
    cat = p.fields.get("category") if isinstance(p.fields, dict) else None
    bad_cat = _run_together(re.findall(r"[A-Za-z]+", cat), p.category_raw, vocab, own_heading_counts(cat)) \
        if isinstance(cat, str) and cat else []
    if bad_cat:
        checks = [*checks, Check("category-words-known", False, f"the category {cat!r} holds words probably run "
                                 "together by the OCR (printed nowhere else in the book): " + ", ".join(bad_cat))]
    if not long:
        return checks
    why = "a long word printed nowhere else in the book (very long, or with a capital inside it in print): probably " \
          "words run together by the OCR: " + ", ".join(long)
    return [Check("name-words-known", False, why) if c.name == "name-words-known" and c.passed else c
            for c in checks]


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
            memo.append(placement_doubts(sections, chapter_pages(body, ctx), ctx.cache.get("claimed_lines", set()),
                                         body=body))
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


def _squash(text: str) -> str:
    return re.sub(r"[^a-z0-9]+", "", text.lower())


def _strip_line(entities: list[dict], line: str, entity_lines: list[list[Line]] | None = None, page: int | None = None) -> None:
    """Remove a printed line that stands alone in an entity's canonicalText or fields (a margin note's signature), only
    from the entities printed on the note's page: the same signature ("-Mordenkainen") is printed on many pages, and one
    note's must not take the signature out of an aside glued into another page's entity."""
    def clean(v):
        if isinstance(v, str):
            return "\n".join(x for x in v.split("\n") if x.strip() != line)
        if isinstance(v, list):
            return [clean(x) for x in v if not (isinstance(x, str) and x.strip() == line)]
        if isinstance(v, dict):
            return {k: clean(x) for k, x in v.items()}
        return v

    for i, e in enumerate(entities):
        if page is not None and entity_lines is not None and not (i < len(entity_lines) and any(l.page == page for l in entity_lines[i])):
            continue
        if line in e["canonicalText"]:
            e["canonicalText"] = clean(e["canonicalText"])
        if line in json.dumps(e["fields"], ensure_ascii=False):
            e["fields"] = clean(e["fields"])


_APPEND_TO_OWNER = False  # a flagged Lore of its own instead: appending changes reviewed entities (answer keys)


def _keep_unheld(entities: list[dict], entity_lines: list[list[Line]], ordered: dict, opening_runs: list[list[Line]],
                 left_out: list, note_texts: set, make_lore: bool, keys: list, firsts: list, texts: list,
                 book_slug: str, seen: Counter, vocab: Vocabulary, by_number: dict, claimed: set, covered: set) -> None:
    """Keep-all-content: a run of printed prose that no entity's text, no note and no left-out rule holds is never dropped.
    Lines an entity claimed but its own pruning took out of its text go back into that entity (its `entries`
    and canonicalText) when it has entries, else into a flagged Lore; an opening no entity claimed (a chapter's or
    section's drop-cap text) is a flagged Lore named after its chapter or section title, else its first line."""
    page_text: dict[int, str] = {}
    for e, lines_ in zip(entities, entity_lines):
        for pg in {l.page for l in lines_}:
            page_text[pg] = page_text.get(pg, "") + "\n" + e["canonicalText"] + "\n" + " ".join(
                n["text"] for n in e.get("notes", []))
    page_text = {pg: _squash(t) for pg, t in page_text.items()}
    left_texts = {t for _, t in left_out}

    def unheld(l: Line) -> bool:
        t = l.text.strip()
        # (the entity text is digit-repaired, the OCR line is not: "ld6" is held by "1d6")
        return (len(_squash(t)) >= 8 and _squash(t) not in page_text.get(l.page, "")
                and _squash(fix_digits(t)) not in page_text.get(l.page, "") and t not in left_texts
                and t not in note_texts and not PAGE_NO.fullmatch(t) and not RUNNING_HEAD.match(t))

    def qualifies(run: list[Line]) -> bool:
        return any(len(x.text.strip()) >= 30 and _is_prose(x.text) for x in run) and not quote_box(run) \
            and not _epigraph(run)

    jobs: list[tuple[int | None, list[Line]]] = []
    for i, lines_ in enumerate(entity_lines):
        run: list[Line] = []
        for l in [*lines_, None]:
            if l is not None and unheld(l) and (not run or l.page == run[-1].page):
                run.append(l)
                continue
            if run and qualifies(run):
                jobs.append((i, run))
            run = [l] if l is not None and unheld(l) else []
    for run in opening_runs:
        run = [l for l in run if unheld(l)]
        if run:
            jobs.append((None, run))
    # an opening nobody claimed: unclaimed runs on a chapter's first page, or set like a drop-cap opening anywhere
    claimed_here = {k for lines_ in entity_lines for k in _line_keys(lines_)}
    for n, page_lines in ordered.items():
        opener_page = any(CHAPTER_TITLE.match(l.text.strip()) for l in page_lines)
        run = []
        for l in [*page_lines, None]:
            if l is not None and unheld(l) and next(iter(_line_keys([l]))) not in claimed_here:
                run.append(l)
                continue
            if run and ((opener_page and qualifies(run)) or _drop_cap_opening(run)):
                jobs.append((None, run))
            run = []
    # text the art and pull-quote rules would take out of a column that is neither a note, an epigraph nor a caption: body
    # text they misread (an inset speech box, scaled body text). Kept by the page's main entity, else a flagged Lore.
    ctx_ = ParseContext(slug=book_slug, source=book_slug.upper(), pages=by_number)
    on_page: dict[int, tuple[int, int]] = {}
    for i, lines_ in enumerate(entity_lines):
        for pg in {l.page for l in lines_}:
            chars = sum(len(l.text) for l in lines_ if l.page == pg)
            if pg not in on_page or chars > on_page[pg][0]:
                on_page[pg] = (chars, i)
    for n, page_lines in ordered.items():
        cand = ({id(l) for l in page_lines} - {id(l) for l in drop_art(page_lines, ctx_, rescue=False)}) \
            | ({id(l) for l in page_lines} - {id(l) for l in drop_pull_quotes(page_lines, 0, rescue=False)})
        run = []
        for l in [*page_lines, None]:
            if l is not None and id(l) in cand and unheld(l):
                run.append(l)
                continue
            if run and qualifies(run):
                jobs.append((on_page[n][1] if n in on_page else None, run))
            run = []
    # prose no entity claimed at all (a paragraph after a stat block, a column's continuation, a run the section reader
    # dropped): kept by the entity it continues, else a flagged Lore named after the nearest heading
    fresh: list[tuple[int | None, list[Line]]] = []
    for n, page_lines in ordered.items():
        run = []
        for l in [*page_lines, None]:
            if l is not None and unheld(l) and next(iter(_line_keys([l]))) not in claimed:
                run.append(l)
                continue
            if run and qualifies(run):
                fresh.append((None, run))
            run = []
    last_of = {}
    for i, lines_ in enumerate(entity_lines):
        if lines_:
            last_of.setdefault(next(iter(_line_keys(lines_[-1:]))), i)

    def preceding(run: list[Line]) -> tuple[Line | None, int]:
        """The line printed just before the run in reading order (the previous page's last line for a page's first run)."""
        n = run[0].page
        at = next((k for k, x in enumerate(ordered[n]) if x is run[0] or _line_keys([x]) == _line_keys(run[:1])), 0)
        if at > 0:
            return ordered[n][at - 1], at
        before = ordered.get(n - 1)
        return (before[-1] if before else None), at

    def real_heading(t: str, from_here: list[Line]) -> bool:
        """A heading, not a table's column header or OCR debris."""
        return (heading_words(t) and not prose(t) and len(_squash(t)) >= 3 and not garbage_heading(t)
                and not DICE_HEADER.match(t) and not table_title(from_here, t))

    def nearest_heading(run: list[Line], at: int) -> str:
        pages_ = [ordered[run[0].page][:at][::-1], (ordered.get(run[0].page - 1) or [])[::-1]]
        for k, lines_ in enumerate(pages_):
            own = ordered[run[0].page] if k == 0 else (ordered.get(run[0].page - 1) or [])
            for x in lines_:
                t = x.text.strip()
                if real_heading(t, own[own.index(x):]):
                    return t.rstrip(".,;:")
        return " ".join(run[0].text.split()[:8]).rstrip(".,;:")

    done: set[str] = set()
    book_text = _squash("\n".join(e["canonicalText"] + " " + " ".join(n["text"] for n in e.get("notes", []))
                                 for e in entities))
    title = None
    for owner, run, is_fresh in [(o, r, False) for o, r in jobs] + [(o, r, True) for o, r in fresh]:
        title = None
        if is_fresh:  # lines an earlier job of this pass already kept
            run = [l for l in run if next(iter(_line_keys([l]))) not in claimed]
            if not run:
                continue
            if len(run) > 1 and real_heading(run[0].text.strip(), run):
                title, run = run[0].text.strip().rstrip(".,;:"), run[1:]  # its own heading: the Lore's name
            if not qualifies(run):
                continue
        text = _join_run(run)
        if _squash(text) in done:
            continue
        if _squash(text) in book_text or _squash(fix_digits(text)) in book_text:
            continue  # another entity holds the whole run (a pruned duplicate of a repaired line): nothing to keep
        done.add(_squash(text))
        heading = None
        if is_fresh:
            prev, at = preceding(run)
            owner = last_of.get(next(iter(_line_keys([prev]))), None) if prev is not None else None
            host = entities[owner] if owner is not None else None
            ended = bool(re.search(r"[.!?:\"\u201d\u2019']$", host["canonicalText"].rstrip())) if host else True
            if host is not None and host["type"] in _ENTRIES_TYPES and isinstance(host["fields"].get("entries"), list) \
                    and host["fields"]["entries"] and title is None \
                    and (text[:1].islower() or not ended):
                # the host's last line directly precedes the run: it is that text, going on
                if isinstance(host["fields"]["entries"][-1], str):
                    host["fields"]["entries"][-1] += " " + text
                else:  # the last entry is a table: the text goes on after it
                    host["fields"]["entries"].append(text)
                host["canonicalText"] = host["canonicalText"].rstrip("\n") + " " + text
                done.add(_squash(text))
                claimed |= _line_keys(run)
                continue
            heading = title or nearest_heading(run, at)
        host = entities[owner] if owner is not None and not is_fresh else None
        if host is not None and host["type"] in _ENTRIES_TYPES and isinstance(host["fields"].get("entries"), list) \
                and _APPEND_TO_OWNER:
            host["fields"]["entries"].append(text)
            host["canonicalText"] = host["canonicalText"].rstrip("\n") + "\n" + text
            continue
        if not make_lore:
            continue
        name = heading or run_title(run, ordered.get(run[0].page, run))
        flag = (Check("text-complete", False, f"unclaimed prose printed on p{run[0].page} that no entity claimed: kept "
                      "for review") if is_fresh else
                Check("sidebar-detached", False, f"text printed on p{run[0].page} that no entity held "
                      "(an opening or a run the owner's own pruning took out): kept for review"))
        p = Parsed(type="Lore", name=name, fields={"entries": [text]}, text=f"{name}\n{text}", lines=run,
                   checks=[flag])
        eid, checks = _id_and_checks(p, book_slug, seen, vocab)
        claimed |= _line_keys(p.lines)
        covered.update(l.page for l in p.lines)
        keys.append(_line_keys(p.lines))
        firsts.append(next(iter(_line_keys(p.lines[:1])), None))
        texts.append([(next(iter(_line_keys([l]))), l.text) for l in p.lines])
        entity_lines.append(p.lines)
        entities.append({"id": eid, "type": "Lore", "name": name, "page": run[0].page, "canonicalText": p.text,
                         "fields": p.fields,
                         "checks": [{"name": c.name, "passed": c.passed, "reason": c.reason} for c in checks],
                         "provenance": _provenance(p.lines, by_number)})


def _drop_duplicate_sidebars(entities: list[dict], entity_lines: list, keys: list, firsts: list, texts: list) -> None:
    """A flagged `sidebar-detached` Lore whose whole text another entity holds (compared as the net compares: letters and
    digits only, the OCR digit repair on both sides) keeps nothing: it is dropped. One at a time, so two that repeat each
    other leave one."""
    def body(e: dict) -> str:
        return e["canonicalText"].split("\n", 1)[-1]

    def flagged(e: dict) -> bool:
        return any(c["name"] == "sidebar-detached" and not c["passed"] for c in e["checks"])

    alive = [True] * len(entities)
    for i in range(len(entities) - 1, -1, -1):
        if not flagged(entities[i]):
            continue
        mine = {_squash(body(entities[i])), _squash(fix_digits(body(entities[i])))} - {""}
        others = _squash("\n".join(e["canonicalText"] + " " + " ".join(n["text"] for n in e.get("notes", []))
                                   for j, e in enumerate(entities) if j != i and alive[j]))
        if mine and any(m in others for m in mine):
            alive[i] = False
    for lst in (entities, entity_lines, keys, firsts, texts):
        if len(lst) == len(alive):
            lst[:] = [x for x, a in zip(lst, alive) if a]


def _unique_ids(entities: list[dict]) -> None:
    """Ids are unique over the book. A world type's ids are made over its own type, the later passes (the sidebars, the
    net) count their own, so a flagged Lore can repeat an earlier id: the earlier entity keeps it (reviewed overrides are
    keyed by id), the newcomer gets the next free ordinal and a failing `unique-id` check."""
    used = {e["id"] for e in entities}
    seen: set[str] = set()
    for e in entities:
        if e["id"] not in seen:
            seen.add(e["id"])
            continue
        n = 2
        while f"{e['id']}-{n}" in used:
            n += 1
        old, e["id"] = e["id"], f"{e['id']}-{n}"
        used.add(e["id"])
        seen.add(e["id"])
        e["checks"] = [c for c in e["checks"] if c["name"] != "unique-id"] + [
            {"name": "unique-id", "passed": False, "reason": f"id {old} already used by an earlier entity; suffixed -{n}"}]


def _join_run(run: list[Line]) -> str:
    out = ""
    for l in run:
        t = l.text.strip()
        out = out[:-1] + t if out.endswith("-") and t[:1].islower() else (f"{out} {t}" if out else t)
    return out


def _attach_note(note: Note, entities: list[dict], entity_lines: list[list[Line]], keys: list, firsts: list,
                 texts: list, book_slug: str, seen: Counter, vocab: Vocabulary, by_number: dict, claimed: set,
                 covered: set, make_lore: bool = True) -> None:
    """A margin note is kept in `notes` of the entity that holds the most text on its page (ties: the nearest to the
    note's box); it is not part of that entity's canonicalText. A page no entity holds gets a Lore of its own, failing
    `margin-note` so it is reviewed."""
    page = note.page
    top = (note.lines[0].box[1] + note.lines[-1].box[3]) / 2
    best, best_key = None, None
    for i, lines in enumerate(entity_lines):
        here = [l for l in lines if l.page == page]
        if not here:
            continue
        chars = sum(len(l.text) for l in here)
        gap = min(abs((l.box[1] + l.box[3]) / 2 - top) for l in here)
        if best_key is None or (chars, -gap) > best_key:
            best, best_key = i, (chars, -gap)
    if best is not None:
        entities[best].setdefault("notes", []).append(note.as_dict())
        return
    if not make_lore:
        return  # (Lore entities were not asked for)
    name = f"{note.by} note, p{page}" if note.by else f"Margin note, p{page}"
    p = Parsed(type="Lore", name=name, fields={"entries": [note.text]}, text=f"{name}\n{note.text}", lines=note.lines,
               checks=[Check("margin-note", False, f"a margin note on p{page} with no entity on its page: kept for review")])
    eid, checks = _id_and_checks(p, book_slug, seen, vocab)
    claimed |= _line_keys(p.lines)
    covered.update(l.page for l in p.lines)
    keys.append(_line_keys(p.lines))
    firsts.append(next(iter(_line_keys(p.lines[:1])), None))
    texts.append([(next(iter(_line_keys([l]))), l.text) for l in p.lines])
    entity_lines.append(p.lines)
    entities.append({"id": eid, "type": "Lore", "name": name, "page": page, "canonicalText": p.text,
                     "fields": p.fields,
                     "checks": [{"name": c.name, "passed": c.passed, "reason": c.reason} for c in checks],
                     "provenance": _provenance(p.lines, by_number)})


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
    heights = {n: pg.height for n, pg in by_number.items()}
    recurring = recurring_labels(body)
    claimed_lines: set[LineKey] = set()
    claimed_names: set[str] = set()
    god_names: set[str] = set()  # the gods' printed names: a world section naming one is a part of its text
    monster_names: set[str] = set()  # the creatures' names: a section named like one is the lore around its block
    monster_pages: set[int] = set()
    entity_lines: list[list[Line]] = []  # the printed lines of each entity, parallel to `entities`
    dropped: list[tuple[int, str]] = []
    first_at: list[tuple[int, str]] = []  # (the body index of an entity's first line, its name), for sidebars' names
    index = {_line_keys([l]).pop(): i for i, l in enumerate(body)}
    for t in ALL_TYPES:
        if t not in types:
            continue
        ctx.cache["claimed_lines"], ctx.cache["claimed_names"] = set(claimed_lines), set(claimed_names)
        ctx.cache["god_names"] = set(god_names)
        ctx.cache["monster_names"] = set(monster_names)
        parsed = _PARSERS[t](body, ctx)
        claimed_names.update(p.name for p in parsed if p.name.strip())  # as printed, before the name repair
        if t == "God":
            god_names.update(p.name for p in parsed if p.name.strip())
        if t == "Monster":
            monster_names.update(p.name for p in parsed if p.name.strip())
        # a world type's ids are assigned over the whole type at once (rulings W4, W11, W19)
        named = ([_id_and_checks(p, book_slug, seen, vocab) for p in parsed] if t not in WORLD_TYPES else
                 _world_ids(parsed, [_unseen_long_word_check(p, _id_and_checks(p, book_slug, Counter(), vocab)[1], vocab)
                                     for p in parsed], book_slug, ctx, body))
        for p, (eid, checks) in zip(parsed, named):
            if t in WORLD_TYPES:
                checks = [*checks, *residual_checks(p, heights, recurring)]
            if p.lines:  # art text and margin notes inside the entity's lines that `drop_art` takes out of its text
                dropped.extend((l.page, l.text.strip()) for l in left_out_art(p.lines, ctx))
            claimed_lines |= _line_keys(p.lines)
            if t == "Monster":
                monster_pages.update(l.page for l in p.lines)
            if p.lines and p.name.strip():
                first_at.append((index.get(next(iter(_line_keys(p.lines[:1]))), 0), p.name))
            covered.update(l.page for l in p.lines)
            keys.append(_line_keys(p.lines))
            firsts.append(next(iter(_line_keys(p.lines[:1])), None))
            texts.append([(next(iter(_line_keys([l]))), l.text) for l in p.lines])
            entity_lines.append(p.lines)
            entities.append({
                "id": eid, "type": p.type, "name": p.name,
                "page": p.lines[0].page if p.lines else 0,
                "canonicalText": p.text, "fields": p.fields,
                "checks": [{"name": c.name, "passed": c.passed, "reason": c.reason} for c in checks],
                "provenance": _provenance(p.lines, by_number),
            })

    found_notes: list[Note] = []
    if entities:  # the writers' margin notes: kept in `notes` of an entity, never swept up as a sidebar
        page_text: dict[int, str] = {}
        for e, lines_ in zip(entities, entity_lines):
            for pg in {l.page for l in lines_}:
                page_text[pg] = page_text.get(pg, "") + "\n" + e["canonicalText"]
        page_text = {pg: _squash(t) for pg, t in page_text.items()}  # (a line is held by the text of its own page)
        epigraph_lines: list[Line] = []
        opening_runs: list[list[Line]] = []
        for n in sorted(ordered):
            found_notes += margin_notes(ordered[n], ctx, lambda l: len(_squash(l.text)) >= 8
                                        and _squash(l.text) in page_text.get(l.page, ""), epigraph_lines,
                                        opening_runs)
        claimed_lines |= {k for note in found_notes for k in _line_keys(note.lines)}
        claimed_lines |= _line_keys(epigraph_lines)
        for note in found_notes:
            if note.signature is not None:
                _strip_line(entities, note.signature.text.strip(), entity_lines, note.page)  # a signature glued into an entity's text is the note's
        dropped.extend((l.page, l.text.strip()) for l in epigraph_lines)
    left_out: list[tuple[int, str]] = list(dropped)
    # front and back matter (contents, index, credits) is left out on purpose
    left_out.extend((p.number, l.text.strip()) for p in pages if kinds[p.number] in ("toc", "index", "credits") for l in p.lines)
    if "Lore" in types and "Monster" in types:
        # text no entity holds after a stat block (a boxed sidebar, lore a stop rule cut off): a flagged Lore of its own
        def previous_name(line: Line) -> str:
            at = index.get(next(iter(_line_keys([line]))), 0)
            return max(((i, n) for i, n in first_at if i < at), default=(0, "Unnamed"))[1]

        extra = detached_sidebars({n: ordered[n] for n in sorted(ordered)}, monster_pages, by_number, ctx,
                                  claimed_lines, lambda l: next(iter(_line_keys([l]))), previous_name, left_out,
                                  {_squash(n) for n in claimed_names})
        for p, (eid, checks) in zip(extra, [_id_and_checks(p, book_slug, seen, vocab) for p in extra]):
            entity_lines.append(p.lines)
            claimed_lines |= _line_keys(p.lines)
            covered.update(l.page for l in p.lines)
            keys.append(_line_keys(p.lines))
            firsts.append(next(iter(_line_keys(p.lines[:1])), None))
            texts.append([(next(iter(_line_keys([l]))), l.text) for l in p.lines])
            entities.append({
                "id": eid, "type": p.type, "name": p.name, "page": p.lines[0].page, "canonicalText": p.text,
                "fields": p.fields,
                "checks": [{"name": c.name, "passed": c.passed, "reason": c.reason} for c in checks],
                "provenance": _provenance(p.lines, by_number),
            })

    note_texts: set[str] = {l.text.strip() for note in found_notes for l in note.lines}
    for note in found_notes:
        _attach_note(note, entities, entity_lines, keys, firsts, texts, book_slug, seen, vocab, by_number,
                     claimed_lines, covered, "Lore" in types)
    left_out = [(pg, t) for pg, t in left_out if t not in note_texts]
    if entities:
        _keep_unheld(entities, entity_lines, ordered, opening_runs, left_out, note_texts, "Lore" in types, keys, firsts,
                     texts, book_slug, seen, vocab, by_number, claimed_lines, covered)
    if entities and len({len(entities), len(entity_lines), len(keys), len(firsts), len(texts)}) == 1:
        _drop_duplicate_sidebars(entities, entity_lines, keys, firsts, texts)
    _unique_ids(entities)
    # a line an entity holds is not "left out": only what no entity's text holds is listed
    held = _squash("\n".join(e["canonicalText"] for e in entities))
    left_out = [(pg, t) for pg, t in left_out if _squash(t) not in held]

    shared_text_checks(entities, keys, firsts, texts)
    for p in rules:
        if sum(_is_prose(l.text) for l in ordered[p.number]) >= PROSE_LINES_PER_PAGE or is_table_page(p):
            covered.add(p.number)
    rules_numbers = {p.number for p in rules}
    report = build_report(book_slug, len(pages), len(rules_numbers), len(covered & rules_numbers), entities)
    held = [e["canonicalText"] for e in entities] + [n["text"] for e in entities for n in e.get("notes", [])]
    page_lines = [(p.number, l.text, min((f.score for f in l.frags), default=1.0)) for p in pages for l in p.lines]
    coverage = measure(page_lines, held, left_out).to_dict()
    return ExtractionResult(entities=entities, report=report, left_out=left_out, coverage=coverage)
