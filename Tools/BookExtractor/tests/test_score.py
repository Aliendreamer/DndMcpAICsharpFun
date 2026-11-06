import json
import subprocess
import sys

from bookextractor.cli import main
from bookextractor.score import base_item_name, match_one_to_one, score_book, untag


def test_tag_expansion():
    assert untag("{@atk mw}") == "Melee Weapon Attack:"
    assert untag("{@hit 4}") == "+4"
    assert untag("{@h}") == "Hit: "
    assert untag("{@dc 13}") == "DC 13"
    assert untag("{@damage 1d6}") == "1d6"


def test_variant_collapse():
    assert base_item_name("Potion of Healing (Greater)") == "Potion of Healing"
    assert base_item_name("Potion of Greater Healing") == "Potion of Healing"
    assert base_item_name("+2 Wand of the War Mage") == "Wand of the War Mage"
    assert base_item_name("Armor, +1, +2, or +3") == "Armor"
    assert base_item_name("Hand of Vecna") == "Eye and Hand of Vecna"


def test_extraction_path_never_imports_score():
    code = ("import sys, bookextractor.assemble, bookextractor.cli, bookextractor.reader;"
            "assert 'bookextractor.score' not in sys.modules")
    subprocess.run([sys.executable, "-c", code], check=True)


def test_score_book_items_collapses_variants(tmp_path):
    items = [{"name": n, "source": "DMG", "rarity": r, "entries": ["x y z a b c d e"]}
             for n, r in [("Potion of Healing", "common"), ("Potion of Greater Healing", "uncommon"),
                          ("Hand of Vecna", "artifact"), ("Eye of Vecna", "artifact")]]
    (tmp_path / "items.json").write_text(json.dumps({"item": items}))
    ents = [{"type": "MagicItem", "name": "Potion of Healing",
             "fields": {"rarity": "varies", "reqAttune": None, "entries": ["x y z a b c d e"]}},
            {"type": "MagicItem", "name": "Eye and Hand of Vecna",
             "fields": {"rarity": "artifact", "reqAttune": None, "entries": ["x y z a b c d e"]}}]
    res = score_book("dmg", ents, tmp_path)
    assert res["truth"] == 2 and res["found"] == 2 and res["extra"] == 0
    assert res["fields"]["rarity"] == {"pct": 100.0, "n": 1}
    assert res["rarityMultiFamilies"] == {"n": 1, "variesAgree": 1}


def _monster(name, ac=12):
    return {"name": name, "source": "MM", "ac": [ac], "hp": {"average": 5}, "cr": "1", "str": 1, "dex": 1,
            "con": 1, "int": 1, "wis": 1, "cha": 1}


def _ours_monster(name, ac=12):
    return {"type": "Monster", "name": name, "fields": {"ac": [ac], "hp": {"average": 5}, "cr": "1", "str": 1,
            "dex": 1, "con": 1, "int": 1, "wis": 1, "cha": 1}}


def test_wrong_ac_missing_and_no_double_claim(tmp_path):
    (tmp_path / "bestiary").mkdir()
    truth = [_monster("Young Black Dragon"), _monster("Young Blue Dragon"), _monster("Zombie")]
    (tmp_path / "bestiary/bestiary-mm.json").write_text(json.dumps({"monster": truth}))
    res = score_book("mm", [_ours_monster("Young Black Dragon", ac=99), _ours_monster("Zombie")], tmp_path)
    assert res["found"] == 2 and res["extra"] == 0
    assert res["missing"] == ["Young Blue Dragon"]
    assert any("Young Black Dragon: ac wrong" in d for d in res["diffs"])


def test_matcher_one_to_one():
    m = match_one_to_one(["youngblackdragon", "youngbluedragon"], ["youngblackdragon"])
    assert m == {0: ("youngblackdragon", True)}
    m = match_one_to_one(["youngblackdragn", "youngbluedragon"], ["youngbluedragn"])
    assert len(m) == 1 and list(m.values())[0][1] is False


def test_wrong_school_and_range_reported(tmp_path):
    (tmp_path / "spells").mkdir()
    sp = {"name": "Foo", "level": 1, "school": "V", "time": [{"number": 1, "unit": "action"}],
          "range": {"type": "point", "distance": {"type": "feet", "amount": 60}},
          "components": {"v": True}, "duration": [{"type": "instant"}], "entries": ["a b c d e f"]}
    (tmp_path / "spells/spells-phb.json").write_text(json.dumps({"spell": [sp]}))
    ours = {"type": "Spell", "name": "Foo", "fields": {"level": 1, "school": "C", "time": [{"number": 1, "unit": "action"}],
            "range": {"type": "point", "distance": {"type": "feet", "amount": 30}}, "components": {"v": True},
            "duration": [{"type": "instant"}], "entries": ["a b c d e f"]}}
    res = score_book("phb", [ours], tmp_path)
    assert res["fields"]["school"]["pct"] == 0 and res["fields"]["range"]["pct"] == 0
    assert res["fields"]["level"]["pct"] == 100


def test_missing_fivetools_dir_is_clean_error(tmp_path, capsys):
    out = tmp_path / "o.json"
    out.write_text(json.dumps({"entities": []}))
    assert main(["score", "mm", str(out), "--fivetools", str(tmp_path / "nope")]) == 1
    assert "error:" in capsys.readouterr().err


def _revivify_truth(tmp_path):
    (tmp_path / "spells").mkdir()
    sp = {"name": "Revivify", "level": 3, "school": "N", "time": [{"number": 1, "unit": "action"}],
          "range": {"type": "point", "distance": {"type": "touch"}},
          "components": {"v": True}, "duration": [{"type": "instant"}], "entries": ["a b c d e f"]}
    (tmp_path / "spells/spells-phb.json").write_text(json.dumps({"spell": [sp]}))


