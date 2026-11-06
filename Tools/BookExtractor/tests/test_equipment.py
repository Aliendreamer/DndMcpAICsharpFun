import json
from dataclasses import replace
from functools import lru_cache
from pathlib import Path

import jsonschema

from bookextractor.layout import reading_order
from bookextractor.model import load_ocr_page
from bookextractor.pages import classify_page
from bookextractor.parsers import ParseContext
from bookextractor.parsers.equipment import (cost_check, damage_check, parse_armor, parse_weapons, properties_check,
                                             weight_check)

FIX = Path(__file__).parent / "fixtures" / "ocr"
SCHEMAS = Path(__file__).parents[3] / "Schemas" / "canonical"
BOOKS = {
    "phb": ("phb14", "PHB", [146, 150]),
    "dmg": ("dmg14", "DMG", [268]),
}


def _pages(book):
    _, _, ns = BOOKS[book]
    return {n: load_ocr_page(FIX / book / f"{n:03d}.json", n) for n in ns}


def _run(fn, book, pages):
    slug, source, _ = BOOKS[book]
    body = [l for n in sorted(pages) if classify_page(pages[n]) in ("rules", "art") for l in reading_order(pages[n])]
    return fn(body, ParseContext(slug=slug, source=source, pages=pages))


@lru_cache(maxsize=None)
def weapons(book="phb"):
    return tuple(_run(parse_weapons, book, _pages(book)))


@lru_cache(maxsize=None)
def armor(book="phb"):
    return tuple(_run(parse_armor, book, _pages(book)))


def get(items, name):
    return next(p for p in items if p.name.lower() == name.lower())


def failed(p):
    return [c for c in p.checks if not c.passed]


def check(p, name):
    return next(c for c in p.checks if c.name == name)


def schema(name):
    return json.loads((SCHEMAS / f"{name}.schema.json").read_text(encoding="utf-8"))


def _edit(book, page, old, new):
    """The book's pages with the line reading `old` on `page` read as `new` (an OCR misread)."""
    pages = _pages(book)
    pg = pages[page]
    hit = [l for l in pg.lines if l.text == old]
    assert hit, old
    pages[page] = replace(pg, lines=[replace(l, text=new) if l is hit[0] else l for l in pg.lines])
    return pages


# ---------- spec scenarios ----------
def test_longsword():
    p = get(weapons(), "Longsword")
    f = p.fields
    assert p.type == "Weapon"
    assert f["weaponCategory"] == "martial" and f["type"] == "M"
    assert f["value"] == 1500 and f["weight"] == 3
    assert f["dmg1"] == "1d8" and f["dmgType"] == "S"
    assert f["property"] == ["V"] and f["dmg2"] == "1d10"
    assert failed(p) == []


def test_chain_mail():
    p = get(armor(), "Chain mail")
    f = p.fields
    assert p.type == "Armor"
    assert f == {"type": "HA", "ac": 16, "strength": "13", "stealth": True, "value": 7500, "weight": 55}
    assert failed(p) == []


def test_unreadable_cost_cell_is_produced_and_fails():
    rows = _run(parse_weapons, "phb", _edit("phb", 150, "15 gP", "1S gp"))
    p = get(rows, "Longsword")
    c = check(p, "cost-valid")
    assert not c.passed and "1S gp" in c.reason and "Longsword" in c.reason
    assert "value" not in p.fields  # not guessed from the misread


def test_unknown_property_fails_naming_it():
    rows = _run(parse_weapons, "phb", _edit("phb", 150, "Finesse, light", "Finesse, Lihgt"))
    bad = [p for p in rows if not check(p, "properties-known").passed]
    assert bad and all("Lihgt" in check(p, "properties-known").reason for p in bad)


def test_schema():
    for p in weapons() + weapons("dmg"):
        jsonschema.validate(p.fields, schema("WeaponFields"))
    for p in armor():
        jsonschema.validate(p.fields, schema("ArmorFields"))


