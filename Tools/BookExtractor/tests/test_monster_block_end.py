"""Stat-block end (design D3 #3): a block ends at its last entry; header fields stop at the first entry lead-in."""
from regression_run import load_fixture

from bookextractor.pages_io import extract_pages

PAGES, ENTRIES = load_fixture()


def _extract(entry_id: str) -> list[dict]:
    e = next(x for x in ENTRIES if x["id"] == entry_id)
    return extract_pages([PAGES[f"{e['slug']}:{n}"] for n in sorted(e["ctx_pages"])], e["slug"])


def _entity(entities: list[dict], entry_id: str) -> dict:
    return next(x for x in entities if x["id"] == entry_id)


def test_homunculus_servant_ends_at_its_last_reaction():
    ents = _extract("erlw.monster.homunculus-servant")
    h = _entity(ents, "erlw.monster.homunculus-servant")
    assert h["canonicalText"].rstrip().endswith("feet of you.")
    assert h["fields"]["languages"] == ["understands the languages you speak"]
    assert h["fields"]["trait"] and h["fields"]["action"] and h["fields"]["reaction"]


def test_detached_text_stays_in_some_entity():
    names = {e["name"] for e in _extract("erlw.monster.homunculus-servant")}
    assert {"Replicate Magic Item", "Group Patrons"} <= names


def test_traits_never_land_in_languages():
    s = _entity(_extract("mpmm.monster.sacred-statue"), "mpmm.monster.sacred-statue")
    assert s["fields"]["languages"] == ["the languages the eidolon knew in life"]
    assert [t["name"] for t in s["fields"]["trait"]][:1] == ["False Appearance"]


def _bgg_extract() -> list[dict]:
    nums = sorted({n for e in ENTRIES if e["slug"] == "bgg" for n in e["ctx_pages"]})
    return extract_pages([PAGES[f"bgg:{n}"] for n in nums], "bgg")


def test_cut_lore_lands_in_another_entity():
    """Lore that sat after a stat block is not the monster's: it must survive as an entity of its own."""
    ents = _bgg_extract()
    phrase = "Dust hulks are descended"
    holders = [e for e in ents if phrase in e["canonicalText"]]
    assert holders and all(e["type"] != "Monster" for e in holders)
