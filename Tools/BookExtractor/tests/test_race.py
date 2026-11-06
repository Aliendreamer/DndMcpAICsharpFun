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
