"""Dev-only scorer: compare an extract output file against the 5etools answer key.

Never imported by the extraction path (assemble, parsers, reader, service); the CLI loads it lazily
for the `score` subcommand only.
"""
from __future__ import annotations

import collections
import json
import re
import statistics
import sys
from pathlib import Path

from rapidfuzz import fuzz

BOOKS = {"mm": "Monster", "phb": "Spell", "dmg": "MagicItem"}

_SCAG_FERAL = "SCAG prints the increases as the Feral option; 5etools folds Feral into every tiefling variant"
# Verified book-vs-5etools differences: the printed page (checked against the page image) says what we
# extracted, and 5etools says otherwise. (book, 5etools name, field) -> (our value, what the book prints).
# A difference is excused only while our value is still exactly the one listed.
KNOWN_DIFFERENCES: dict[tuple[str, str, str], tuple[object, str]] = {
    ("phb", "Mass Cure Wounds", "school"): ("C", "the page prints 5th-level conjuration (5etools: evocation)"),
    ("phb", "Mass Heal", "school"): ("C", "the page prints 9th-level conjuration (5etools: evocation)"),
    ("phb", "Revivify", "school"): ("C", "the page prints 3rd-level conjuration (5etools: necromancy)"),
    ("phb", "Crusader's Mantle", "range"): (
        {"type": "point", "distance": {"type": "self"}},
        "the page prints 'Range: Self' with no radius (5etools: 30-foot radius)"),
    ("mm", "Orc Eye of Gruumsh", "abilities"): (
        [16, 12, 16, 9, 13, 12], "the stat block prints CON 16 (+3) (5etools: 17)"),
    **{("scag", f"Tiefling Variant; {v}", "ability"): (None, f"the {v} option prints no increase ({_SCAG_FERAL})")
       for v in ("Devil's Tongue", "Hellfire", "Winged")},
    ("phb", "Human", "ability"): (
        [{"str": 1, "dex": 1, "con": 1, "int": 1, "wis": 1, "cha": 1}],
        "the race prints +1 to every score (5etools puts it on a nameless Human subrace)"),
    ("dmg", "Automatic Success", "ruleType"): ("V", "the heading prints 'Variant: Automatic Success' (5etools: VO)"),
    **{("phb", g, "domains"): ([], "the page prints 'Suggested Domains: no clerics' (5etools: Arcana, a later "
                                   "book's domain)") for g in ("Solinari", "Lunitari", "Nuitari")},
}
# a pantheon the book prints under another word than 5etools: (book, 5etools pantheon) -> (ours, what is printed).
# Excused only while our value is exactly the one listed.
PRINTED_PANTHEONS: dict[tuple[str, str], tuple[str, str]] = {
    ("mtf", "Elven"): ("Elf", "the table title prints 'Elf Deities (The Seldarine)' (5etools: Elven)"),
    ("mtf", "Dwarven"): ("Dwarf", "the table title prints 'Dwarf Deities (The Mordinsamman)' (5etools: Dwarven)"),
}
# a rule heading's printed label ("Variant: Encumbrance", "Optional Rule: Mad Winds"); the OCR may join the words
_RULE_LABEL = re.compile(r"^(?:variants?|optional\s*rules?)\s*:", re.I)
_TCE_BLESSED = "TCE prints it as a Cleric class option, not inside the domain section"
# 5etools feature refs the book does not print in that entry: (book, entry name) -> [(feature, level, why)].
# They are left out of the features comparison only.
NOT_PRINTED_FEATURES: dict[tuple[str, str], list[tuple[str, int, str]]] = {
    ("tce", "Artificer"): [("Optional Rule: Firearm Proficiency", 1, "a sidebar, not a class feature")],
    **{("tce", d): [("Blessed Strikes", 8, _TCE_BLESSED)]
       for d in ("Order Domain", "Peace Domain", "Twilight Domain")},
    **{("tce", k): [("Sidekick Class", 1, "the book prints 'Gaining a Sidekick Class' once for all three classes")]
       for k in ("Expert", "Spellcaster", "Warrior")},
}
_SIDEKICK_TABLE = "the class table prints the improved feature under its own name"
# 5etools feature refs the book's table prints under another name: (book, entry) -> [(5etools, level, printed, why)]
PRINTED_FEATURE_NAMES: dict[tuple[str, str], list[tuple[str, int, str, str]]] = {
    ("tce", "Expert"): [("Inspiring Help Improvement", 20, "Inspiring Help (2d6)", _SIDEKICK_TABLE)],
    ("tce", "Warrior"): [("Extra Attack Improvement", 15, "Extra Attack (2 extra)", _SIDEKICK_TABLE),
                         ("Indomitable Improvement", 18, "Indomitable (2 uses)", _SIDEKICK_TABLE),
                         ("Second Wind Improvement", 20, "Second Wind (2 uses)", _SIDEKICK_TABLE)],
}
_SCAG_HALF_ELF = ("a sentence in the 'Half-Elf Variants' sidebar naming a Player's Handbook elf trait to take, "
                  "not a trait block")
# entries 5etools builds that the book never prints as their own entry
NOT_PRINTED: dict[tuple[str, str], str] = {
    ("vrgr", "Elise"): "the book says she uses the flesh golem stat block with changes",
    ("vrgr", "Isolde"): "the book says she uses the cambion stat block with changes",
    **{("scag", f"Half-Elf Variant; {d} Descent"): _SCAG_HALF_ELF
       for d in ("Aquatic Elf", "Drow", "Moon Elf or Sun Elf", "Wood Elf")},
    ("mtf", "Tiefling Asmodeus"): ("not printed as a trait block: Asmodeus tieflings use the Player's Handbook "
                                   "tiefling traits"),
    ("scag", "Tiefling Variant; Infernal Legacy"): (
        "not printed as an option: it stands for the Feral and Appearance options with the Player's Handbook "
        "Infernal Legacy trait"),
}
# entries the book prints that 5etools does not keep as their own record: (book, etype, printed name) -> why
PRINTED_NOT_IN_5ETOOLS: dict[tuple[str, str, str], str] = {
    ("scag", "Subrace", "Tiefling Feral"): _SCAG_FERAL,
}
_ABIL = ("str", "dex", "con", "int", "wis", "cha")

_ATK = {"mw": "Melee Weapon Attack:", "rw": "Ranged Weapon Attack:", "mw,rw": "Melee or Ranged Weapon Attack:",
        "ms": "Melee Spell Attack:", "rs": "Ranged Spell Attack:", "ms,rs": "Melee or Ranged Spell Attack:"}


def key(n: str | None) -> str:
    return re.sub(r"[^a-z0-9+]", "", (n or "").lower())


def spaced_key(n: str | None) -> str:
    """Case-insensitive name key that keeps word boundaries ("Fey Wanderer" != "Feywanderer")."""
    return " ".join(re.sub(r"[^a-z0-9+\s]", "", (n or "").lower()).split())


def untag(e: str) -> str:
    """Expand 5etools {@tag ...} markup to the plain text the printed book shows."""
    e = re.sub(r"\{@atk ([^}]*)\}", lambda m: _ATK.get(m.group(1), m.group(1)), e)
    e = re.sub(r"\{@hit (-?\d+)\}", lambda m: f"{int(m.group(1)):+d}", e)
    e = re.sub(r"\{@h\}", "Hit: ", e)
    e = re.sub(r"\{@dc (\d+)\}", r"DC \1", e)
    e = re.sub(r"\{@recharge (\d)\}", r"(Recharge \1-6)", e)
    e = re.sub(r"\{@recharge\}", "(Recharge 6)", e)
    return re.sub(r"\{@\w+ ([^|}]*)[^}]*\}", r"\1", e)


def flat(e) -> str:
    if e is None:
        return ""
    if isinstance(e, str):
        return untag(e)
    if isinstance(e, list):
        return " ".join(flat(x) for x in e)
    if isinstance(e, dict):
        out = " ".join(flat(e.get(k)) for k in ("name", "entries", "items", "text", "entry") if e.get(k))
        if e.get("rows"):
            out += " " + " ".join(flat(r) for r in e["rows"])
        return out
    return str(e)


def words(s: str) -> list[str]:
    return re.findall(r"[a-z0-9]+", re.sub(r"(\d)\s*ft\b", r"\1 ft", s.lower()))


def shingles(s: str, n: int = 5) -> set[str]:
    w = words(s)
    return {" ".join(w[i:i + n]) for i in range(len(w) - n + 1)}


def coverage(truth: str, got: str) -> tuple[float | None, float | None]:
    """(share of the truth's 5-word shingles we have, share of our shingles that are truth's)."""
    t, g = shingles(truth), shingles(got)
    inter = len(t & g)
    return (inter / len(t) if t else None), (inter / len(g) if g else None)


