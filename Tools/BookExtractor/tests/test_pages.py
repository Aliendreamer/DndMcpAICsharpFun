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


def _case_pages(case: str) -> list[Page]:
    import gzip
    import json

    from bookextractor.pages_io import page_from_dict
    raw = json.loads(gzip.decompress((Path(__file__).parent / "data" / "fullbook_cases.json.gz").read_bytes()))
    c = raw["cases"][case]
    return [page_from_dict(raw["pages"][f"{c['slug']}:{n}"]) for n in c["pages"]]


def test_see_style_index_page_is_index():
    for p in _case_pages("front-phb-index"):
        if p.number in (314, 315, 316, 317):
            assert classify_page(p) == "index", p.number


def test_credits_by_roles_is_credits():
    from bookextractor.model import Line
    rows = ["Lead Designers: Jeremy Crawford", "Art Directors: Kate Irwin", "Editors: Scott Fitzgerald Gray",
            "Producers: Dan Tovar"] + [f"Some more text on the credits page number {i}" for i in range(20)]
    lines = [Line(t, (0, i * 30, 300, i * 30 + 25), 25, 1) for i, t in enumerate(rows)]
    assert classify_page(Page(1, 600, 900, lines, "ocr")) == "credits"


def test_designers_note_is_not_a_credit_role():
    from bookextractor.model import Line
    rows = ["Designer's Note: why we changed this rule", "Editor's Note: see the errata"] + [
        f"Some more rules text on this ordinary page number {i}" for i in range(20)]
    lines = [Line(t, (0, i * 30, 300, i * 30 + 25), 25, 1) for i, t in enumerate(rows)]
    assert classify_page(Page(1, 600, 900, lines, "ocr")) == "rules"


def test_prose_page_with_one_see_reference_is_rules():
    from bookextractor.model import Line
    p = next(p for p in _case_pages("front-phb-prose") if p.number == 159)
    assert classify_page(p) == "rules"
    rows = [f"A line of ordinary rules prose number {i} here" for i in range(30)] + ["See chapter 5 for details"]
    lines = [Line(t, (0, i * 30, 300, i * 30 + 25), 25, 1) for i, t in enumerate(rows)]
    assert classify_page(Page(1, 600, 900, lines, "ocr")) == "rules"


def test_spell_list_page_is_rules():
    p = next(p for p in _case_pages("front-phb-prose") if p.number == 208)
    assert classify_page(p) == "rules"


def test_front_back_matter_lines_are_left_out():
    from bookextractor.assemble import ALL_TYPES, extract_book
    from bookextractor.coverage import is_content_line
    pages = _case_pages("front-phb-index") + _case_pages("front-mm14") + _case_pages("front-phb-prose")
    kinds = {p.number: classify_page(p) for p in pages}
    r = extract_book([p for p in pages if p.number in (159, 314, 315, 316, 317)], "phb14", set(ALL_TYPES))
    left = {(pg, t) for pg, t in r.left_out}
    for p in pages:
        if p.number in (314, 315, 316, 317):
            assert all((p.number, l.text.strip()) in left for l in p.lines if is_content_line(l.text, 1.0)), p.number
    assert not [e for e in r.entities if e["page"] in (314, 315, 316, 317)]
    mm = [p for p in pages if p.number in (3, 4, 159)]
    assert {kinds[3], kinds[4]} == {"credits", "toc"}
    r2 = extract_book(mm, "mm14", set(ALL_TYPES))
    assert all((p.number, l.text.strip()) in set(r2.left_out) for p in mm if p.number != 159 for l in p.lines
               if is_content_line(l.text, 1.0))
