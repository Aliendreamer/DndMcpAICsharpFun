"""Dev tool: fix flagged (NeedsReview) programmatic entities from the 5etools data and write
`<book>.overrides.json` entries (field overrides + an approval "verified against 5etools").

Owner rule: when a flagged entity has exactly one unambiguous 5etools counterpart (same book source, same type,
same name key), the 5etools record is trusted. Every field of our schema is set to the 5etools value in OUR shape and
canonicalText is re-rendered from the record. Anything that cannot be mapped with certainty is left untouched.

    python -m bookextractor.fivetools_fix --canonical books/canonical --fivetools 5etools --schemas Schemas/canonical \
        [--books mm14,phb14] [--report out.json] [--dry-run]
"""
from __future__ import annotations

import argparse
import copy
import json
import re
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path

from . import score
from .score import key, match_key

REASON = "verified against 5etools"
SKIP_BOOKS = {"system-reference-document", "dragonborn-slice", "bgg"}
BOOK_SOURCE = {"elemental-evil-player-s-companion": "EEPC"}  # every other slug: the file's book.sourceBook

# ---------------------------------------------------------------- markup -> display text

_ATKR = {"m": "Melee Attack:", "r": "Ranged Attack:", "m,r": "Melee or Ranged Attack:", "r,m": "Melee or Ranged Attack:"}
# tags whose display text is the third `|` part when present ({@item name|source|display})
_DISPLAY3 = {"item", "spell", "creature", "class", "condition", "disease", "status", "background", "race", "feat",
             "optfeature", "object", "vehicle", "trap", "hazard", "action", "sense", "skill", "variantrule", "language",
             "reward", "cult", "boon", "psionic", "card", "deck", "itemMastery", "classFeature", "subclassFeature",
             "table", "charoption", "recipe", "facility", "legroup"}
_TAG = re.compile(r"\{@(\w+)(?: ([^{}]*))?\}")


def _tag_text(m: re.Match) -> str:
    tag, body = m.group(1), m.group(2) or ""
    parts = body.split("|")
    if tag == "atk":
        return score._ATK.get(body, body)
    if tag == "atkr":
        return _ATKR.get(body, body)
    if tag == "hit":
        return f"{int(parts[0]):+d}" if re.fullmatch(r"-?\d+", parts[0]) else parts[0]
    if tag == "h":
        return "Hit: "
    if tag == "m":
        return "Miss: "
    if tag == "dc":
        return f"DC {parts[0]}"
    if tag == "recharge":
        return f"(Recharge {parts[0]}-6)" if parts[0] and parts[0] != "6" else "(Recharge 6)"
    if tag in ("dice", "damage") and len(parts) > 1 and parts[1]:
        return parts[1]
    if tag == "deity":
        return parts[3] if len(parts) > 3 and parts[3] else parts[0]
    if tag == "quickref":
        return parts[4] if len(parts) > 4 and parts[4] else parts[0]
    if tag in _DISPLAY3 and len(parts) > 2 and parts[2]:
        return parts[2]
    return parts[0]


def untag(s: str) -> str:
    """Reduce all {@tag x|y} markup to its display text; nested tags are resolved innermost first."""
    prev = None
    while prev != s and "{@" in s:
        prev = s
        s = _TAG.sub(_tag_text, s)
    return s


def clean(v):
    """Deep copy of a 5etools value with every string untagged."""
    if isinstance(v, str):
        return untag(v)
    if isinstance(v, list):
        return [clean(x) for x in v]
    if isinstance(v, dict):
        return {k: clean(x) for k, x in v.items()}
    return v


# ---------------------------------------------------------------- canonicalText rendering

_SIZE = {"T": "Tiny", "S": "Small", "M": "Medium", "L": "Large", "H": "Huge", "G": "Gargantuan"}
_ALIGN = {"L": "lawful", "N": "neutral", "NX": "neutral", "NY": "neutral", "C": "chaotic", "G": "good", "E": "evil",
          "U": "unaligned", "A": "any alignment"}


def _alignment_text(al) -> str:
    if not al:
        return ""
    if al == ["U"]:
        return "unaligned"
    if al == ["A"]:
        return "any alignment"
    if al == ["N"]:
        return "neutral"
    if al == ["L", "N", "C"] or al == ["L", "NX", "C", "NY", "E", "G"]:
        return "any alignment"
    if set(al) == {"N", "G", "E"} or al == ["N", "G", "E"]:
        return "any non-lawful alignment"
    return " ".join(_ALIGN.get(a, a) for a in al)