def _revivify_ours(school):
    return {"type": "Spell", "name": "Revivify", "fields": {
        "level": 3, "school": school, "time": [{"number": 1, "unit": "action"}],
        "range": {"type": "point", "distance": {"type": "touch"}}, "components": {"v": True},
        "duration": [{"type": "instant"}], "entries": ["a b c d e f"]}}


def test_known_book_difference_is_listed_and_counted_only_while_value_matches(tmp_path):
    _revivify_truth(tmp_path)
    res = score_book("phb", [_revivify_ours("C")], tmp_path)  # the book prints conjuration
    assert res["fields"]["school"]["pct"] == 100 and res["diffs"] == []
    assert len(res["knownDifferences"]) == 1 and "Revivify: school" in res["knownDifferences"][0]
    res = score_book("phb", [_revivify_ours("A")], tmp_path)  # any other value is still a real disagreement
    assert res["fields"]["school"]["pct"] == 0 and res["knownDifferences"] == []
    assert any("Revivify: school wrong" in d for d in res["diffs"])


def test_any_bestiary_book_scores_monsters(tmp_path):
    (tmp_path / "bestiary").mkdir()
    truth = [dict(_monster("Githyanki Supreme Commander"), source="MPMM"), dict(_monster("Sacred Statue"), source="MPMM")]
    (tmp_path / "bestiary/bestiary-mpmm.json").write_text(json.dumps({"monster": truth}))
    res = score_book("mpmm", [_ours_monster("Githyanki Supreme Commander", ac=99)], tmp_path, "Monster")
    assert res["book"] == "mpmm" and res["type"] == "Monster" and res["truth"] == 2 and res["found"] == 1
    assert res["missing"] == ["Sacred Statue"] and res["fields"]["ac"]["pct"] == 0


def test_any_book_scores_its_items_and_spells_by_source(tmp_path):
    items = [{"name": "Thunderbuss", "source": "BGG", "rarity": "rare", "entries": ["a b c d e f"]},
             {"name": "Bag of Holding", "source": "DMG", "rarity": "uncommon", "entries": ["a b c d e f"]}]
    (tmp_path / "items.json").write_text(json.dumps({"item": items}))
    ours = {"type": "MagicItem", "name": "Thunderbuss",
            "fields": {"rarity": "rare", "reqAttune": None, "entries": ["a b c d e f"]}}
    res = score_book("bgg", [ours], tmp_path, "MagicItem")
    assert res["truth"] == 1 and res["found"] == 1 and res["fields"]["rarity"]["pct"] == 100
    (tmp_path / "spells").mkdir()
    (tmp_path / "spells/spells-xge.json").write_text(json.dumps({"spell": []}))
    assert score_book("xge", [], tmp_path, "Spell")["truth"] == 0


def test_cli_scores_every_type_a_book_has(tmp_path, capsys):
    (tmp_path / "bestiary").mkdir()
    (tmp_path / "bestiary/bestiary-ftd.json").write_text(json.dumps({"monster": [dict(_monster("Ashen Dragon"), source="FTD")]}))
    (tmp_path / "items.json").write_text(json.dumps({"item": [
        {"name": "Crystalblade", "source": "FTD", "rarity": "rare", "entries": ["a b c d e f"]}]}))
    out = tmp_path / "o.json"
    out.write_text(json.dumps({"entities": [_ours_monster("Ashen Dragon")]}))
    assert main(["score", "ftd", str(out), "--fivetools", str(tmp_path)]) == 0
    text = capsys.readouterr().out
    assert "FTD Monster: 1 entries" in text and "FTD MagicItem: 1 entries" in text


def test_core_books_keep_their_single_type(tmp_path, capsys):
    (tmp_path / "bestiary").mkdir()
    (tmp_path / "bestiary/bestiary-mm.json").write_text(json.dumps({"monster": [_monster("Zombie")]}))
    out = tmp_path / "o.json"
    out.write_text(json.dumps({"entities": [_ours_monster("Zombie")]}))
    assert main(["score", "mm", str(out), "--fivetools", str(tmp_path)]) == 0
    assert "MM Monster: 1 entries" in capsys.readouterr().out


def test_ftd_tiered_items_collapse_to_the_printed_entry():
    for tier in ("Slumbering", "Stirring", "Wakened", "Ascendant"):
        assert base_item_name(f"{tier} Dragon-Touched Focus") == "Dragon-Touched Focus"
        assert base_item_name(f"{tier} Scaled Ornament") == "Scaled Ornament"


def test_ggr_guild_items_collapse_to_the_printed_entry():
    assert base_item_name("Azorius Guild Signet") == "Guild Signet"
    assert base_item_name("Simic Keyrune") == "Guild Keyrune"
    assert base_item_name("Rakdos Riteknife") == "Rakdos Riteknife"


def test_tce_absorbing_tattoos_collapse_to_the_printed_entry():
    assert base_item_name("Necrotic Absorbing Tattoo") == "Absorbing Tattoo"
    assert base_item_name("Baba Yaga's Pestle") == base_item_name("Baba Yaga's Mortar") == "Baba Yaga's Mortar and Pestle"