# ---------- whole tables ----------
def test_phb_weapons_all_rows():
    ws = weapons()
    assert len(ws) == 38  # the 37 5etools weapons plus the printed "Unarmed strike" row
    assert [p.name for p in ws if failed(p)] == []
    dagger = get(ws, "Dagger").fields
    assert dagger["property"] == ["F", "L", "T"] and dagger["range"] == "20/60" and dagger["type"] == "M"
    dart = get(ws, "Dart").fields
    assert dart["weight"] == 0.25 and dart["value"] == 5 and dart["type"] == "R" and dart["weaponCategory"] == "simple"
    blow = get(ws, "Blowgun").fields
    assert blow["dmg1"] == "1" and blow["property"] == ["A", "LD"] and blow["range"] == "25/100"
    heavy = get(ws, "Crossbow, heavy").fields
    assert heavy["property"] == ["A", "H", "LD", "2H"] and heavy["range"] == "100/400"
    assert get(ws, "Lance").fields["property"] == ["R", "S"]
    assert get(ws, "Spear").fields["property"] == ["T", "V"] and get(ws, "Spear").fields["dmg2"] == "1d8"


def test_blank_cells_are_absent():
    net = get(weapons(), "Net").fields
    assert "dmg1" not in net and "dmgType" not in net and net["property"] == ["S", "T"] and net["range"] == "5/15"
    assert "weight" not in get(weapons(), "Sling").fields
    unarmed = get(weapons(), "Unarmedstrike").fields
    assert "value" not in unarmed and unarmed["dmg1"] == "1" and unarmed["dmgType"] == "B"


def test_phb_armor_all_rows():
    arm = armor()
    assert len(arm) == 13 and [p.name for p in arm if failed(p)] == []
    assert get(arm, "Padded").fields == {"type": "LA", "ac": 11, "stealth": True, "value": 500, "weight": 8}
    assert get(arm, "Half plate").fields["ac"] == 15 and get(arm, "Half plate").fields["type"] == "MA"
    assert get(arm, "Plate").fields["value"] == 150000 and get(arm, "Plate").fields["strength"] == "15"
    assert get(arm, "Shield").fields == {"type": "S", "ac": 2, "value": 1000, "weight": 6}


def test_armor_text_and_lines():
    p = get(armor(), "Chain mail")
    assert p.text.startswith("Chain mail") and "Str 13" in p.text and len(p.text) >= 30
    assert all(l.page == 146 for l in p.lines)


def test_dmg_firearms():
    ws = weapons("dmg")
    assert sorted(p.name for p in ws) == sorted([
        "Pistol", "Musket", "Pistol, automatic", "Revolver", "Rifle, hunting", "Rifle, automatic", "Shotgun",
        "Laser pistol", "Antimatter rifle", "Laser rifle"])  # ammunition rows are not weapons
    musket = get(ws, "Musket").fields
    assert musket["value"] == 50000 and musket["weight"] == 10 and musket["age"] == "renaissance"
    assert musket["property"] == ["AF|DMG", "LD", "2H"] and musket["range"] == "40/120" and musket["firearm"] is True
    auto = get(ws, "Rifle, automatic").fields
    assert auto["property"] == ["AF|DMG", "BF|DMG", "RLD", "2H"] and auto["reload"] == 30 and auto["weight"] == 8
    assert "value" not in auto and auto["age"] == "modern"
    anti = get(ws, "Antimatter rifle").fields
    assert anti["dmg1"] == "6d8" and anti["dmgType"] == "N" and anti["age"] == "futuristic"
    assert [p.name for p in ws if failed(p)] == []


# ---------- shared checks ----------
def test_cost_check():
    assert cost_check("15 gp").passed and cost_check("1,500 gp").passed and cost_check("5 cp").passed
    assert cost_check("-").passed  # a printed dash
    bad = cost_check("1S gp", "Longsword")
    assert not bad.passed and bad.name == "cost-valid" and "1S gp" in bad.reason and "Longsword" in bad.reason


def test_weight_check():
    assert weight_check("3 lb.").passed and weight_check("1/4 lb.").passed and weight_check("2lb.").passed
    assert weight_check("-").passed
    bad = weight_check("3 kg")
    assert not bad.passed and bad.name == "weight-valid" and "3 kg" in bad.reason


def test_damage_check():
    assert damage_check("1d8 slashing").passed and damage_check("1 piercing").passed
    bad = damage_check("1d8 slashng")
    assert not bad.passed and bad.name == "damage-valid" and "1d8 slashng" in bad.reason


def test_properties_check():
    assert properties_check(["Finesse", "light", "thrown (range 20/60)"]).passed
    bad = properties_check(["Finesse", "Lihgt"])
    assert not bad.passed and bad.name == "properties-known" and "Lihgt" in bad.reason
