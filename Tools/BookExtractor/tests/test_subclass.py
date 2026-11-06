import dataclasses
import json
from pathlib import Path

import jsonschema

from bookextractor.layout import reading_order
from bookextractor.model import load_ocr_page
from bookextractor.pages import classify_page
from bookextractor.parsers import ParseContext
from bookextractor.parsers.subclass import parse_subclasses, subclass_checks

FIX = Path(__file__).parent / "fixtures" / "ocr"
SCHEMAS = Path(__file__).parents[3] / "Schemas" / "canonical"
BOOKS = {
    "phb": ("phb14", "PHB", [50, 51]),
    "xge": ("xge", "XGE", [10, 11, 12, 13]),
    "dmg": ("dmg14", "DMG", [96, 97, 98]),
    "ftd": ("ftd", "FTD", [15, 16, 17, 18]),
    "bgg": ("bgg", "BGG", [11, 12]),
    "vrgr": ("vrgr", "VRGR", [29, 30, 31, 32]),
    "egw": ("egw", "EGW", [184, 185, 186, 187]),
    "scag": ("scag", "SCAG", [122, 123]),
}


def _squash(s):
    return "".join(c for c in s.lower() if c.isalpha())


def parse(book="phb", mutate_heading=None):
    slug, source, ns = BOOKS[book]
    pages = {n: load_ocr_page(FIX / book / f"{n:03d}.json", n) for n in ns}
    body = [l for n in sorted(pages) if classify_page(pages[n]) in ("rules", "art") for l in reading_order(pages[n])]
    if mutate_heading:
        body = [dataclasses.replace(l, text=mutate_heading) if _squash(l.text) == "primalpaths" else l for l in body]
    return parse_subclasses(body, ParseContext(slug=slug, source=source, pages=pages))


def get_sub(name, book="phb", mutate_heading=None):
    return next((p for p in parse(book, mutate_heading) if _squash(p.name) == _squash(name)), None)


def failed(p):
    return [c for c in p.checks if not c.passed]


def check(checks, name):
    return next(c for c in checks if c.name == name)


def schema(name):
    return json.loads((SCHEMAS / f"{name}.schema.json").read_text(encoding="utf-8"))


def test_zealot():
    p = get_sub("Path of the Zealot", book="xge")
    assert p.fields["className"] == "Barbarian" and p.fields["classSource"] == "PHB" and p.fields["shortName"] == "Zealot"
    assert sorted({int(r.split("|")[-1]) for r in p.fields["subclassFeatures"]}) == [3, 6, 10, 14]
    assert any(r.endswith("|Barbarian||Zealot|XGE|3") for r in p.fields["subclassFeatures"])
    assert not failed(p), failed(p)


def test_zealot_refs_follow_5etools():
    p = get_sub("Path of the Zealot", book="xge")
    assert p.fields["subclassFeatures"] == [
        "Path of the Zealot|Barbarian||Zealot|XGE|3", "Fanatical Focus|Barbarian||Zealot|XGE|6",
        "Zealous Presence|Barbarian||Zealot|XGE|10", "Rage beyond Death|Barbarian||Zealot|XGE|14"]
    names = [e["name"] for e in p.fields["entries"]]
    assert names[:2] == ["Divine Fury", "Warrior of the Gods"]


def test_xge_group_reads_every_subclass():
    assert [p.name for p in parse("xge")] == ["Path of the Ancestral Guardian", "Path of the Storm Herald",
                                              "Path of the Zealot"]


def test_berserker_phb():
    p = get_sub("Path of the Berserker")
    assert p.fields["className"] == "Barbarian"
    assert p.fields["subclassFeatures"] == [
        "Path of the Berserker|Barbarian||Berserker||3", "Mindless Rage|Barbarian||Berserker||6",
        "Intimidating Presence|Barbarian||Berserker||10", "Retaliation|Barbarian||Berserker||14"]
    assert not failed(p), failed(p)


def test_group_heading_casing_noise():   # Review Focus 5
    assert get_sub("Path of the Zealot", book="xge", mutate_heading="PRIMAL PATHs")


def test_feature_levels_mismatch():
    c = check(subclass_checks({"className": "Barbarian", "subclassFeatures": ["A|Barbarian||X|XGE|3", "B|Barbarian||X|XGE|5"]}), "feature-levels-match-class")
    assert not c.passed and "5" in c.reason


def test_feature_levels_backwards():   # two subclasses read as one: the levels restart
    refs = ["A|Fighter||X|XGE|3", "B|Fighter||X|XGE|7", "C|Fighter||X|XGE|15", "D|Fighter||X|XGE|3",
            "E|Fighter||X|XGE|10", "F|Fighter||X|XGE|18"]
    c = check(subclass_checks({"className": "Fighter", "subclassFeatures": refs}), "feature-levels-match-class")
    assert not c.passed and "D (level 3)" in c.reason and "C (level 15)" in c.reason


def test_parent_class_unknown():
    c = check(subclass_checks({"className": "Gunslinger", "subclassFeatures": []}), "parent-class-known")
    assert not c.passed and "Gunslinger" in c.reason


def test_subclass_schema():
    jsonschema.validate(get_sub("Path of the Zealot", book="xge").fields, schema("SubclassFields"))


# ---------- subclasses outside a class chapter: "<Class>: <Subclass>" labels and bare class headings ----------
def test_dmg_class_label_death_domain():   # "Cleric: Death Domain" under "Villainous Class Options"
    p = get_sub("Death Domain", book="dmg")
    assert p.fields["className"] == "Cleric" and p.fields["shortName"] == "Death"
    assert p.fields["subclassFeatures"] == [
        "Death Domain|Cleric||Death|DMG|1", "Channel Divinity: Touch of Death|Cleric||Death|DMG|2",
        "Inescapable Destruction|Cleric||Death|DMG|6", "Divine Strike|Cleric||Death|DMG|8",
        "Improved Reaper|Cleric||Death|DMG|17"]
    assert not failed(p), failed(p)


