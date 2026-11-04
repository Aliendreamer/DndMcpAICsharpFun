import json
from pathlib import Path

import pytest

from bookextractor.checks import hp_dice_check
from bookextractor.layout import reading_order
from bookextractor.model import load_ocr_page
from bookextractor.parsers.monster import monster_checks, parse_monsters

FIX = Path(__file__).parent / "fixtures" / "ocr" / "mm"
SCHEMA = Path(__file__).parents[3] / "Schemas" / "canonical" / "MonsterFields.schema.json"


def lines(n):
    return reading_order(load_ocr_page(FIX / f"{n:03d}.json", n))


def by_name(parsed, name):
    return next(p for p in parsed if p.name == name)


def test_goblin():
    g = by_name(parse_monsters(lines(167)), "Goblin")
    f = g.fields
    assert f["size"] == ["S"] and f["type"] == {"type": "humanoid", "tags": ["goblinoid"]}
    assert f["alignment"] == ["N", "E"]
    assert f["ac"] == [{"ac": 15, "from": ["leather armor", "shield"]}]
    assert f["hp"] == {"average": 7, "formula": "2d6"}
    assert f["speed"] == {"walk": 30}
    assert (f["str"], f["dex"], f["con"], f["int"], f["wis"], f["cha"]) == (8, 14, 10, 10, 8, 8)
    assert f["cr"] == "1/4"
    assert f["skill"] == {"stealth": "+6"}
    assert f["senses"] == ["darkvision 60 ft."] and f["passive"] == 9
    assert f["languages"] == ["Common", "Goblin"]
    assert [t["name"] for t in f["trait"]] == ["Nimble Escape"]
    assert [a["name"] for a in f["action"]] == ["Scimitar", "Shortbow"]
    assert all(c.passed for c in g.checks), [c for c in g.checks if not c.passed]
    assert all(l.page == 167 for l in g.lines)


def test_goblin_boss_on_same_page():
    parsed = parse_monsters(lines(167))
    assert [p.name for p in parsed] == ["Goblin", "Goblin Boss"]
    boss = parsed[1]
    assert boss.fields["hp"]["average"] == 21 and boss.fields["cr"] == "1"
    assert [r["name"] for r in boss.fields["reaction"]] == ["Redirect Attack"]
    assert all(c.passed for c in boss.checks)


def test_goblin_fields_match_schema():
    jsonschema = pytest.importorskip("jsonschema")
    schema = json.loads(SCHEMA.read_text(encoding="utf-8"))
    for p in parse_monsters(lines(167)):
        jsonschema.validate(p.fields, schema)


def test_clay_golem_hp_mismatch():
    g = by_name(parse_monsters(lines(169)), "Clay Golem")
    hp = next(c for c in g.checks if c.name == "hp-matches-dice")
    assert not hp.passed and "133" in hp.reason and "14d70" in hp.reason
    mods = next(c for c in g.checks if c.name == "ability-modifiers")
    assert not mods.passed  # OCR read 18 (+4) as 78 (+4)


def test_name_never_guessed():
    ls = [l for l in lines(167)[:15] if l.text != "GOBLIN"]
    parsed = parse_monsters(ls)
    assert len(parsed) == 1 and parsed[0].name == ""


def test_no_previous_page_name_fallback():
    # Same stat block without its name line, preceded by a line from another page.
    ls = [l for l in lines(167)[:15] if l.text != "GOBLIN"]
    other = lines(169)[:1]
    assert parse_monsters(other + ls)[0].name == ""


def test_faerie_dragon_variants():
    parsed = [p for p in parse_monsters(lines(134)) if p.variant_of]
    assert [p.name for p in parsed] == [f"Faerie Dragon ({c})" for c in
                                        ("Red", "Orange", "Yellow", "Green", "Blue", "Indigo", "Violet")]
    assert {p.variant_of for p in parsed} == {"Faerie Dragon"}
    assert [p.fields["cr"] for p in parsed] == ["1", "1", "1", "2", "2", "2", "2"]
    assert all(c.passed for p in parsed for c in p.checks if c.name == "xp-matches-cr")


