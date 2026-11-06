import json
from functools import lru_cache
from pathlib import Path

import jsonschema

from bookextractor.layout import reading_order
from bookextractor.model import load_ocr_page
from bookextractor.pages import classify_page
from bookextractor.parsers import ParseContext
from bookextractor.parsers.race import parse_races, race_checks

FIX = Path(__file__).parent / "fixtures" / "ocr"
SCHEMAS = Path(__file__).parents[3] / "Schemas" / "canonical"
BOOKS = {
    "phb": ("phb14", "PHB", [19, 20, 21, 22]),
    "mpmm": ("mpmm", "MPMM", [5, 21, 22]),
    "mtf": ("mtf", "MTF", [22, 23, 24, 62, 63, 64, 82]),
    "erlw": ("erlw", "ERLW", [42, 44]),
    "dmg": ("dmg14", "DMG", [282, 285, 286, 287]),
    "tce": ("tce", "TCE", [8, 9]),
}


def _squash(s):
    return "".join(c for c in s.lower() if c.isalpha())


@lru_cache(maxsize=None)
def parse(book="phb"):
    slug, source, ns = BOOKS[book]
    pages = {n: load_ocr_page(FIX / book / f"{n:03d}.json", n) for n in ns}
    body = [l for n in sorted(pages) if classify_page(pages[n]) in ("rules", "art") for l in reading_order(pages[n])]
    return tuple(parse_races(body, ParseContext(slug=slug, source=source, pages=pages)))


def get(name, etype, book="phb"):
    return next((p for p in parse(book) if p.type == etype and _squash(p.name) == _squash(name)), None)


def names(etype, book="phb"):
    return [p.name for p in parse(book) if p.type == etype]


def failed(p):
    return [c for c in p.checks if not c.passed]


def check(checks, name):
    return next(c for c in checks if c.name == name)


def schema(name):
    return json.loads((SCHEMAS / f"{name}.schema.json").read_text(encoding="utf-8"))


def test_dwarf():
    d = get("Dwarf", "Race")
    assert d.fields["size"] == ["M"] and d.fields["speed"] == 25 and d.fields["darkvision"] == 60
    assert d.fields["ability"] == [{"con": 2}] and not failed(d)


def test_hill_dwarf():
    h = get("Hill Dwarf", "Subrace")
    assert h.fields["raceName"] == "Dwarf" and h.fields["ability"] == [{"wis": 1}] and not failed(h)


def test_lore_heading_is_not_a_subrace():
    assert "Dwarf Names" not in names("Subrace")


def test_mpmm_goliath():
    assert get("Goliath", "Race", book="mpmm").fields["size"] == ["M"]


def test_speed_not_multiple_of_5():
    c = check(race_checks({"ability": [{"con": 2}], "size": ["M"], "speed": 27}, False, set()), "speed-valid")
    assert not c.passed and "27" in c.reason


def test_race_schemas():
    jsonschema.validate(get("Dwarf", "Race").fields, schema("RaceFields"))
    jsonschema.validate(get("Hill Dwarf", "Subrace").fields, schema("SubraceFields"))


# ---------- added ----------
def test_phb_dwarf_page_reads_exactly_one_race_and_two_subraces():
    assert names("Race") == ["Dwarf"]
    assert names("Subrace") == ["Hill Dwarf", "Mountain Dwarf"]


def test_mountain_dwarf_stops_before_the_duergar_sidebar():
    m = get("Mountain Dwarf", "Subrace")
    assert m.fields["ability"] == [{"str": 2}] and m.fields["raceSource"] == "PHB"
    assert "Underdark" not in m.text and not failed(m)


def test_dwarf_text_stops_at_its_first_subrace():
    d = get("Dwarf", "Race")
    assert "Stonecunning" in d.text and "Hill Dwarf" not in d.text
    assert "Ability Score Increase" not in [e["name"] for e in d.fields["entries"]]


def test_mpmm_goliath_ability_is_chosen_by_the_book_rule():
    g = get("Goliath", "Race", book="mpmm")
    assert g.fields["ability"] is None and g.fields["speed"] == 30 and g.fields.get("darkvision") is None
    assert not failed(g), failed(g)
    jsonschema.validate(g.fields, schema("RaceFields"))


def test_mpmm_reads_two_races_and_no_subrace():
    assert names("Race", book="mpmm") == ["Goliath", "Harengon"] and names("Subrace", book="mpmm") == []


def test_ability_not_read_fails_with_the_printed_text():
    c = check(race_checks({"ability": None, "size": ["M"], "speed": 30}, False, set(),
                          asi_text="Your Strength score goes up a lot."), "ability-increase-parsed")
    assert not c.passed and "goes up a lot" in c.reason


