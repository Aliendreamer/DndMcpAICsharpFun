from functools import lru_cache
from pathlib import Path

from bookextractor.layout import reading_order
from bookextractor.model import load_ocr_page
from bookextractor.pages import classify_page
from bookextractor.parsers import ParseContext
from bookextractor.parsers.sections import chapter_titles, read_sections

FIX = Path(__file__).parent / "fixtures" / "ocr"
# GGR 12: "Chapter 1: Character Creation"; 28: the end of chapter 1 (Circle of Spores); 30-39: "Chapter 2: Guilds of
# Ravnica", Guild Membership (with "The Guildless" sidebar), the Azorius Senate (with the "Three Capitals" sidebar)
# and the start of the Boros Legion; 100: "Chapter 3: The Tenth District"
GGR = [12, 28, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 100]
CHAPTER_1 = "Chapter 1: Character Creation"
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
    # the sidebar stripper also takes the column line after the "Three Capitals" box (it sits as far in as the box);
    # the section records it as stripped rather than losing it silently
    lost = "As an Azorius official, you can call on recruits"
    assert lost not in text(azorius)
    assert lost in " ".join(l.text for l in azorius.stripped)

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
    assert fungal.parents[0] == CHAPTER_1
    for s in ggr():
        assert not any("Guilds of Ravnica" in l.text for l in s.lines), s.title
        if s.page <= 28:
            assert all(l.page <= 28 for l in s.lines) and s.parents[0] == CHAPTER_1, s.title
        elif s.page < 100:
            assert s.parents[0] == CHAPTER_2, s.title
            assert all(l.page < 100 for l in s.lines), s.title
        if s.page < 100:  # no chapter gap between chapters 1, 2 and 3 (a "close" or "holds" doubt is no gap)
            assert s.complete or s.end_reason.startswith(("close", "holds")), (s.title, s.end_reason)


def test_a_lost_chapter_title_is_a_chapter_gap():
    """With the "Chapter 2" title unread, chapter 1 runs on to chapter 3: no section in between claims chapter 1,
    and none is complete."""
    body, pages = _body("ggr", GGR)
    body = [l for l in body if "Guilds of Ravnica" not in l.text]
    secs = read_sections(body, ParseContext(slug="ggr", source="GGR", pages=pages))
    between = [s for s in secs if s.page < 100]
    assert any(s.title == "Azorius Senate" for s in between)
    for s in between:
        assert not s.complete, s.title
        assert s.end_reason.startswith("chapter gap: Chapter 2 title not found"), (s.title, s.end_reason)
        assert s.parents[0] == "Chapter ? (title not found)", (s.title, s.parents)
    after = [s for s in secs if s.page >= 100]
    assert after and all(s.parents[0] == "Chapter 3: The Tenth District" for s in after)


def test_stripped_text_is_recorded():
    """A sidebar inside a section is not its text, but the section records it as stripped."""
    membership = get("Guild Membership")
    stripped = " ".join(l.text for l in membership.stripped)
    assert "The Guildless" in stripped and "guildless are especially common" in stripped
    assert "Three CApitals" in " ".join(l.text for l in get("Azorius Senate").stripped)
    assert not any(l in membership.lines for l in membership.stripped)


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


def test_display_titles_are_read_and_map_labels_are_not():   # W23 (c): VRGR domains
    """A domain's display title over its short "Domain of ..." / "Darklord: ..." block is a heading (FALKOVNIA,
    KALAKERI, MORDENT); the "DEMENTLIEU" label over its "MAP 3.4: DEMENTLIEU" caption is art text."""
    body, pages = _body("vrgr", [96, 97, 98, 101, 124, 125, 144, 145])
    secs = read_sections(body, ParseContext(slug="vrgr", source="VRGR", pages=pages))
    tops = {(s.title, s.page) for s in secs if s.level == 1}
    assert {("Falkovnia", 101), ("Kalakeri", 125), ("Mordent", 145)} <= tops
    assert not any(s.title == "Dementlieu" and s.page == 96 for s in secs)


