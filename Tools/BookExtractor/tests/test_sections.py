from functools import lru_cache
from pathlib import Path

from bookextractor.layout import reading_order
from bookextractor.model import load_ocr_page
from bookextractor.pages import classify_page
from bookextractor.parsers import ParseContext
from bookextractor.parsers.sections import chapter_titles, read_sections

FIX = Path(__file__).parent / "fixtures" / "ocr"
# GGR 28: the end of chapter 1 (Circle of Spores); 30-39: "Chapter 2: Guilds of Ravnica", Guild Membership (with
# "The Guildless" sidebar), the Azorius Senate (with the "Three Capitals" sidebar) and the start of the Boros Legion
GGR = [28, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39]
CHAPTER_2 = "Chapter 2: Guilds of Ravnica"


def _body(book, numbers):
    pages = {n: load_ocr_page(FIX / book / f"{n:03d}.json", n) for n in numbers}
    body = [l for n in sorted(pages) if classify_page(pages[n]) in ("rules", "art") for l in reading_order(pages[n])]
    return body, pages


@lru_cache(maxsize=None)
def ggr():
    body, pages = _body("ggr", GGR)
    return tuple(read_sections(body, ParseContext(slug="ggr", source="GGR", pages=pages)))


def get(name):
    hit = [s for s in ggr() if s.title == name]
    assert hit, f"{name} not among {[s.title for s in ggr()]}"
    return hit[0]


def text(s):
    return " ".join(l.text for l in s.lines)


def test_ggr_guild_sections_with_parents():
    azorius = get("Azorius Senate")
    assert azorius.level == 1
    assert azorius.parents == [CHAPTER_2]
    assert azorius.page == 33
    assert azorius.complete and azorius.end_reason == "heading"
    # its sub-sections are its text; it stops where the next guild starts
    assert "Enemies and Allies" in [s.title for s in ggr() if s.parents[:2] == [CHAPTER_2, "Azorius Senate"]]
    assert "Bringing order from chaos" in text(azorius)
    assert "Boros" not in azorius.lines[-1].text
    assert all(l.page <= 38 for l in azorius.lines)

    for sub in ("Inside the Senate", "Azorius Characters"):
        s = get(sub)
        assert s.level == 2 and s.parents == [CHAPTER_2, "Azorius Senate"], (sub, s.level, s.parents)
    goals = get("Goals of the Azorius")
    assert goals.parents == [CHAPTER_2, "Azorius Senate", "Inside the Senate"]
    assert goals.level == 3
    assert get("Inside the Senate").lines[-1] is not get("Azorius Characters").lines[0]

    boros = get("Boros Legion")
    assert boros.level == 1 and boros.parents == [CHAPTER_2]


def test_a_section_never_crosses_a_chapter_title():
    """Review Focus 1: the last section of chapter 1 stops at the chapter 2 title, and says so."""
    fungal = get("Fungal Body")
    assert fungal.end_reason == "chapter"
    assert fungal.complete
    assert {l.page for l in fungal.lines} == {28}
    assert CHAPTER_2 not in fungal.parents  # (chapter 1's title page is not among the fixture pages)
    for s in ggr():
        assert not any("Guilds of Ravnica" in l.text for l in s.lines), s.title
        if s.page == 28:
            assert all(l.page == 28 for l in s.lines), s.title
        else:
            assert s.parents[0] == CHAPTER_2, s.title


def test_a_sidebar_inside_a_section_is_excluded():
    titles = {s.title for s in ggr()}
    assert "The Guildless" not in titles
    assert "Three Capitals" not in titles
    membership = get("Guild Membership")
    assert "guild spells" in text(membership).lower()  # the section around the sidebar reads on
    assert "guildless are especially common" not in text(membership)
    azorius = text(get("Azorius Senate"))
    assert "Three CApitals" not in azorius
    assert "The arbiter of the Sova Column" not in azorius


def test_chapter_titles_in_their_printed_forms():
    for book, page, want in [
        ("phb", 122, "Chapter 4: Personality and Background"),  # one tall line in mixed OCR casing
        ("tce", 8, "Chapter 1: Character Options"),  # "CHAPTER 1" set small over the tall title
        ("mpmm", 5, "Chapter 1: Fantastical Races"),  # "CHAPTERI", the title read after a page-number glyph
        ("bgg", 11, "Chapter 1: Character Creation"),
    ]:
        body, _ = _body(book, [page])
        assert [t for _, t in chapter_titles(body)] == [want], (book, page)
    # a running foot ("APPENDIX A: MISCELLANEOUS CREATURES" at the page foot, set small) is not one
    body, _ = _body("mm", [338])
    assert chapter_titles(body) == []
