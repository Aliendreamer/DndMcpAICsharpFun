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
