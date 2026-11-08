from bookextractor import fivetools_fix as F


def test_untag_reduces_markup_to_display_text():
    assert F.untag("{@atk mw} {@hit 4} to hit. {@h}4 ({@damage 1d4 + 2}) slashing") == \
        "Melee Weapon Attack: +4 to hit. Hit: 4 (1d4 + 2) slashing"
    assert F.untag("an {@creature air elemental|MM|elemental} and {@condition prone}") == "an elemental and prone"
    assert F.untag("{@dc 13} save, {@recharge 5}") == "DC 13 save, (Recharge 5-6)"
    assert F.untag("{@b {@i nested}}") == "nested"


def test_monster_text_and_fields():
    rec = {"name": "Imp", "source": "MM", "size": ["T"], "type": {"type": "fiend", "tags": ["devil"]},
           "alignment": ["L", "E"], "ac": [{"ac": 13, "from": ["natural armor"]}],
           "hp": {"average": 10, "formula": "3d4 + 3"}, "speed": {"walk": 20, "fly": {"number": 40, "condition": "(hover)"}},
           "str": 6, "dex": 17, "con": 13, "int": 11, "wis": 12, "cha": 14, "passive": 11, "cr": "1",
           "action": [{"name": "Sting", "entries": ["{@atk mw} {@hit 5} to hit. {@h}5 ({@damage 1d4 + 3})"]}]}
    schema = {"properties": {k: {} for k in ("size", "type", "alignment", "ac", "hp", "speed", "str", "dex", "con", "int",
                                             "wis", "cha", "passive", "cr", "action", "trait")}}
    f = F.map_fields("Monster", rec, {"trait": [{"name": "stale"}]}, schema)
    assert f["speed"] == {"walk": 20, "fly": 40, "canHover": True}
    assert f["trait"] is None  # ours has a trait 5etools does not
    text = F.render_monster("Imp", f)
    assert text.splitlines()[:5] == ["Imp", "Tiny fiend (devil), lawful evil", "Armor Class 13 (natural armor)",
                                     "Hit Points 10 (3d4 + 3)", "Speed 20 ft., fly 40 ft. (hover)"]
    assert "Challenge 1 (200 XP)" in text and "Sting. Melee Weapon Attack: +5 to hit. Hit: 5 (1d4 + 3)" in text
