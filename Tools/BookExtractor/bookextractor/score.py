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
}
# entries 5etools builds that the book never prints as their own entry
NOT_PRINTED: dict[tuple[str, str], str] = {
    ("vrgr", "Elise"): "the book says she uses the flesh golem stat block with changes",
    ("vrgr", "Isolde"): "the book says she uses the cambion stat block with changes",
}
_ABIL = ("str", "dex", "con", "int", "wis", "cha")

_ATK = {"mw": "Melee Weapon Attack:", "rw": "Ranged Weapon Attack:", "mw,rw": "Melee or Ranged Weapon Attack:",
        "ms": "Melee Spell Attack:", "rs": "Ranged Spell Attack:", "ms,rs": "Melee or Ranged Spell Attack:"}


def key(n: str | None) -> str:
    return re.sub(r"[^a-z0-9+]", "", (n or "").lower())


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
TYPES = ("Monster", "Spell", "MagicItem", "Class", "Subclass", "Race", "Subrace", "Feat", "Background")


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
    seen: dict[str, dict] = {}
    for i in json.loads((d / "items.json").read_text("utf-8"))["item"]:
        if i.get("source") == book.upper() and i.get("rarity") not in (None, "none"):
            b = base_item_name(i["name"])
            rec = seen.setdefault(b, dict(i, name=b, _rarities=set()))
            rec["_rarities"].add(i["rarity"])
            rec["reqAttune"] = rec.get("reqAttune") or i.get("reqAttune")  # the printed entry covers every tier
    return list(seen.values())


def _load_character_truth(src: str, d: Path, etype: str) -> list[dict]:
    return _split_character_truth(src, d, etype)[0]


def _split_character_truth(src: str, d: Path, etype: str) -> tuple[list[dict], list[str]]:
    """(truth records, known-difference lines for the records left out of the truth)."""
    if etype in ("Class", "Subclass"):
        files = sorted((d / "class").glob("class-*.json"))
        if not files:
            raise FileNotFoundError(str(d / "class"))
        lst = "class" if etype == "Class" else "subclass"
        kept, dropped = [], []
        for f in files:
            for r in json.loads(f.read_text("utf-8")).get(lst, []):
                if r.get("source") != src:
                    continue
                # 5etools stores each supplement subclass twice; the XPHB (2024 class) and EFA (2024 Artificer)
                # copies are twins of the printed one
                if etype == "Subclass" and r.get("classSource") in ("XPHB", "EFA"):
                    dropped.append(f"reprint (classSource {r['classSource']}) not scored: {r['name']}")
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


def dropped_truth_lines(book: str, fivetools_dir: Path | str, etype: str) -> list[str]:
    """Known-difference lines for the 5etools records load_truth leaves out of `etype`'s truth."""
    if etype not in _CHARACTER_TYPES:
        return []
    return _split_character_truth(book.upper(), Path(fivetools_dir), etype)[1]


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
            out.add((key(parts[0]), int(lvl)))
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
            if load_truth(book, fivetools_dir, t):
                out.append(t)
        except (FileNotFoundError, KeyError):
            pass
    return out


def score_record(etype: str, fields: dict, t: dict, src: str | None = None) -> tuple[dict, float | None, float | None]:
    r: dict[str, bool] = {}
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
    else:
        if len(rarity_family(t)) == 1:
            r["rarity"] = same_rarity(t, fields.get("rarity"))
        r["attune"] = attunes(fields.get("reqAttune")) == attunes(t.get("reqAttune"))
        cov, prec = coverage(flat(t.get("entries")), flat(fields.get("entries")))
    return r, cov, prec


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
        r["shortName"] = key(f.get("shortName")) == key(t.get("shortName"))
        r["features"] = feature_levels(f.get("subclassFeatures"), src, 5, 6) == \
            feature_levels(t.get("subclassFeatures"), src, 5, 6)
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


def match_key(etype: str, name: str | None) -> str:
    """Name key for matching. Subrace words are order-insensitive: 5etools "Dwarf Hill" is printed "Hill Dwarf"."""
    if etype in ("Race", "Subrace"):
        low = (name or "").lower()
        ws = set(re.findall(r"[a-z0-9+]+", low))
        if etype == "Subrace" and "variant;" in low:  # 5etools "Variant; Mark of Detection" is printed without it
            ws.discard("variant")
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


