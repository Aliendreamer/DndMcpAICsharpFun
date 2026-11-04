import json
import subprocess
import sys

from bookextractor.score import base_item_name, score_book, untag


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
    assert res["fields"]["rarity"]["pct"] == 100
