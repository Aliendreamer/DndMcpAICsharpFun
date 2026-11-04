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