# ---------------- item variant collapse ----------------
_COLORS = "Black|Blue|Brass|Bronze|Copper|Gold|Green|Red|Silver|White"


def base_item_name(n: str) -> str:
    """Collapse a 5etools item variant to the base entry the book prints."""
    n = re.sub(r"^\+\d\s+", "", n)
    n = re.sub(r"\s*\([^)]*\)$", "", n)
    n = n.split(",")[0]
    n = re.sub(rf"^({_COLORS}) Dragon Scale Mail$", "Dragon Scale Mail", n)
    n = re.sub(r"^Belt of \w+ Giant Strength$", "Belt of Giant Strength", n)
    n = re.sub(r"^Crystal Ball of .*", "Crystal Ball", n)
    n = re.sub(r"^Potion of \w+ Giant Strength$", "Potion of Giant Strength", n)
    n = re.sub(r"^Ioun Stone.*", "Ioun Stone", n)
    n = re.sub(r"^(Potion|Ring) of \w+ Resistance$", r"\1 of Resistance", n)
    n = re.sub(r"^Scroll of Protection from .*", "Scroll of Protection", n)
    n = re.sub(r"^Manual of \w+ Golems$", "Manual of Golems", n)
    n = re.sub(r"^Potion of (Greater|Superior|Supreme) Healing$", "Potion of Healing", n)
    n = re.sub(r"^Ring of \w+ Elemental Command$", "Ring of Elemental Command", n)
    n = re.sub(r"^(Eye|Hand) of Vecna$", "Eye and Hand of Vecna", n)
    # TCE prints Baba Yaga's Mortar and Pestle as one artifact; 5etools splits the two objects
    n = re.sub(r"^Baba Yaga's (?:Mortar|Pestle)$", "Baba Yaga's Mortar and Pestle", n)
    # TCE prints one Absorbing Tattoo; 5etools splits it per damage type
    n = re.sub(r"^\w+ Absorbing Tattoo$", "Absorbing Tattoo", n)
    # GGR prints one Guild Signet and one Guild Keyrune; 5etools splits them per guild
    n = re.sub(r"^(?:Azorius|Boros|Dimir|Golgari|Gruul|Izzet|Orzhov|Rakdos|Selesnya|Simic) (Guild Signet|Keyrune)$",
               lambda m: "Guild Signet" if m.group(1) == "Guild Signet" else "Guild Keyrune", n)
    # FTD prints one entry per item with four tiers; 5etools splits them by tier
    n = re.sub(r"^(Slumbering|Stirring|Wakened|Ascendant) ", "", n)
    return n


_GREATWYRM = {"Chromatic": "Black|Blue|Green|Red|White", "Gem": "Amethyst|Crystal|Emerald|Sapphire|Topaz",
              "Metallic": "Brass|Bronze|Copper|Gold|Silver"}


def base_monster_name(n: str) -> str:
    """Collapse a 5etools monster variant to the stat block the book prints (FTD's three greatwyrms)."""
    for family, colours in _GREATWYRM.items():
        if re.fullmatch(rf"({colours}) Greatwyrm", n):
            return f"{family} Greatwyrm"
    return n


_GENASI_NOTE = "printed as separate races (Air/Earth/Fire/Water Genasi)"
_CHARACTER_TYPES = ("Class", "Subclass", "Race", "Subrace", "Feat", "Background")
_EQUIPMENT_TYPES = ("Weapon", "Armor", "Item", "VehicleMount", "Object", "Trap", "DiseasePoison", "Condition")
_SECTION_TYPES = ("Plane", "Faction", "Location", "Lore")  # scored together, as "Section": 5etools has no such types
_WORLD_TYPES = ("God", "Rule", "Section")
_UNMEASURED_TYPES = _EQUIPMENT_TYPES + _WORLD_TYPES  # truth may carry no text: unmeasured, not 0%
TYPES = ("Monster", "Spell", "MagicItem", "Class", "Subclass", "Race", "Subrace", "Feat", "Background",
         *_EQUIPMENT_TYPES, *_WORLD_TYPES)


def load_truth(book: str, fivetools_dir: Path, etype: str | None = None) -> list[dict]:
    """5etools records of `etype` for the 5etools source `book` (lower case: mm, mpmm, xge ...).

    `_copy` monsters are left out: 5etools stores them as a diff against another record, so their
    fields are not the printed ones."""
    d = Path(fivetools_dir)
    etype = etype or BOOKS[book]
    if etype == "Monster":
        out: dict[str, dict] = {}
        for m in json.loads((d / f"bestiary/bestiary-{book}.json").read_text("utf-8"))["monster"]:
            if not m.get("_copy"):
                b = base_monster_name(m["name"])
                out.setdefault(b, dict(m, name=b))  # a collapsed family is scored against its first member
        return list(out.values())
    if etype == "Spell":
        return json.loads((d / f"spells/spells-{book}.json").read_text("utf-8"))["spell"]
    if etype in _CHARACTER_TYPES:
        return _load_character_truth(book.upper(), d, etype)
    if etype in _EQUIPMENT_TYPES:
        return _split_equipment_truth(book.upper(), d, etype)[0]
    if etype in _WORLD_TYPES:
        return _split_world_truth(book.upper(), d, etype)[0]
    seen: dict[str, dict] = {}
    for i in json.loads((d / "items.json").read_text("utf-8"))["item"]:
        if i.get("source") == book.upper() and i.get("rarity") not in (None, "none"):
            b = base_item_name(i["name"])
            rec = seen.setdefault(b, dict(i, name=b, _rarities=set()))
            rec["_rarities"].add(i["rarity"])
            rec["reqAttune"] = rec.get("reqAttune") or i.get("reqAttune")  # the printed entry covers every tier
    return list(seen.values())


# 5etools names a downtime activity "Downtime Activity: Crafting"; the book prints the heading "Crafting"
_RULE_PREFIX = re.compile(r"^(variant rule|variant|optional rule|downtime activity)\s*:\s*", re.I)


def _section_nodes(node: dict, chapter: str, depth: int, out: list[dict]) -> None:
    """Every named section/entries node under `node` (a heading); depth 1 is a chapter's own sections. A node
    whose first entry is a table, or an inset (sidebar) node, is marked `_table` / `_inset`: the book prints a
    caption or a box title there, not a section heading (ruling W27 (c))."""
    for x in node.get("entries") or []:
        if isinstance(x, dict) and x.get("type") in ("section", "entries") and x.get("name"):
            ents = x.get("entries") or []
            first = ents[0] if ents else None
            table = isinstance(first, dict) and first.get("type") in ("table", "tableGroup") \
                and not any(isinstance(e, str) for e in ents)
            out.append({"name": x["name"], "_chapter": chapter, "_depth": depth, "entries": ents, "_table": table})
            _section_nodes(x, chapter, depth + 1, out)


def _split_world_truth(src: str, d: Path, etype: str) -> tuple[list[dict], list[str]]:
    """(records, known-difference lines for the records left out) for God, Rule and the section tree."""
    if etype == "Section":
        out: list[dict] = []
        for ch in json.loads((d / f"book/book-{src.lower()}.json").read_text("utf-8"))["data"]:
            if isinstance(ch, dict) and ch.get("type") in ("section", "entries") and ch.get("name"):
                _section_nodes(ch, ch["name"], 1, out)
        return out, []
    f, k = ("deities.json", "deity") if etype == "God" else ("variantrules.json", "variantrule")
    recs = [r for r in json.loads((d / f).read_text("utf-8"))[k] if r.get("source") == src]
    return ([r for r in recs if not r.get("_copy")],
            [f"variant (_copy) not scored: {r['name']}" for r in recs if r.get("_copy")])


def _is_armor(i: dict) -> bool:
    return bool(i.get("armor")) or str(i.get("type") or "").split("|")[0] in ("LA", "MA", "HA", "S")


def _is_weapon(i: dict) -> bool:
    # a spellcasting focus (type SCF) may carry a weaponCategory, but the book prints it as a focus
    return (bool(i.get("weapon")) or bool(i.get("weaponCategory"))) and not _is_armor(i) \
        and str(i.get("type") or "").split("|")[0] != "SCF"


def _is_poison(i: dict) -> bool:
    return bool(i.get("poisonTypes")) or bool(i.get("poison"))


def _dedupe(recs: list[dict]) -> list[dict]:
    """First record per name key: 5etools repeats a name for a printed entry's reprints and variants."""
    seen: dict[str, dict] = {}
    for r in recs:
        seen.setdefault(key(r["name"]), r)
    return list(seen.values())


