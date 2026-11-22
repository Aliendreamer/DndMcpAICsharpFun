"""Sidebars / quotes (design D3 #5): a headed sidebar is its own entity; a headingless quote box is left out."""
import gzip
import json

from regression_run import DATA, load_fixture

from bookextractor.pages_io import extract_pages, page_from_dict

PAGES, ENTRIES = load_fixture()
CASES = json.loads(gzip.decompress((DATA / "pattern_cases.json.gz").read_bytes()))


def _werewolf_pages() -> list:
    case = CASES["cases"]["mm14.monster.werewolf"]
    return [page_from_dict(CASES["pages"][f"mm14:{n}"]) for n in sorted(case["ctx_pages"])]


def test_werewolf_quote_box_is_in_no_entity():
    """The headingless "Company of the Black Moon" quote that opens page 212 stays out of every entity."""
    ents = extract_pages(_werewolf_pages(), "mm14")
    assert not [e["id"] for e in ents if "Black" in e["canonicalText"] or "ADVENTURERS LOYAL" in e["canonicalText"]]
    werewolf = next(e for e in ents if e["id"] == "mm14.monster.werewolf")
    assert werewolf["fields"]["action"]


def test_stat_block_before_the_quote_keeps_its_last_action():
    ents = extract_pages(_werewolf_pages(), "mm14")
    tiger = next(e for e in ents if e["id"] == "mm14.monster.weretiger")
    assert "Longbow (Humanoid or Hybrid Form Only)" in [a["name"] for a in tiger["fields"]["action"]]


def test_headed_sidebar_is_its_own_entity():
    e = next(x for x in ENTRIES if x["id"] == "phb14.rule.concentration")
    ents = extract_pages([PAGES[f"phb14:{n}"] for n in sorted(e["ctx_pages"])], "phb14")
    conc = next(x for x in ents if x["name"].lower() == "concentration")
    assert "Schools of Magic" not in conc["canonicalText"]
    assert any(x["name"] == "The Schools of Magic" for x in ents)
