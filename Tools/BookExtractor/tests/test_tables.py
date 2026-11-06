import json
from pathlib import Path

import pytest

from bookextractor.model import Line, Page, load_ocr_page
from bookextractor.parsers.charcommon import SUBCLASS_LEVELS, ordinal
from bookextractor.tables import read_row_tables, read_rows, read_table

FIX = Path(__file__).parent / "fixtures" / "ocr" / "phb"
FIVETOOLS = Path(__file__).parents[3] / "5etools" / "class"


def page(n):
    return load_ocr_page(FIX / f"{n:03d}.json", n)


def gain_levels_from_5etools(cls: str) -> list[int]:
    data = json.loads((FIVETOOLS / f"class-{cls.lower()}.json").read_text(encoding="utf-8"))
    want = "TCE" if cls == "Artificer" else "PHB"
    rec = next(c for c in data["class"] if c["name"] == cls and c["source"] == want)
    out = []
    for f in rec["classFeatures"]:
        if isinstance(f, dict) and f.get("gainSubclassFeature"):
            out.append(int(f["classFeature"].rsplit("|", 1)[1]))
    return out


def test_fighter_table():
    t = read_table(page(72), "Level")
    assert len(t.rows) == 20 and t.rows[0][1] == "+2" and t.rows[19][1] == "+6"
    assert "Action Surge" in t.rows[1][2]


def test_barbarian_table_multiline_cells():
    t = read_table(page(48), "Level")
    assert len(t.rows) == 20
    assert t.rows[0][2] == "Rage, Unarmored Defense"
    assert t.rows[0][3] == "2" and t.rows[0][4] == "+2"
    assert "Rage" in t.headers[3] and "Damage" in t.headers[4]


def test_wizard_table_spell_columns():
    t = read_table(page(114), "Level")
    assert len(t.rows) == 20 and len(t.headers) >= 12 and t.rows[0][3] == "3"   # cantrips known at 1st
    assert t.rows[0][4] == "2" and t.rows[2][5] == "2"


def test_no_table_on_prose_page():
    assert read_table(page(242), "Level") is None


@pytest.mark.skipif(not FIVETOOLS.exists(), reason="5etools/ absent")
def test_subclass_levels_match_5etools():
    for cls, levels in SUBCLASS_LEVELS.items():
        assert levels == gain_levels_from_5etools(cls), cls


def test_ordinal():
    assert [ordinal(x) for x in ("1st", "lst", "2nd", "3rd", "20th", "x")] == [1, 1, 2, 3, 20, None]


def test_line_running_into_next_column_is_split():
    t = read_table(page(48), "Level")
    assert t.rows[19][2] == "Primal Champion" and t.rows[19][3] == "Unlimited"


def test_long_cell_overhanging_its_header_stays_whole():
    assert read_table(page(114), "Level").rows[0][2] == "Spellcasting, Arcane Recovery"
    assert read_table(page(58), "Level").rows[7][2] == \
        "Ability Score Improvement, Destroy Undead (CR 1), Divine Domain feature"


def test_first_row_cell_starting_above_header_bottom():
    assert read_table(page(66), "Level").rows[0][2] == "Druidic, Spellcasting"


def test_dropped_level_cell_keeps_later_rows():
    t = read_table(page(78), "Level")   # the OCR lost the Monk's "3rd"
    assert len(t.rows) == 20 and t.rows[2][0] == "" and t.rows[3][0] == "4th"
    assert t.rows[2][5] == "Monastic Tradition, Deflect Missiles"


# ---------- read_rows: label-anchored tables (equipment) ----------
WEAPONS = ["Name", "Cost", "Damage", "Weight", "Properties"]
ARMOR = ["Armor", "Cost", "Armor Class (AC)", "Strength", "Stealth", "Weight"]
GEAR = ["Item", "Cost", "Weight"]


def row(t, label):
    return next(r for r in t.rows if r.label == label)


def test_phb_weapons():
    t = read_rows(page(150), WEAPONS)
    r = row(t, "Longsword")
    assert r.cells["Cost"] == "15 gP" and r.cells["Damage"] == "1d8 slashing"
    assert r.cells["Properties"] == "Versatile (1d10)" and r.group.startswith("Martial Melee")
    assert row(t, "Club").group == "Simple Melee Weapons" and row(t, "Net").group == "Martial Ranged Weapons"
    assert row(t, "Greataxe").cells["Cost"] == "30 gp" and row(t, "Glaive").cells["Cost"] == "20 gp"
    assert row(t, "Crossbow, heavy").cells["Properties"] == "Ammunition (range 100/400), heavy, loading, two-handed"
    assert row(t, "Unarmedstrike").cells["Cost"] == ""  # the book prints no cost
    assert len(t.rows) == 38 and t.headers == WEAPONS and t.page == 150


