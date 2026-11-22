"""Stat-block cases that passed every fixture page but regressed on whole-book extraction (mm14)."""
import gzip
import json

import pytest
from regression_run import DATA

from bookextractor.pages_io import extract_pages, page_from_dict

_RAW = json.loads(gzip.decompress((DATA / "fullbook_cases.json.gz").read_bytes()))


def _monsters(case: str) -> dict[str, dict]:
    slug = _RAW["cases"][case]["slug"]
    pages = [page_from_dict(_RAW["pages"][f"{slug}:{n}"]) for n in _RAW["cases"][case]["pages"]]
    return {e["name"]: e for e in extract_pages(pages, slug) if e["type"] == "Monster"}


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


@pytest.mark.parametrize("case,name", [("mpmm-drow", "Drow Arachnomancer"), ("mpmm-giff", "Giff"),
                                       ("mpmm-troll", "Spirit Troll"), ("mpmm-warlock", "Warlock of the Fiend")])
def test_lore_on_the_next_page_stays_out_of_a_block_ended_by_a_foot_or_a_stray_glyph(case, name):
    # a lone drop-cap glyph ("G") or a "CHAPTER 2 | BESTIARY" foot ends the block's page; the lore after it is not its
    monster = _monsters(case)[name]
    assert "stat-block-tail" not in _failed(monster)
    assert len(monster["canonicalText"]) < 3200


def test_plain_lore_in_the_right_column_is_not_glued_after_a_block_that_ends_at_the_left_foot():
    # ggr p168: the block ends at the left column's foot; the right column holds only lore ("Day 3 Chaos")
    pages = [page_from_dict(_RAW["pages"][f"ggr:{n}"]) for n in _RAW["cases"]["ggr-goblin"]["pages"]]
    found = {e["id"]: e for e in extract_pages(pages, "ggr")}
    assert len(found["ggr.monster.goblin-gang-member"]["canonicalText"]) < 1100
    assert "ggr.lore.day-3-chaos" in found
