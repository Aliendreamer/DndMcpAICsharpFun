import json
from functools import lru_cache
from pathlib import Path

import jsonschema

from bookextractor.layout import reading_order
from bookextractor.model import load_ocr_page
from bookextractor.pages import classify_page
from bookextractor.parsers import ParseContext
from bookextractor.parsers.feat import feat_checks, parse_feats, parse_prerequisite

FIX = Path(__file__).parent / "fixtures" / "ocr"
SCHEMAS = Path(__file__).parents[3] / "Schemas" / "canonical"
BOOKS = {
    "phb": ("phb14", "PHB", "phb", [166, 167, 168]),
    "xge": ("xge", "XGE", "xge", [74, 75]),
    "phb_fighter_pages": ("phb14", "PHB", "phb", [71, 72, 73]),
}


def _squash(s):
    return "".join(c for c in s.lower() if c.isalpha())


@lru_cache(maxsize=None)
def parse(book="phb"):
    slug, source, folder, ns = BOOKS[book]
    pages = {n: load_ocr_page(FIX / folder / f"{n:03d}.json", n) for n in ns}
    body = [l for n in sorted(pages) if classify_page(pages[n]) in ("rules", "art") for l in reading_order(pages[n])]
    return tuple(parse_feats(body, ParseContext(slug=slug, source=source, pages=pages)))


def get(name, book="phb"):
    return next((p for p in parse(book) if _squash(p.name) == _squash(name)), None)


def names(book="phb"):
    return [p.name for p in parse(book)]


def failed(p):
    return [c for c in p.checks if not c.passed]


def check(checks, name):
    return next(c for c in checks if c.name == name)


def schema(name):
    return json.loads((SCHEMAS / f"{name}.schema.json").read_text(encoding="utf-8"))


def _bullets(p):
    return [i for e in p.fields["entries"] if isinstance(e, dict) and e.get("type") == "list" for i in e["items"]]


def test_heavily_armored():
    f = get("Heavily Armored")
    assert f.type == "Feat"
    assert f.fields["prerequisite"] == [{"proficiency": [{"armor": "medium"}]}]
    assert f.fields["ability"] == [{"str": 1}]
    assert any("Strength" in g for g in _bullets(f)) and not failed(f)


def test_feat_without_prerequisite():
    a = get("Alert")
    assert a.fields.get("prerequisite") is None and len(_bullets(a)) >= 2 and not failed(a)


def test_ability_prerequisite():
    assert get("Defensive Duelist").fields["prerequisite"] == [{"ability": [{"dex": 13}]}]
    assert get("Elemental Adept").fields["prerequisite"] == [{"spellcasting": True}]


def test_choice_of_ability_increase():
    assert get("Athlete").fields["ability"] == [{"choose": {"from": ["str", "dex"], "amount": 1}}]


def test_xge_racial_feat():
    d = get("Dwarven Fortitude", book="xge")
    assert d.fields["prerequisite"] == [{"race": [{"name": "dwarf"}]}] and d.fields["ability"] == [{"con": 1}]
    assert not failed(d)


def test_xge_racial_feats_table_rows_are_not_feats():
    """The Racial Feats table lists every feat name; only the printed feat sections are feats."""
    got = names(book="xge")
    assert len(got) == len(set(got))
    assert not any(n.startswith(("Dragonborn", "Gnome", "Halfling", "Human", "Tiefling")) for n in got)
    assert {"Bountiful Luck", "Dragon Fear", "Elven Accuracy"} <= set(got)


def test_xge_or_races():
    elven = get("Elven Accuracy", book="xge")
    assert elven.fields["prerequisite"] == [{"race": [{"name": "elf"}, {"name": "half-elf"}]}]
    assert get("Drow High Magic", book="xge").fields["prerequisite"] == [{"race": [{"name": "elf", "subrace": "drow"}]}]


def test_class_feature_heading_not_a_feat():  # Review Focus 3
    assert "Great Weapon Fighting" not in names(book="phb_fighter_pages")
    assert names(book="phb_fighter_pages") == []


def test_prerequisite_parsing():
    assert parse_prerequisite("Intelligence or Wisdom 13 or higher") == ([{"ability": [{"int": 13}, {"wis": 13}]}], [])
    assert parse_prerequisite("Half-elf, half-orc, or human") == (
        [{"race": [{"name": "half-elf"}, {"name": "half-orc"}, {"name": "human"}]}], [])
    assert parse_prerequisite("4th Level, Strixhaven Initiate Feat") == (
        [{"level": 4, "feat": ["strixhaven initiate"]}], [])
    assert parse_prerequisite("Spellcasting or Pact Magic feature") == ([{"spellcasting2020": True}], [])
    assert parse_prerequisite("Proficiency with a martial weapon") == ([{"proficiency": [{"weapon": "martial"}]}], [])


def test_unparsed_prerequisite_fails_naming_it():
    fields = {"prerequisite": [{"other": "Dwraf"}]}
    c = check(feat_checks(fields, "Prerequisite: Dwraf"), "prerequisite-parsed")
    assert not c.passed and "Dwraf" in c.reason


def test_empty_prerequisite_line_fails():
    c = check(feat_checks({"prerequisite": None}, "Prerequisite:"), "prerequisite-parsed")
    assert not c.passed and "empty" in c.reason


def test_no_prerequisite_passes():
    assert check(feat_checks({}, None), "prerequisite-parsed").passed


def test_feat_schema():
    jsonschema.validate(get("Alert").fields, schema("FeatFields"))
    jsonschema.validate(get("Heavily Armored").fields, schema("FeatFields"))
    jsonschema.validate(get("Dwarven Fortitude", book="xge").fields, schema("FeatFields"))


def _lines(texts, x=100, h=30):
    from bookextractor.model import Line
    return [Line(text=t, box=(x, 40 * k, x + 20 * len(t), 40 * k + h), height=h, page=1) for k, t in enumerate(texts)]


def test_feat_printed_on_its_own():
    """ERLW prints "Feat: Revenant Blade" inside a race chapter, with no feats section."""
    body = _lines(["the double-bladed scimitar deals 2d4 damage.", "Feat: Revenant Blade", "Prerequisite: Elf",
                   "You are descended from a master of the double-bladed", "scimitar. You gain these benefits:",
                   "Shifting Ground", "When you shift, the ground moves with you and nobody", "notices it."])
    got = parse_feats(body, ParseContext(slug="erlw", source="ERLW", pages={}))
    assert [p.name for p in got] == ["Revenant Blade"]
    assert got[0].fields["prerequisite"] == [{"race": [{"name": "elf"}]}] and len(got[0].lines) == 4


def test_mixed_and_or_prerequisite():  # review fix: "A, B or C" is A AND (B OR C), never A OR B OR C
    assert parse_prerequisite("4th level, elf or half-elf") == (
        [{"level": 4, "race": [{"name": "elf"}, {"name": "half-elf"}]}], [])
    pre, unread = parse_prerequisite("4th level, elf or spellcasting feature")
    c = check(feat_checks({"prerequisite": pre}, "Prerequisite: 4th level, elf or spellcasting feature"),
              "prerequisite-parsed")
    assert unread and not c.passed and "mixed and/or not understood" in c.reason