def score_book(book: str, entities: list[dict], fivetools_dir: Path | str, etype: str | None = None) -> dict:
    """Score extracted entities of one type (default: the core book's type) against 5etools."""
    etype = etype or BOOKS.get(book)
    if etype not in TYPES:
        raise ValueError(f"book {book!r} needs a type, one of {list(TYPES)}")
    truth = load_truth(book, Path(fivetools_dir), etype)
    known: list[str] = [f"{t['name']}: not printed ({NOT_PRINTED[(book, t['name'])]})"
                        for t in truth if (book, t["name"]) in NOT_PRINTED]
    truth = [t for t in truth if (book, t["name"]) not in NOT_PRINTED]
    known += dropped_truth_lines(book, fivetools_dir, etype)
    idx: dict[str, dict] = {}
    alias: dict[str, str] = {}  # "Dark Elf (Drow)" also answers to "Drow" + race
    for e in entities:
        if e.get("type") == etype:
            if book == "mpmm" and etype == "Race" and re.fullmatch(r"(Air|Earth|Fire|Water) Genasi", e["name"]):
                known.append(f"{_GENASI_NOTE}: {e['name']}")
                continue
            name = base_item_name(e["name"]) if etype == "MagicItem" else e["name"]
            if etype == "Subrace":  # the printed heading may omit the race word ("Drow" in the Elf section)
                name = f"{name} {(e.get('fields') or {}).get('raceName') or ''}"
            idx.setdefault(match_key(etype, name), e)
            par = re.search(r"\(([^)]+)\)", e["name"])
            if etype == "Subrace" and par:
                alias.setdefault(match_key(etype, f"{par.group(1)} {(e.get('fields') or {}).get('raceName') or ''}"),
                                 match_key(etype, name))
    tkeys = [match_key(etype, t["name"]) for t in truth]
    tkeys = [alias[k] if k not in idx and k in alias else k for k in tkeys]
    matches = match_one_to_one(tkeys, list(idx))
    agg: dict[str, list[int]] = collections.defaultdict(lambda: [0, 0])
    covs: list[float] = []
    precs: list[float] = []
    missing: list[str] = []
    diffs: list[str] = []
    fuzzy: list[str] = []
    text_ok = [0, 0]
    fam = [0, 0]  # multi-rarity families: count, ours == "varies"
    for i, t in enumerate(truth):
        if i not in matches:
            missing.append(t["name"])
            continue
        k, exact = matches[i]
        if not exact:
            fuzzy.append(f"{t['name']} ~ {idx[k]['name']}")
        fields = idx[k]["fields"]
        res, cov, prec = score_record(etype, fields, t, book.upper())
        if etype == "MagicItem" and len(rarity_family(t)) > 1:
            fam[0] += 1
            fam[1] += fields.get("rarity") == "varies"
        for f, ok in res.items():
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
    found = len(matches)
    return {
        "book": book, "type": etype, "truth": len(truth), "found": found, "extra": len(idx) - found,
        "fields": {f: {"pct": 100 * a / n, "n": n} for f, (a, n) in agg.items() if n},
        "rarityMultiFamilies": {"n": fam[0], "variesAgree": fam[1]} if etype == "MagicItem" else None,
        "textAtLeast90": {"pct": 100 * text_ok[0] / text_ok[1] if text_ok[1] else 0.0, "n": text_ok[1]},
        "textCoverageMedian": 100 * statistics.median(covs) if covs else 0.0,
        "textPrecisionMedian": 100 * statistics.median(precs) if precs else 0.0,
        "missing": missing, "fuzzyMatches": fuzzy, "diffs": diffs, "knownDifferences": known,
    }


def format_table(res: dict) -> str:
    fields = list(res["fields"])
    head = f"{'found':>9} {'extra':>6} " + " ".join(f"{f:>11}" for f in fields) + \
        "  text>=90%  text-median  text-precision"
    cells = " ".join(f"{res['fields'][f]['pct']:>6.0f}% ({res['fields'][f]['n']})".rjust(11) for f in fields)
    row = (f"{res['found']:>4}/{res['truth']:<4} {res['extra']:>6} {cells}  "
           f"{res['textAtLeast90']['pct']:8.0f}%  {res['textCoverageMedian']:10.0f}%  "
           f"{res['textPrecisionMedian']:12.0f}%")
    out = f"=== {res['book'].upper()} {res['type']}: {res['truth']} entries in 5etools ===\n{head}\n{row}"
    if res.get("rarityMultiFamilies"):
        m = res["rarityMultiFamilies"]
        out += (f"\nrarity (strict) covers single-rarity items only; multi-rarity families: {m['n']}, "
                f"book says 'varies' for {m['variesAgree']}")
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