def _split_equipment_truth(src: str, d: Path, etype: str) -> tuple[list[dict], list[str]]:
    """(truth records, known-difference lines for the records left out of the truth) for the equipment and
    hazard types. Weapon, Armor, Item, DiseasePoison and MagicItem partition the 5etools item records: an
    `items.json` record is MagicItem when its rarity is set, else a poison, a weapon, armor or an Item."""
    def read(fname: str, *lists: str) -> list[dict]:
        data = json.loads((d / fname).read_text("utf-8"))
        return [r for lst in lists for r in data.get(lst, []) if r.get("source") == src and not r.get("_copy")]

    known: list[str] = []
    ships: list[dict] = []
    # the extractor emits ships and airships as vehicle statistics (a design decision)
    ships = [i for i in read("items.json", "item") if _code(i.get("type")) in ("SHP", "AIR")]
    ship_names = {i["name"] for i in ships}
    if etype in ("Weapon", "Armor", "Item", "DiseasePoison"):
        base = read("items-base.json", "baseitem")
        mundane = [i for i in read("items.json", "item") if i.get("rarity") in (None, "none")]
        if etype == "Weapon":
            return _dedupe([i for i in base + mundane if _is_weapon(i)]), known
        if etype == "Armor":
            return _dedupe([i for i in base + mundane if _is_armor(i)]), known
        if etype == "Item":
            items = _dedupe([i for i in base + mundane
                             if not _is_weapon(i) and not _is_armor(i) and not _is_poison(i)])
            return _drop_equipment_truth(src, etype, items, ship_names)
        poisons = [i for i in mundane if _is_poison(i)]
        diseases = read("conditionsdiseases.json", "disease")
        return _dedupe(diseases + poisons), known
    if etype == "VehicleMount":
        known = [f"stored by 5etools as items; printed as vehicle statistics: {n}" for n in sorted(ship_names)]
        return _dedupe(read("vehicles.json", "vehicle") + ships), known
    if etype == "Object":
        return _drop_equipment_truth(src, etype, _dedupe(read("objects.json", "object")), ship_names)
    if etype == "Trap":
        return _drop_equipment_truth(src, etype, _dedupe(read("trapshazards.json", "trap", "hazard")), ship_names)
    known = [f"status effect, not a condition: {r['name']}" for r in read("conditionsdiseases.json", "status")]
    return _dedupe(read("conditionsdiseases.json", "condition")), known


# 5etools records the book prints no entry for: (source, type, 5etools name) -> why
EQUIPMENT_NOT_AN_ENTRY: dict[tuple[str, str, str], str] = {
    **{("TCE", "Object", f"Eldritch Cannon, {o}"):
       "a summon option inside a subclass feature, no stat block of its own"
       for o in ("Flamethrower", "Force Ballista", "Protector")},
    ("DMG", "Object", "Generic Object"): "a rules section on object statistics, not an entry",
    ("VRGR", "Item", "Monster Hunter's Pack"): "printed only inside a background's equipment line, not as an entry",
    ("EGW", "Item", "Pride Silk"): "priced only in running prose, no entry",
    ("EGW", "Item", "Pride Silk Outfit"): "priced only in running prose, no entry",
    ("DMG", "Trap", "Improvised Damage"): "a rules table of damage suggestions, not an entry",
}
# entries the book prints but whose name the OCR lost: they stay in the truth and stay misses, with a visible line
OCR_LOST_NAME: dict[tuple[str, str, str], str] = {
    ("DMG", "VehicleMount", "Sailing Ship"): "the ships table row has no label cell in the OCR text",
    ("GGR", "Item", "Electrum 50-Zib Coin"): "the coin row's name is not legible in the OCR text",
}
_TRINKET_NOTE = "a trinket roll table that only 5etools names as an item"


def _drop_equipment_truth(src: str, etype: str, recs: list[dict], ship_names: set[str]) -> tuple[list[dict], list[str]]:
    """Take out the records the book prints no entry for; one known-difference line per record."""
    names = [r["name"] for r in recs]
    keep: list[dict] = []
    known: list[str] = []
    for r in recs:
        n = r["name"]
        bundle = next((o for o in names if o != n and re.match(rf"{re.escape(n.lower())}s \(", o.lower())), None)
        if (src, etype, n) in EQUIPMENT_NOT_AN_ENTRY:
            known.append(f"{n}: not an entry ({EQUIPMENT_NOT_AN_ENTRY[(src, etype, n)]})")
        elif etype == "Item" and (n == "Trinket" or n.endswith(" Trinket")):
            known.append(f"{n}: not an entry ({_TRINKET_NOTE})")
        elif etype == "Item" and bundle:
            known.append(f"{n}: not printed on its own (the book prints only the bundle row {bundle})")
        elif etype == "Item" and n in ship_names:
            known.append(f"stored by 5etools as items; printed as vehicle statistics: {n}")
        else:
            keep.append(r)
    return keep, known


def _load_character_truth(src: str, d: Path, etype: str) -> list[dict]:
    return _split_character_truth(src, d, etype)[0]


def _feature_key(ref: str) -> tuple:
    """(name, class, class source, short name, subclass source, level) of a 5etools subclassFeature ref."""
    p = ref.split("|")
    return (key(p[0]), key(p[1]), (p[2] or "PHB").upper(), key(p[3]), (p[4] or "PHB").upper(), p[5]) \
        if len(p) >= 6 else ()


def _subclass_feature_index(files: list[Path]) -> dict[tuple, dict]:
    out: dict[tuple, dict] = {}
    for f in files:
        for r in json.loads(f.read_text("utf-8")).get("subclassFeature", []):
            k = (key(r["name"]), key(r["className"]), (r.get("classSource") or "PHB").upper(),
                 key(r["subclassShortName"]), (r.get("subclassSource") or "PHB").upper(), str(r["level"]))
            out.setdefault(k, r)
    return out


def _feature_text(refs, index: dict[tuple, dict], seen: set | None = None) -> str:
    """The prose of a subclass's features: each ref's subclassFeature record, with the refSubclassFeature
    entries the first-level record holds expanded in place."""
    seen = set() if seen is None else seen
    parts = []

    def walk(e) -> str:
        if isinstance(e, dict) and e.get("type") == "refSubclassFeature":
            return _feature_text([e["subclassFeature"]], index, seen)
        if isinstance(e, list):
            return " ".join(walk(x) for x in e)
        if isinstance(e, dict) and isinstance(e.get("entries"), list):
            return " ".join(x for x in (flat(e.get("name")), walk(e["entries"])) if x)
        return flat(e)

    for r in refs or []:
        ref = r.get("subclassFeature") if isinstance(r, dict) else r
        k = _feature_key(str(ref))
        if not k or k in seen or k not in index:
            continue
        seen.add(k)
        parts.append(walk(index[k].get("entries")))
    return " ".join(parts)


def _split_character_truth(src: str, d: Path, etype: str) -> tuple[list[dict], list[str]]:
    """(truth records, known-difference lines for the records left out of the truth)."""
    if etype in ("Class", "Subclass"):
        files = sorted((d / "class").glob("class-*.json"))
        if not files:
            raise FileNotFoundError(str(d / "class"))
        lst = "class" if etype == "Class" else "subclass"
        feats = _subclass_feature_index(files) if etype == "Subclass" else {}
        kept, dropped = [], []
        for f in files:
            for r in json.loads(f.read_text("utf-8")).get(lst, []):
                if r.get("source") != src:
                    continue
                if etype == "Class" and r.get("isSidekick") and r["name"].endswith(" Sidekick"):
                    printed = r["name"][: -len(" Sidekick")]
                    dropped.append(f"sidekick class scored under its printed heading: {r['name']} -> {printed}")
                    kept.append(dict(r, name=printed))
                    continue
                # 5etools stores each supplement subclass twice; the XPHB (2024 class) and EFA (2024 Artificer)
                # copies are twins of the printed one
                if etype == "Subclass" and r.get("classSource") in ("XPHB", "EFA"):
                    dropped.append(f"reprint (classSource {r['classSource']}) not scored: {r['name']}")
                elif etype == "Subclass":
                    kept.append(dict(r, _featureText=_feature_text(r.get("subclassFeatures"), feats)))
                else:
                    kept.append(r)
        return kept, dropped
    if etype in ("Race", "Subrace"):
        data = json.loads((d / "races.json").read_text("utf-8"))
        if etype == "Race":
            recs = [r for r in data["race"] if r.get("source") == src and not r.get("_copy")]
            if src == "MPMM":
                return ([r for r in recs if r["name"] != "Genasi"],
                        [f"{_GENASI_NOTE}: {r['name']}" for r in recs if r["name"] == "Genasi"])
            return recs, []
        # nameless base subraces are dropped; the match name carries the race word, like the printed heading
        subs = [dict(r, name=f"{r['raceName']} {r['name']}", _subName=r["name"]) for r in data["subrace"]
                if r.get("source") == src and r.get("name") and r.get("raceName")]
        if src == "MPMM":
            return ([r for r in subs if r["raceName"] != "Genasi"],
                    [f"{_GENASI_NOTE}: {r['name']}" for r in subs if r["raceName"] == "Genasi"])
        return subs, []
    fname, lst = ("feats.json", "feat") if etype == "Feat" else ("backgrounds.json", "background")
    recs = [r for r in json.loads((d / fname).read_text("utf-8"))[lst] if r.get("source") == src]
    # a `_copy` background is a sidebar variant that modifies a base background, not a printed entry
    if etype == "Background":
        # PHB prints "Customizing a Background" rules, not a background with a Skill Proficiencies line
        rules = [r for r in recs if src == "PHB" and r["name"] == "Custom Background" and not r.get("_copy")]
        return ([r for r in recs if not r.get("_copy") and r not in rules],
                [f"variant (_copy) not scored: {r['name']}" for r in recs if r.get("_copy")]
                + [f"rules, not a background entry: {r['name']}" for r in rules])
    return [r for r in recs if not r.get("_copy")], []