def test_no_ability_trait_and_no_book_rule_fails():
    c = check(race_checks({"ability": None, "size": ["M"], "speed": 30}, False, set()), "ability-increase-parsed")
    assert not c.passed


def test_size_invalid_names_the_value():
    c = check(race_checks({"ability": [{"con": 2}], "size": None, "speed": 30}, False, set()), "size-valid")
    assert not c.passed and "None" in c.reason


def test_fly_speed_shape_is_valid():
    c = check(race_checks({"ability": [{"dex": 2}], "size": ["M"], "speed": {"walk": 25, "fly": 50}}, False, set()),
              "speed-valid")
    assert c.passed


def test_parent_race_unknown():
    cs = race_checks({"ability": [{"wis": 1}], "raceName": "Dwurf"}, True, {"Dwarf"})
    c = check(cs, "parent-race-known")
    assert not c.passed and "Dwurf" in c.reason
    # a subrace prints no size; its speed is checked only when it changes the race's
    assert {c.name for c in cs} == {"ability-increase-parsed", "parent-race-known"}


def test_later_books_phrase_the_chosen_increase_rule_differently():
    vrgr = ("When determining your ability scores, you increase one of those scores by 2 and increase a different "
            "score by 1, or you increase three different scores by 1.")
    c = check(race_checks({"ability": None, "size": ["M"], "speed": 30}, False, set(), asi_text=vrgr),
              "ability-increase-parsed")
    assert c.passed


def test_ability_amount_out_of_range_fails_naming_the_value():
    for v in (7, 21):
        c = check(race_checks({"ability": [{"con": v}], "size": ["M"], "speed": 30}, False, set()),
                  "ability-increase-parsed")
        assert not c.passed and str(v) in c.reason
    c = check(race_checks({"ability": [{"choose": {"from": ["str"], "count": 4, "amount": 3}}], "size": ["M"],
                           "speed": 30}, False, set()), "ability-increase-parsed")
    assert not c.passed and "4" in c.reason and "3" in c.reason


def test_ocr_merged_amount_is_read_but_fails_the_check():
    from bookextractor.parsers.race import _ability
    assert _ability("Your Constitution score increases by 7.") == [{"con": 7}]
    c = check(race_checks({"ability": [{"con": 7}], "size": ["M"], "speed": 30}, False, set()),
              "ability-increase-parsed")
    assert not c.passed


def test_speed_modes_after_an_options_trait_still_count():
    from bookextractor.parsers.race import _modes
    traits = [("Speed", "Your base walking speed is 30 feet."),
              ("Enhancement", "Choose one of the following options: you have a climbing speed equal to your "
                              "walking speed."),
              ("Wings", "You have a flying speed equal to your walking speed."),
              ("Fins", "You have a swimming speed of 30 feet.")]
    assert _modes(traits) == {"fly": True, "swim": 30}


# ---------- fix round 9d ----------
TIEFLING_SUBRACES = {"Baalzebul": {"int": 1, "cha": 2}, "Dispater": {"dex": 1, "cha": 2},
                     "Fierna": {"wis": 1, "cha": 2}, "Glasya": {"dex": 1, "cha": 2},
                     "Levistus": {"con": 1, "cha": 2}, "Mammon": {"int": 1, "cha": 2},
                     "Mephistopheles": {"int": 1, "cha": 2}, "Zariel": {"str": 1, "cha": 2}}


def test_mtf_tiefling_subraces_take_their_race_from_the_enclosing_subraces_heading():
    for name, ability in TIEFLING_SUBRACES.items():
        s = get(name, "Subrace", book="mtf")
        assert s is not None, name
        assert s.fields["raceName"] == "Tiefling" and s.fields["raceSource"] == "PHB", (name, s.fields["raceName"])
        assert s.fields["ability"] == [ability] and not failed(s), (name, failed(s))


def test_mtf_asmodeus_prints_no_traits_so_it_is_not_a_subrace():
    assert "Asmodeus" not in names("Subrace", book="mtf")


def test_mtf_elf_subraces_take_their_race_from_the_enclosing_subraces_heading():
    for name, ability in {"Eladrin": {"cha": 1}, "Sea Elf": {"con": 1}, "Shadar-Kai": {"con": 1}}.items():
        s = get(name, "Subrace", book="mtf")
        assert s is not None, name
        assert s.fields["raceName"] == "Elf" and s.fields["ability"] == [ability] and not failed(s), name


def test_mtf_duergar_prints_no_parent_race_heading_and_stays_flagged():
    d = get("Duergar", "Subrace", book="mtf")
    assert d.fields["raceName"] is None
    assert not check(d.checks, "parent-race-known").passed