def _type_text(t) -> str:
    if isinstance(t, str):
        return t
    base = t.get("type")
    if isinstance(base, dict):  # {"choose": [...]}
        base = "/".join(base.get("choose", []))
    tags = t.get("tags") or []
    out = base or ""
    if tags:
        out += " (" + ", ".join(x if isinstance(x, str) else x.get("tag", "") for x in tags) + ")"
    if t.get("swarmSize"):
        out = f"swarm of {_SIZE.get(t['swarmSize'], t['swarmSize'])} {out}s"
    return out


def _ac_text(ac) -> str:
    parts = []
    for a in ac or []:
        if isinstance(a, int):
            parts.append(str(a))
        else:
            s = str(a.get("ac"))
            frm = a.get("from")
            if frm:
                s += " (" + ", ".join(frm) + ")"
            if a.get("condition"):
                s += " " + a["condition"]
            parts.append(s)
    return ", ".join(parts)


def _hp_text(hp) -> str:
    if not hp:
        return ""
    if hp.get("special"):
        return hp["special"]
    return f"{hp.get('average')} ({hp.get('formula')})"


def _speed_text(sp) -> str:
    if not sp:
        return ""
    out = []
    for k in ("walk", "burrow", "climb", "fly", "swim"):
        v = sp.get(k)
        if v is None or v is False:
            continue
        cond = ""
        if isinstance(v, dict):
            cond = f" {v.get('condition', '')}".rstrip()
            v = v.get("number")
        s = f"{v} ft.{cond}" if k == "walk" else f"{k} {v} ft.{cond}"
        if k == "fly" and sp.get("canHover"):
            s += " (hover)"
        out.append(s)
    return ", ".join(out) if out else "0 ft."


def _mod(score_) -> str:
    return f"{score_} ({(score_ - 10) // 2:+d})"


def _cr_text(cr) -> str:
    if isinstance(cr, dict):
        cr = cr.get("cr")
    xp = {"0": "0", "1/8": "25", "1/4": "50", "1/2": "100"}
    from .checks import XP_BY_CR
    n = XP_BY_CR.get(str(cr))
    return f"{cr} ({n:,} XP)" if n is not None else str(cr)


def _list_text(v) -> str:
    out = []
    for x in v or []:
        if isinstance(x, str):
            out.append(x)
        elif isinstance(x, dict):
            inner = x.get("immune") or x.get("resist") or x.get("vulnerable") or x.get("conditionImmune") or []
            body = ", ".join(i if isinstance(i, str) else _list_text([i]) for i in inner)
            note = x.get("note") or ""
            pre = x.get("preNote") or ""
            out.append(" ".join(p for p in (pre, body, note) if p))
    return "; ".join(out)


def _entries_text(entries, indent: bool = False) -> str:
    return "\n".join(_entry_lines(entries))


def _entry_lines(e) -> list[str]:
    if e is None:
        return []
    if isinstance(e, str):
        return [e]
    if isinstance(e, list):
        out: list[str] = []
        for x in e:
            out += _entry_lines(x)
        return out
    if isinstance(e, dict):
        t = e.get("type")
        if t == "list":
            return ["- " + (_item_text(i) if isinstance(i, dict) and (i.get("name") or i.get("entry"))
                            else " ".join(_entry_lines(i))) for i in e.get("items", [])]
        if t == "table":
            out = []
            if e.get("caption"):
                out.append(e["caption"])
            if e.get("colLabels"):
                out.append(" | ".join(str(c) for c in e["colLabels"]))
            for row in e.get("rows", []):
                out.append(" | ".join(" ".join(_entry_lines(c)) for c in row))
            return out
        if t == "item":
            return [_item_text(e)]
        if t == "inset" or t == "insetReadaloud" or t == "quote":
            body = _entry_lines(e.get("entries") or e.get("entry"))
            return ([e["name"]] if e.get("name") else []) + body
        if t in ("entries", "section", "options", "inlineBlock") or "entries" in e:
            head = e.get("name")
            body = _entry_lines(e.get("entries"))
            if head and body:
                return [f"{head}. {body[0]}"] + body[1:]
            return ([head] if head else []) + body
        if t == "abilityDc":
            return [f"{e.get('name')} spell save DC = 8 + your proficiency bonus + your {', '.join(e.get('attributes', []))} modifier"]
        if t == "abilityAttackMod":
            return [f"{e.get('name')} attack modifier = your proficiency bonus + your {', '.join(e.get('attributes', []))} modifier"]
        if e.get("entry"):
            return _entry_lines(e["entry"])
        if e.get("name"):
            return [str(e["name"])]
    return [str(e)]


