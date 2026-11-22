"""Table and section cases that passed every fixture page but regressed on whole-book extraction.

The page runs start at the chapter's first page (the section reader needs the chapter title) and cover enough of the
chapter for the recurring-heading rules to see what the whole book shows them."""
import gzip
import json
from functools import cache

from regression_run import DATA

from bookextractor.model import Line
from bookextractor.pages_io import extract_pages, page_from_dict
from bookextractor.parsers.sections import Section
from bookextractor.parsers.world import _doubt

_RAW = json.loads(gzip.decompress((DATA / "fullbook_cases.json.gz").read_bytes()))


@cache
def _entities(case: str) -> dict[str, dict]:
    c = _RAW["cases"][case]
    pages = [page_from_dict(_RAW["pages"][f"{c['slug']}:{n}"]) for n in c["pages"]]
    return {e["id"]: e for e in extract_pages(pages, c["slug"])}


def _text(case: str, id_: str) -> str:
    return _entities(case)[id_]["canonicalText"]


def _reasons(case: str, id_: str, check: str) -> list[str]:
    return [c["reason"] for c in _entities(case)[id_]["checks"] if c["name"] == check and not c["passed"]]


def test_encounter_tables_are_not_swallowed_by_their_chapter_lead_in():
    # F1: a table title in a sibling run is not a caption, so each "X Encounters" table is its own entity
    assert len(_text("xge-encounters", "xge.rule.flight-or-fight-or")) < 2000
    assert "xge.rule.arctic-encounters-levels-1-4" in _entities("xge-encounters")


def test_monster_research_keeps_its_own_text_and_the_beast_dragon_fiend_tables_are_entities():
    # F1
    assert len(_text("tce-tables", "tce.rule.monster-research")) < 800
    for name in ("beasts", "dragons", "fiends"):
        assert f"tce.rule.{name}" in _entities("tce-tables")


def test_haunted_continuation_rows_do_not_land_in_mirror_zone_effects():
    # F2: a repeated die header is no heading, so the table's rows stay with the table that printed them
    assert len(_text("tce-tables", "tce.rule.haunted")) < 6500
    assert len(_text("tce-tables", "tce.rule.mirror-zone-effects")) > 4000


def test_folded_recurring_heading_hoists_its_kids():
    # F3: "How Do I Fit In?" recurs per guild, so it folds as a part; its kid "A Boros Party" is still an entity
    assert "ggr.lore.a-boros-party" in _entities("ggr-guilds")


def test_a_die_header_under_a_table_title_is_not_an_odd_opening():
    # F4
    assert not [r for r in _reasons("tce-tables", "tce.rule.blessed-radiance-effects", "text-complete")
                if "set like the heading" in r]


def _line(text: str, y: float, height: float) -> Line:
    return Line(text=text, box=(100, y, 600, y + height), height=height, page=1, frags=[])


def test_a_die_header_is_not_a_doubtful_heading():
    # F5: "OOLP Property", the die column of the table under the section's title, is the table's own header
    own = [_line("Dungeon Goals", 100, 40), _line("OOLP Property", 160, 40),
           _line("You find a cache of old coins hidden in a crack in the wall near the entrance.", 220, 20),
           _line("A guard dog sleeps in the corridor beyond and wakes at the slightest noise from you.", 250, 20),
           _line("Nothing happens here, and the dust lies thick over every surface in the room.", 280, 20)]
    s = Section(title="Dungeon Goals", level=1, parents=[], lines=own, page=1, complete=True, end_reason="")
    assert _doubt(s, own, 1, {}, {}) == []
    own[1] = _line("Treasure Hoard", 160, 40)  # the same shape with a real heading's words stays in doubt
    assert _doubt(s, own, 1, {}, {})


def test_lore_under_a_creatures_own_section_is_not_dropped():
    # mm14 pp85-100: "Black Dragon" (the lore) is printed under "Black Dragon Wyrmling", whose name a monster owns;
    # the sub-sections of an owned sub-section are still opened
    assert any("Black dragons dwell in swamps" in e["canonicalText"] for e in _entities("dragon-lore").values())


def test_a_folded_parts_own_text_stays_in_an_entity():
    # F3 follow-up: "How Do I Fit In?" (a recurring part) is folded; its own prose and its lead-in line must not be
    # dropped when its kid "A Boros Party" is hoisted
    assert any("As a member of the Boros Legion, you are subject to the" in e["canonicalText"]
               for e in _entities("ggr-guilds").values())
    assert any("orders of your superior officers" in e["canonicalText"] for e in _entities("ggr-guilds").values())


def test_lore_the_reader_nested_under_a_stat_blocks_actions_is_not_dropped():
    # mm14 p165: "Deep gnomes, or svirfneblin ..." is printed after the Gnoll Fang's last action; the reader nests it
    # in the owned "Actions" section. The prose the stat block does not hold is kept (flagged), never dropped
    assert any("Deep gnomes, or svirfneblin" in e["canonicalText"] for e in _entities("gnoll-lore").values())


def test_lore_printed_under_a_stat_blocks_own_heading_is_kept():
    # mm14 p345 "Bandit": "Bandits rove in gangs ..." follows the block under its heading; p230 the Mummy Lord's lair
    assert any("Bandits rove in gangs" in e["canonicalText"] for e in _entities("bandit-lore").values())
    assert any("Each undead in the lair has advantage" in e["canonicalText"] for e in _entities("mummy-lore").values())
