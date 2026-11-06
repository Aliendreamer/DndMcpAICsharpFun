import json
from dataclasses import replace
from functools import lru_cache
from pathlib import Path

import jsonschema

from bookextractor.layout import reading_order
from bookextractor.model import load_ocr_page
from bookextractor.pages import classify_page
from bookextractor.parsers import ParseContext
from bookextractor.parsers.vehicle_object import parse_objects, parse_vehicles

FIX = Path(__file__).parent / "fixtures" / "ocr"
SCHEMAS = Path(__file__).parents[3] / "Schemas" / "canonical"
BOOKS = {
    # 119: Airborne and Waterborne Vehicles table; 151: Apparatus of Kwalish; 255-256: Siege Equipment
    "dmg": ("dmg14", "DMG", [119, 151, 255, 256]),
    # 090-091: githyanki astral ships described in prose; 092: a large-type quote after Planar Raider
    "mtf": ("mtf", "MTF", [90, 91, 92]),
    # 073: the Skyship heading over "Purchase Cost: / Speed: / Maximum Cargo: / Crew: ..." lines
    "egw": ("egw", "EGW", [73]),
}


def _pages(book):
    _, _, ns = BOOKS[book]
    return {n: load_ocr_page(FIX / book / f"{n:03d}.json", n) for n in ns}


def _run(fn, book, pages=None):
    slug, source, _ = BOOKS[book]
    pages = pages or _pages(book)
    body = [l for n in sorted(pages) if classify_page(pages[n]) in ("rules", "art") for l in reading_order(pages[n])]
    return fn(body, ParseContext(slug=slug, source=source, pages=pages))


@lru_cache(maxsize=None)
def vehicles(book="dmg"):
    return tuple(_run(parse_vehicles, book))


@lru_cache(maxsize=None)
def objects(book="dmg"):
    return tuple(_run(parse_objects, book))


def get(ps, name):
    hit = [p for p in ps if p.name.lower() == name.lower()]
    assert hit, f"{name} not among {[p.name for p in ps]}"
    return hit[0]


def failed(p):
    return [c for c in p.checks if not c.passed]


def _edit(book, page, old, new):
    pages = _pages(book)
    pg = pages[page]
    hit = [l for l in pg.lines if l.text == old]
    assert hit, old
    pages[page] = replace(pg, lines=[replace(l, text=new) if l is hit[0] else l for l in pg.lines])
    return pages


def _validate(ps, schema):
    s = json.loads((SCHEMAS / schema).read_text("utf-8"))
    for p in ps:
        jsonschema.validate(p.fields, s)


# ---------- spec scenario: a DMG vehicle with its printed statistics ----------
def test_galley_row_has_every_printed_statistic():
    p = get(vehicles(), "Galley")
    assert p.type == "VehicleMount"
    assert p.fields == {"vehicleType": "SHIP", "cost": 3000000, "pace": 4, "capCrew": 80, "capCargo": 150,
                        "hull": {"ac": 15, "hp": 500, "dt": 20}}  # Passengers prints a dash: absent
    assert failed(p) == []


def test_vehicle_rows_with_dashes_place_their_cells_by_column():
    air = get(vehicles(), "Airship")
    assert air.fields["capCargo"] == 1 and air.fields["hull"] == {"ac": 13, "hp": 300}  # no damage threshold
    assert air.fields["capPassenger"] == 20 and air.fields["pace"] == 8 and air.fields["cost"] == 2000000
    row = get(vehicles(), "Rowboat")
    assert "capCargo" not in row.fields and row.fields["hull"] == {"ac": 11, "hp": 50}
    assert row.fields["pace"] == 1.5 and row.fields["cost"] == 5000
    assert get(vehicles(), "Keelboat").fields["capCargo"] == 0.5
    assert get(vehicles(), "Warship").fields["pace"] == 2.5
    assert all(failed(p) == [] for p in (air, row))


def test_vehicle_row_without_a_printed_label_is_not_named():
    # the OCR lost the "Sailing ship" label; its cells are never given a name from elsewhere
    names = sorted(p.name for p in vehicles() if p.fields.get("vehicleType") == "SHIP")
    assert names == ["Airship", "Galley", "Keelboat", "Longship", "Rowboat", "Warship"]


def test_unreadable_vehicle_cell_fails_its_check():
    pages = _pages("dmg")
    pg = pages[119]
    hp = min((l for l in pg.lines if l.text == "500"), key=lambda l: l.box[1])  # the Galley's HP cell
    pages[119] = replace(pg, lines=[replace(l, text="5#0") if l is hp else l for l in pg.lines])
    p = get(_run(parse_vehicles, "dmg", pages), "Galley")
    assert any(c.name == "vehicle-stats-valid" for c in failed(p))


def test_object_statistics_with_a_speed_are_a_vehicle():
    p = get(vehicles(), "Apparatus of Kwalish")
    assert p.fields["vehicleType"] == "OBJECT" and p.fields["size"] == "L"
    assert p.fields["ac"] == [20] and p.fields["hp"] == {"average": 200}
    assert p.fields["speed"] == {"walk": 30, "swim": 30}
    assert p.fields["immune"] == ["poison", "psychic"]
    assert failed(p) == []


