import dataclasses
import json
from pathlib import Path

import jsonschema

from bookextractor.layout import reading_order
from bookextractor.model import load_ocr_page
from bookextractor.pages import classify_page
from bookextractor.parsers import ParseContext
from bookextractor.parsers.subclass import parse_subclasses, subclass_checks

FIX = Path(__file__).parent / "fixtures" / "ocr"
SCHEMAS = Path(__file__).parents[3] / "Schemas" / "canonical"
BOOKS = {
    "phb": ("phb14", "PHB", [50, 51]),
    "xge": ("xge", "XGE", [10, 11, 12, 13]),
}


def _squash(s):
    return "".join(c for c in s.lower() if c.isalpha())


def parse(book="phb", mutate_heading=None):
    slug, source, ns = BOOKS[book]
    pages = {n: load_ocr_page(FIX / book / f"{n:03d}.json", n) for n in ns}
    body = [l for n in sorted(pages) if classify_page(pages[n]) in ("rules", "art") for l in reading_order(pages[n])]
    if mutate_heading:
        body = [dataclasses.replace(l, text=mutate_heading) if _squash(l.text) == "primalpaths" else l for l in body]
    return parse_subclasses(body, ParseContext(slug=slug, source=source, pages=pages))


def get_sub(name, book="phb", mutate_heading=None):
    return next((p for p in parse(book, mutate_heading) if _squash(p.name) == _squash(name)), None)


def failed(p):
    return [c for c in p.checks if not c.passed]


def check(checks, name):
    return next(c for c in checks if c.name == name)


def schema(name):
    return json.loads((SCHEMAS / f"{name}.schema.json").read_text(encoding="utf-8"))


def test_zealot():
    p = get_sub("Path of the Zealot", book="xge")
    assert p.fields["className"] == "Barbarian" and p.fields["classSource"] == "PHB" and p.fields["shortName"] == "Zealot"
    assert sorted({int(r.split("|")[-1]) for r in p.fields["subclassFeatures"]}) == [3, 6, 10, 14]
    assert any(r.endswith("|Barbarian||Zealot|XGE|3") for r in p.fields["subclassFeatures"])
    assert not failed(p), failed(p)


def test_zealot_refs_follow_5etools():
    p = get_sub("Path of the Zealot", book="xge")
    assert p.fields["subclassFeatures"] == [
        "Path of the Zealot|Barbarian||Zealot|XGE|3", "Fanatical Focus|Barbarian||Zealot|XGE|6",
        "Zealous Presence|Barbarian||Zealot|XGE|10", "Rage beyond Death|Barbarian||Zealot|XGE|14"]
    names = [e["name"] for e in p.fields["entries"]]
    assert names[:2] == ["Divine Fury", "Warrior of the Gods"]


def test_xge_group_reads_every_subclass():
    assert [p.name for p in parse("xge")] == ["Path of the Ancestral Guardian", "Path of the Storm Herald",
                                              "Path of the Zealot"]


def test_berserker_phb():
    p = get_sub("Path of the Berserker")
    assert p.fields["className"] == "Barbarian"
    assert p.fields["subclassFeatures"] == [
        "Path of the Berserker|Barbarian||Berserker||3", "Mindless Rage|Barbarian||Berserker||6",
        "Intimidating Presence|Barbarian||Berserker||10", "Retaliation|Barbarian||Berserker||14"]
    assert not failed(p), failed(p)


def test_group_heading_casing_noise():   # Review Focus 5
    assert get_sub("Path of the Zealot", book="xge", mutate_heading="PRIMAL PATHs")


def test_feature_levels_mismatch():
    c = check(subclass_checks({"className": "Barbarian", "subclassFeatures": ["A|Barbarian||X|XGE|3", "B|Barbarian||X|XGE|5"]}), "feature-levels-match-class")
    assert not c.passed and "5" in c.reason


def test_feature_levels_backwards():   # two subclasses read as one: the levels restart
    refs = ["A|Fighter||X|XGE|3", "B|Fighter||X|XGE|7", "C|Fighter||X|XGE|15", "D|Fighter||X|XGE|3",
            "E|Fighter||X|XGE|10", "F|Fighter||X|XGE|18"]
    c = check(subclass_checks({"className": "Fighter", "subclassFeatures": refs}), "feature-levels-match-class")
    assert not c.passed and "D (level 3)" in c.reason and "C (level 15)" in c.reason


def test_parent_class_unknown():
    c = check(subclass_checks({"className": "Gunslinger", "subclassFeatures": []}), "parent-class-known")
    assert not c.passed and "Gunslinger" in c.reason


def test_subclass_schema():
    jsonschema.validate(get_sub("Path of the Zealot", book="xge").fields, schema("SubclassFields"))
