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
    # 122: "Chapter 4: Personality and Background"; 128: a chapter 4 background page; 144: "Chapter 5: Equipment"
    # and the coin table; 149-159: the chapter
    "phb": ("phb14", "PHB", [122, 128, 144, 149, 150, 151, 152, 153, 154, 155, 156, 158, 159]),
    "dmg": ("dmg14", "DMG", [134, 135, 268]),
    "scag": ("scag", "SCAG", [125]),
    "egw": ("egw", "EGW", [71, 153, 226]),
    "erlw": ("erlw", "ERLW", [245]),
    "ggr": ("ggr", "GGR", [10]),
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


def _edit(book, page, old, new, pages=None):
    pages = pages or _pages(book)
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
    # page 128 sits under the printed "Chapter 4: Personality and Background" heading (p122): an "Abacus." lead-in
    # there is not the gear's description (chapter 5)
    pages = _edit("phb", 128, "Choose a god, a pantheon of gods, or some other", "Abacus. A frame of beads for counting.")
    assert "entries" not in get(_run("phb", pages), "Abacus").fields


def test_chapter_cross_reference_is_not_a_chapter_heading():
    # a text line starting "chapter 5 for ..." above the lead-in does not move page 128 into chapter 5
    pages = _edit("phb", 128, "traits, one ideal, one bond, and one flaw. If you can't find",
                  "chapter 5: Equipment, for the gear you start with.")
    pages = _edit("phb", 128, "Choose a god, a pantheon of gods, or some other", "Abacus. A frame of beads for counting.",
                  pages)
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
        p = get(items(), name)
        assert p.fields["entries"][0].startswith("Rope, whether made of hemp or silk")
        # one paragraph on two rows: it goes to review once, naming the lead-in and the rows
        c = next(c for c in p.checks if c.name == "entries-shared")
        assert not c.passed and "'Rope'" in c.reason and "'Rope, silk (50 feet)'" in c.reason
    # a bracket's comma is not the label's comma; an exact lead-in carries no fallback check
    bb = get(items(), "Ball bearings (bag of 1,000)")
    assert not {"entries-shared", "entries-from-lead-in"} & {c.name for c in bb.checks}


def test_fallback_on_one_row_is_recorded_and_passes():
    t = get(items(), "Tent, two-person")
    assert t.fields["entries"][0].startswith("A simple and portable canvas shelter")
    c = next(c for c in t.checks if c.name == "entries-from-lead-in")
    assert c.passed and "'Tent'" in c.reason and "'Tent, two-person'" in c.reason
    assert failed(t) == []


def test_saddles_paragraph_on_four_saddles_goes_to_review():
    for kind in ("exotic", "military", "pack", "riding"):
        p = get(items(), f"Saddle, {kind}")
        c = next(c for c in p.checks if c.name == "entries-shared")
        assert not c.passed and "'Saddles'" in c.reason and "'Saddle, pack'" in c.reason


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


