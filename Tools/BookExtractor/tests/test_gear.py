import json
from dataclasses import replace
from functools import lru_cache
from pathlib import Path

import jsonschema

from bookextractor.layout import reading_order
from bookextractor.model import Line, load_ocr_page
from bookextractor.pages import classify_page
from bookextractor.parsers import ParseContext
from bookextractor.parsers.gear import parse_items_mundane

FIX = Path(__file__).parent / "fixtures" / "ocr"
SCHEMAS = Path(__file__).parents[3] / "Schemas" / "canonical"
BOOKS = {
    # 128: a chapter 4 background page; 144: "Chapter 5: Equipment" and the coin table; 149-159: the chapter
    "phb": ("phb14", "PHB", [128, 144, 149, 150, 151, 152, 153, 154, 155, 156, 158, 159]),
    "dmg": ("dmg14", "DMG", [134, 135, 268]),
    "scag": ("scag", "SCAG", [125]),
}


def _pages(book):
    _, _, ns = BOOKS[book]
    return {n: load_ocr_page(FIX / book / f"{n:03d}.json", n) for n in ns}


def _run(book, pages=None):
    slug, source, _ = BOOKS[book]
    pages = pages or _pages(book)
    body = [l for n in sorted(pages) if classify_page(pages[n]) in ("rules", "art") for l in reading_order(pages[n])]
    return parse_items_mundane(body, ParseContext(slug=slug, source=source, pages=pages))


@lru_cache(maxsize=None)
def items(book="phb"):
    return tuple(_run(book))


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


# ---------- spec scenario ----------
def test_crowbar_row_joined_to_its_description():
    p = get(items(), "Crowbar")
    assert p.type == "Item"
    assert p.fields["value"] == 200 and p.fields["weight"] == 5 and p.fields["type"] == "G"
    assert p.fields["entries"][0].startswith("Using a crowbar grants advantage")
    assert p.fields["entries"][0].endswith("can be applied.")
    assert failed(p) == []
    assert "Using a crowbar" in p.text


# ---------- Review Focus 2: the join stays inside the equipment chapter ----------
def test_same_name_lead_in_outside_the_chapter_is_not_joined():
    # page 128 is a chapter 4 background page: an "Abacus." lead-in there is not the gear's description
    pages = _edit("phb", 128, "Choose a god, a pantheon of gods, or some other", "Abacus. A frame of beads for counting.")
    assert "entries" not in get(_run("phb", pages), "Abacus").fields


def test_same_name_lead_in_inside_the_chapter_is_joined():
    pages = _edit("phb", 153, "Lock. A key is provided with the lock. Without the", "Abacus. A frame of beads for counting.")
    p = get(_run("phb", pages), "Abacus")
    assert p.fields["entries"][0].startswith("A frame of beads for counting.")


def test_descriptions_skip_the_tables_between_their_lines():
    # Alchemist's Fire starts on p149 and ends on p152 after the weapon and gear tables
    e = get(items(), "Alchemist's fire (flask)").fields["entries"][0]
    assert e.startswith("This sticky, adhesive fluid ignites") and e.endswith("extinguish the flames.")
    assert "gp" not in e


def test_description_stops_before_a_table_footnote():
    # "Torch." is the last lead-in above the Container Capacity table and its "* You can also strap items" note
    assert get(items(), "Torch").fields["entries"] == [
        "A torch burns for 1 hour, providing bright light in a 20-foot radius and dim light for an additional 20 "
        "feet. If you make a melee attack with a burning torch and hit, it deals 1 fire damage."]


def test_lead_in_names_the_row_without_its_bracket_and_plural():
    assert get(items(), "Ball bearings (bag of 1,000)").fields["entries"][0].startswith("As an action, you can spill")
    assert get(items(), "Case, map or scroll").fields["entries"][0].startswith("This cylindrical leather case")
    # "Rope." describes both ropes: the lead-in is the part of the label before its comma, and no row is "Rope"
    for name in ("Rope, hempen (50 feet)", "Rope, silk (50 feet)"):
        assert get(items(), name).fields["entries"][0].startswith("Rope, whether made of hemp or silk")


def test_row_without_description_has_no_entries():
    assert "entries" not in get(items(), "Abacus").fields


# ---------- packs ----------
def test_explorers_pack_lists_its_contents():
    p = get(items(), "Explorer's Pack")
    assert p.fields["value"] == 1000 and p.fields["type"] == "G" and "weight" not in p.fields
    e = p.fields["entries"][0]
    assert e.startswith("Includes a backpack, a bedroll, a mess kit, a tinderbox, 10 torches")
    assert e.endswith("strapped to the side of it.")
    assert failed(p) == []
    assert len([q for q in items() if q.name.endswith("Pack")]) == 7


# ---------- type codes from the table and its sub-headings ----------
def test_gear_type_codes():
    ps = items()
    assert get(ps, "Arrows (20)").fields == {"type": "A", "value": 100, "weight": 1}
    assert get(ps, "Crossbow bolts (20)").fields["weight"] == 1.5  # "11/2 lb." is 1 1/2
    assert get(ps, "Orb").fields["type"] == "SCF" and get(ps, "Amulet").fields["type"] == "SCF"
    assert get(ps, "Waterskin").fields["weight"] == 5  # "5 lb. (full)"
    assert failed(get(ps, "Waterskin")) == []
    assert get(ps, "Signet ring").fields.get("value") is None  # "S gp" does not read
    assert [c.name for c in failed(get(ps, "Signet ring"))] == ["cost-valid", "weight-valid"]