def test_phb_armor():
    t = read_rows(page(146), ARMOR)
    r = row(t, "Chain mail")
    assert r.cells["Strength"] == "Str 13" and r.cells["Cost"] == "75 gp" and r.group == "Heavy Armor"
    assert r.cells["Stealth"] == "Disadvantage" and r.cells["Armor Class (AC)"] == "16"
    assert row(t, "Ring mail").cells["Cost"] == "30 gp"
    assert row(t, "Shield").group == "Shield" and row(t, "Padded").group == "Light Armor"
    assert len(t.rows) == 13 and "PART" not in " ".join(row(t, "Shield").cells.values())


def test_phb_gear_two_tables_side_by_side():
    t = read_rows(page(151), GEAR)
    labels = [r.label for r in t.rows]
    assert labels[0] == "Abacus" and labels[-1] == "Whetstone" and "Holy symbol" not in labels
    assert row(t, "Ball bearings (bag of 1,000)").cells == {"Cost": "1 gp", "Weight": "2 lb."}
    assert row(t, "Arrows (20)").group == "Ammunition" and row(t, "Orb").group == "Arcane focus"
    assert row(t, "Backpack").group is None and row(t, "Amulet").group == "Holy symbol"
    assert row(t, "Hourglass").group is None and row(t, "Hourglass").cells["Cost"] == "25 gp"
    assert row(t, "Fishing tackle").group is None  # back at the heading's indent: out of Druidic focus


def test_phb_tools_beside_prose():
    t = read_rows(page(155), GEAR)
    assert row(t, "Alchemist's supplies").group == "Artisan's tools"
    assert row(t, "Herbalism kit").group is None and row(t, "Bagpipes").group == "Musical instrument"
    assert row(t, "Vehicles (land or water)").cells["Cost"] == "*"
    assert all("proficiency" not in " ".join(r.cells.values()) for r in t.rows)  # the prose column is not a cell


def test_marker_stripped_and_table_ends_at_footnote():
    t = read_rows(page(154), ["Container", "Capacity"])
    r = row(t, "Backpack")
    assert r.marker == "*" and r.cells["Capacity"] == "1 cubic foot/30 pounds of gear"
    assert t.rows[-1].label == "Waterskin" and len(t.rows) == 13


def _ln(text, x0, y0, x1, h=30):
    return Line(text=text, box=(x0, y0, x1, y0 + h), height=h, page=1)


def test_wrapped_label_and_marker():
    lines = [_ln("Item", 100, 100, 160), _ln("Cost", 500, 100, 560), _ln("Weight", 650, 100, 740),
             _ln("Abacus", 100, 140, 200), _ln("2 gp", 500, 140, 560), _ln("2 lb.", 680, 140, 740),
             _ln("Case, crossbow bolt", 100, 173, 380), _ln("1 gp", 500, 173, 560), _ln("1 lb.", 680, 173, 740),
             _ln("(holds 20 bolts)†", 120, 206, 360),
             _ln("Chalk", 100, 239, 180), _ln("1 cp", 500, 239, 560)]
    t = read_rows(Page(number=1, width=1600, height=2200, lines=lines, source="ocr"), GEAR)
    assert [r.label for r in t.rows] == ["Abacus", "Case, crossbow bolt (holds 20 bolts)", "Chalk"]
    r = t.rows[1]
    assert r.marker == "†" and r.cells == {"Cost": "1 gp", "Weight": "1 lb."} and len(r.lines) == 4
    assert t.rows[2].cells == {"Cost": "1 cp", "Weight": ""}


def test_read_rows_none_without_header():
    assert read_rows(page(242), WEAPONS) is None


def test_phb_mounts_stacked_header_and_two_tables_in_a_column():
    p = page(158)
    mounts = read_rows(p, ["Item", "Cost", "Speed", "Carrying Capacity"])
    assert row(mounts, "Warhorse").cells == {"Cost": "400 gp", "Speed": "60 ft.", "Carrying Capacity": "540 lb."}
    tack = read_rows(p, ["Item", "Cost", "Weight"])
    assert tack.rows[-1].label == "Wagon" and row(tack, "Exotic").group == "Saddle"  # ends at the next table's title
    mounts, boats = read_row_tables(p, ["Item", "Cost", "Speed"])
    assert row(boats, "Galley").cells == {"Cost": "30,000 gp", "Speed": "4 mph"} and len(boats.rows) == 6
