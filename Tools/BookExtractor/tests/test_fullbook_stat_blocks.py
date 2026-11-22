"""Stat-block cases that passed every fixture page but regressed on whole-book extraction (mm14)."""
import gzip
import json

import pytest
from regression_run import DATA

from bookextractor.pages_io import extract_pages, page_from_dict

_RAW = json.loads(gzip.decompress((DATA / "fullbook_cases.json.gz").read_bytes()))


def _monsters(case: str) -> dict[str, dict]:
    pages = [page_from_dict(_RAW["pages"][f"mm14:{n}"]) for n in _RAW["cases"][case]["pages"]]
    return {e["name"]: e for e in extract_pages(pages, "mm14") if e["type"] == "Monster"}


def _failed(e: dict) -> list[str]:
    return [c["name"] for c in e["checks"] if not c["passed"]]


def test_chimera_lore_remainder_stays_out_of_the_block():
    chimera = _monsters("chimera")["Chimera"]
    assert "stat-block-tail" not in _failed(chimera)
    assert len(chimera["canonicalText"]) < 1500


def test_green_hag_ends_before_the_night_hag_lore():
    hag = _monsters("green-hag")["Green Hag"]
    assert "stat-block-tail" not in _failed(hag)
    assert len(hag["canonicalText"]) < 2200


def test_ancient_copper_dragon_keeps_its_actions_and_bronze_wyrmling_does_not_swallow_them():
    found = _monsters("ancient-copper-dragon")
    assert found["Ancient Copper Dragon"]["fields"].get("action")
    assert len(found["Bronze Dragon Wyrmling"]["canonicalText"]) < 1500


def test_death_tyrant_ends_before_the_spectator_lore():
    assert "stat-block-tail" not in _failed(_monsters("death-tyrant")["Death Tyrant"])


def test_demilich_lair_heading_in_lore_does_not_veto_the_block_end():
    demilich = _monsters("demilich")["Demilich"]
    assert "stat-block-tail" not in _failed(demilich)