def test_dmg_class_label_oathbreaker():   # "PALADIN: OATHBREAKER"; Dread Lord's cue is "At 20th-level"
    p = get_sub("Oathbreaker", book="dmg")
    assert p.fields["className"] == "Paladin" and p.fields["shortName"] == "Oathbreaker"
    assert sorted({int(r.split("|")[-1]) for r in p.fields["subclassFeatures"]}) == [3, 7, 15, 20]
    assert not failed(p), failed(p)
    assert [q.name for q in parse("dmg")] == ["Death Domain", "Oathbreaker"]


def test_ftd_label_split_over_two_lines():   # "MONK:" / "Way of the Ascendant Dragon"; TCE-style cues
    p = get_sub("Way of the Ascendant Dragon", book="ftd")
    assert p.fields["className"] == "Monk" and p.fields["shortName"] == "Ascendant Dragon"
    assert sorted({int(r.split("|")[-1]) for r in p.fields["subclassFeatures"]}) == [3, 6, 11, 17]
    assert not failed(p), failed(p)
    d = get_sub("Drakewarden", book="ftd")
    assert d.fields["className"] == "Ranger" and d.fields["subclassFeatures"][0] == "Drakewarden|Ranger||Drakewarden|FTD|3"
    assert not failed(d), failed(d)


def test_bgg_label_same_height_as_body():
    p = get_sub("Path of the Giant", book="bgg")
    assert p.fields["className"] == "Barbarian" and p.fields["shortName"] == "Giant"
    assert sorted({int(r.split("|")[-1]) for r in p.fields["subclassFeatures"]}) == [3, 6, 10, 14]
    assert not failed(p), failed(p)


def test_vrgr_labels():
    assert [q.name for q in parse("vrgr")] == ["College of Spirits", "The Undead"]
    b, w = get_sub("College of Spirits", book="vrgr"), get_sub("The Undead", book="vrgr")
    assert b.fields["className"] == "Bard" and w.fields["className"] == "Warlock"
    assert w.fields["shortName"] == "Undead" and w.fields["subclassFeatures"][0] == "The Undead|Warlock||Undead|VRGR|1"
    assert not failed(b) and not failed(w), (failed(b), failed(w))


def test_egw_bare_class_heading():   # "FIGHTER" heading, then "Eco Knight" (OCR) named by "3rd-level Echo Knight feature"
    assert [q.name for q in parse("egw")] == ["Echo Knight", "Chronurgy Magic", "Graviturgy Magic"]
    e = get_sub("Echo Knight", book="egw")
    assert e.fields["className"] == "Fighter" and e.fields["shortName"] == "Echo Knight"
    assert sorted({int(r.split("|")[-1]) for r in e.fields["subclassFeatures"]}) == [3, 7, 10, 15, 18]
    g = get_sub("Graviturgy Magic", book="egw")
    assert g.fields["className"] == "Wizard" and g.fields["shortName"] == "Graviturgy"
    assert not failed(e) and not failed(g), (failed(e), failed(g))


def test_label_with_unknown_class_fails_parent_check():   # never guess the parent class
    pages = {n: load_ocr_page(FIX / "bgg" / f"{n:03d}.json", n) for n in (11, 12)}
    body = [l for n in sorted(pages) for l in reading_order(pages[n])]
    body = [dataclasses.replace(l, text="Gunslinger: Path of the Giant") if _squash(l.text) == "barbarianpathofthegiant"
            else l for l in body]
    p = next(q for q in parse_subclasses(body, ParseContext(slug="bgg", source="BGG", pages=pages))
             if _squash(q.name) == "pathofthegiant")
    assert "className" not in p.fields and not check(p.checks, "parent-class-known").passed
    jsonschema.validate(p.fields, schema("SubclassFields"))


def test_spell_range_label_is_not_a_ranger_subclass():   # TCE "Range: Self" fuzzily resembles "Ranger: ..."
    pages = {n: load_ocr_page(FIX / "dmg" / f"{n:03d}.json", n) for n in (96, 97)}
    body = [l for n in sorted(pages) for l in reading_order(pages[n])]
    body = [dataclasses.replace(l, text="Range: Self") if _squash(l.text) == "clericdeathdomain" else l for l in body]
    assert [q.name for q in parse_subclasses(body, ParseContext(slug="dmg14", source="DMG", pages=pages))] == \
        ["Oathbreaker"]


def test_egw_cue_name_correction_is_recorded():   # R11: a cue-based rename passes but is never silent
    e = get_sub("Echo Knight", book="egw")
    c = check(e.checks, "name-repaired")
    assert c.passed and "Eco Knight" in c.reason and "Echo Knight" in c.reason and "cues" in c.reason
    assert not any(c.name == "name-repaired" for c in get_sub("Chronurgy Magic", book="egw").checks)


def test_scag_battlerager_is_a_subclass():
    p = get_sub("Path of the Battlerager", book="scag")
    assert p.fields["className"] == "Barbarian" and p.fields["shortName"] == "Battlerager"
    assert sorted({int(r.split("|")[-1]) for r in p.fields["subclassFeatures"]}) == [3, 6, 10, 14]


def test_scag_totem_warrior_options_are_not_a_subclass():
    # "If you follow the Path of the Totem Warrior from the Player's Handbook, you have access to the options
    # presented here": new totem options for a PHB subclass, not a subclass SCAG prints
    assert get_sub("Path of the Totem Warrior", book="scag") is None
    assert [p.name for p in parse("scag")] == ["Path of the Battlerager"]
