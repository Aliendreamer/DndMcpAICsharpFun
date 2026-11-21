import json
from pathlib import Path

from bookextractor.assemble import ALL_TYPES, extract_book
from bookextractor.model import load_ocr_page
from bookextractor.pages_io import extract_pages, page_from_dict, page_to_dict

FIX = Path(__file__).parent / "fixtures" / "ocr" / "mm" / "167.json"


def test_roundtrip_keeps_every_line():
    p = load_ocr_page(FIX, 167)
    d = json.loads(json.dumps(page_to_dict(p)))
    assert page_from_dict(d) == p


def test_extract_pages_matches_extract_book():
    p = load_ocr_page(FIX, 167)
    got = extract_pages([p], "mm14")
    want = extract_book([p], "mm14", set(ALL_TYPES)).entities
    assert got == want
    assert any(e["name"] == "Goblin" for e in got)
