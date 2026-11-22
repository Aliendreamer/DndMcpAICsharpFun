"""Table row vs heading: a numbered table row is never an item name or heading (GGR 69-71)."""
import gzip
import json
from pathlib import Path

from bookextractor.pages_io import extract_pages, page_from_dict

CASES = Path(__file__).parent / "data" / "pattern_cases.json.gz"


def _ggr() -> list[dict]:
    raw = json.loads(gzip.decompress(CASES.read_bytes()))
    pages = sorted((page_from_dict(v) for k, v in raw["pages"].items() if k.startswith("ggr:")),
                   key=lambda p: p.number)
    return extract_pages(pages, "ggr")


def test_table_row_is_no_entity():
    assert not [e for e in _ggr() if e["name"].lower().startswith("5 i helped")]


def test_sections_after_the_contacts_table_are_found():
    named = {e["name"]: e for e in _ggr()}
    for name in ("How Do I Fit In?", "An Izzet Party", "Rank and Renown"):
        assert named[name]["type"] in ("Lore", "Rule")


def test_row_text_is_kept():
    text = " ".join(e["canonicalText"] for e in _ggr())
    assert "acquire an Izzet weapon" in text