def test_ftd_greatwyrm_colours_collapse_to_the_printed_family(tmp_path):
    from bookextractor.score import base_monster_name
    assert base_monster_name("Red Greatwyrm") == "Chromatic Greatwyrm"
    assert base_monster_name("Topaz Greatwyrm") == "Gem Greatwyrm"
    assert base_monster_name("Gold Greatwyrm") == "Metallic Greatwyrm"
    assert base_monster_name("Red Dragon Wyrmling") == "Red Dragon Wyrmling"
    (tmp_path / "bestiary").mkdir()
    truth = [dict(_monster(n), source="FTD") for n in ("Red Greatwyrm", "Blue Greatwyrm", "Gold Greatwyrm")]
    (tmp_path / "bestiary/bestiary-ftd.json").write_text(json.dumps({"monster": truth}))
    res = score_book("ftd", [_ours_monster("Chromatic Greatwyrm"), _ours_monster("Metallic Greatwyrm")],
                     tmp_path, "Monster")
    assert res["truth"] == 2 and res["found"] == 2 and res["missing"] == []


def test_item_family_requires_attunement_when_any_tier_does(tmp_path):
    items = [{"name": f"{t} Dragon Vessel", "source": "FTD", "rarity": r, "entries": ["a b c d e f"],
              **({"reqAttune": True} if t == "Slumbering" else {})}
             for t, r in [("Ascendant", "legendary"), ("Slumbering", "uncommon")]]
    (tmp_path / "items.json").write_text(json.dumps({"item": items}))
    ours = {"type": "MagicItem", "name": "Dragon Vessel",
            "fields": {"rarity": "varies", "reqAttune": True, "entries": ["a b c d e f"]}}
    assert score_book("ftd", [ours], tmp_path, "MagicItem")["fields"]["attune"]["pct"] == 100


def test_entries_the_book_never_prints_are_not_counted_missing(tmp_path):
    # VRGR: Elise and Isolde are "uses the X stat block with changes"; 5etools builds full blocks
    (tmp_path / "bestiary").mkdir()
    truth = [dict(_monster(n), source="VRGR") for n in ("Loup Garou", "Elise", "Isolde")]
    (tmp_path / "bestiary/bestiary-vrgr.json").write_text(json.dumps({"monster": truth}))
    res = score_book("vrgr", [_ours_monster("Loup Garou")], tmp_path, "Monster")
    assert res["truth"] == 1 and res["found"] == 1 and res["missing"] == []
    assert any(k.startswith("Elise: not printed") for k in res["knownDifferences"])


# ---------------- character types (read the real 5etools directory) ----------------
from pathlib import Path  # noqa: E402

import pytest  # noqa: E402

from bookextractor.score import available_types, dropped_truth_lines, load_truth, score_record  # noqa: E402

FT = Path(__file__).resolve().parents[3] / "5etools"
needs_5etools = pytest.mark.skipif(not (FT / "races.json").exists(), reason="5etools directory not present")


@needs_5etools
def test_truth_phb_classes():
    names = {t["name"] for t in load_truth("phb", FT, "Class")}
    assert names >= {"Barbarian", "Wizard"} and len(load_truth("phb", FT, "Class")) == 12


@needs_5etools
def test_truth_xge_subclasses():
    assert "Path of the Zealot" in {t["name"] for t in load_truth("xge", FT, "Subclass")}


@needs_5etools
def test_truth_phb_subraces_named():
    truth = load_truth("phb", FT, "Subrace")
    assert truth and all(t.get("name") for t in truth)
    assert "Dwarf Hill" in {t["name"] for t in truth}


@needs_5etools
def test_truth_phb_feats_backgrounds():
    assert len(load_truth("phb", FT, "Feat")) == 42
    assert "Acolyte" in {t["name"] for t in load_truth("phb", FT, "Background")}


@needs_5etools
def test_score_class_fields():
    t = next(t for t in load_truth("phb", FT, "Class") if t["name"] == "Barbarian")
    r, *_ = score_record("Class", {"hd": {"number": 1, "faces": 12}, "proficiency": ["str", "con"],
                                   "subclassTitle": "Primal Path", "classFeatures": t["classFeatures"]}, t)
    assert all(r.values())


@needs_5etools
def test_score_class_features_ignore_other_sources_and_detect_wrong_level():
    t = next(t for t in load_truth("phb", FT, "Class") if t["name"] == "Barbarian")
    f = {"hd": {"faces": 12}, "proficiency": ["con", "str"], "subclassTitle": "Primal Path",
         "classFeatures": [{"classFeature": "Rage|Barbarian||1"}]}
    assert score_record("Class", f, t)[0]["features"] is False
    assert score_record("Class", {**f, "hd": {"faces": 8}}, t)[0]["hd"] is False
    refs = ["Rage|Barbarian||1", "Primal Knowledge|Barbarian||3|TCE"]
    from bookextractor.score import feature_levels
    assert feature_levels(refs, "PHB", 3, 4) == {("rage", 1)}
    assert feature_levels(refs, "TCE", 3, 4) == {("rage", 1), ("primal knowledge", 3)}


@needs_5etools
def test_score_other_character_types():
    sub = next(t for t in load_truth("phb", FT, "Subrace") if t["name"] == "Dwarf Hill")
    r, *_ = score_record("Subrace", {"raceName": "Dwarf", "ability": sub["ability"]}, sub)
    assert all(r.values())
    race = next(t for t in load_truth("phb", FT, "Race") if t["name"] == "Dwarf")
    r, *_ = score_record("Race", {"size": ["M"], "speed": 25, "ability": [{"con": 2}], "darkvision": 60}, race)
    assert all(r.values())
    bg = next(t for t in load_truth("phb", FT, "Background") if t["name"] == "Acolyte")
    r, *_ = score_record("Background", {"skillProficiencies": [{"insight": True, "religion": True}]}, bg)
    assert r["skillProficiencies"]
    feat = load_truth("phb", FT, "Feat")[0]
    r, *_ = score_record("Feat", {"prerequisite": [{"x": 1}] if not feat.get("prerequisite") else None}, feat)
    assert r["prerequisite"] is False