def test_an_ordinal_heading_is_read_and_its_ocr_digits_repaired():   # W24: PHB "Beyond lst Level"
    """"Beyond 1st Level" (p16, 45.9 tall) encloses "Tiers of Play"; the OCR prints "lst", a numeral, not a
    lower-case word, and the title is repaired as a chapter number is."""
    from bookextractor.parsers.sections import _heading_text

    assert _heading_text("Beyond lst Level") and _heading_text("Beyond 1st Level") and _heading_text("Tier of 20th")
    assert not _heading_text("lst") and not _heading_text("20th Level")  # a heading never opens with a numeral
    body, pages = _body("phb", [15, 16])
    secs = read_sections(body, ParseContext(slug="phb14", source="PHB", pages=pages))
    beyond = next(s for s in secs if s.page == 16 and s.level == 1 and s.title.startswith("Beyond"))
    assert beyond.title == "Beyond 1st Level"
    tiers = next(s for s in secs if s.title == "Tiers of Play")
    assert tiers.parents[-1] == "Beyond 1st Level"


def test_a_heading_after_a_map_caption_is_read():   # W24: EGW "GREYING WILDLANDS" p112
    """The region heading under "MAP 3.6: GREYING WILDLANDS" opens after the caption, not after a sentence: it is
    read, and Mythburrow (p115) sits under it, not under the region before the map."""
    body, pages = _body("egw", [111, 112, 113, 115])
    secs = read_sections(body, ParseContext(slug="egw", source="EGW", pages=pages))
    wild = next(s for s in secs if s.title == "Greying Wildlands")
    assert wild.level == 1 and wild.page == 112
    myth = next(s for s in secs if s.title == "Mythburrow")
    assert myth.parents[-1] == "Greying Wildlands"
    assert not any(l.text.strip() == "MYTHBURROW" for s in secs if s.title == "Yrrosa" for l in s.lines)


def test_section_ends_bound_the_widest_sub_heading_and_doubt_the_midpoint_band():   # W24 (iii): DMG Healing
    """The widest sub-heading grows only with headings clearly smaller than the section's own, so a swallowed
    sibling cannot raise the midpoint for the next one; a heading barely under the midpoint, or not clearly smaller
    than the section's own, is returned as the one that nearly ended the section."""
    from bookextractor.parsers.sections import _ends

    # Healing 0.97 over Healing Surges 0.61, then Rest Variants 0.78 (just under the midpoint 0.79), then Firearms
    stop, widest, near = _ends([0.97, 0.61, 0.78, 0.96])[0]
    assert (stop, near) == (3, 2) and abs(widest - 0.78) < 1e-9
    # the creep: 1.0 over 0.6 (midpoint 0.8), then 0.78 (in the band: the doubt), 0.86 and 0.90, each under the
    # running midpoint; 0.90 is not clearly smaller than 1.0 (over 0.88 = 1 - _SUB_STEP) so it does not widen, and
    # 0.94 then ends the section (unbounded, 0.90 would have raised the midpoint to 0.95 and swallowed 0.94 too)
    stop, widest, near = _ends([1.0, 0.6, 0.78, 0.86, 0.90, 0.94])[0]
    assert (stop, near) == (5, 2) and abs(widest - 0.86) < 1e-9
    # a clean tree: no doubt
    assert _ends([1.0, 0.6, 0.62, 1.0])[0] == (3, 0.62, None)


def test_a_section_holding_a_near_midpoint_heading_is_not_complete():   # W24: DMG Healing / Rest Variants
    body, pages = _body("dmg", [265, 266, 267])
    secs = read_sections(body, ParseContext(slug="dmg14", source="DMG", pages=pages))
    healing = next(s for s in secs if s.title == "Healing")
    assert not healing.complete and healing.end_reason.startswith("holds 'Rest VarianTs'"), healing.end_reason
    assert any(l.text.strip() == "Rest VarianTs" for l in healing.lines)  # kept whole, never a silent cut
