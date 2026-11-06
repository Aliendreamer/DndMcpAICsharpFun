import json
from pathlib import Path

import pytest

from bookextractor.model import load_ocr_page
from bookextractor.parsers.charcommon import SUBCLASS_LEVELS, ordinal
from bookextractor.tables import read_table

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