@needs_5etools
def test_available_types_core_book_includes_character_types():
    assert {"Spell", "Class", "Feat"} <= set(available_types("phb", FT))
    assert "Monster" in available_types("mm", FT)


def test_score_type_choices_come_from_score_types(capsys):
    with pytest.raises(SystemExit):
        main(["score", "phb", "x.json", "--fivetools", ".", "--type", "Nope"])
    assert "Subrace" in capsys.readouterr().err


@needs_5etools
def test_score_book_matches_printed_subrace_names():
    ents = [{"type": "Subrace", "name": n, "fields": {"raceName": r}}
            for n, r in [("Hill Dwarf", "Dwarf"), ("High Elf", "Elf"), ("Lightfoot Halfling", "Halfling"),
                         ("Drow", "Elf")]]
    res = score_book("phb", ents, FT, "Subrace")
    assert res["found"] == 4 and res["extra"] == 0
    assert not any(m in res["missing"] for m in ("Dwarf Hill", "Elf High", "Halfling Lightfoot", "Elf Drow"))


@needs_5etools
def test_truth_subclasses_drop_xphb_copies():
    counts = {b: len(load_truth(b, FT, "Subclass")) for b in ("phb", "xge", "tce")}
    assert counts == {"phb": 40, "xge": 31, "tce": 30}
    names = [t["name"] for t in load_truth("xge", FT, "Subclass") if t.get("className") == "Barbarian"]
    assert sorted(names) == sorted(set(names))


@needs_5etools
def test_truth_backgrounds_drop_copy_variants():
    assert len(load_truth("phb", FT, "Background")) == 13
    assert len(load_truth("egw", FT, "Background")) == 2
    assert not any(t.get("_copy") for t in load_truth("phb", FT, "Background"))
    res = score_book("phb", [], FT, "Background")
    assert any("variant (_copy) not scored: Variant Criminal (Spy)" in k for k in res["knownDifferences"])


@needs_5etools
def test_score_book_lists_dropped_subclass_reprints():
    res = score_book("xge", [], FT, "Subclass")
    assert "reprint (classSource XPHB) not scored: Path of the Zealot" in res["knownDifferences"]
    assert any(k.startswith("reprint (classSource XPHB)") for k in res["knownDifferences"])
    assert all(t.get("classSource") not in ("XPHB", "EFA") for t in load_truth("xge", FT, "Subclass"))


def _ent(t, n, **f):
    return {"type": t, "name": n, "fields": f}


@needs_5etools
def test_race_match_is_word_order_insensitive_without_parentheses():
    ents = [_ent("Race", n) for n in ("Chromatic Dragonborn", "Gem Dragonborn", "Metallic Dragonborn")]
    res = score_book("ftd", ents, FT, "Race")
    assert res["found"] == 3 and res["missing"] == []


@needs_5etools
def test_subrace_parenthetical_alias_matches_drow():
    res = score_book("phb", [_ent("Subrace", "Dark Elf (Drow)", raceName="Elf")], FT, "Subrace")
    assert res["found"] == 1 and "Elf Drow" not in res["missing"] and res["extra"] == 0


@needs_5etools
def test_subrace_variant_prefix_dropped_from_truth():
    res = score_book("erlw", [_ent("Subrace", "Mark of Detection", raceName="Half-Elf")], FT, "Subrace")
    assert res["found"] == 1 and res["extra"] == 0


@needs_5etools
def test_mpmm_genasi_printed_as_separate_races():
    ents = [_ent("Race", f"{e} Genasi") for e in ("Air", "Earth", "Fire", "Water")]
    res = score_book("mpmm", ents, FT, "Race")
    assert "Genasi" not in res["missing"] and res["extra"] == 0
    assert any(k.startswith("printed as separate races (Air/Earth/Fire/Water Genasi): Genasi") for k in res["knownDifferences"])
    sub = score_book("mpmm", [], FT, "Subrace")
    assert sub["truth"] == 0 and sum("printed as separate races" in k for k in sub["knownDifferences"]) == 4


@needs_5etools
def test_phb_custom_background_is_rules_not_entry():
    assert "Custom Background" not in [t["name"] for t in load_truth("phb", FT, "Background")]
    res = score_book("phb", [], FT, "Background")
    assert "rules, not a background entry: Custom Background" in res["knownDifferences"]


def test_feature_names_ignore_case_and_a_trailing_die_note():
    from bookextractor.score import feature_levels
    assert feature_levels(["Bardic Inspiration (d6)|Bard||1", "Artificer Specialist feature|Artificer|TCE|5"],
                          "TCE", 3, 4) == {("bardic inspiration", 1), ("artificer specialist feature", 5)}
    assert feature_levels(["Brutal Critical (1 die)|Barbarian||9"], "PHB", 3, 4) == {("brutal critical 1 die", 9)}


def _truth_refs(book, etype, name):
    t = next(t for t in load_truth(book, FT, etype) if t["name"] == name)
    return t, t["classFeatures" if etype == "Class" else "subclassFeatures"]


@needs_5etools
def test_artificer_firearm_sidebar_is_a_known_difference():
    t, refs = _truth_refs("tce", "Class", "Artificer")
    ours = [r for r in refs if not str(r).startswith("Optional Rule: Firearm Proficiency")]
    res = score_book("tce", [_ent("Class", "Artificer", hd=t["hd"], proficiency=t["proficiency"],
                                  subclassTitle=t["subclassTitle"], classFeatures=ours)], FT, "Class")
    assert res["fields"]["features"]["pct"] == 100, res["diffs"]
    assert any("Artificer" in k and "Optional Rule: Firearm Proficiency" in k for k in res["knownDifferences"])