def printed_elsewhere(book: str, fivetools_dir: Path | str, etype: str) -> dict[str, tuple[str, dict]]:
    """Match key -> (known-difference line, stored record) for each Subclass/Subrace this book prints that 5etools
    stores under another book, listing this one in `otherSources` (SCAG's Way of the Sun Soul is under XGE)."""
    if etype not in ("Subclass", "Subrace"):
        return {}
    src, d = book.upper(), Path(fivetools_dir)
    if etype == "Subclass":
        recs = [r for f in sorted((d / "class").glob("class-*.json"))
                for r in json.loads(f.read_text("utf-8")).get("subclass", [])
                if r.get("classSource") not in ("XPHB", "EFA")]
    else:
        recs = [dict(r, name=f"{r['raceName']} {r['name']}")
                for r in json.loads((d / "races.json").read_text("utf-8"))["subrace"]
                if r.get("name") and r.get("raceName")]
    return {match_key(etype, r["name"]): (f"printed here, 5etools stores it under {r['source']} "
                                          f"(otherSources {src}): {r['name']}", r)
            for r in recs if r.get("source") != src and not r.get("_copy")
            and any(o.get("source") == src for o in r.get("otherSources") or [])}


def dropped_truth_lines(book: str, fivetools_dir: Path | str, etype: str) -> list[str]:
    """Known-difference lines for the 5etools records load_truth leaves out of `etype`'s truth."""
    if etype in _EQUIPMENT_TYPES:
        return _split_equipment_truth(book.upper(), Path(fivetools_dir), etype)[1]
    if etype in _WORLD_TYPES:
        return _split_world_truth(book.upper(), Path(fivetools_dir), etype)[1]
    if etype not in _CHARACTER_TYPES:
        return []
    return _split_character_truth(book.upper(), Path(fivetools_dir), etype)[1]


_DIE_NOTE = re.compile(r"\s*\(\d*d\d+\)\s*$", re.I)


def _drop_unprinted_features(book: str, t: dict, known: list[str]) -> dict:
    """`t` without the feature refs NOT_PRINTED_FEATURES lists for it, and with the refs PRINTED_FEATURE_NAMES
    lists renamed to the printed name; each one adds a known line."""
    drop = NOT_PRINTED_FEATURES.get((book, t["name"]), [])
    rename = PRINTED_FEATURE_NAMES.get((book, t["name"]), [])
    if not drop and not rename:
        return t
    out = dict(t)
    for fld, lvl_idx in (("classFeatures", 3), ("subclassFeatures", 5)):
        kept = []
        for r in t.get(fld) or []:
            ref = str(r.get("classFeature") or r.get("subclassFeature") if isinstance(r, dict) else r)
            parts = ref.split("|")

            def at(d) -> bool:
                return key(parts[0]) == key(d[0]) and len(parts) > lvl_idx and parts[lvl_idx] == str(d[1])

            hit = next((d for d in drop if at(d)), None)
            new = next((d for d in rename if at(d)), None)
            if hit:
                known.append(f"{t['name']}: feature {hit[0]} (level {hit[1]}) not printed in the entry ({hit[2]})")
            elif new:
                known.append(f"{t['name']}: feature {new[0]} (level {new[1]}) is printed '{new[2]}' ({new[3]})")
                kept.append("|".join([new[2], *parts[1:]]))
            else:
                kept.append(r)
        if fld in t:
            out[fld] = kept
    return out


def _rename_subclass(t: dict, printed: str, known: list[str]) -> dict:
    """`t` scored under the name the book prints: 5etools "Purple Dragon Knight (Banneret)" is printed "Purple
    Dragon Knight" (the parenthetical is a name the book gives in prose); the feature ref holding the subclass's
    first features, and a short name spelled like the full name, are renamed with it."""
    known.append(f"{t['name']}: printed '{printed}' (the parenthetical is an alias, not part of the heading)")
    refs = []
    for r in t.get("subclassFeatures") or []:
        s = str(r.get("subclassFeature") if isinstance(r, dict) else r)
        parts = s.split("|")
        refs.append("|".join([printed, *parts[1:]]) if key(parts[0]) == key(t["name"]) else r)
    short = t.get("shortName")
    return dict(t, subclassFeatures=refs, shortName=printed if short and key(short) == key(t["name"]) else short)


def feature_levels(refs, src: str, level_idx: int, source_idx: int) -> set[tuple[str, int]]:
    """(name, level) of the feature refs that belong to `src`: a ref carrying a different source in its
    optional trailing part (a TCE optional feature in the PHB class) is left out. Refs are 5etools strings
    or dicts holding one under `classFeature`/`subclassFeature`; our dicts may carry `level` instead."""
    out: set[tuple[str, int]] = set()
    for r in refs or []:
        lvl = None
        if isinstance(r, dict):
            lvl = r.get("level")
            r = r.get("classFeature") or r.get("subclassFeature") or ""
        parts = str(r).split("|")
        if len(parts) > source_idx and parts[source_idx] and parts[source_idx].upper() != src:
            continue
        if lvl is None and len(parts) > level_idx and parts[level_idx].isdigit():
            lvl = int(parts[level_idx])
        if parts[0] and lvl is not None:
            # a trailing die note is the table's, not the name's: "Bardic Inspiration (d6)"; spaces kept, so a
            # glued "Extraattack" is a misread of "Extra Attack", not the same name
            out.add((spaced_key(_DIE_NOTE.sub("", parts[0])), int(lvl)))
    return out


def t_walk(speed):
    return speed.get("walk") if isinstance(speed, dict) else speed


def skill_set(v) -> set[str]:
    """Skill names of a 5etools skillProficiencies list: [{"insight": true, ...}] -> {"insight", ...}."""
    return {k.lower() for d in (v or []) if isinstance(d, dict) for k, on in d.items() if on is True}


# ---------------- truth-side field renderers ----------------
def t_ac(m: dict):
    a = m.get("ac")
    a = (a[0] if a else None) if isinstance(a, list) else a
    return a.get("ac") if isinstance(a, dict) else a


def t_cr(m: dict):
    c = m.get("cr")
    return c.get("cr") if isinstance(c, dict) else c


def _first_int(v):
    if isinstance(v, list):
        v = v[0] if v else None
    if isinstance(v, dict):
        v = v.get("ac")
    return v


def text_monster(m: dict) -> str:
    return flat([m.get(k) for k in ("trait", "action", "reaction", "legendary") if m.get(k)])


def same_time(truth: dict, got) -> bool:
    if not got:
        return False
    g = got[0] if isinstance(got, list) else got
    t = truth["time"][0]
    return g.get("number") == t["number"] and g.get("unit") == t["unit"]


def same_range(truth: dict, got) -> bool:
    if not got:
        return False
    t = truth["range"]
    td, gd = t.get("distance", {}), got.get("distance", {})
    return (t["type"] == got.get("type") and td.get("type") == gd.get("type")
            and td.get("amount") == gd.get("amount"))


def same_components(truth: dict, got) -> bool:
    if not got:
        return False
    c = truth.get("components", {})
    return {k for k in "vsm" if c.get(k)} == {k for k in "vsm" if got.get(k)}


def same_duration(truth: dict, got, concentration) -> bool:
    if not got:
        return False
    t = truth["duration"][0]
    g = got[0] if isinstance(got, list) else got
    if t["type"] != g.get("type"):
        return False
    if bool(t.get("concentration")) != bool(g.get("concentration", concentration)):
        return False
    if t["type"] == "timed":
        td, gd = t.get("duration", {}), g.get("duration", {})
        return td.get("type") == gd.get("type") and td.get("amount") == gd.get("amount")
    return True


def rarity_family(truth: dict) -> set:
    return truth.get("_rarities") or {truth.get("rarity")}


def same_rarity(truth: dict, got) -> bool:
    """Strict: only meaningful for single-rarity families (multi-rarity ones are scored separately)."""
    return got == truth.get("rarity")


def attunes(v) -> bool:
    return bool(v)