def test_lizard_king_queen():
    parsed = parse_monsters(lines(206))
    assert [p.name for p in parsed] == ["Lizardfolk Shaman", "Lizard King/Queen"]
    k = parsed[1]
    assert k.fields["hp"] == {"average": 78, "formula": "12d8 + 24"} and k.fields["cr"] == "4"
    assert k.fields["save"] == {"con": "+4", "wis": "+2"}
    assert k.fields["conditionImmune"] == ["frightened"]
    assert all(c.passed for c in k.checks), [c for c in k.checks if not c.passed]


def test_ability_row_split_across_two_lines():
    ls = lines(167)
    i = next(n for n, l in enumerate(ls) if l.text.startswith("8 (-1) 14"))
    row = ls[i]
    a, b = row.text.split(" 10 (+0) ", 1)
    first = type(row)(a + " 10 (+0)", row.box, row.height, row.page, row.frags)
    second = type(row)(b, row.box, row.height, row.page, row.frags)
    g = by_name(parse_monsters(ls[:i] + [first, second] + ls[i + 1:]), "Goblin")
    assert (g.fields["str"], g.fields["dex"], g.fields["cha"]) == (8, 14, 8)
    assert all(c.passed for c in g.checks)


@pytest.mark.parametrize("avg,formula,ok", [(7, "2d6", True), (133, "14d10+56", True), (133, "14d10 + 56", True),
                                            (53, "8d8+16", False), (52, "8d8+16", True)])
def test_hp_formula_math(avg, formula, ok):
    assert hp_dice_check(avg, formula).passed is ok


def test_check_names():
    g = parse_monsters(lines(167))[0]
    assert [c.name for c in monster_checks(g)] == [
        "abilities-present", "ac-present", "hp-present", "cr-present", "speed-present", "hp-matches-dice",
        "cr-legal", "xp-matches-cr", "ability-modifiers"]


def test_ability_modifier_sign_is_checked():
    g = by_name(parse_monsters(lines(167)), "Goblin")
    assert next(c for c in g.checks if c.name == "ability-modifiers").passed
    assert g.fields["str"] == 8  # (-1) is read as negative, not as an unsigned magnitude


def _goblin_with_row(row_text):
    ls = lines(167)
    i = next(n for n, l in enumerate(ls) if l.text.startswith("8 (-1) 14"))
    ls[i] = type(ls[i])(row_text, ls[i].box, ls[i].height, ls[i].page, ls[i].frags)
    return by_name(parse_monsters(ls), "Goblin")


def test_ability_leading_one_read_as_seven_is_repaired():
    g = _goblin_with_row("73 (+1) 14 (+2) 10 (+0) 10 (+0) 8 (-1) 8 (-1)")
    assert g.fields["str"] == 13
    rep = [c for c in g.checks if c.name == "ability-repaired"]
    assert len(rep) == 1 and rep[0].passed and "STR read 73 (+1), repaired to 13" in rep[0].reason
    assert next(c for c in g.checks if c.name == "ability-modifiers").passed


def test_ability_uncorroborated_misread_is_not_repaired():
    g = _goblin_with_row("73 (+6) 14 (+2) 10 (+0) 10 (+0) 8 (-1) 8 (-1)")
    assert g.fields["str"] == 73
    assert not any(c.name == "ability-repaired" for c in g.checks)
    assert not next(c for c in g.checks if c.name == "ability-modifiers").passed


def test_swarm_type_is_schema_valid():
    ps = parse_monsters(lines(338))
    assert any(p.name == "Swarm of Bats" for p in ps)
    assert next(p for p in ps if p.name == "Swarm of Bats").fields["type"] == "beast"