def _item_text(i: dict) -> str:
    name = i.get("name")
    body = " ".join(_entry_lines(i.get("entries") or i.get("entry")))
    return f"{name}. {body}".strip() if name else body


def render_monster(name: str, f: dict) -> str:
    L = [name]
    L.append(f"{' or '.join(_SIZE.get(s, s) for s in f.get('size', []))} {_type_text(f.get('type', ''))}, "
             f"{_alignment_text(f.get('alignment'))}".replace(" ,", ","))
    L.append(f"Armor Class {_ac_text(f.get('ac'))}")
    L.append(f"Hit Points {_hp_text(f.get('hp'))}")
    L.append(f"Speed {_speed_text(f.get('speed'))}")
    L.append("STR DEX CON INT WIS CHA")
    L.append(" ".join(_mod(f.get(a, 10)) for a in score._ABIL))
    if f.get("save"):
        L.append("Saving Throws " + ", ".join(f"{k.capitalize()} {v}" for k, v in f["save"].items()))
    if f.get("skill"):
        L.append("Skills " + ", ".join(f"{k.title()} {v}" for k, v in f["skill"].items() if k != "other"))
    for lab, k in (("Damage Vulnerabilities", "vulnerable"), ("Damage Resistances", "resist"),
                   ("Damage Immunities", "immune"), ("Condition Immunities", "conditionImmune")):
        if f.get(k):
            L.append(f"{lab} {_list_text(f[k])}")
    senses = list(f.get("senses") or []) + [f"passive Perception {f.get('passive', 10)}"]
    L.append("Senses " + ", ".join(senses))
    L.append("Languages " + (", ".join(f.get("languages") or []) or "—"))
    L.append(f"Challenge {_cr_text(f.get('cr'))}")

    def block(items):
        for it in items or []:
            L.append(f"{it.get('name')}. " + (_entries_text(it.get("entries")) if it.get("name") else ""))

    block(f.get("trait"))
    if f.get("spellcasting"):
        for sc in f["spellcasting"]:
            body = list(sc.get("headerEntries") or [])
            for k, v in (sc.get("will") and {"At will": sc["will"]} or {}).items():
                body.append(f"{k}: " + ", ".join(v))
            for per, spells in (sc.get("daily") or {}).items():
                body.append(f"{per[0]}/day{' each' if per.endswith('e') else ''}: " + ", ".join(spells))
            for lvl, d in (sc.get("spells") or {}).items():
                body.append(f"Level {lvl}" + (f" ({d['slots']} slots)" if d.get("slots") else "") + ": "
                            + ", ".join(d.get("spells", [])))
            L.append(f"{sc.get('name', 'Spellcasting')}. " + " ".join(body))
    for lab, k in (("Actions", "action"), ("Bonus Actions", "bonus"), ("Reactions", "reaction")):
        if f.get(k):
            L.append(lab)
            block(f[k])
    if f.get("legendary"):
        L.append("Legendary Actions")
        L += f.get("legendaryHeader") or []
        block(f["legendary"])
    if f.get("lair"):
        L.append("Lair Actions")
        L += _entry_lines(f.get("lairHeader"))
        L += _entry_lines(f["lair"])
    return "\n".join(L)


def _spell_level_text(f: dict) -> str:
    school = {"A": "Abjuration", "C": "Conjuration", "D": "Divination", "E": "Enchantment", "V": "Evocation",
              "I": "Illusion", "N": "Necromancy", "T": "Transmutation"}.get(f.get("school"), f.get("school", ""))
    lvl = f.get("level", 0)
    ordn = {1: "1st", 2: "2nd", 3: "3rd"}.get(lvl, f"{lvl}th")
    s = f"{school} cantrip" if lvl == 0 else f"{ordn}-level {school.lower()}"
    return s + (" (ritual)" if f.get("ritual") else "")


