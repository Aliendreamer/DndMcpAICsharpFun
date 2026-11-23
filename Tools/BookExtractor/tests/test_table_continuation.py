"""Table continuation: a header line that repeats the open table's column header continues the table, it opens no
section (TCE "Haunted Effects" over pp. 155-156, XGE's encounter tables)."""
import gzip
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))

from regression_run import load_fixture

from bookextractor.pages_io import extract_pages, page_from_dict

_PAGES, _ = load_fixture()
_CASES = json.loads(gzip.decompress((Path(__file__).parent / "data" / "pattern_cases.json.gz").read_bytes()))
_PAGES.update({k: page_from_dict(v) for k, v in _CASES["pages"].items() if k not in _PAGES})


def _run(slug: str, first: int, last: int) -> list[dict]:
    pages = sorted((p for k, p in _PAGES.items() if k.startswith(slug + ":") and first <= p.number <= last),
                   key=lambda p: p.number)
    return extract_pages(pages, slug)


def test_tce_haunted_table_continues_over_the_page_break():
    entities = _run("tce", 0, 999)
    assert not [e["name"] for e in entities if "oolp" in e["name"].lower()]
    haunted = next(e for e in entities if e["id"] == "tce.rule.haunted")
    for row in ("01-05", "96-00"):
        assert row in haunted["canonicalText"]
    assert haunted["canonicalText"].count("OOLP Effect") >= 2  # the repeated header stays in the text


def test_xge_has_no_entity_named_for_a_die_header():
    entities = _run("xge", 92, 114)
    # (starts with: the printed name "Blibdoolpoolp" holds the letters but is no die header)
    assert not [e["name"] for e in entities if e["name"].lower().startswith("oolp")]