def test_every_fixture_monster_matches_schema():
    jsonschema = pytest.importorskip("jsonschema")
    schema = json.loads(SCHEMA.read_text(encoding="utf-8"))
    n = 0
    for page in (167, 169, 134, 206, 338, 50):
        for p in parse_monsters(lines(page)):
            jsonschema.validate(p.fields, schema)
            n += 1
    assert n >= 10


def _abilities(g):
    return [g.fields.get(k) for k in ("str", "dex", "con", "int", "wis", "cha")]


def _check(g, name):
    return next(c for c in g.checks if c.name == name)


def test_mm_azer_score_with_junk_leading_glyph_is_repaired_from_its_modifier():
    # printed "17 (+3)", OCR "$7+3)": the only score ending in 7 with modifier +3 is 17
    g = _goblin_with_row("$7+3) 12 (+1) 15 (+2) 12 (+1) 13 (+1) 10 (+0)")
    assert _abilities(g) == [17, 12, 15, 12, 13, 10]
    rep = _check(g, "ability-repaired")
    assert rep.passed and "STR" in rep.reason and "17" in rep.reason
    assert _check(g, "ability-modifiers").passed and _check(g, "abilities-present").passed


def test_mm_swarm_of_bats_score_with_junk_around_it_is_repaired():
    g = _goblin_with_row("5 (-3) 15 (+2) 10 (+0) 2 (-4) 12 (+1) .4:(-3)")
    assert _abilities(g) == [5, 15, 10, 2, 12, 4]
    assert _check(g, "ability-modifiers").passed


def test_mm_drow_priestess_period_for_space_before_modifier():
    g = _goblin_with_row("10.(+0) 14 (+2) 12 (+1) 13 (+1) 17 (+3) 18 (+4)")
    assert _abilities(g) == [10, 14, 12, 13, 17, 18]
    assert _check(g, "ability-modifiers").passed


def test_mm_jackal_comma_between_score_and_modifier():
    g = _goblin_with_row("8 (-1) 15 (+2) 11, (+0) 3 (-4) 12 (+1) 6 (-2)")
    assert _abilities(g) == [8, 15, 11, 3, 12, 6]
    assert _check(g, "abilities-present").passed


def test_mm_goristro_one_read_as_seven_in_score_and_modifier():
    # printed "13 (+1)", OCR "73 (+7)": 73 is impossible, and 7 -> 1 in both gives a consistent 13 (+1)
    g = _goblin_with_row("25 (+7) 11 (+0) 25 (+7) 6 (-2) 73 (+7) 14 (+2)")
    assert _abilities(g) == [25, 11, 25, 6, 13, 14]
    assert "WIS read 73 (+7), repaired to 13 (+1)" in _check(g, "ability-repaired").reason
    assert _check(g, "ability-modifiers").passed


@pytest.mark.parametrize("row", [
    "12 (+1) 17 (+3) 12 (+1) 10 (+0) 11 (+0)",                      # Blink Dog: a whole pair dropped
    "12 (+1) 13 (+1) 11 (+0) 2 (-4) 10 (+0) 3 (5.4)",              # Giant Frog: unreadable modifier
    "19 (+4) 10 (+0) 13 (+1) 3 (-4) 12 (+1) 7)",                    # Killer Whale
    "3 (-4) 13 (+1) 8 (-1) 2(-4) 12 (+1) 7.$(2)",                   # Owl
    "5 (-3) 15 (+2) 10 (+0) 2 (-4) 12 (+1) .4:(+9)",                # junk score with a modifier no score fits
])
def test_mm_unreadable_rows_are_flagged_not_guessed(row):
    g = _goblin_with_row(row)
    assert not (_check(g, "abilities-present").passed and _check(g, "ability-modifiers").passed)


def test_mm_orc_eye_of_gruumsh_con_is_what_the_book_prints():
    # the printed row is "16 (+3)" for CON; 5etools lists 17 (a known book-vs-5etools difference)
    g = _goblin_with_row("16 (+3) 12 (+1) 16 (+3) 9 (-1) 13 (+1) 12 (+1)")
    assert _abilities(g) == [16, 12, 16, 9, 13, 12]