def _time_text(t) -> str:
    return ", ".join(f"{x.get('number')} {x.get('unit')}" + (f", {x['condition']}" if x.get("condition") else "")
                     for x in t or [])


def _range_text(r) -> str:
    if not r:
        return ""
    d = r.get("distance") or {}
    t = r.get("type")
    if t == "point":
        if d.get("type") in ("self", "touch", "sight", "unlimited"):
            return d["type"].capitalize()
        return f"{d.get('amount')} {d.get('type')}"
    if t == "special":
        return "Special"
    return f"Self ({d.get('amount')}-{d.get('type', 'foot').rstrip('s')} {t})" if d.get("amount") else "Self"


def _components_text(c) -> str:
    out = []
    if c.get("v"):
        out.append("V")
    if c.get("s"):
        out.append("S")
    m = c.get("m")
    if m:
        out.append("M (" + (m if isinstance(m, str) else m.get("text", "")) + ")")
    return ", ".join(out)


def _duration_text(ds) -> str:
    out = []
    for d in ds or []:
        if d.get("type") == "instant":
            out.append("Instantaneous")
        elif d.get("type") == "permanent":
            out.append("Until dispelled" if "dispel" in (d.get("ends") or []) else "Permanent")
        elif d.get("type") == "special":
            out.append("Special")
        else:
            dur = d.get("duration") or {}
            s = f"{dur.get('amount')} {dur.get('type')}" + ("s" if dur.get("amount", 1) != 1 else "")
            if d.get("concentration"):
                s = "Concentration, up to " + s
            elif dur.get("upTo"):
                s = "Up to " + s
            out.append(s)
    return ", ".join(out)


def render_spell(name: str, f: dict) -> str:
    L = [name, _spell_level_text(f), f"Casting Time: {_time_text(f.get('time'))}",
         f"Range: {_range_text(f.get('range'))}", f"Components: {_components_text(f.get('components') or {})}",
         f"Duration: {_duration_text(f.get('duration'))}"]
    L += _entry_lines(f.get("entries"))
    hl = _entry_lines(f.get("entriesHigherLevel"))
    if hl:
        L += hl
    return "\n".join(L)


def _value_text(v) -> str:
    if v is None:
        return ""
    if v % 100 == 0:
        return f"{v // 100} gp"
    if v % 10 == 0:
        return f"{v // 10} sp"
    return f"{v} cp"


def render_generic(name: str, f: dict, header: list[str] | None = None) -> str:
    L = [name] + (header or [])
    L += _entry_lines(f.get("entries"))
    return "\n".join(x for x in L if x)


def render(etype: str, name: str, f: dict) -> str:
    if etype == "Monster":
        return render_monster(name, f)
    if etype == "Spell":
        return render_spell(name, f)
    hdr: list[str] = []
    if etype == "MagicItem":
        t = f.get("type", "")
        kind = "Wondrous item" if f.get("wondrous") else str(t).split("|")[0]
        hdr.append(f"{kind}, {f.get('rarity')}" + (f" (requires attunement)" if f.get("reqAttune") else ""))
    elif etype in ("Item", "Weapon", "Armor", "DiseasePoison"):
        if f.get("value") is not None:
            hdr.append(f"Cost: {_value_text(f['value'])}")
        if etype == "Weapon" and f.get("dmg1"):
            dt = {"B": "bludgeoning", "P": "piercing", "S": "slashing"}.get(f.get("dmgType"), f.get("dmgType", ""))
            hdr.append(f"Damage: {f['dmg1']} {dt}".strip())
        if etype == "Armor" and f.get("ac") is not None:
            hdr.append(f"Armor Class (AC): {f['ac']}")
        if f.get("weight") is not None:
            hdr.append(f"Weight: {f['weight']} lb.")
    elif etype in ("Race", "Subrace"):
        ab = ", ".join(f"{k.upper()} {v:+d}" for a in f.get("ability") or [] for k, v in a.items() if isinstance(v, int))
        if ab:
            hdr.append(f"Ability Score Increase: {ab}")
        if f.get("size"):
            hdr.append("Size: " + "/".join(_SIZE.get(x, x) for x in f["size"]))
        sp = f.get("speed")
        if sp is not None:
            hdr.append("Speed: " + (f"{sp} ft." if isinstance(sp, int) else _speed_text(sp)))
        if f.get("darkvision"):
            hdr.append(f"Darkvision: {f['darkvision']} ft.")
    elif etype == "God":
        if f.get("alignment"):
            hdr.append("Alignment: " + "".join(f["alignment"]))
        if f.get("province"):
            hdr.append(f"Province: {f['province']}")
        if f.get("domains"):
            hdr.append("Suggested Domains: " + ", ".join(f["domains"]))
        if f.get("symbol"):
            hdr.append(f"Common Symbol: {f['symbol']}")
    return render_generic(name, f, hdr)


