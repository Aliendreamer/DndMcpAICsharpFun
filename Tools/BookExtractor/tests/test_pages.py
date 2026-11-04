from pathlib import Path

from bookextractor.model import Page, load_ocr_page
from bookextractor.pages import classify_page

FIX = Path(__file__).parent / "fixtures" / "ocr"


def _load(book: str, n: int) -> Page:
    return load_ocr_page(FIX / book / f"{n:03d}.json", n)


def test_classify_index_page():
    assert classify_page(_load("mm", 352)) == "index"


def test_classify_blank_and_art():
    assert classify_page(Page(1, 100, 100, [], "ocr")) == "blank"
    assert classify_page(_load("mm", 2)) == "art"


def test_classify_credits_and_toc():
    assert classify_page(_load("mm", 3)) == "credits"
    assert classify_page(_load("mm", 4)) == "toc"


def test_classify_rules_pages():
    for book, n in [("mm", 167), ("mm", 169), ("phb", 242), ("dmg", 200), ("dmg", 150), ("mm", 50)]:
        assert classify_page(_load(book, n)) == "rules", (book, n)