# ---------------- scoring ----------------
def available_types(book: str, fivetools_dir: Path) -> list[str]:
    """Every type 5etools has truth for in this book (a core book keeps its Phase 1 type as well)."""
    out = []
    for t in TYPES:
        try:
            if load_truth(book, fivetools_dir, t) or (t in _UNMEASURED_TYPES and dropped_truth_lines(book, fivetools_dir, t)):
                out.append(t)
        except (FileNotFoundError, KeyError):
            pass
    return out


def score_record(etype: str, fields: dict, t: dict, src: str | None = None, entity: dict | None = None) -> tuple[dict, float | None, float | None]:
    r: dict[str, bool | None] = {}
    if etype == "Monster":
        r["ac"] = _first_int(fields.get("ac")) == t_ac(t)
        r["hp"] = (fields.get("hp") or {}).get("average") == t.get("hp", {}).get("average")
        r["cr"] = str(fields.get("cr")) == str(t_cr(t))
        r["abilities"] = [fields.get(k) for k in ("str", "dex", "con", "int", "wis", "cha")] == \
                         [t.get(k) for k in ("str", "dex", "con", "int", "wis", "cha")]
        got = flat([fields.get(k) for k in ("trait", "action", "reaction", "legendary") if fields.get(k)])
        cov, prec = coverage(text_monster(t), got)
    elif etype == "Spell":
        r["level"] = fields.get("level") == t["level"]
        r["school"] = fields.get("school") == t["school"]
        r["time"] = same_time(t, fields.get("time"))
        r["range"] = same_range(t, fields.get("range"))
        r["components"] = same_components(t, fields.get("components"))
        r["duration"] = same_duration(t, fields.get("duration"), fields.get("concentration"))
        got = flat(fields.get("entries")) + " " + flat(fields.get("entriesHigherLevel"))
        cov, prec = coverage(flat(t.get("entries")) + " " + flat(t.get("entriesHigherLevel")), got)
    elif etype in _CHARACTER_TYPES:
        cov, prec = _score_character(etype, fields, t, r, src)
    elif etype in _EQUIPMENT_TYPES:
        cov, prec = _score_equipment(etype, fields, t, r, entity)
    elif etype in _WORLD_TYPES:
        cov, prec = _score_world(etype, fields, t, r)
    else:
        if len(rarity_family(t)) == 1:
            r["rarity"] = same_rarity(t, fields.get("rarity"))
        r["attune"] = attunes(fields.get("reqAttune")) == attunes(t.get("reqAttune"))
        cov, prec = coverage(flat(t.get("entries")), flat(fields.get("entries")))
    return r, cov, prec


def extra_god_domains(fields: dict, t: dict) -> list[str]:
    """Truth domains our god does not print (5etools merges later books' domains into the PHB god)."""
    ours = {key(x) for x in fields.get("domains") or []}
    return [x for x in t.get("domains") or [] if ours and key(x) not in ours]


def _score_world(etype: str, f: dict, t: dict, r: dict) -> tuple[float | None, float | None]:
    if etype == "God":
        r["alignment"] = (f.get("alignment") or None) == (t.get("alignment") or None)
        mine, theirs = {key(x) for x in f.get("domains") or []}, {key(x) for x in t.get("domains") or []}
        r["domains"] = None if not mine and not theirs else bool(mine) and mine <= theirs
        ours, want = f.get("symbol"), t.get("symbol")
        r["symbol"] = None if not ours and not want else bool(ours and want) and \
            fuzz.ratio(key(ours), key(want)) >= 90
        r["pantheon"] = (f.get("pantheon") or None) == (t.get("pantheon") or None)
    elif etype == "Rule":
        r["ruleType"] = (f.get("ruleType") or None) == (t.get("ruleType") or None)
    return coverage(flat(t.get("entries")), flat(f.get("entries")))


def _code(v) -> str | None:
    """A 5etools code without its source suffix: 'AF|DMG' -> 'AF'."""
    return str(v).split("|")[0] if v is not None else None


def _codes(v) -> set[str]:
    return {_code(x) for x in (v or [])}


_TYPED_ROWS = {"TAH", "VEH", "MNT", "SHP", "AIR"}  # tack, drawn vehicles, mounts, ships: the type is always scored


def _printed(e: dict | None, label: str) -> bool:
    """True when our entity shows the cell: a `<label>:` line in its text, or a failed check on it."""
    if not e:
        return False
    if re.search(rf"^{label}:", e.get("canonicalText") or "", re.M):
        return True
    return any(not c.get("passed") and c.get("name", "").lower().startswith(label.lower())
               for c in e.get("checks") or [])


def _all_text(e) -> str:
    """Every printed string of a 5etools record part (attack and hit entries too), tags expanded."""
    if e is None:
        return ""
    if isinstance(e, str):
        return untag(e)
    if isinstance(e, list):
        return " ".join(_all_text(x) for x in e)
    if isinstance(e, dict):
        kind = _ATTACK_KINDS.get(str(e.get("attackType", "")).upper(), "")
        parts = [kind] if kind else []
        for k, v in e.items():
            if k in ("type", "attackType", "style"):
                continue
            parts.append(("Hit: " if k == "hitEntries" else "") + _all_text(v))
        return " ".join(parts)
    return ""


# the printed words a 5etools attackType stands for ("Ranged Weapon Attack: +6 to hit ...")
_ATTACK_KINDS = {"MW": "Melee Weapon Attack:", "RW": "Ranged Weapon Attack:", "MS": "Melee Spell Attack:",
                 "RS": "Ranged Spell Attack:"}


def _number_of(v):
    """A 5etools number that may be stored bare or as {"average": n} / [n] / {"ac": n}."""
    if isinstance(v, list):
        return _number_of(v[0]) if v else None
    if isinstance(v, dict):
        return v.get("average", v.get("ac"))
    return v


# (our field, its 5etools keys in order: vehicles.json, then the item record of a ship)
_VEHICLE_FIELDS = [("cost", ("cost", "value")), ("pace", ("pace", "vehSpeed")), ("capCrew", ("capCrew", "crew")),
                   ("capPassenger", ("capPassenger",)), ("capCargo", ("capCargo",)),
                   ("hull.ac", ("hull.ac", "vehAc")), ("hull.hp", ("hull.hp", "vehHp")),
                   ("hull.dt", ("hull.dt", "vehDmgThresh")), ("ac", ("ac",)), ("hp", ("hp",))]


def _get(d: dict, path: str):
    for part in path.split("."):
        d = d.get(part) if isinstance(d, dict) else None
    return d


def _score_vehicle_object(etype: str, f: dict, t: dict, r: dict) -> tuple[float | None, float | None]:
    """VehicleMount and Object fields; a field is compared only when both sides hold it. Text: entries (and an
    Object's description and actions) against the 5etools entries and action entries; None when 5etools prints
    no text for the record (unmeasured)."""
    for ours, theirs in _VEHICLE_FIELDS:
        got = _number_of(_get(f, ours))
        want = next((_number_of(_get(t, k)) for k in theirs if _get(t, k) is not None), None)
        if got is not None and want is not None:
            r[ours] = got == want
    if f.get("immune") is not None and t.get("immune") is not None:
        r["immune"] = {str(x).lower() for x in f["immune"]} == {str(x).lower() for x in t["immune"]}
    truth = _all_text([t.get("entries"), t.get("actionEntries")])
    if not shingles(truth):
        return None, None  # 5etools prints no text for it: unmeasured
    got = _all_text([f.get("entries"), f.get("description"), f.get("action")])
    return coverage(truth, got)


def _score_equipment(etype: str, f: dict, t: dict, r: dict, e: dict | None = None) -> tuple[float | None, float | None]:
    if etype in ("VehicleMount", "Object"):
        return _score_vehicle_object(etype, f, t, r)
    if etype == "Weapon":
        for fld in ("value", "weight", "dmg1", "dmgType", "weaponCategory", "dmg2", "range"):
            r[fld] = f.get(fld) == t.get(fld)
        # a weapon described in prose may print no melee or ranged word ("a martial weapon"): our type is absent,
        # and that comparison is skipped
        r["type"] = _code(f.get("type")) == _code(t.get("type")) if f.get("type") is not None else None
        r["property"] = _codes(f.get("property")) == _codes(t.get("property"))
    elif etype == "Armor":
        r["ac"] = f.get("ac") == t.get("ac")
        r["strength"] = (str(f["strength"]) if f.get("strength") is not None else None) == \
            (str(t["strength"]) if t.get("strength") is not None else None)
        r["stealth"] = bool(f.get("stealth")) == bool(t.get("stealth"))
        r["value"] = f.get("value") == t.get("value")
        r["weight"] = f.get("weight") == t.get("weight")
    elif etype == "Item":
        r["value"] = f.get("value") == t.get("value")
        # a field our entity omits is skipped (and counted) only when the book prints no cell for it: our
        # canonical text shows no such line and the value check did not fail. A cell we could not read
        # (a failed weight-valid), and the type of tack or vehicle rows, are wrong, not skipped.
        r["weight"] = f.get("weight") == t.get("weight") if f.get("weight") is not None or _printed(e, "Weight") \
            else None
        printed_type = _code(t.get("type")) in _TYPED_ROWS
        r["type"] = _code(f.get("type")) == _code(t.get("type")) if f.get("type") is not None or printed_type \
            else None
    elif etype == "Trap":
        r["trapHazType"] = f.get("trapHazType") == t.get("trapHazType")
    elif etype == "DiseasePoison":
        r["poisonTypes"] = set(f.get("poisonTypes") or []) == set(t.get("poisonTypes") or [])
        r["value"] = f.get("value") == t.get("value") if f.get("value") is not None or t.get("value") is not None \
            else None
    if not shingles(flat(t.get("entries"))):
        return None, None  # 5etools prints no text for it: unmeasured, not 0%
    return coverage(flat(t.get("entries")), flat(f.get("entries")))