# ---------------------------------------------------------------- truth records

def _load_json(p: Path):
    return json.loads(p.read_text("utf-8"))


class Truth:
    """5etools records of one book, per entity type, indexed by match key."""

    def __init__(self, fivetools: Path, src: str):
        self.d = fivetools
        self.src = src
        self._cache: dict[str, dict[str, list[dict]]] = {}
        self.copies: set[str] = set()

    def records(self, etype: str) -> dict[str, list[dict]]:
        if etype not in self._cache:
            try:
                recs = self._load(etype)
            except FileNotFoundError:
                recs = []
            idx: dict[str, list[dict]] = defaultdict(list)
            for r in recs:
                idx[match_key(etype, r["name"])].append(r)
            self._cache[etype] = idx
        return self._cache[etype]

    def _load(self, etype: str) -> list[dict]:
        d, src = self.d, self.src
        if etype == "Monster":
            out = [m for f in sorted((d / "bestiary").glob("bestiary-*.json")) for m in _load_json(f).get("monster", [])
                   if m.get("source") == src]
            self.copies = {match_key("Monster", m["name"]) for m in out if m.get("_copy")}
            return [m for m in out if not m.get("_copy")]
        if etype == "Spell":
            return [s for f in sorted((d / "spells").glob("spells-*.json")) for s in _load_json(f).get("spell", [])
                    if s.get("source") == src]
        if etype == "MagicItem":
            return [i for i in _load_json(d / "items.json")["item"]
                    if i.get("source") == src and i.get("rarity") not in (None, "none")]
        if etype in ("Weapon", "Armor", "Item", "DiseasePoison", "Trap", "Condition", "VehicleMount", "Object"):
            return score._split_equipment_truth(src, d, etype)[0]
        if etype in score._CHARACTER_TYPES:
            return score._split_character_truth(src, d, etype)[0]
        if etype in ("God", "Rule"):
            return score._split_world_truth(src, d, etype)[0]
        return []


# ---------------------------------------------------------------- per-type field mapping

class Unmappable(Exception):
    pass


def _schema(schemas: Path, etype: str) -> dict:
    return _load_json(schemas / f"{etype}Fields.schema.json")


def _nullable(prop_schema: dict) -> bool:
    t = prop_schema.get("type")
    if t == "null" or (isinstance(t, list) and "null" in t):
        return True
    return any(_nullable(s) for s in prop_schema.get("oneOf", []))


# 5etools classification tags the printed book does not carry and the extractor never fills: left as they are
MONSTER_BLOCKS = ("trait", "action", "bonus", "reaction", "legendary", "legendaryHeader", "lair", "lairHeader",
                  "regional", "spellcasting", "variant", "resist", "immune", "vulnerable", "conditionImmune", "save",
                  "skill", "senses", "languages", "legendaryGroup")
TAG_KEYS = {"miscTags", "damageTags", "languageTags", "senseTags", "actionTags", "environment", "keywords",
            "areaTags", "scalingLevelDice", "traitTags", "reqAttuneTags", "conditionInflict", "damageInflict",
            "savingThrow", "classes"}


