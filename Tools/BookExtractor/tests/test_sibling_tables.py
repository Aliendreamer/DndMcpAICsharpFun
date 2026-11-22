"""Sibling tables: a table's title over a dice header starts its own entity (XGE 94-97, TCE 149-150)."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))

from regression_run import load_fixture

from bookextractor.pages_io import extract_pages

_PAGES, _ = load_fixture()


def _run(slug: str, first: int, last: int) -> dict[str, dict]:
    pages = sorted((p for k, p in _PAGES.items() if k.startswith(slug + ":") and first <= p.number <= last),
                   key=lambda p: p.number)
    return {e["name"]: e for e in extract_pages(pages, slug)}


def test_xge_encounter_tables_are_separate_entities():
    named = _run("xge", 92, 100)
    for name in ("Coastal Encounters (Levels 5-10)", "Coastal Encounters (Levels 11-16)",
                 "Desert Encounters (Levels 1-4)"):
        assert name in named, sorted(named)
    assert "Desert Encounters" not in named["Coastal Encounters (Levels 5-10)"]["canonicalText"]
    assert "Coastal Encounters" not in named["Desert Encounters (Levels 1-4)"]["canonicalText"]


def test_tce_monsters_desires_tables_are_separate_entities():
    named = _run("tce", 148, 151)
    assert "Beasts" in named and "Fiends" in named, sorted(named)
    assert "Fresh meat" in named["Beasts"]["canonicalText"]
    assert "Fresh meat" not in named["Fiends"]["canonicalText"]