def _score_character(etype: str, f: dict, t: dict, r: dict, src: str | None) -> tuple[float | None, float | None]:
    src = (src or t.get("source") or "").upper()
    if etype == "Class":
        r["hd"] = (f.get("hd") or {}).get("faces") == (t.get("hd") or {}).get("faces")
        r["proficiency"] = sorted(f.get("proficiency") or []) == sorted(t.get("proficiency") or [])
        r["subclassTitle"] = f.get("subclassTitle") == t.get("subclassTitle")
        r["features"] = feature_levels(f.get("classFeatures"), src, 3, 4) == \
            feature_levels(t.get("classFeatures"), src, 3, 4)
    elif etype == "Subclass":
        r["className"] = key(f.get("className")) == key(t.get("className"))
        # spaces kept: a glued "Feywanderer" is a misread of "Fey Wanderer", not the same short name
        r["shortName"] = spaced_key(f.get("shortName")) == spaced_key(t.get("shortName"))
        r["features"] = feature_levels(f.get("subclassFeatures"), src, 5, 6) == \
            feature_levels(t.get("subclassFeatures"), src, 5, 6)
        # the subclass's prose lives in its subclassFeature records (joined at load time)
        return coverage(t.get("_featureText") or "", flat(f.get("entries")))
    elif etype == "Race":
        r["size"] = set(f.get("size") or []) == set(t.get("size") or [])
        r["speed"] = t_walk(f.get("speed")) == t_walk(t.get("speed"))
        r["ability"] = (f.get("ability") or None) == (t.get("ability") or None)
        r["darkvision"] = f.get("darkvision") == t.get("darkvision")
    elif etype == "Subrace":
        r["raceName"] = key(f.get("raceName")) == key(t.get("raceName"))
        r["ability"] = (f.get("ability") or None) == (t.get("ability") or None)
    elif etype == "Feat":
        r["prerequisite"] = bool(f.get("prerequisite")) == bool(t.get("prerequisite"))
    else:
        r["skillProficiencies"] = skill_set(f.get("skillProficiencies")) == skill_set(t.get("skillProficiencies"))
    return coverage(flat(t.get("entries")), flat(f.get("entries")))


_EQ_STOP = {"of", "a", "an", "the"}
_EQ_SUFFIX = {"Armor": {"armor"}, "Trap": {"trap"}}  # 5etools adds the word; the book prints the bare name
# two printed names that are not word permutations of each other: key -> the key of the other spelling
_EQ_ALIASES = {"sledgehammer": "hammer sledge", "gunpowder horn": "gunpowder horn powder"}
_EQ_UNITS = _EQ_STOP | {"1", "lb", "sq", "yd", "one", "per"}


def _eq_words(name: str | None, etype: str) -> list[str]:
    ws = re.findall(r"[a-z0-9+]+", (name or "").lower())
    return sorted(w for w in ws if w not in _EQ_STOP and w not in _EQ_SUFFIX.get(etype, ()))


def _eq_key(name: str | None, etype: str) -> str:
    k = " ".join(_eq_words(name, etype))
    return _EQ_ALIASES.get(k, k)


def _eq_segments(name: str) -> list[str]:
    """Names a printed row such as "Donkey or mule" or "1 lb. of cinnamon or pepper, or one sheep" stands for."""
    parts = [p for p in re.split(r"\s*,?\s+or\s+", name) if p.strip()]
    return parts if len(parts) > 1 else [name]


def _eq_unit_key(name: str) -> str:
    """Key with quantity words dropped: "1 sq. yd. of canvas" and "Canvas (1 sq. yd.)" are both "canvas"."""
    return " ".join(sorted(w for w in re.findall(r"[a-z0-9+]+", name.lower()) if w not in _EQ_UNITS))


def match_key(etype: str, name: str | None) -> str:
    """Name key for matching. Subrace words are order-insensitive: 5etools "Dwarf Hill" is printed "Hill Dwarf".
    Equipment names are word-order, punctuation and bracket insensitive ("Crossbow, light" is "Light Crossbow")."""
    if etype in _EQUIPMENT_TYPES:
        return _eq_key(name, etype)
    if etype == "Rule":
        return key(_RULE_PREFIX.sub("", name or ""))
    if etype in ("Race", "Subrace"):
        low = (name or "").lower()
        ws = set(re.findall(r"[a-z0-9+]+", low))
        if etype == "Subrace" and "variant;" in low:  # 5etools "Variant; Mark of Detection" is printed without it
            ws.discard("variant")
        # a plural heading names its race too: "Ghostwise Halflings" (+ raceName Halfling) is "Halfling Ghostwise"
        ws = {w for w in ws if not (w.endswith("s") and w[:-1] in ws)}
        return "".join(sorted(ws))
    return key(name)


def match_one_to_one(truth_keys: list[str], keys: list[str]) -> dict[int, tuple[str, bool]]:
    """truth index -> (entity key, exact). Exact key matches first, then fuzzy (ratio >= 90) by best
    score first; every entity is claimed at most once."""
    out: dict[int, tuple[str, bool]] = {}
    pool = set(keys)
    for i, tk in enumerate(truth_keys):
        if tk in pool:
            out[i] = (tk, True)
            pool.discard(tk)
    left = [i for i in range(len(truth_keys)) if i not in out]
    cands = []
    for i in left:
        for k in pool:
            sc = fuzz.ratio(truth_keys[i], k)
            if sc >= 90:
                cands.append((sc, i, k))
    cands.sort(key=lambda c: (-c[0], c[1], c[2]))
    for _, i, k in cands:
        if i in out or k not in pool:
            continue
        out[i] = (k, False)
        pool.discard(k)
    return out


def _section_owners(entities: list[dict]) -> dict[str, str]:
    """match key -> id of the earlier-type entity (not a world type) printed under that name: a 5etools section
    node named like it is that entity's text (ruling W27 (a))."""
    out: dict[str, str] = {}
    for e in entities:
        if e.get("type") not in _SECTION_TYPES + ("God", "Rule") and e.get("name"):
            out.setdefault(match_key("Section", e["name"]), e.get("id", e["name"]))
    return out


def _section_texts(entities: list[dict]) -> list[tuple[str, str]]:
    """(id, text) of every world-type entity, for the printed-inside test."""
    return [(e.get("id", e["name"]), e.get("canonicalText") or "") for e in entities
            if e.get("type") in _SECTION_TYPES]


def _printed_inside(name: str, texts: list[tuple[str, str]]) -> str | None:
    """The id of the world entity whose text prints `name` (its own casing) as a lead-in ("Building Bruenor, Step
    1." / "Name:" opening a paragraph or a sentence) or as a folded sub-heading (a line the text joins to the
    capitalised prose under it: "Government The island is ruled ..."), else None (ruling W27 (b)). A mention
    mid-sentence is none: the name must open the text, a line or a sentence."""
    rx = re.compile(r"(?:^|\n|\. )" + re.escape(name) + r"(?:[.:]|\s*\n|$| (?=[A-Z]))")
    for eid, text in texts:
        if rx.search(text):
            return eid
    return None