def map_fields(etype: str, rec: dict, ours: dict, schema: dict) -> dict:
    """Our `fields` object (only the keys that change are the caller's business) built from a 5etools record."""
    props = schema["properties"]
    new: dict = {}
    r = rec
    if etype == "Subrace":
        r = dict(rec)
        r["raceName"] = rec.get("raceName")
        r["raceSource"] = rec.get("raceSource") or rec.get("source")
    for k in props:
        if k in TAG_KEYS:
            continue
        if k in r and r[k] is not None:
            new[k] = clean(r[k])
    if etype in ("Item", "Weapon", "Armor", "DiseasePoison"):
        # a value 5etools has no record of is not printed: clear ours where the schema allows a null
        for k, ps in props.items():
            if k not in new and k != "entries" and ours.get(k) is not None and _nullable(ps):
                new[k] = None
    if etype == "Spell":
        comp = new.get("components")
        if isinstance(comp, dict) and isinstance(comp.get("m"), dict):  # our shape keeps the material text only
            comp["m"] = comp["m"].get("text", "")
        new["ritual"] = bool((rec.get("meta") or {}).get("ritual"))
        new["concentration"] = any(d.get("concentration") for d in rec.get("duration") or [])
    if etype == "MagicItem":
        new.pop("type", None)  # ours is the printed type line ("weapon (longsword)"); 5etools holds codes
        new["wondrous"] = bool(rec.get("wondrous"))
        base = {"P": "potion", "RG": "ring", "WD": "wand", "RD": "rod", "SC": "scroll", "ST": "staff"}.get(
            str(rec.get("type") or "").split("|")[0])
        ours_t = str(ours.get("type") or "").lower()
        if base and not ours_t.startswith(base):
            raise Unmappable(f"printed type {ours_t!r} disagrees with 5etools {base}")
        if rec.get("wondrous") and not ours_t.startswith("wondrous"):
            raise Unmappable(f"printed type {ours_t!r} disagrees with 5etools wondrous item")
    if etype == "Monster":
        # our shape keeps `type` as a bare string without tags (the schema allows both)
        sp = new.get("speed")
        if isinstance(sp, dict):  # {"fly": {"number": 60, "condition": "(hover)"}} is our {"fly": 60, "canHover": true}
            for mode, v in list(sp.items()):
                if isinstance(v, dict) and v.get("condition") == "(hover)" and isinstance(v.get("number"), int):
                    sp[mode] = v["number"]
                    sp["canHover"] = True
        if isinstance(new.get("cr"), dict):  # our shape keeps the plain challenge rating
            new["cr"] = new["cr"].get("cr")
        hp = new.get("hp")
        if not isinstance(hp, dict) or not isinstance(hp.get("average"), int):
            raise Unmappable("hit points have no average")
        for k in MONSTER_BLOCKS:  # a stat block is described completely by 5etools: a key it lacks is not printed
            if k not in new and ours.get(k) not in (None, [], {}):
                new[k] = None
        t = new.get("type")
        if isinstance(t, dict) and not t.get("tags") and isinstance(t.get("type"), str) and len(t) == 1:
            new["type"] = t["type"]
    return new






# ---------------------------------------------------------------- the run

SUPPORTED = ("Monster", "Spell", "MagicItem", "Item", "Weapon", "Armor", "DiseasePoison", "Trap", "Condition",
             "Race", "Subrace", "Feat", "Background", "God", "Rule")
NO_5ETOOLS_COUNTERPART = {"Plane", "Faction", "Location", "Lore", "Class", "Subclass", "Object", "VehicleMount"}
NAME_FROM_5ETOOLS = {"Monster", "Spell", "MagicItem", "Feat", "Background", "God", "Race", "Condition"}
IGNORED_FAILS = {"override-drift", "override-unapplied"}


def _norm(v):
    return json.dumps(v, sort_keys=True, ensure_ascii=False)


def _same(a, b) -> bool:
    if a in (None, [], {}) and b in (None, [], {}):
        return True
    if isinstance(a, list) and isinstance(b, list) and all(isinstance(x, str) for x in a + b):
        return sorted(a) == sorted(b)
    return _norm(a) == _norm(b)


def _validate(schema: dict, fields: dict) -> str | None:
    import jsonschema
    err = next(iter(jsonschema.Draft4Validator(schema).iter_errors(fields)), None)
    return None if err is None else f"{'/'.join(str(p) for p in err.absolute_path)}: {err.message[:100]}"


def _now() -> str:
    return datetime.now(timezone.utc).isoformat()