@needs_5etools
def test_tce_domain_blessed_strikes_is_a_known_difference():
    t, refs = _truth_refs("tce", "Subclass", "Order Domain")
    ours = [r for r in refs if not str(r).startswith("Blessed Strikes")]
    res = score_book("tce", [_ent("Subclass", "Order Domain", className="Cleric", shortName="Order",
                                  subclassFeatures=ours)], FT, "Subclass")
    assert not any(d.startswith("Order Domain: features") for d in res["diffs"]), res["diffs"]
    assert any("Order Domain" in k and "Blessed Strikes" in k for k in res["knownDifferences"])


@needs_5etools
def test_phb_human_ability_on_the_race_is_a_known_difference():
    allp1 = [{"str": 1, "dex": 1, "con": 1, "int": 1, "wis": 1, "cha": 1}]
    res = score_book("phb", [_ent("Race", "Human", size=["M"], speed=30, ability=allp1)], FT, "Race")
    assert not any(d.startswith("Human: ability") for d in res["diffs"]), res["diffs"]
    assert any(k.startswith("Human: ability") for k in res["knownDifferences"])
    # excused only while our value is the printed one
    res = score_book("phb", [_ent("Race", "Human", size=["M"], speed=30, ability=[{"str": 2}])], FT, "Race")
    assert any(d.startswith("Human: ability") for d in res["diffs"])


@needs_5etools
def test_sidekick_classes_score_under_their_printed_names():
    assert {"Expert", "Spellcaster", "Warrior"} <= {t["name"] for t in load_truth("tce", FT, "Class")}
    printed = {
        "Expert": ["Bonus Proficiencies|Expert|TCE|1", "Helpful|Expert|TCE|1", "Cunning Action|Expert|TCE|2",
                   "Expertise|Expert|TCE|3", "Ability Score Improvement|Expert|TCE|4",
                   "Coordinated Strike|Expert|TCE|6", "Evasion|Expert|TCE|7", "Ability Score Improvement|Expert|TCE|8",
                   "Ability Score Improvement|Expert|TCE|10", "Inspiring Help (1d6)|Expert|TCE|11",
                   "Ability Score Improvement|Expert|TCE|12", "Reliable Talent|Expert|TCE|14",
                   "Expertise|Expert|TCE|15", "Ability Score Improvement|Expert|TCE|16", "Sharp Mind|Expert|TCE|18",
                   "Ability Score Improvement|Expert|TCE|19", "Inspiring Help (2d6)|Expert|TCE|20"]}
    res = score_book("tce", [_ent("Class", "Expert", classFeatures=printed["Expert"])], FT, "Class")
    assert res["fields"]["features"]["pct"] == 100, res["diffs"]
    known = "\n".join(res["knownDifferences"])
    assert "Expert: feature Sidekick Class (level 1) not printed" in known
    assert "Expert: feature Inspiring Help Improvement (level 20) is printed 'Inspiring Help (2d6)'" in known
    assert "Expert Sidekick" in known


@needs_5etools
def test_subclass_parenthetical_alias_matches_purple_dragon_knight():
    refs = ["Purple Dragon Knight|Fighter||Purple Dragon Knight|SCAG|3", "Royal Envoy|Fighter||Purple Dragon Knight|SCAG|7",
            "Inspiring Surge|Fighter||Purple Dragon Knight|SCAG|10", "Bulwark|Fighter||Purple Dragon Knight|SCAG|15",
            "Inspiring Surge|Fighter||Purple Dragon Knight|SCAG|18"]
    res = score_book("scag", [_ent("Subclass", "Purple Dragon Knight", className="Fighter",
                                   shortName="Purple Dragon Knight", subclassFeatures=refs)],
                     FT, "Subclass")
    assert "Purple Dragon Knight (Banneret)" not in res["missing"] and res["extra"] == 0
    assert res["fields"]["shortName"]["pct"] == 100, res["diffs"]
    assert not any(d.startswith("Purple Dragon Knight (Banneret): features") for d in res["diffs"]), res["diffs"]


@needs_5etools
def test_subclass_stored_under_another_source_is_a_known_difference_not_an_extra():
    # SCAG prints Way of the Sun Soul; 5etools stores it under XGE with otherSources SCAG
    res = score_book("scag", [_ent("Subclass", "Way of the Sun Soul", className="Monk", shortName="Sun Soul")],
                     FT, "Subclass")
    assert res["extra"] == 0
    assert any("Way of the Sun Soul" in k and "XGE" in k for k in res["knownDifferences"])


@needs_5etools
def test_subrace_slash_name_and_plural_heading_match():
    ents = [_ent("Subrace", "Svirfneblin", raceName="Gnome", ability=[{"dex": 1}]),
            _ent("Subrace", "Ghostwise Halflings", raceName="Halfling", ability=[{"wis": 1}])]
    res = score_book("scag", ents, FT, "Subrace")
    assert "Gnome Deep/Svirfneblin" not in res["missing"] and "Halfling Ghostwise" not in res["missing"]
    assert res["fuzzyMatches"] == [] and res["diffs"] == [], res


@needs_5etools
def test_scag_half_elf_variant_sentences_are_known_differences():
    res = score_book("scag", [], FT, "Subrace")
    assert not any(m.startswith("Half-Elf Variant;") for m in res["missing"])
    assert sum(k.startswith("Half-Elf Variant;") for k in res["knownDifferences"]) == 4
    assert "Tiefling Variant; Infernal Legacy" not in res["missing"]