def test_last_pack_of_the_sidebar_stops_where_the_column_text_resumes():
    # p152: the inset "Equipment Packs" sidebar ends the right column inside the Holy Water description; the text
    # that resumes on p153 ("A cleric or paladin may create holy water ...") is Holy Water's, not Scholar's Pack's
    ps = items()
    scholar = get(ps, "Scholar's Pack")
    assert scholar.fields["entries"] == [scholar.fields["entries"][0]]
    assert scholar.fields["entries"][0].endswith("a small knife.")
    assert "holy water" not in scholar.text.lower()
    assert {l.page for l in scholar.lines} == {152}
    water = get(ps, "Holy water (flask)")
    assert water.fields["entries"][0].startswith("As an action, you can splash the contents")
    assert water.fields["entries"][1].startswith("A cleric or paladin may create holy water by performing")
    assert water.fields["entries"][1].endswith("expend a 1st-level spell slot.")
    assert not any(l.text.startswith("Equipment Packs") or "Pack (" in l.text for l in water.lines)
    assert failed(water) == [] and failed(scholar) == []


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
    saddle = get(ps, "Saddle, exotic")
    assert saddle.fields["value"] == 6000 and saddle.fields["weight"] == 40 and saddle.fields["type"] == "TAH"
    # "Saddles." names no row; the rows' names start "Saddle,"
    assert saddle.fields["entries"][0].startswith("A military saddle braces the rider")
    c = next(c for c in saddle.checks if c.name == "name-from-group")
    assert c.passed and "'Saddle'" in c.reason and "'Exotic'" in c.reason
    assert "name-from-group" not in {c.name for c in get(ps, "Saddlebags").checks}
    assert get(ps, "Galley").fields == {"type": "SHP", "value": 3000000}
    # the "Tack, Harness, and Drawn Vehicles" table: a row naming a drawn vehicle is VEH, any other row tack
    for name in ("Carriage", "Cart", "Chariot", "Sled", "Wagon"):
        assert get(ps, name).fields["type"] == "VEH", name
    for name in ("Bit and bridle", "Feed (per day)", "Saddlebags", "Stabling (per day)", "Barding"):
        assert get(ps, name).fields["type"] == "TAH", name
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


def test_treasure_rows_are_not_attested_as_table_labels():
    # P17: a gem/art row's value comes from its tier title, not a cell checked per row, so its label is not
    # attested by the table (name-words-known judges it like any printed name)
    ps = items("dmg")
    treasure = [p for p in ps if p.fields.get("type") in ("$G|DMG", "$A|DMG")]
    assert treasure and not any(p.table_row for p in treasure)


def test_dmg_explosives_and_ammunition():
    ps = items("dmg")
    assert get(ps, "Bomb").fields == {"type": "EXP|DMG", "value": 15000, "weight": 1}
    assert get(ps, "Dynamite (stick)").fields["weight"] == 1
    rb = get(ps, "Renaissance bullets (10)")
    assert rb.fields == {"type": "AF|DMG", "value": 300, "weight": 2}
    c = next(c for c in rb.checks if c.name == "name-from-group")
    assert c.passed and "'Renaissance'" in c.reason and "'Bullets (10)'" in c.reason
    assert "name-from-group" not in {c.name for c in get(ps, "Energy cell").checks}
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


# ---------- goods sidebars: lead-ins under a heading naming a kind of goods ----------
def test_egw_herbs_contraband_and_alchemical_items():
    """EGW "Herbs of the Othemoor" (p.70, "Cost: 3 gp per dose."), "Blightshore Contraband" (p.152, "Black sap is
    valued at 300 gp per dose.") and "Alchemical Items" (p.225, no price) sidebars: each lead-in is an Item."""
    ps = items("egw")
    assert sorted(p.name for p in ps) == ["Black Sap", "Blasting Powder", "Blight Ichor", "Explosive Seed",
                                          "Muroosa Balm", "Olisuba Leaf", "Soothsalts", "Theki Root",
                                          "Willowshade Oil"]
    want = {"Theki Root": 300, "Olisuba Leaf": 5000, "Willowshade Oil": 3000, "Black Sap": 30000,
            "Blight Ichor": 20000, "Soothsalts": 15000}
    for name, value in want.items():
        assert get(ps, name).fields.get("value") == value, name
        assert failed(get(ps, name)) == [], name
    t = get(ps, "Theki Root")
    assert "type" not in t.fields and t.fields["entries"][0].startswith("This thick marsh root tastes bitter")
    assert t.fields["entries"][-1].endswith("Cost: 3 gp per dose.")
    for name in ("Blasting Powder", "Explosive Seed"):  # no price printed: review whether it is an item
        assert "value" not in get(ps, name).fields
        assert [c.name for c in failed(get(ps, name))] == ["goods-attested"]
        assert "'Alchemical Items'" in failed(get(ps, name))[0].reason
    assert get(ps, "Explosive Seed").fields["entries"][0].endswith("half as much damage on a successful one.")