def test_prose_vehicles_read_their_stated_crew_passengers_and_speed():
    vs = vehicles("mtf")
    assert sorted(p.name for p in vs) == ["Astral Brig", "Astral Skiff", "Planar Raider"]
    skiff = get(vs, "Astral Skiff")
    assert skiff.fields == {"capCrew": 3, "capPassenger": 12, "pace": 15, "dimensions": ["30 ft.", "10 ft."],
                            "entries": skiff.fields["entries"]}
    brig = get(vs, "Astral Brig")
    assert brig.fields["capCrew"] == 5 and brig.fields["capPassenger"] == 60 and brig.fields["pace"] == 12
    raider = get(vs, "Planar Raider")
    # "can carry more than a hundred passengers" is not a number of passengers: absent
    assert raider.fields["capCrew"] == 10 and "capPassenger" not in raider.fields
    assert raider.fields["dimensions"] == ["120 ft.", "40 ft."]  # printed "40 feet wide and 120 feet long"
    assert all(failed(p) == [] for p in vs)


def test_prose_vehicle_section_ends_at_the_column_before_the_next_page_quote():
    # p092 opens with a quote set in large type ("I have been to Tunarath. ...") and OCR debris ("a vsibe"):
    # not the ship's text, which ends at p091's column bottom
    raider = get(vehicles("mtf"), "Planar Raider")
    assert raider.fields["entries"][-1].endswith("is equipped with three ballistae and a catapult.")
    assert "Tunarath" not in raider.text and "vsibe" not in raider.text
    assert {l.page for l in raider.lines} == {91}
    assert failed(raider) == []


def test_prose_vehicle_printing_two_crews_fails():
    pages = _edit("mtf", 91, "is equipped with three ballistae and a catapult.",
                  "is equipped with a crew of four and a catapult.")
    raider = get(_run(parse_vehicles, "mtf", pages), "Planar Raider")
    c = [c for c in failed(raider) if c.name == "vehicle-stats-valid"]
    assert c and "crew" in c[0].reason and "ten" in c[0].reason and "four" in c[0].reason


def test_prose_vehicle_section_ending_mid_sentence_fails():
    # the column ends mid-sentence and the next page's text is set in another size: the section cannot end cleanly
    pages = _edit("mtf", 91, "is equipped with three ballistae and a catapult.", "is equipped with three ballistae and")
    raider = get(_run(parse_vehicles, "mtf", pages), "Planar Raider")
    c = [c for c in failed(raider) if c.name == "vehicle-stats-valid"]
    assert c and "mid-sentence" in c[0].reason


# ---------- spec scenario: a siege object with its printed stat block ----------
def test_ballista_stat_block():
    p = get(objects(), "Ballista")
    assert p.type == "Object"
    assert p.fields["ac"] == [15] and p.fields["hp"] == {"average": 50}
    assert p.fields["immune"] == ["poison", "psychic"]
    assert p.fields["description"].startswith("A ballista is a massive crossbow")
    assert p.fields["description"].endswith("one action to fire it.")
    assert p.fields["action"] == [{"name": "Bolt", "entries": [
        "Ranged Weapon Attack: +6 to hit, range 120/480 ft., one target. Hit: 16 (3d10) piercing damage."]}]
    assert failed(p) == []


def test_every_siege_object_is_read():
    names = sorted(p.name for p in objects())
    assert names == ["Ballista", "Cannon", "Cauldron, Suspended", "Mangonel", "Ram", "Siege Tower", "Trebuchet"]
    assert "action" not in get(objects(), "Siege Tower").fields
    assert get(objects(), "Trebuchet").fields["hp"] == {"average": 150}
    assert all(failed(p) == [] for p in objects())


def test_object_description_continues_across_the_column():
    p = get(objects(), "Cannon")
    assert p.fields["description"].startswith("A cannon uses gunpowder")
    assert p.fields["action"][0]["name"] == "Cannon Ball"
    assert p.fields["action"][0]["entries"][0].endswith("bludgeoning damage.")


def test_unreadable_object_hit_points_fail():
    pages = _edit("dmg", 255, "Hit Points: 50", "Hit Points: SO")
    p = get(_run(parse_objects, "dmg", pages), "Ballista")
    assert "hp" not in p.fields
    assert any(c.name == "object-stats-valid" and "Hit Points" in c.reason for c in failed(p))


def test_schemas():
    _validate(vehicles("dmg") + vehicles("mtf") + vehicles("egw"), "VehicleMountFields.schema.json")
    _validate(objects("dmg"), "ObjectFields.schema.json")


def test_egw_skyship_statistics_lines():
    ps = vehicles("egw")
    assert [p.name for p in ps] == ["Skyship"]
    f = ps[0].fields
    assert {k: v for k, v in f.items() if k != "entries"} == {
        "vehicleType": "SHIP", "cost": 10000000, "pace": 10, "capCargo": 10, "capPassenger": 30}
    assert "Personal Travel Cost: 2 gp per mile per passenger" in f["entries"]
    assert failed(ps[0]) == []
    crew = next(c for c in ps[0].checks if c.name == "crew-range")
    assert crew.passed and "6-10" in crew.reason