def process_book(path: Path, fivetools: Path, schemas: Path, existing: dict, log: dict) -> tuple[list, list]:
    """(new overrides, new approvals) for one canonical file; `log` collects per-type outcomes."""
    data = _load_json(path)
    if data.get("schemaVersion") != "2":
        return [], []
    src = BOOK_SOURCE.get(path.stem, data["book"]["sourceBook"])
    truth = Truth(fivetools, src)
    touched = {o["entityId"] for o in existing.get("overrides", [])} | {a["entityId"] for a in existing.get("approvals", [])}
    ents = data["entities"]
    by_key: Counter = Counter((e["type"], match_key(e["type"], e["name"])) for e in ents)
    overrides, approvals = [], []
    for e in ents:
        if e["disposition"] == "Accepted" and not e.get("needsReview"):
            continue
        t = e["type"]
        out = log.setdefault(t, Counter())
        if t not in SUPPORTED:
            out["left: not a 5etools type" if t in NO_5ETOOLS_COUNTERPART else "left: type not supported"] += 1
            continue
        if e["id"] in touched:
            out["left: owner already overrode/approved"] += 1
            continue
        mk = match_key(t, e["name"])
        recs = truth.records(t).get(mk, [])
        if not recs:
            out["left: no 5etools match" + (" (_copy)" if mk in truth.copies else "")] += 1
            continue
        if len(recs) > 1 or by_key[(t, mk)] > 1:
            out["left: ambiguous match"] += 1
            continue
        schema = _schema(schemas, t)
        ours = e["fields"] or {}
        try:
            new = map_fields(t, recs[0], ours, schema)
            merged = {**ours, **new}
            bad = _validate(schema, merged)
            if bad:
                raise Unmappable("schema " + bad)
            if "required" in schema and any(k not in merged for k in schema["required"]):
                raise Unmappable("required field missing in 5etools record")
        except Unmappable as u:
            out["left: unmappable field (" + str(u)[:60] + ")"] += 1
            continue
        at = _now()
        todo = []
        for k, v in new.items():
            cur = ours.get(k)
            if _same(cur, v):
                continue
            todo.append({"entityId": e["id"], "field": f"fields.{k}", "value": v, "extracted": cur,
                         "reason": REASON, "at": at, "extractedAbsent": k not in ours})
        name = e["name"]
        theirs = untag(recs[0]["name"])
        if t in NAME_FROM_5ETOOLS and theirs != name and key(theirs) == key(name):
            name = theirs
            todo.append({"entityId": e["id"], "field": "name", "value": theirs, "extracted": e["name"],
                         "reason": REASON, "at": at, "extractedAbsent": False})
        text = render(t, name, merged)
        if text != e["canonicalText"]:
            todo.append({"entityId": e["id"], "field": "canonicalText", "value": text, "extracted": e["canonicalText"],
                         "reason": REASON, "at": at, "extractedAbsent": False})
        waived = sorted({c["name"] for c in e["checks"] if not c["passed"]} - IGNORED_FAILS)
        overrides += todo
        approvals.append({"entityId": e["id"], "reason": REASON, "at": at, "waivedChecks": waived})
        out["fixed+approved"] += 1
    return overrides, approvals


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(prog="fivetools_fix")
    ap.add_argument("--canonical", type=Path, required=True)
    ap.add_argument("--fivetools", type=Path, required=True)
    ap.add_argument("--schemas", type=Path, required=True)
    ap.add_argument("--books", default="", help="comma list of book slugs (default: every programmatic book)")
    ap.add_argument("--report", type=Path, default=None)
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args(argv)
    want = {b.strip() for b in a.books.split(",") if b.strip()}
    summary: dict = {}
    for path in sorted(a.canonical.glob("*.json")):
        if path.name.endswith((".report.json", ".overrides.json", ".errors.json", ".warnings.json", ".declined.json")):
            continue
        if path.stem in SKIP_BOOKS or (want and path.stem not in want):
            continue
        ov_path = path.with_name(path.stem + ".overrides.json")
        existing = _load_json(ov_path) if ov_path.exists() else {"overrides": [], "approvals": []}
        log: dict = {}
        new_o, new_a = process_book(path, a.fivetools, a.schemas, existing, log)
        summary[path.stem] = {t: dict(c) for t, c in log.items()}
        if new_a and not a.dry_run:
            merged = {"overrides": existing.get("overrides", []) + new_o,
                      "approvals": existing.get("approvals", []) + new_a}
            ov_path.write_text(json.dumps(merged, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print(path.stem, {t: dict(c) for t, c in log.items()})
    if a.report:
        a.report.write_text(json.dumps(summary, indent=2), encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