def test_two_prices_in_a_cost_sentence_go_to_review():
    """Muroosa Balm: "Cost: 1 gp per dose for sunburn, 100 gp for a full application against fire." names two
    prices: no value, and the entity goes to review."""
    m = get(items("egw"), "Muroosa Balm")
    assert "value" not in m.fields
    assert [c.name for c in failed(m)] == ["cost-valid"] and "1 gp" in failed(m)[0].reason


def test_erlw_narcotics_sidebar():
    ps = items("erlw")
    assert sorted(p.name for p in ps) == ["Dragon's Blood", "Dreamlily"]
    d = get(ps, "Dreamlily")
    # "costs around 1 gp, or up to ten times that" is no Cost cue: no value, and no price attests the item
    assert "value" not in d.fields and [c.name for c in failed(d)] == ["goods-attested"]
    assert d.fields["entries"][-1].endswith("the duration or the price might vary accordingly.")
    assert get(ps, "Dragon's Blood").fields["entries"][0].startswith("Introduced into Sharn by Daask")


def test_lead_in_continuing_a_sentence_is_not_an_item():
    """"Sharn. The following drugs ..." continues the sentence above it: not a lead-in."""
    assert "Sharn" not in [p.name for p in items("erlw")]


def test_heading_without_a_goods_word_starts_no_goods_section():
    pages = _edit("erlw", 245, "NaRCotICS in ShaRn", "Crime in Sharn", pages=_pages("erlw"))
    assert _run("erlw", pages) == []


def test_ggr_currency_matrix():
    """GGR p.9 Ravnica Currency: a Value column, then one column of coin names per guild; a name wrapping onto a
    second line is one cell; the same coin under two guilds is one Item."""
    coins = {p.name: p for p in items("ggr") if p.fields.get("type") == "$C"}
    assert {n: p.fields["value"] for n, p in coins.items()} == {
        "Copper zib": 1, "Copper alms-coin": 1, "Silver 25-zib coin": 25, "Gold zino": 100, "Gold 5-zino coin": 500,
        "Platinum 10-zino coin": 1000, "Platinum 100-zino coin": 10000}
    assert all(failed(p) == [] for p in coins.values())


def test_currency_cell_missing_its_first_line_is_not_named():
    """The 1 ep row's cell reads only "zib coin" (its first line, "electrum 50-", is lost to the OCR): a name is
    never made from the part of a cell that survived."""
    assert not [p for p in items("ggr") if "zib coin" == p.name.lower()]


def test_coin_name_repeated_with_another_value_fails():
    pages = _edit("ggr", 10, "gold zino", "gold 5-zino coin")  # now under 1 gp as well as 5 gp
    c = get(_run("ggr", pages), "Gold 5-zino coin")
    assert [x.name for x in failed(c)] == ["cost-valid"]
    assert "100" in failed(c)[0].reason and "500" in failed(c)[0].reason


def test_moorbounders_heading_with_a_price_range():
    """EGW p.132: "MOORBOUNDERS" and "Moorbounders can cost anywhere from 300 to 500 gp each": an Item with no
    value (a range), the range recorded in a passing check."""
    ps = _run("egw", {n: load_ocr_page(FIX / "egw" / f"{n:03d}.json", n) for n in (133,)})
    m = get(ps, "Moorbounders")
    assert "value" not in m.fields and failed(m) == []
    c = next(c for c in m.checks if c.name == "price-range")
    assert "300 to 500 gp" in c.reason


# ---------- schema ----------
def test_fields_validate_against_the_item_schema():
    schema = json.loads((SCHEMAS / "ItemFields.schema.json").read_text(encoding="utf-8"))
    for book in BOOKS:
        for p in items(book):
            jsonschema.validate(p.fields, schema)
            assert p.type == "Item" and p.text and p.lines
