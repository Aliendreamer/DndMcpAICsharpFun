"""Text a stop rule cut off a stat block's tail, which no entity holds, is kept as a flagged Lore entity."""
import gzip
import json

import pytest
from regression_run import DATA

from bookextractor.pages_io import extract_pages, page_from_dict

_RAW = json.loads(gzip.decompress((DATA / "fullbook_cases.json.gz").read_bytes()))


def _entities(case: str) -> list[dict]:
    c = _RAW["cases"][case]
    return extract_pages([page_from_dict(_RAW["pages"][f"{c['slug']}:{n}"]) for n in c["pages"]], c["slug"])


@pytest.mark.parametrize("case,line,monster_id", [
    ("sidebar-xge", "Coordinating a regular schedule of D&D game sessions", "xge.monster.tiny-servant"),
    ("sidebar-hag", "A night hag carries two very rare magic items", "mm14.monster.night-hag"),
    ("sidebar-ggr", "Under the authority of the Obzedat", None),
])
def test_a_detached_sidebar_is_a_flagged_lore_entity_not_part_of_the_block(case, line, monster_id):
    ents = _entities(case)
    holders = [e for e in ents if line in e["canonicalText"]]
    assert holders, f"{line!r} is in no entity"
    assert all(e["type"] == "Lore" for e in holders)
    assert any(not c["passed"] and c["name"] == "sidebar-detached" for e in holders for c in e["checks"])
    if monster_id:
        assert line not in next(e for e in ents if e["id"] == monster_id)["canonicalText"]


def test_a_one_sentence_remnant_under_an_owned_section_is_kept():
    # tce p76: the Wizard's "The wizard class receives new features ..." sits under the owned "Limited Wish"
    assert any("The wizard class receives new features" in e["canonicalText"] for e in _entities("tce-wizard"))