def test_poison_and_potion_rows_are_left_to_their_types():
    names = {p.name for p in items()}
    assert "Poison, basic (vial)" not in names and "Potion of healing" not in names


def test_tools_mounts_tack_and_vessels():
    ps = items()
    assert get(ps, "Alchemist's supplies").fields["type"] == "AT"
    assert get(ps, "Dice set").fields["type"] == "GS" and get(ps, "Lute").fields["type"] == "INS"
    t = get(ps, "Thieves' tools")
    assert t.fields == {"type": "T", "value": 2500, "weight": 1, "entries": t.fields["entries"]}
    assert t.fields["entries"][0].startswith("This set of tools includes a small file")
    assert get(ps, "Warhorse").fields == {"type": "MNT", "value": 40000}
    saddle = get(ps, "Saddle, exotic")  # the table mixes tack and vehicles: no type
    assert saddle.fields["value"] == 6000 and saddle.fields["weight"] == 40 and "type" not in saddle.fields
    # "Saddles." names no row; the rows' names start "Saddle,"
    assert saddle.fields["entries"][0].startswith("A military saddle braces the rider")
    assert get(ps, "Galley").fields == {"type": "SHP", "value": 3000000}
    assert "Vehicles (land or water)" not in {p.name for p in ps}


def test_trade_goods_food_and_coins():
    ps = items()
    assert get(ps, "1 lb. of wheat").fields == {"type": "TG", "value": 1}
    assert get(ps, "1 lb. of platinum").fields == {"type": "TG", "value": 50000}
    assert get(ps, "Ale, gallon").fields == {"type": "FD", "value": 20}
    assert get(ps, "Wine, fine (bottle)").fields == {"type": "FD", "value": 1000}
    assert get(ps, "Gold (gp)").fields == {"type": "$C", "value": 100}
    assert get(ps, "Platinum (pp)").fields == {"type": "$C", "value": 1000}


# ---------- DMG treasure tables, explosives and firearm ammunition ----------
def test_dmg_gemstones_and_art_objects():
    ps = items("dmg")
    az = get(ps, "Azurite")
    assert az.fields == {"type": "$G|DMG", "value": 1000, "entries": ["opaque mottled deep blue"]}
    assert get(ps, "Banded agate").fields["entries"] == ["translucent striped brown, blue, white, or red"]
    assert get(ps, "Black opal").fields["value"] == 100000
    assert get(ps, "Silver ewer").fields == {"type": "$A|DMG", "value": 2500}
    gems = [p for p in ps if p.fields.get("type") == "$G|DMG"]
    art = [p for p in ps if p.fields.get("type") == "$A|DMG"]
    assert len(gems) == 52 and len(art) == 48
    assert all(failed(p) == [] for p in gems + art)


def test_dmg_explosives_and_ammunition():
    ps = items("dmg")
    assert get(ps, "Bomb").fields == {"type": "EXP|DMG", "value": 15000, "weight": 1}
    assert get(ps, "Dynamite (stick)").fields["weight"] == 1
    assert get(ps, "Renaissance bullets (10)").fields == {"type": "AF|DMG", "value": 300, "weight": 2}
    assert get(ps, "Modern bullets (10)").fields == {"type": "AF|DMG", "weight": 1}
    assert get(ps, "Energy cell").fields == {"type": "AF|DMG", "weight": 5 / 16}  # "5 oz."
    assert not any(p.name in ("Pistol", "Musket") for p in ps)


# ---------- SCAG: an instrument list printed as lead-ins ----------
def test_scag_musical_instruments():
    ps = items("scag")
    assert len(ps) == 12
    b = get(ps, "Birdpipes")
    assert b.fields["type"] == "INS" and b.fields["entries"][0].startswith("Pan pipes or satyr pipes")
    assert get(ps, "Zulkoon").fields["entries"][0].endswith("but sinister, sound.")
    assert all(failed(p) == [] for p in ps)


def test_instrument_words_in_a_proficiency_line_are_not_a_list_heading():
    def ln(text, y, x=100):
        return Line(text=text, box=(x, y, x + 600, y + 30), height=30, page=1)
    body = [ln("Tool Proficiencies: Disguise kit, one type of", 100), ln("musical instrument", 132, 130),
            ln("Saving Throws: Strength, Dexterity", 164), ln("Skills: Choose two", 196)]
    assert parse_items_mundane(body, ParseContext(slug="phb14", source="PHB", pages={})) == []


# ---------- schema ----------
def test_fields_validate_against_the_item_schema():
    schema = json.loads((SCHEMAS / "ItemFields.schema.json").read_text(encoding="utf-8"))
    for book in BOOKS:
        for p in items(book):
            jsonschema.validate(p.fields, schema)
            assert p.type == "Item" and p.text and p.lines
