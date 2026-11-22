"""Two-column stat blocks (design D3 #4): a block cut at the foot of the left column continues in the right."""
import gzip
import json

from regression_run import DATA, load_fixture

from bookextractor.pages_io import extract_pages, page_from_dict

PAGES, ENTRIES = load_fixture()
CASES = json.loads(gzip.decompress((DATA / "pattern_cases.json.gz").read_bytes()))


def _entity(entities: list[dict], entry_id: str) -> dict:
    return next(x for x in entities if x["id"] == entry_id)


def _fixture_extract(entry_id: str) -> list[dict]:
    e = next(x for x in ENTRIES if x["id"] == entry_id)
    return extract_pages([PAGES[f"{e['slug']}:{n}"] for n in sorted(e["ctx_pages"])], e["slug"])


def test_gold_dragon_continues_in_right_column():
    gold = _entity(_fixture_extract("mm14.monster.adult-gold-draon"), "mm14.monster.adult-gold-draon")
    assert "Weakening Breath" in gold["canonicalText"]
    assert "legendary" in gold["fields"]


def test_jackalwere_keeps_sleep_gaze():
    j = _entity(_fixture_extract("mm14.monster.ackalwere"), "mm14.monster.ackalwere")
    assert "Sleep Gaze" in [a["name"] for a in j["fields"]["action"]]


def test_bronze_dragon_does_not_end_mid_sentence():
    c = CASES["cases"]["mm14.monster.adult-bronze-dragon"]
    pages = [page_from_dict(CASES["pages"][f"{c['slug']}:{n}"]) for n in sorted(c["ctx_pages"])]
    bronze = _entity(extract_pages(pages, c["slug"]), "mm14.monster.adult-bronze-dragon")
    assert not bronze["canonicalText"].rstrip().endswith(",")
    assert "Repulsion Breath" in bronze["canonicalText"]


def test_continuation_text_is_not_duplicated_in_lore():
    ents = _fixture_extract("mm14.monster.adult-gold-draon")
    lore = [e["id"] for e in ents if e["type"] == "Lore" and "Weakening Breath" in e["canonicalText"]]
    assert lore == []