def _section_truth_at_chosen_depth(truth: list[dict], entities: list[dict], known: list[str]) -> list[dict]:
    """The section tree's recall truth: per chapter, the headings at one depth, each heading name once.

    5etools has no world-type records, so the depth cannot come from it and must not be picked in a way that hides
    misses. A chapter defaults to the depth with the MOST named headings (ties go shallower). A shallower depth is
    taken only when our entities match at least half of that depth's headings (votes are distinct names per depth).
    Recall at every depth is listed per chapter, and a chosen depth with under a quarter of the largest depth's
    headings is flagged."""
    ours = {key(e["name"]) for e in entities if e.get("type") in _SECTION_TYPES}
    chapters: dict[str, list[dict]] = {}
    for t in truth:
        chapters.setdefault(t["_chapter"], []).append(t)
    out: list[dict] = []
    seen: dict[str, int] = {}
    for ch, secs in chapters.items():
        names: dict[int, set[str]] = collections.defaultdict(set)
        for sc in secs:
            names[sc["_depth"]].add(key(sc["name"]))
        total = {d: len(v) for d, v in names.items()}
        hits = {d: len(v & ours) for d, v in names.items()}
        depth = max(total, key=lambda d: (total[d], -d))
        shallow = [d for d in total if d < depth and 2 * hits[d] >= total[d] and hits[d] > 0]
        if shallow:
            depth = max(shallow, key=lambda d: (hits[d], -d))
        known.append(f"{ch}: scored at heading depth {depth}; recall by depth: "
                     + "; ".join(f"depth {d} {hits[d]}/{total[d]}" for d in sorted(total)))
        if 4 * total[depth] < max(total.values()):
            known.append(f"WARNING {ch}: depth {depth} has {total[depth]} headings, under a quarter of the "
                         f"largest depth's {max(total.values())}; deeper sections may be missed")
        for sc in secs:
            if sc["_depth"] == depth:
                seen[key(sc["name"])] = seen.get(key(sc["name"]), 0) + 1
                if seen[key(sc["name"])] == 1:
                    out.append(sc)
    for sc in out:
        if seen[key(sc["name"])] > 1:
            known.append(f"heading printed {seen[key(sc['name'])]} times, scored once: {sc['name']}")
    return out


PRECISION_FLOOR = 0.7  # an entity whose text precision is under this is listed by name


def score_book(book: str, entities: list[dict], fivetools_dir: Path | str, etype: str | None = None) -> dict:
    """Score extracted entities of one type (default: the core book's type) against 5etools."""
    etype = etype or BOOKS.get(book)
    if etype in _SECTION_TYPES:
        etype = "Section"
    if etype not in TYPES:
        raise ValueError(f"book {book!r} needs a type, one of {list(TYPES)}")
    truth = load_truth(book, Path(fivetools_dir), etype)
    known: list[str] = [f"{t['name']}: not printed ({NOT_PRINTED[(book, t['name'])]})"
                        for t in truth if (book, t["name"]) in NOT_PRINTED]
    truth = [t for t in truth if (book, t["name"]) not in NOT_PRINTED]
    known += dropped_truth_lines(book, fivetools_dir, etype)
    if etype == "Section":
        truth = _section_truth_at_chosen_depth(truth, entities, known)
    god_dups = {key(t["name"]) for t in truth if etype == "God"
                and sum(key(o["name"]) == key(t["name"]) for o in truth) > 1}

    def god_key(name: str, pantheon) -> str:  # a name that repeats in the book is told apart by pantheon
        return key(name) + ("|" + key(pantheon) if key(name) in god_dups else "")
    idx: dict[str, dict] = {}
    alias: dict[str, str] = {}  # "Dark Elf (Drow)" also answers to "Drow" + race
    by_type: dict[str, int] = {t: 0 for t in _SECTION_TYPES}
    for e in entities:
        if e.get("type") == etype or (etype == "Section" and e.get("type") in _SECTION_TYPES):
            if book == "mpmm" and etype == "Race" and re.fullmatch(r"(Air|Earth|Fire|Water) Genasi", e["name"]):
                known.append(f"{_GENASI_NOTE}: {e['name']}")
                continue
            name = base_item_name(e["name"]) if etype == "MagicItem" else e["name"]
            if etype == "Subrace":  # the printed heading may omit the race word ("Drow" in the Elf section)
                name = f"{name} {(e.get('fields') or {}).get('raceName') or ''}"
            idx.setdefault(god_key(name, (e.get("fields") or {}).get("pantheon")) if etype == "God"
                           else match_key(etype, name), e)
            par = re.search(r"\(([^)]+)\)", e["name"])
            if etype == "Subrace" and par:
                alias.setdefault(match_key(etype, f"{par.group(1)} {(e.get('fields') or {}).get('raceName') or ''}"),
                                 match_key(etype, name))
    tkeys = [god_key(t["name"], t.get("pantheon")) if etype == "God" else match_key(etype, t["name"])
             for t in truth]
    tkeys = [alias[k] if k not in idx and k in alias else k for k in tkeys]
    for i, t in enumerate(truth):  # 5etools "Gnome Deep/Svirfneblin" is printed under one of the two names
        sub = t.get("_subName") or ""
        if etype == "Subrace" and tkeys[i] not in idx and "/" in sub:
            alt = [k for part in sub.split("/") if (k := match_key(etype, f"{t['raceName']} {part}")) in idx]
            if alt:
                tkeys[i] = alt[0]
    renamed: dict[int, str] = {}  # truth index -> our printed name, for a truth name whose parenthetical is an alias
    for i, t in enumerate(truth):
        bare = re.sub(r"\s*\([^)]*\)\s*$", "", t["name"])
        if etype == "Subclass" and tkeys[i] not in idx and bare != t["name"] and key(bare) in idx:
            tkeys[i] = key(bare)
            renamed[i] = idx[key(bare)]["name"]
    matches = match_one_to_one(tkeys, list(idx))
    multi: dict[int, str] = {}  # truth index -> key of the one printed row that stands for several records
    if etype in _EQUIPMENT_TYPES:
        for k in [k for k in idx if k not in {m for m, _ in matches.values()}]:
            segs = [_eq_unit_key(sg) for sg in _eq_segments(idx[k]["name"])]
            # every segment of the row must name an unmatched truth record, else the row claims nothing
            per = [[i for i, t in enumerate(truth) if i not in matches and i not in multi
                    and _eq_unit_key(t["name"]) == sg] for sg in segs]
            if per and all(per):
                hit = {i for h in per for i in h}
                for i in hit:
                    multi[i] = k
                known.append(f"printed row matched by its quantity-free name: {idx[k]['name']} = "
                             + ", ".join(truth[i]["name"] for i in sorted(hit)))
    matches.update({i: (k, False) for i, k in multi.items()})
    elsewhere = printed_elsewhere(book, fivetools_dir, etype)
    claimed = {k for k, _ in matches.values()}
    for i, t in enumerate(truth):  # a spelling that is more than a word permutation of 5etools' is listed
        if i in matches and i not in multi and etype in _EQUIPMENT_TYPES:
            ours = idx[matches[i][0]]["name"]
            if " ".join(_eq_words(ours, etype)) != " ".join(_eq_words(t["name"], etype)):
                known.append(f"printed name differs: {ours} (5etools: {t['name']})")
    here = {match_key(etype, n): f"{n}: printed, not a 5etools record ({why})"
            for (b, et, n), why in PRINTED_NOT_IN_5ETOOLS.items() if b == book and et == etype}
    printed_here = [k for k in idx if k not in claimed and (k in elsewhere or k in here)]
    known += [elsewhere[k][0] if k in elsewhere else here[k] for k in printed_here]
    agg: dict[str, list[int]] = collections.defaultdict(lambda: [0, 0])
    covs: list[float] = []
    precs: list[float] = []
    named_precs: list[tuple[str, float]] = []  # (our name, text precision) per measured entity
    missing: list[str] = []
    diffs: list[str] = []
    fuzzy: list[str] = []
    text_ok = [0, 0]
    skipped: dict[str, int] = collections.defaultdict(int)  # comparisons skipped: the book prints no value
    fam = [0, 0]  # multi-rarity families: count, ours == "varies"
    owners, inside = (_section_owners(entities), _section_texts(entities)) if etype == "Section" else ({}, [])
    found_by_owner = 0
    excluded = 0  # section nodes left out of the truth (a caption, a lead-in): never silently
    for i, t in enumerate(truth):
        if i not in matches:
            # ruling W27: a section node our world types deliberately do not emit is no miss, and never a silent
            # one: (a) an earlier type owns its text, (b) it is printed inside a parent entity's text (an inline
            # lead-in, a folded recurring sub-heading), (c) the book prints a table caption or a box title there
            if etype == "Section":
                k = match_key(etype, t["name"])
                if t.get("_table"):
                    known.append(f"{t['name']}: not a section (a table's caption in 5etools): not scored")
                    excluded += 1
                    continue
                if k in owners:
                    known.append(f"{t['name']}: found by owner {owners[k]} (an earlier type's entity)")
                    found_by_owner += 1
                    continue
                if (host := _printed_inside(t["name"], inside)) is not None:
                    known.append(f"{t['name']}: printed inside {host} (a lead-in or a folded sub-heading): not "
                                 "scored")
                    excluded += 1
                    continue
            missing.append(t["name"])
            if (book.upper(), etype, t["name"]) in OCR_LOST_NAME:
                known.append(f"{t['name']}: missing, printed name lost in OCR "
                             f"({OCR_LOST_NAME[(book.upper(), etype, t['name'])]})")
            continue
        k, exact = matches[i]
        if not exact:
            fuzzy.append(f"{t['name']} ~ {idx[k]['name']}")
        if i in multi:
            # the row's own fields are not this record's; its cost is when every record of the row shares it
            row = [truth[j] for j, (m, _) in matches.items() if m == k]
            if etype == "Item" and len({r.get("value") for r in row}) == 1:
                ok = idx[k]["fields"].get("value") == t.get("value")
                agg["value"][0] += ok
                agg["value"][1] += 1
                if not ok:
                    diffs.append(f"{t['name']}: value wrong (one printed row: {idx[k]['name']})")
            continue
        fields = idx[k]["fields"]
        if i in renamed:
            t = _rename_subclass(t, renamed[i], known)
        if etype in ("Class", "Subclass"):
            t = _drop_unprinted_features(book, t, known)
        res, cov, prec = score_record(etype, fields, t, book.upper(), idx[k])
        if etype == "God" and (extra := extra_god_domains(fields, t)):
            known.append(f"{t['name']}: domains added by later books, not printed here: {', '.join(extra)}")
        if etype == "Section":
            by_type[idx[k].get("type")] += 1
        if etype == "MagicItem" and len(rarity_family(t)) > 1:
            fam[0] += 1
            fam[1] += fields.get("rarity") == "varies"
        if etype == "God" and res.get("pantheon") is False and \
                (pp := PRINTED_PANTHEONS.get((book, t.get("pantheon") or ""))) and fields.get("pantheon") == pp[0]:
            known.append(f"{t['name']}: pantheon ({pp[1]})")
            res["pantheon"] = True  # agrees with the printed book
        if etype == "Rule" and res.get("ruleType") is False and not fields.get("ruleType") and \
                not _RULE_LABEL.match((idx[k].get("canonicalText") or "").split("\n", 1)[0].strip()):
            # 5etools classifies the rule; the book prints no label on its heading, so there is no value to compare
            known.append(f"{t['name']}: ruleType not printed (no Variant/Optional Rule label on the heading; "
                         f"5etools: {t.get('ruleType')})")
            res["ruleType"] = None
        for f, ok in res.items():
            if ok is None:
                skipped[f] += 1
                continue
            if not ok and (book, t["name"], f) in KNOWN_DIFFERENCES:
                ours = [fields.get(k) for k in _ABIL] if f == "abilities" else fields.get(f)
                want, why = KNOWN_DIFFERENCES[(book, t["name"], f)]
                if ours == want:
                    known.append(f"{t['name']}: {f} ({why})")
                    ok = True  # agrees with the printed book
            agg[f][0] += ok
            agg[f][1] += 1
            if not ok:
                diffs.append(f"{t['name']}: {f} wrong" + ("" if exact else " (fuzzy match)"))
        if cov is not None:
            covs.append(cov)
            text_ok[0] += cov >= 0.9
            text_ok[1] += 1
        if prec is not None:
            precs.append(prec)
            named_precs.append((idx[k]["name"], prec))
    # an entry printed here but stored under another book is scored against that record: its diffs show (and
    # reach the wrong-and-accepted audit), while the book's own truth counts above stay as they are
    for k in printed_here:
        if k not in elsewhere:
            continue
        t = elsewhere[k][1]
        fields = idx[k]["fields"]
        if etype == "Subclass":
            t = _drop_unprinted_features(book, t, known)
        res, _, _ = score_record(etype, fields, t, book.upper())
        for f, ok in res.items():
            if not ok and (book, t["name"], f) in KNOWN_DIFFERENCES and \
                    fields.get(f) == KNOWN_DIFFERENCES[(book, t["name"], f)][0]:
                known.append(f"{t['name']}: {f} ({KNOWN_DIFFERENCES[(book, t['name'], f)][1]})")
                continue
            if not ok:
                diffs.append(f"{t['name']}: {f} wrong (scored against the {t['source']} record)")
    found = len(matches) + found_by_owner
    unmeasured = etype in _UNMEASURED_TYPES  # a new type with no truth text is unmeasured, not 0%
    return {
        "book": book, "type": etype, "truth": len(truth) - excluded, "found": found,
        "extra": len(idx) - len(claimed) - len(printed_here), "skipped": dict(skipped),
        "fields": {f: {"pct": 100 * a / n, "n": n} for f, (a, n) in agg.items() if n},
        "rarityMultiFamilies": {"n": fam[0], "variesAgree": fam[1]} if etype == "MagicItem" else None,
        "textAtLeast90": {"pct": 100 * text_ok[0] / text_ok[1] if text_ok[1] else 0.0, "n": text_ok[1]},
        "textCoverageMedian": 100 * statistics.median(covs) if covs else (None if unmeasured else 0.0),
        "textPrecisionMedian": 100 * statistics.median(precs) if precs else (None if unmeasured else 0.0),
        "textPrecisionMin": min(precs) if precs else None,
        "textMeasured": len(precs),
        "lowPrecision": sorted(((n, p) for n, p in named_precs if p < PRECISION_FLOOR), key=lambda x: (x[1], x[0])),
        "perType": by_type if etype == "Section" else None,
        "missing": missing, "fuzzyMatches": fuzzy, "diffs": diffs, "knownDifferences": known,
        "foundByOwner": found_by_owner,
    }


