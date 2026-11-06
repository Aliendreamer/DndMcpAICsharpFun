import json
from dataclasses import replace
from functools import lru_cache
from pathlib import Path

import jsonschema

from bookextractor.layout import reading_order
from bookextractor.model import load_ocr_page
from bookextractor.pages import classify_page
from bookextractor.parsers import ParseContext
from bookextractor.parsers.background import _languages, background_checks, parse_backgrounds

FIX = Path(__file__).parent / "fixtures" / "ocr"
SCHEMAS = Path(__file__).parents[3] / "Schemas" / "canonical"
BOOKS = {
    "phb": ("phb14", "PHB", [128, 129]),
    "scc": ("scc", "SCC", [32, 33]),
}


def _squash(s):
    return "".join(c for c in s.lower() if c.isalpha())


def _body(book):
    _, _, ns = BOOKS[book]
    pages = {n: load_ocr_page(FIX / book / f"{n:03d}.json", n) for n in ns}
    body = [l for n in sorted(pages) if classify_page(pages[n]) in ("rules", "art") for l in reading_order(pages[n])]
    return body, pages


def _parse(body, pages, book):
    slug, source, _ = BOOKS[book]
    return tuple(parse_backgrounds(body, ParseContext(slug=slug, source=source, pages=pages)))


@lru_cache(maxsize=None)
def parse(book="phb"):
    return _parse(*_body(book), book)


def get(name, book="phb"):
    return next((p for p in parse(book) if _squash(p.name) == _squash(name)), None)


def failed(p):
    return [c for c in p.checks if not c.passed]


def check(checks, name):
    return next(c for c in checks if c.name == name)


def schema(name):
    return json.loads((SCHEMAS / f"{name}.schema.json").read_text(encoding="utf-8"))


def feature(p):
    return next(e for e in p.fields["entries"] if isinstance(e, dict) and e.get("data", {}).get("isFeature"))


def get_wrapped_fixture():
    """The Acolyte skill line split in two, as OCR does when the line wraps: "Skill Proficiencies: Insight," over
    an indented "Religion"."""
    body, pages = _body("phb")
    i = next(k for k, l in enumerate(body) if l.text.startswith("Skill Proficiencies: Insight"))
    l = body[i]
    first = replace(l, text="Skill Proficiencies: Insight,")
    x0, y0, x1, y1 = l.box
    second = replace(l, text="Religion", box=(x0 + 25, y1 + 2, x0 + 160, y1 + 2 + l.height))
    body = body[:i] + [first, second] + body[i + 1:]
    return next(p for p in _parse(body, pages, "phb") if p.name == "Acolyte")


def test_acolyte():
    b = get("Acolyte")
    assert b.type == "Background"
    assert b.fields["skillProficiencies"] == [{"insight": True, "religion": True}]
    assert feature(b)["name"] == "Feature: Shelter of the Faithful"
    assert feature(b)["entries"][0].startswith("As an acolyte, you command the respect")
    assert b.fields["languageProficiencies"] == [{"anyStandard": 2}]
    assert not failed(b)


def test_tools_and_equipment():
    c = get("Charlatan")
    assert c.fields["skillProficiencies"] == [{"deception": True, "sleight of hand": True}]
    assert c.fields["toolProficiencies"] == [{"disguise kit": True, "forgery kit": True}]
    labels = next(e for e in c.fields["entries"] if isinstance(e, dict) and e.get("type") == "list")["items"]
    equipment = next(i for i in labels if i["name"] == "Equipment:")["entry"]
    assert equipment.endswith("and a belt pouch containing 15 gp") and feature(c)["name"] == "Feature: False Identity"


def test_wrapped_skill_line():  # Review Focus 4
    assert get_wrapped_fixture().fields["skillProficiencies"] == [{"insight": True, "religion": True}]


def test_unknown_skill():  # spec scenario
    fields = {"skillProficiencies": [{"insigth": True, "religion": True}],
              "entries": [{"type": "entries", "name": "Feature: X", "entries": ["Text."], "data": {"isFeature": True}}]}
    c = check(background_checks(fields), "skills-valid")
    assert not c.passed and "insigth" in c.reason.lower()
    assert check(background_checks(fields), "feature-present").passed


def test_missing_feature_fails():
    c = check(background_checks({"skillProficiencies": [{"insight": True}], "entries": []}), "feature-present")
    assert not c.passed


def test_supplement_background():
    b = get("Lorehold Student", book="scc")
    assert b.fields["skillProficiencies"] == [{"history": True, "religion": True}]
    assert feature(b)["name"] == "Feature: Lorehold Initiate" and not failed(b)


def test_supplement_wrapped_tool_line():
    p = get("Prismari Student", book="scc")
    assert p.fields["toolProficiencies"] == [{"choose": {"from": ["musical instrument", "anyArtisansTool"]}}]
    assert feature(p)["name"] == "Feature: Prismari Initiate"


def test_background_schema():
    jsonschema.validate(get("Acolyte").fields, schema("BackgroundFields"))
    jsonschema.validate(get("Charlatan").fields, schema("BackgroundFields"))
    jsonschema.validate(get("Lorehold Student", book="scc").fields, schema("BackgroundFields"))


def test_unknown_language_in_choice_fails():  # review fix: OCR'd language names are never accepted
    fields = {"skillProficiencies": [{"insight": True}], "languageProficiencies":
              _languages("Choose one of Dwarvlsh, Elvlsh, or Gnomlsh"), "entries": []}
    c = check(background_checks(fields), "languages-valid")
    assert not c.passed and "dwarvlsh" in c.reason


def test_known_setting_language_choice_passes():
    fields = {"languageProficiencies": _languages("Choose one of Celestial, Draconic, Goblin, or Minotaur")}
    assert check(background_checks(fields), "languages-valid").passed
