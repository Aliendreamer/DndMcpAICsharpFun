import json
from dataclasses import replace
from functools import lru_cache
from pathlib import Path

import jsonschema

from bookextractor.layout import reading_order
from bookextractor.model import Line, load_ocr_page
from bookextractor.pages import classify_page
from bookextractor.parsers import ParseContext
from bookextractor.parsers.equipment import (cost_check, damage_check, parse_armor, parse_weapons, properties_check,
                                             weight_check)

FIX = Path(__file__).parent / "fixtures" / "ocr"
SCHEMAS = Path(__file__).parents[3] / "Schemas" / "canonical"
BOOKS = {
    "phb": ("phb14", "PHB", [146, 150]),
    "dmg": ("dmg14", "DMG", [268]),
    "scag": ("scag", "SCAG", [122]),
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


def _edit(book, page, old, new, near_y=None, pages=None):
    """The book's pages with the line reading `old` on `page` (the one nearest `near_y`) read as `new` (an OCR
    misread)."""
    pages = pages or _pages(book)
    pg = pages[page]
    hit = sorted((l for l in pg.lines if l.text == old), key=lambda l: abs(l.box[1] - (near_y or 0)))
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
    for p in armor() + armor("scag"):
        jsonschema.validate(p.fields, schema("ArmorFields"))


# ---------- whole tables ----------
def test_phb_weapons_all_rows():
    ws = weapons()
    assert len(ws) == 38  # the 37 5etools weapons plus the printed "Unarmed strike" row
    # the book prints a dash in these cells; the OCR drops it, so the blank cells fail and the rows go to review
    assert {p.name: sorted(c.name for c in failed(p)) for p in ws if failed(p)} == {
        "Unarmedstrike": ["cost-valid", "weight-valid"], "Sling": ["weight-valid"], "Net": ["damage-valid"]}
    assert "blank" in check(get(ws, "Net"), "damage-valid").reason
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
    # the modern and futuristic tables print a dash for every cost: blank, so cost-valid fails
    assert {p.name: [c.name for c in failed(p)] for p in ws if failed(p)} == {
        n: ["cost-valid"] for n in ["Pistol, automatic", "Revolver", "Rifle, hunting", "Rifle, automatic", "Shotgun",
                                    "Laser pistol", "Antimatter rifle", "Laser rifle"]}


def test_misread_header_cost_still_stops_the_table_above():
    """"Cost" in the Modern (then the Futuristic) header OCR'd as "Csst": the Renaissance table must still stop at
    the "Modern Item" header row, so the modern firearms keep their own table's age."""
    pages = _edit("dmg", 268, "Cost", "Csst", near_y=1396)
    pages = _edit("dmg", 268, "Cost", "Csst", near_y=1729, pages=pages)
    ws = _run(parse_weapons, "dmg", pages)
    ages = {p.name: p.fields.get("age") for p in ws}
    assert ages["Revolver"] == "modern" and ages["Shotgun"] == "modern" and ages["Laser rifle"] == "futuristic"
    assert ages["Musket"] == "renaissance" and len(ws) == 10
    assert all(check(p, "table-rows-read").passed for p in ws)


def test_header_like_row_read_as_data_fails_from_there_on():
    """A header row too garbled to read as a header ("Modern Item" read as "Modrn", "Cost" as "Csst") is read as a
    row: it resembles a header (one cell still reads "Damage"), so it and every row after it fail; the modern rows
    are still produced (for review), not lost."""
    pages = _edit("dmg", 268, "Modern Item", "Modrn", near_y=1398)
    pages = _edit("dmg", 268, "Cost", "Csst", near_y=1396, pages=pages)
    pages = _edit("dmg", 268, "Weight Properties", "Wt Props", near_y=1397, pages=pages)
    ws = _run(parse_weapons, "dmg", pages)
    bad = [p.name for p in ws if not check(p, "table-rows-read").passed]
    assert "Pistol" not in bad and "Musket" not in bad
    assert {"Modrn", "Revolver", "Shotgun", "Laser rifle"} <= set(bad) and len(ws) == 11  # the header row too
    assert "looks like a table header" in check(get(ws, "Revolver"), "table-rows-read").reason


def test_reload_is_a_firearm_property_only():
    rows = _run(parse_weapons, "phb", _edit("phb", 150, "Finesse, light", "Finesse, reload (6 shots)"))
    bad = [p for p in rows if not check(p, "properties-known").passed]
    assert bad and all("reload (6 shots)" in check(p, "properties-known").reason for p in bad)
    assert not properties_check(["burst fire"]).passed and properties_check(["burst fire"], firearm=True).passed


def test_text_uses_repaired_cells():
    p = get(weapons(), "Longsword")
    assert "Cost: 15 gp" in p.text and "gP" not in p.text


def test_stray_line_in_a_row_fails_row_complete():
    """A line the table reader cannot place (set far taller than the cells) inside the Longsword row."""
    pages = _pages("phb")
    pg = pages[150]
    stray = Line(text="XQZ", box=(330.0, 1200.0, 420.0, 1310.0), height=110.0, page=150)
    pages[150] = replace(pg, lines=pg.lines + [stray])
    rows = _run(parse_weapons, "phb", pages)
    c = check(get(rows, "Longsword"), "row-complete")
    assert not c.passed and "XQZ" in c.reason and "Longsword" in c.reason
    assert check(get(rows, "Club"), "row-complete").passed


# ---------- shared checks ----------
def test_cost_check():
    assert cost_check("15 gp").passed and cost_check("1,500 gp").passed and cost_check("5 cp").passed
    assert cost_check("-").passed  # a printed dash
    assert not cost_check("", "Revolver").passed and "Revolver" in cost_check("", "Revolver").reason  # blank fails
    bad = cost_check("1S gp", "Longsword")
    assert not bad.passed and bad.name == "cost-valid" and "1S gp" in bad.reason and "Longsword" in bad.reason


def test_weight_check():
    assert weight_check("3 lb.").passed and weight_check("1/4 lb.").passed and weight_check("2lb.").passed
    assert weight_check("-").passed and not weight_check("").passed
    bad = weight_check("3 kg")
    assert not bad.passed and bad.name == "weight-valid" and "3 kg" in bad.reason


def test_damage_check():
    assert damage_check("1d8 slashing").passed and damage_check("1 piercing").passed and not damage_check("").passed
    bad = damage_check("1d8 slashng")
    assert not bad.passed and bad.name == "damage-valid" and "1d8 slashng" in bad.reason


def test_properties_check():
    assert properties_check(["Finesse", "light", "thrown (range 20/60)"]).passed
    bad = properties_check(["Finesse", "Lihgt"])
    assert not bad.passed and bad.name == "properties-known" and "Lihgt" in bad.reason


# ---------- printed outside a table ----------
def test_scag_spiked_armor_stat_lines_under_a_heading():
    """SCAG p.121: a "Spiked Armor" heading, a description, then Cost / AC / Stealth / Weight lines."""
    a = get(armor("scag"), "Spiked Armor")
    assert a.fields == {"type": "MA", "ac": 14, "stealth": True, "value": 7500, "weight": 45,
                        "entries": ["Spiked armor is a rare type of medium armor made by dwarves. It consists of a "
                                    "leather coat and leggings covered with spikes that are usually made of metal."]}
    assert failed(a) == []
    assert "Cost: 75 gp" in a.text and len(armor("scag")) == 1


def test_armor_stat_lines_without_an_ac_line_are_not_armor():
    pages = _edit("scag", 122, "AC: 14 + Dexterity modifier (max 2)", "Speed: 30 ft.", pages=_pages("scag"))
    assert _run(parse_armor, "scag", pages) == []