@needs_5etools
def test_scag_tiefling_variants_score_against_the_printed_options():
    ents = [_ent("Subrace", n, raceName="Tiefling") for n in ("Devil's Tongue", "Hellfire", "Winged")]
    ents.append(_ent("Subrace", "Feral", raceName="Tiefling", ability=[{"dex": 2, "int": 1}]))
    ents.append(_ent("Subrace", "Duergar", raceName="Dwarf", ability=[{"str": 1}]))
    res = score_book("scag", ents, FT, "Subrace")
    assert res["found"] == 3 and res["extra"] == 0, res
    assert res["diffs"] == [], res["diffs"]
    assert any(k.startswith("Tiefling Variant; Winged: ability") for k in res["knownDifferences"])
    assert any("Feral" in k for k in res["knownDifferences"])
    assert any("Dwarf Duergar" in k and "MTF" in k for k in res["knownDifferences"])



@needs_5etools
def test_mtf_asmodeus_prints_no_trait_block():
    res = score_book("mtf", [], FT, "Subrace")
    assert "Tiefling Asmodeus" not in res["missing"]
    assert any("not printed as a trait block" in k and "Asmodeus" in k for k in res["knownDifferences"])


@needs_5etools
def test_subclass_printed_elsewhere_is_scored_against_its_stored_record():
    t = next(r for r in json.loads((FT / "class" / "class-monk.json").read_text("utf-8"))["subclass"]
             if r["name"] == "Way of the Sun Soul" and r.get("classSource") == "PHB")
    good = [str(r).replace("|XGE|", "|SCAG|") for r in t["subclassFeatures"]]
    res = score_book("scag", [_ent("Subclass", "Way of the Sun Soul", className="Monk", shortName="Sun Soul",
                                   subclassFeatures=good)], FT, "Subclass")
    assert not any(d.startswith("Way of the Sun Soul") for d in res["diffs"]), res["diffs"]
    bad = [r.replace("Searing Arc Strike", "Searing Arc Spike") for r in good]
    res = score_book("scag", [_ent("Subclass", "Way of the Sun Soul", className="Monk", shortName="Sun Soul",
                                   subclassFeatures=bad)], FT, "Subclass")
    assert any(d.startswith("Way of the Sun Soul: features wrong") for d in res["diffs"]), res["diffs"]
    assert res["extra"] == 0 and res["truth"] == 6 and res["found"] == 0  # recall numbers unchanged


@needs_5etools
def test_subrace_printed_elsewhere_is_scored_against_its_stored_record():
    res = score_book("scag", [_ent("Subrace", "Duergar", raceName="Dwarf", ability=[{"str": 2}])], FT, "Subrace")
    assert any(d.startswith("Dwarf Duergar: ability wrong") for d in res["diffs"]), res["diffs"]


@needs_5etools
def test_subclass_short_name_keeps_its_spaces():   # a glued "Feywanderer" is not 5etools "Fey Wanderer"
    t = next(t for t in load_truth("tce", FT, "Subclass") if t["name"] == "Fey Wanderer")
    r, *_ = score_record("Subclass", {"className": "Ranger", "shortName": "Feywanderer"}, t)
    assert r["shortName"] is False
    r, *_ = score_record("Subclass", {"className": "Ranger", "shortName": "fey  wanderer"}, t)
    assert r["shortName"] is True


@needs_5etools
def test_subclass_text_scored_against_its_subclass_features():
    # 5etools keeps subclass prose in subclassFeature records; text bleed shows as low precision
    t = next(t for t in load_truth("xge", FT, "Subclass") if t["name"] == "Path of the Zealot")
    good = [{"type": "entries", "name": "Fanatical Focus", "entries": [
        "Starting at 6th level, the divine power that fuels your rage can protect you. If you fail a saving throw "
        "while you're raging, you can reroll it, and you must use the new roll. You can use this ability only "
        "once per rage."]}]
    _, cov, prec = score_record("Subclass", {"className": "Barbarian", "entries": good}, t)
    assert cov is not None and cov > 0.05 and prec > 0.9, (cov, prec)
    bleed = good + [{"type": "entries", "name": "Zealous Presence", "entries": [
        "This section presents two backgrounds that provide options linking characters to the ancient magic of "
        "giants. The giant foundling background represents a life spent around titanic creatures and their "
        "supernatural might. Skill Proficiencies: Intimidation, Survival. Languages: Giant and one other "
        "language of your choice. Equipment: A backpack, a set of traveler's clothes, a small stone or sprig "
        "that reminds you of home, and a pouch containing 10 gp."]}]
    _, _, prec2 = score_record("Subclass", {"className": "Barbarian", "entries": bleed}, t)
    assert prec2 < 0.7, prec2


def test_feature_names_keep_word_boundaries():   # a glued "Extraattack" is a misread of "Extra Attack"
    from bookextractor.score import feature_levels
    assert feature_levels(["Extraattack|Artificer|TCE|Armorer|TCE|5"], "TCE", 5, 6) != \
        feature_levels(["Extra Attack|Artificer|TCE|Armorer|TCE|5"], "TCE", 5, 6)
    assert feature_levels(["EXTRA attack|Artificer|TCE|Armorer|TCE|5"], "TCE", 5, 6) == \
        feature_levels(["Extra Attack|Artificer|TCE|Armorer|TCE|5"], "TCE", 5, 6)


@needs_5etools
def test_truth_phb_weapons():
    assert {t["name"] for t in load_truth("phb", FT, "Weapon")} >= {"Longsword", "Shortbow", "Net"}


@needs_5etools
def test_truth_phb_armor():
    assert len(load_truth("phb", FT, "Armor")) == 13