def format_table(res: dict) -> str:
    fields = list(res["fields"])
    head = f"{'found':>9} {'extra':>6} " + " ".join(f"{f:>11}" for f in fields) + \
        "  text>=90%  text-median  text-precision"
    cells = " ".join(f"{res['fields'][f]['pct']:>6.0f}% ({res['fields'][f]['n']})".rjust(11) for f in fields)

    def pct(v, w: int) -> str:
        return f"{v:{w}.0f}%" if v is not None else "n/a".rjust(w + 1)
    at90 = res["textAtLeast90"]["pct"] if res["textAtLeast90"]["n"] or res["textCoverageMedian"] is not None else None
    row = (f"{res['found']:>4}/{res['truth']:<4} {res['extra']:>6} {cells}  "
           f"{pct(at90, 8)}  {pct(res['textCoverageMedian'], 10)}  "
           f"{pct(res['textPrecisionMedian'], 12)}")
    out = f"=== {res['book'].upper()} {res['type']}: {res['truth']} entries in 5etools ===\n{head}\n{row}"
    if res.get("perType"):
        out += "\nfound by entity type (informational): " + ", ".join(f"{t} {n}" for t, n in res["perType"].items())
    if res["type"] in _UNMEASURED_TYPES:
        if res.get("textMeasured"):
            low = res.get("lowPrecision") or []
            out += (f"\ntext precision: min {100 * res['textPrecisionMin']:.0f}%, median "
                    f"{res['textPrecisionMedian']:.0f}% over {res['textMeasured']} entities with 5etools text; "
                    f"under {100 * PRECISION_FLOOR:.0f}%: " +
                    (", ".join(f"{n} ({100 * p:.0f}%)" for n, p in low) if low else "none"))
        else:
            out += "\ntext precision: unmeasured (5etools prints no text for any matched entity)"
    if res.get("rarityMultiFamilies"):
        m = res["rarityMultiFamilies"]
        out += (f"\nrarity (strict) covers single-rarity items only; multi-rarity families: {m['n']}, "
                f"book says 'varies' for {m['variesAgree']}")
    if res.get("skipped"):
        out += "\nunprinted-field comparisons skipped (our entity omits the field): " + \
            ", ".join(f"{f} {n}" for f, n in sorted(res["skipped"].items()))
    if res.get("knownDifferences"):
        out += "\nverified book-vs-5etools differences (counted as agreeing with the book):\n  " + \
            "\n  ".join(res["knownDifferences"])
    return out


def run(book: str, path: Path, fivetools_dir: Path, diffs_out: Path | None = None,
        etype: str | None = None) -> int:
    book = book.lower()
    try:
        data = json.loads(Path(path).read_text("utf-8"))
        types = [etype] if etype else available_types(book, fivetools_dir)
        if not types:
            print(f"error: 5etools has no monsters, spells or items for {book!r} under {fivetools_dir}",
                  file=sys.stderr)
            return 1
        results = [score_book(book, data["entities"], fivetools_dir, t) for t in types]
    except FileNotFoundError as ex:
        print(f"error: file not found: {ex.filename} (check the extract output and --fivetools dir)",
              file=sys.stderr)
        return 1
    print("\n\n".join(format_table(r) for r in results))
    if diffs_out:
        lines = [f"{r['type']} {line}" for r in results for line in
                 [f"MISSING {n}" for n in r["missing"]] + [f"FUZZY {n}" for n in r["fuzzyMatches"]]
                 + [f"KNOWN {n}" for n in r["knownDifferences"]] + r["diffs"]]
        diffs_out.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return 0
