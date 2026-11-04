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


def test_table_heavy_rules_page_is_not_index():
    from bookextractor.model import Line
    rows = [f"Plate armor {i}" if i % 2 else f"Level {i}" for i in range(1, 31)]
    lines = [Line(t, (0, i * 30, 300, i * 30 + 25), 25, 1) for i, t in enumerate(["Armor Prices"] + rows)]
    assert classify_page(Page(1, 600, 900, lines, "ocr")) == "rules"


def test_dmg_index_pages_without_heading():
    for n in (317, 318, 319, 320):
        assert classify_page(_load("dmg", n)) == "index", n


def test_dmg_map_pages_are_art():
    for n in (310, 311, 312, 313, 314, 315, 163):
        assert classify_page(_load("dmg", n)) == "art", n


def test_dmg_table_pages_are_rules_and_table_content():
    from bookextractor.pages import is_table_page
    for n in (95, 137, 144, 145, 299, 300, 301, 306, 307, 308, 309):
        pg = _load("dmg", n)
        assert classify_page(pg) == "rules" and is_table_page(pg), n
    assert not is_table_page(_load("dmg", 224))