@needs_5etools
def test_truth_conditions():
    assert len(load_truth("phb", FT, "Condition")) == 15


@needs_5etools
def test_truth_dmg_poisons():
    assert "Burnt Othur Fumes" in {t["name"] for t in load_truth("dmg", FT, "DiseasePoison")}


@needs_5etools
def test_score_longsword():
    t = next(t for t in load_truth("phb", FT, "Weapon") if t["name"] == "Longsword")
    r, *_ = score_record("Weapon", {"value": 1500, "weight": 3, "dmg1": "1d8", "dmgType": "S", "property": ["V"],
                                    "weaponCategory": "martial"}, t)
    assert all(r.values())


@needs_5etools
def test_equipment_truth_never_double_counts_an_item():
    for book in ("phb", "dmg", "xge", "erlw", "scag"):
        seen: dict[str, str] = {}
        for ty in ("Weapon", "Armor", "Item", "DiseasePoison", "MagicItem"):
            for t in load_truth(book, FT, ty):
                assert seen.setdefault(t["name"], ty) == ty, (book, t["name"], seen[t["name"]], ty)


@needs_5etools
def test_items_leave_out_poisons_weapons_and_armor():
    names = {t["name"] for t in load_truth("phb", FT, "Item")}
    assert "Abacus" in names and not names & {"Longsword", "Plate Armor", "Basic Poison (vial)"}


@needs_5etools
def test_status_records_are_known_not_truth():
    from bookextractor.score import dropped_truth_lines
    assert "Surprised" not in {t["name"] for t in load_truth("phb", FT, "Condition")}
    assert any("Surprised" in line for line in dropped_truth_lines("phb", FT, "Condition"))


@needs_5etools
def test_available_types_lists_equipment():
    assert {"Weapon", "Armor", "Item", "Condition"} <= set(available_types("phb", FT))
    assert {"Trap", "VehicleMount", "Object", "DiseasePoison"} <= set(available_types("dmg", FT))


# ---- Phase 2b: equipment names printed differently from 5etools ----

def _eq(name, etype="Item", **fields):
    return {"type": etype, "name": name, "fields": fields}


def test_equipment_match_key_ignores_word_order_and_punctuation():
    from bookextractor.score import match_key
    for a, b in [("Crossbow, light", "Light Crossbow"), ("Rifle, hunting", "Hunting Rifle"),
                 ("Poison, basic (vial)", "Basic Poison (vial)"), ("Ale, gallon", "Ale (gallon)"),
                 ("Saddle, exotic", "Exotic Saddle"), ("Bread, loaf", "Loaf of Bread")]:
        et = "DiseasePoison" if "Poison" in a else "Weapon" if "Crossbow" in a or "Rifle" in a else "Item"
        assert match_key(et, a) == match_key(et, b), (a, b)
    assert match_key("Armor", "Studded leather") == match_key("Armor", "Studded Leather Armor")
    assert match_key("Trap", "Fiery Blast") == match_key("Trap", "Fiery Blast Trap")
    assert match_key("Item", "Plate Armor") != match_key("Item", "Plate")  # suffix drop only for Armor/Trap


@needs_5etools
def test_equipment_match_keys_do_not_collide_within_a_book():
    from bookextractor.score import _EQUIPMENT_TYPES, match_key
    for book in ("mm", "phb", "dmg", "mpmm", "mtf", "ftd", "bgg", "vrgr", "egw", "erlw", "ggr", "scc", "xge", "tce",
                 "scag", "eepc"):
        for ty in available_types(book, FT):
            if ty not in _EQUIPMENT_TYPES:
                continue
            names = [t["name"] for t in load_truth(book, FT, ty)]
            keys = [match_key(ty, n) for n in names]
            assert len(set(keys)) == len(keys), (book, ty)


@needs_5etools
def test_phb_inverted_labels_score_as_found():
    ents = [_eq("Crossbow, light", "Weapon"), _eq("Padded", "Armor"), _eq("Studded leather", "Armor")]
    assert score_book("phb", ents, FT, "Weapon")["missing"] == [n for n in score_book("phb", [], FT, "Weapon")["missing"]
                                                               if n != "Light Crossbow"]
    r = score_book("phb", ents, FT, "Armor")
    assert "Padded Armor" not in r["missing"] and "Studded Leather Armor" not in r["missing"]


@needs_5etools
def test_one_printed_row_can_cover_several_5etools_records():
    r = score_book("phb", [_eq("Donkey or mule"), _eq("1 lb. of iron or 1 sq. yd. of canvas"),
                           _eq("1 lb. of cinnamon or pepper, or one sheep")], FT, "Item")
    for n in ("Donkey", "Mule", "Iron", "Canvas (1 sq. yd.)", "Cinnamon", "Pepper", "Sheep"):
        assert n not in r["missing"], n
    assert r["extra"] == 0
    assert any("Donkey or mule" in k and "Mule" in k for k in r["knownDifferences"])


@needs_5etools
def test_dmg_ships_are_vehicle_mount_truth_not_items():
    assert {"Airship", "Galley", "Sailing Ship", "Apparatus of Kwalish"} <= {
        t["name"] for t in load_truth("dmg", FT, "VehicleMount")}
    assert len(load_truth("dmg", FT, "VehicleMount")) == 8
    assert "Galley" not in {t["name"] for t in load_truth("dmg", FT, "Item")}
    assert any("Galley" in k for k in dropped_truth_lines("dmg", FT, "Item"))
    assert any("stored by 5etools as items" in k for k in dropped_truth_lines("dmg", FT, "VehicleMount"))


