"""Prose no entity claimed is kept: appended to the section it continues, else a flagged Lore (never a stat block)."""
import gzip
import json

import pytest
from regression_run import DATA

from bookextractor.assemble import ALL_TYPES, extract_book
from bookextractor.coverage import reduce_text
from bookextractor.pages_io import page_from_dict

_RAW = json.loads(gzip.decompress((DATA / "fullbook_cases.json.gz").read_bytes()))

# unheld content lines per fixture case, measured before unclaimed prose was kept
_OLD_UNHELD = {
    "chimera": 0,
    "green-hag": 35,
    "ancient-copper-dragon": 0,
    "death-tyrant": 0,
    "demilich": 0,
    "mpmm-drow": 0,
    "mpmm-giff": 0,
    "mpmm-troll": 0,
    "mpmm-warlock": 0,
    "ggr-goblin": 11,
    "myconid": 0,
    "lich": 0,
    "dragon-lore": 0,
    "gnoll-lore": 24,
    "bandit-lore": 1,
    "mummy-lore": 0,
    "eladrin-lore": 28,
    "scc-lore": 22,
    "sidebar-xge": 0,
    "sidebar-hag": 0,
    "sidebar-ggr": 0,
    "tce-wizard": 0,
    "notes-bgg127": 66,
    "notes-bgg134": 0,
    "notes-ggr6": 6,
    "notes-bgg76": 61,
    "notes-bgg156": 0,
    "notes-mpmm79": 0,
    "notes-bgg152": 0,
    "notes-xge8": 56,
    "keep-bgg58": 3,
    "keep-mtf6": 16,
    "keep-mtf86": 2,
    "keep-egw206": 31,
    "keep-ftd11": 29,
    "xge-encounters": 27,
    "tce-tables": 36,
    "ggr-guilds": 234,
    "r4-bgg143": 0,
    "r4-bgg151": 0,
    "r4-bgg177": 0,
    "r4-bgg187": 0,
    "r4-bgg6": 47,
    "r4-xge126": 88,
    "r4-mtf100": 43,
    "r4-dmg14-191": 36,
    "r4-dmg14-216": 0,
    "r4-dmg14-9": 1,
    "r5-xge148": 14,
    "r5-vrgr9": 42,
    "r5-mm14-240": 0,
    "r6-yuan": 0,
    "r6-geetra": 0,
    "r6-shadar": 0,
    "r7-astral": 42,
    "r7-crystal": 0,
    "r7-runic": 0,
    "r7-avalancher": 0,
    "front-phb-index": 91,
    "front-mm14": 0,
    "front-erlw": 0,
    "front-phb-prose": 67,
    "kc-mm154": 22,
    "kc-egw34": 45,
    "kc-phb159": 15,
    "kc-erlw18": 49,
    "kc-mm107": 0,
}


def _result(case: str):
    c = _RAW["cases"][case]
    return extract_book([page_from_dict(_RAW["pages"][f"{c['slug']}:{n}"]) for n in c["pages"]], c["slug"],
                        set(ALL_TYPES))


def _holders(r, snippet: str) -> list[dict]:
    s = reduce_text(snippet)
    return [e for e in r.entities if s in reduce_text(e["canonicalText"])]


def test_unclaimed_paragraph_after_stat_block_is_flagged_lore():
    r = _result("kc-mm154")
    holders = _holders(r, "When the giants' ancient empires fell, Annam")
    assert holders, "the Annam paragraph is held by no entity"
    assert all(e["type"] == "Lore" for e in holders)
    assert any(not c["passed"] and c["name"] == "text-complete" and "unclaimed prose" in c["reason"]
               for e in holders for c in e["checks"])


def test_a_stat_block_never_gains_unclaimed_prose():
    r = _result("kc-mm154")
    for e in r.entities:
        if e["type"] == "Monster":
            assert "ancient empires fell" not in e["canonicalText"]


def test_continuation_is_appended_to_the_section_it_continues():
    # mtf p85-86: a trait-table row ends mid-sentence at a column foot; its last words open the next column
    for case in ("keep-mtf86", "r4-mtf100", "r7-astral", "keep-mtf6"):
        r = _result(case)
        host = _holders(r, "merriment takes me to the edge of violence")
        if host:
            break
    assert [e["id"] for e in host] == ["mtf.lore.githzerai-flaws"]
    assert not any(c["name"] == "text-complete" and "unclaimed prose" in c["reason"] and not c["passed"]
                   for c in host[0]["checks"])


@pytest.mark.parametrize("case,snippet", [
    ("kc-egw34", "According to the teachings of the Kryn and the Umavi"),
    ("kc-erlw18", "Kalashtar are humanoids bound to spirits"),
    ("kc-phb159", "Wretched. You live in inhumane conditions"),
])
def test_kryn_kalashtar_lifestyle_text_is_held(case, snippet):
    assert _holders(_result(case), snippet), f"{snippet!r} is held by no entity"


def test_fixture_cases_unheld_does_not_rise():
    rose = {}
    for case, old in _OLD_UNHELD.items():
        new = _result(case).coverage["unheld"]
        if new > old:
            rose[case] = (old, new)
    assert not rose, rose
