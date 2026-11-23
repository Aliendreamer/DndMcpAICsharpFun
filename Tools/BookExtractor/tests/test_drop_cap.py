"""A chapter opening's large first letter is put back in front of the opening (OCR reads it as a line of its own)."""
import gzip
import json
import re

from regression_run import DATA

from bookextractor.pages_io import extract_pages, page_from_dict

_RAW = json.loads(gzip.decompress((DATA / "fullbook_cases.json.gz").read_bytes()))


def _text(case: str) -> str:
    c = _RAW["cases"][case]
    pages = [page_from_dict(_RAW["pages"][f"{c['slug']}:{n}"]) for n in c["pages"]]
    return "\n".join(e["canonicalText"] for e in extract_pages(pages, c["slug"]))


def test_drop_cap_letter_restored():
    t = _text("drop-tce8")
    assert "when you make your d&d" in t.lower()
    assert not re.search(r"\bhen you make", t, re.I)


def test_magic_opening_restored():
    assert "magic is everywhere" in _text("drop-tce106").lower()


def test_tab_letter_not_prefixed():
    t = _text("tab-mm107")
    assert not re.search(r"^D(he|ra|the)\w*", t, re.M)
    assert "The most gregarious" in t and "DThe" not in t