@needs_5etools
def test_equipment_known_differences_leave_truth_but_stay_listed():
    from bookextractor.score import dropped_truth_lines as dl
    assert "Eldritch Cannon, Flamethrower" not in {t["name"] for t in load_truth("tce", FT, "Object")}
    assert sum("Eldritch Cannon" in k for k in dl("tce", FT, "Object")) == 3
    assert "Generic Object" not in {t["name"] for t in load_truth("dmg", FT, "Object")}
    assert any("Generic Object" in k for k in dl("dmg", FT, "Object"))
    assert "Improvised Damage" not in {t["name"] for t in load_truth("dmg", FT, "Trap")}
    assert "High Altitude" in {t["name"] for t in load_truth("dmg", FT, "Trap")}  # printed as a hazard heading
    names = {t["name"] for t in load_truth("phb", FT, "Item")}
    assert "Arrow" not in names and "Trinket" not in names and "Arrows (20)" in names
    assert any("Arrow" in k for k in dl("phb", FT, "Item"))
    assert any("Trinket" in k for k in dl("phb", FT, "Item"))


def _item(name, text, fields, failed=()):
    e = _eq(name, **fields)
    e["canonicalText"] = text
    e["checks"] = [{"name": c, "passed": False, "reason": "x"} for c in failed]
    return e


@needs_5etools
def test_unprinted_item_weight_is_skipped_and_counted_not_wrong():
    ents = [_item("Abacus", "Abacus\nCost: 2 gp", {"value": 200, "type": "G"})]  # the text shows no weight cell
    r = score_book("phb", ents, FT, "Item")
    assert "weight" not in r["fields"] and r["skipped"]["weight"] == 1
    ents = [_item("Abacus", "Abacus\nCost: 2 gp\nWeight: 2 lb.", {"value": 200, "weight": 99, "type": "G"})]
    assert score_book("phb", ents, FT, "Item")["fields"]["weight"]["pct"] == 0


@needs_5etools
def test_a_weight_cell_we_could_not_read_is_wrong_not_skipped():
    ents = [_item("Abacus", "Abacus\nCost: 2 gp\nWeight: 21b.", {"value": 200, "type": "G"}, ["weight-valid"])]
    r = score_book("phb", ents, FT, "Item")
    assert r["fields"]["weight"] == {"pct": 0.0, "n": 1} and "weight" not in r["skipped"]
    ents = [_item("Abacus", "Abacus\nCost: 2 gp\nWeight: 2 lb.", {"value": 200, "type": "G"})]  # text shows it
    assert score_book("phb", ents, FT, "Item")["fields"]["weight"]["pct"] == 0


@needs_5etools
def test_tack_and_vehicle_type_is_scored_even_when_omitted():
    ents = [_item("Cart", "Cart\nCost: 15 gp\nWeight: 200 lb.", {"value": 1500, "weight": 200})]
    r = score_book("phb", ents, FT, "Item")
    assert r["fields"]["type"] == {"pct": 0.0, "n": 1} and "type" not in r["skipped"]


@needs_5etools
def test_a_garbled_or_unrelated_row_claims_no_truth():
    r = score_book("phb", [_eq("Pouch, qwzx garbled"), _eq("Torch or something unrelated entirely")], FT, "Item")
    assert "Pouch" in r["missing"] and "Torch" in r["missing"]
    assert r["extra"] == 2


@needs_5etools
def test_every_phb_or_row_still_matches():
    rows = ["Flask or tankard", "Jug or pitcher", "Donkey or mule", "1 lb. of flour or one chicken",
            "1 lb. of iron or 1 sq. yd. of canvas", "1 lb. of copper or 1 sq. yd. of cotton cloth",
            "1 lb. of ginger or one goat", "1 lb. of cinnamon or pepper, or one sheep", "1 lb. of cloves or one pig",
            "1 lb. of silver or 1 sq. yd. of linen", "1 sq. yd. of silk or one cow", "1 lb. of saffron or one ox",
            "1 lb. of wheat", "1 lb. of salt", "1 lb. of gold", "1 lb. of platinum"]
    r = score_book("phb", [_eq(n) for n in rows], FT, "Item")
    assert r["extra"] == 0
    for n in ("Flask", "Tankard", "Jug", "Pitcher", "Donkey", "Mule", "Flour", "Chicken", "Iron", "Canvas (1 sq. yd.)",
              "Pepper", "Sheep", "Salt", "Wheat", "Gold", "Platinum"):
        assert n not in r["missing"], n


@needs_5etools
def test_a_type_whose_truth_is_all_known_differences_stays_available():
    assert "Item" in available_types("mtf", FT)
    r = score_book("mtf", [], FT, "Item")
    assert r["truth"] == 0 and any("Trinket" in k for k in r["knownDifferences"])


@needs_5etools
def test_p19_known_differences_and_ocr_lost_names():
    from bookextractor.score import dropped_truth_lines as dl
    names = {t["name"] for t in load_truth("vrgr", FT, "Item")}
    assert "Monster Hunter's Pack" not in names and any("Monster Hunter" in k for k in dl("vrgr", FT, "Item"))
    assert {"Pride Silk", "Pride Silk Outfit", "Skyship"}.isdisjoint({t["name"] for t in load_truth("egw", FT, "Item")})
    assert "Skyship" in {t["name"] for t in load_truth("egw", FT, "VehicleMount")}
    r = score_book("dmg", [], FT, "VehicleMount")
    assert "Sailing Ship" in r["missing"] and any("Sailing Ship" in k and "lost in OCR" in k
                                                 for k in r["knownDifferences"])
    assert "Electrum 50-Zib Coin" in score_book("ggr", [], FT, "Item")["missing"]