def test_erlw_mark_of_finding_is_one_subrace_per_printed_race():
    marks = [p for p in parse("erlw") if p.type == "Subrace" and p.name == "Mark of Finding"]
    assert sorted(p.fields["raceName"] for p in marks) == ["Half-Orc", "Human"]
    for p in marks:
        assert p.fields["ability"] == [{"con": 1, "wis": 2}] and not failed(p), failed(p)
        jsonschema.validate(p.fields, schema("SubraceFields"))


def test_erlw_mark_of_healing_reads_past_an_art_credit_inside_the_ability_line():
    h = get("Mark of Healing", "Subrace", book="erlw")
    assert h.fields["raceName"] == "Halfling" and h.fields["ability"] == [{"wis": 1}] and not failed(h), failed(h)


def test_dmg_eladrin_example_subrace_names_its_race_in_print():
    e = get("Eladrin", "Subrace", book="dmg")
    assert e.fields["raceName"] == "Elf" and e.fields["ability"] == [{"int": 1}] and not failed(e), failed(e)
    assert "speed" not in e.fields and "Aasimar" not in e.text


def test_dmg_example_race_takes_its_name_from_the_example_race_heading():
    assert not [p for p in parse("dmg") if p.type == "Race" and not p.name and p.lines[0].page != 282]
    a = get("Aasimar", "Race", book="dmg")
    assert a.fields["ability"] == [{"wis": 1, "cha": 2}] and a.fields["size"] == ["M"]
    assert a.fields["speed"] == 30 and a.fields["darkvision"] == 60 and not failed(a), failed(a)


def test_tce_custom_lineage_is_a_race_named_by_its_heading():
    c = get("Custom Lineage", "Race", book="tce")
    assert c is not None
    assert c.fields["size"] == ["S", "M"] and c.fields["speed"] == 30
    assert c.fields["ability"] == [{"choose": {"from": ["str", "dex", "con", "int", "wis", "cha"], "count": 1,
                                                "amount": 2}}]
    assert not failed(c), failed(c)
    assert "high elf" not in c.text and "Languages" in [e["name"] for e in c.fields["entries"]]
    jsonschema.validate(c.fields, schema("RaceFields"))


def _npc(name):
    return next((p for p in parse("dmg") if p.type == "Race" and p.lines and p.lines[0].page == 282
                 and _squash(p.name) == _squash(name)), None)


def test_dmg_npc_features_table_gives_one_race_per_monster_row():
    rows = [p for p in parse("dmg") if p.type == "Race" and p.lines and p.lines[0].page == 282]
    got = {_squash(p.name) for p in rows}
    for n in ("Aarakocra", "Bullywug", "Gnoll", "Gnome, deep", "Goblin", "Grimlock", "Hobgoblin", "Kenku",
              "Kobold", "Kuo-toa", "Lizardfolk", "Merfolk", "Orc", "Skeleton", "Zombie"):
        assert _squash(n) in got, n
    # rows marked * point at the Player's Handbook race, which this table does not define
    for n in ("Dragonborn", "Drow", "Dwarf", "Half-elf", "Half-orc", "Halfling", "Tiefling", "Gnome"):
        assert _squash(n) not in got, n
    for p in rows:
        jsonschema.validate(p.fields, schema("RaceFields"))


def test_dmg_npc_row_fields_are_read_from_the_printed_cells():
    a = _npc("Aarakocra")
    assert a.fields["ability"] == [{"dex": 2, "wis": 2}] and a.fields["speed"] == {"walk": 20, "fly": 50}
    k = _npc("Kobold")
    assert k.fields["ability"] == [{"str": -4, "dex": 2}] and k.fields["size"] == ["S"] and k.fields["darkvision"] == 60
    z = _npc("Zombie")
    assert z.fields["ability"] == [{"str": 1, "con": 2, "int": -6, "wis": -4, "cha": -4}]
    d = _npc("Gnome, deep")
    assert d.fields["size"] == ["S"] and d.fields["speed"] == 20 and d.fields["darkvision"] == 120 and not failed(d)


def test_dmg_npc_row_none_modifiers_pass_and_unprinted_size_fails():
    h = _npc("Hobgoblin")
    assert h.fields["ability"] is None and check(h.checks, "ability-increase-parsed").passed
    g = _npc("Gnoll")  # the table prints no size or walking speed for a Medium race: never assumed
    assert g.fields["size"] is None and not check(g.checks, "size-valid").passed
    assert not check(g.checks, "speed-valid").passed


def test_dmg_npc_row_with_no_printed_name_is_emitted_nameless():
    t = [p for p in parse("dmg") if p.type == "Race" and p.lines and p.lines[0].page == 282 and not p.name]
    assert len(t) == 1 and t[0].fields["ability"] == [{"str": 2, "con": 2, "int": -4, "cha": -4}]
