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


def _result(slug: str, pages: list):
    from bookextractor.assemble import ALL_TYPES, _squash, extract_book
    r = extract_book(pages, slug, set(ALL_TYPES))
    held = _squash("\n".join(e["canonicalText"] for e in r.entities))
    return r, [(pg, t) for pg, t in r.left_out if _squash(t) in held]


def test_left_out_never_lists_a_line_an_entity_holds_on_the_regression_fixture():
    from regression_run import load_fixture
    pages, entries = load_fixture()
    for slug in ("mm14", "xge", "ggr"):
        numbers = sorted({n for e in entries if e["slug"] == slug for n in e["ctx_pages"]})
        r, both = _result(slug, [pages[f"{slug}:{n}"] for n in numbers])
        assert r.left_out, slug  # the fixture pages do leave text out (running heads, art, notes)
        assert both == [], (slug, both[:3])


def test_left_out_never_lists_a_line_an_entity_holds_on_the_whole_book_cases():
    for case, c in _RAW["cases"].items():
        if len(c["pages"]) > 30:
            continue
        pages = [page_from_dict(_RAW["pages"][f"{c['slug']}:{n}"]) for n in c["pages"]]
        _, both = _result(c["slug"], pages)
        assert both == [], (case, both[:3])
