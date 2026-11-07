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


def test_the_structural_net_names_titles_captions_and_display_lines():   # W25
    """`foreign_lines`: a sidebar's title at a page top mid-sentence over prose; a caption naming the entity set
    apart inside a sentence; a caps caption; a display line; and the lines that are body text: a wrapped
    continuation naming the entity, a labelled field, a table's title over its rows."""
    from bookextractor.model import Line
    from bookextractor.parsers.sections import foreign_lines, name_words

    def ln(t, page, y, h=30.0, x=100.0):
        return Line(t, (x, y, x + 600.0, y + h), h, page)

    prose = "the god records the passing of the living and the dead alike."
    own = [ln("JERGAL", 31, 100.0, 40.0), ln(prose, 31, 140.0), ln("ment and placing it in the corpse's mouth. This rite is", 31, 180.0),
           ln("The Legend of Knucklebones, Skull Bowling, And the Empty Throne", 32, 80.0),
           ln("Long ago there was but one god of strife, death, and the", 32, 115.0), ln(prose, 32, 150.0),
           ln("but is then accidentally slain himself-the hand of Hoar", 32, 190.0),
           ln("SYmBol OF JERGAL", 32, 340.0, x=300.0),  # set apart (a gap), naming the god, mid-sentence
           ln("is given credit. When one hears three rolls of thunder", 32, 380.0),
           ln("Prerequisite: Rank 2 and renown 25 or higher in the", 32, 420.0), ln("Lord Jergal", 32, 450.0),
           ln("Your new title depends on your previous role.", 32, 480.0),
           ln("Skill Proficiencies: Insight, Intimidation", 32, 520.0), ln(prose, 32, 550.0),
           ln("Ability Score Point Cost", 32, 600.0), ln("Score Cost", 32, 630.0), ln("8 0", 32, 660.0), ln(prose, 32, 690.0),
           ln("HER COURT CHART THE DESTINY OF A CONTINENT.", 32, 730.0), ln(prose, 32, 760.0),
           ln("A Pull Quote Set Large", 32, 800.0, 48.0), ln(prose, 32, 850.0),
           # the entity's own tables and lists: a title the text names just before it, a header over rows, a
           # list after a colon, a row with digits
           ln("The character makes a check using the Carousing table.", 32, 900.0), ln("CAROUSING", 32, 930.0),
           ln("Check Total Result", 32, 960.0), ln("1-5 Character has made a hostile contact.", 32, 990.0),
           ln("To determine the earnings, the character makes an ability check:", 32, 1030.0),
           ln("Strength (Athletics), Dexterity (Acrobatics), Intelligence", 32, 1060.0), ln(prose, 32, 1090.0),
           ln("Creature Size Initiative Modifier", 32, 1130.0), ln("Huge -5", 32, 1160.0), ln(prose, 32, 1190.0)]
    out = foreign_lines(own, 1, name_words("Jergal"))
    flagged = [o.split(" (p")[0] for o in out]
    assert "'The Legend of Knucklebones, Skull Bowling, And the Empty Throne'" in flagged
    assert "'SYmBol OF JERGAL'" in flagged  # names the god, but a caption set apart inside its sentence
    assert "'HER COURT CHART THE DESTINY OF A CONTINENT.'" in flagged and "'A Pull Quote Set Large'" in flagged
    for ok in ("'Lord Jergal'", "'Skill Proficiencies: Insight, Intimidation'", "'Ability Score Point Cost'",
               "'CAROUSING'", "'Check Total Result'", "'Strength (Athletics), Dexterity (Acrobatics), Intelligence'",
               "'Creature Size Initiative Modifier'", "'Huge -5'"):
        assert ok not in flagged, out
    assert len(out) == 4, out


def test_the_net_names_entities_by_their_own_words_and_sees_labelled_headings():   # Task 7 r1 minors
    from bookextractor.model import Line
    from bookextractor.parsers.sections import _names, _title_like, foreign_lines, name_words

    # a generic name word ("Item", "Magic") names nothing by itself; the entity's own word or whole name does
    assert not _names("Magic Item Price", name_words("Buying a Magic Item"))
    assert _names("Buying a Magic Item", name_words("Buying a Magic Item"))
    assert _names("SYmBol OF BANE", name_words("Bane")) and not _names("Baneful Winds", name_words("Bane"))
    # a labelled heading is a title: a foreign "Variant: ..." inside an entity is named
    assert _title_like("Variant: Encumbrance") and _title_like("OPtional Rule: Mad Winds")
    assert not _title_like("Rank 1: Official")
    # a table's row is no title: an abbreviation cell of two capitals, a footnote marker
    for row in ("Mechanus, the Clockwork Nirvana of LN", "Kiaransalee CE Necromancy", "Death, Grave,* Knowledge"):
        assert not _title_like(row), row
    assert _title_like("RUMBLECUSP") and _title_like("Clan Blackbanner")

    def ln(t, page, y, h=30.0):
        return Line(t, (100.0, y, 700.0, y + h), h, page)
    prose = "the wind howls over the moor and the travellers press on."
    own = [ln("Travel", 1, 0.0, 40.0), ln(prose, 1, 40.0), ln("Variant: Mad Winds", 1, 120.0), ln(prose, 1, 150.0)]
    assert any("'Variant: Mad Winds'" in o for o in foreign_lines(own, 1, name_words("Travel")))


def test_odd_opening_names_a_lower_case_opening_and_a_name_that_is_no_heading():   # round 4
    from bookextractor.model import Line
    from bookextractor.parsers.sections import odd_opening

    def ln(t, y):
        return Line(t, (100.0, y, 700.0, y + 30.0), 30.0, 1)
    ok = [ln("Grappling", 0.0), ln("When you want to grab a creature, you can use the Attack action.", 40.0)]
    assert odd_opening("Grappling", ok, 1) == ""
    tail = [ln("The Hilt", 0.0), ln("lifestyle can be found, but overall the lower wards are poor.", 40.0)]
    assert "opens in lower case" in odd_opening("The Hilt", tail, 1)
    for name in ("Tidal Wave Bat.fur", "If the Charisma (Deception) and Dexterity (Sleight",
                 "If You Roll A Natural One On The Die Then"):
        assert "no printed heading" in odd_opening(name, ok, 1), name
    assert odd_opening("Cyre (the Mournland)", ok, 1) == ""


def test_odd_opening_flags_a_wrapped_heading_a_repeated_word_a_rule_label_and_acronyms():   # round 4
    from bookextractor.model import Line
    from bookextractor.parsers.sections import fix_acronyms, odd_opening

    def ln(t, y, h=30.0):
        return Line(t, (100.0, y, 700.0, y + h), h, 1)
    text = ln("When you want to grab a creature, you can use the Attack action.", 70.0)
    wrapped = [ln("Moving Around Other", 0.0, 36.0), ln("CREATURES", 36.0, 34.0), text]
    assert "may wrap onto it" in odd_opening("Moving Around Other", wrapped, 1)
    fine = [ln("Grappling", 0.0, 36.0), ln("Grab", 36.0, 20.0), text]  # a short line set smaller is body text
    assert odd_opening("Grappling", fine, 1) == ""
    assert "repeats a word" in odd_opening("Standard Campaign High Magic Campaign", [ln("x", 0.0), text], 1)
    assert "rule's label" in odd_opening("Ftional Rule: Psychic Dissonance", [ln("x", 0.0), text], 1)
    assert fix_acronyms("The Dm Adjudicates the Rules") == "The DM Adjudicates the Rules"
    assert fix_acronyms("Customizing Npcs") == "Customizing NPCs"
    assert fix_acronyms("Pcs and Dms; DC") == "PCs and DMs; DC" and fix_acronyms("Dmitri") == "Dmitri"


def test_odd_opening_flags_a_cut_bracket_and_a_trailing_heading_with_its_table():   # round 4
    from bookextractor.model import Line
    from bookextractor.parsers.sections import odd_opening, trailing_heading

    def ln(t, y, h=30.0):
        return Line(t, (100.0, y, 700.0, y + h), h, 1)
    body = "Mimic colonies have learned that adventurers can be bought off with information about nearby creatures."
    cut = [ln("Mimic Colonies", 0.0, 36.0), ln(body[:-1] + " (which the", 40.0), ln("their own young.", 80.0)]
    assert "inside a bracket" in odd_opening("Mimic Colonies", cut, 1)
    closed = [ln("Mimic Colonies", 0.0, 36.0), ln(body[:-1] + " (which the mimics keep).", 40.0)]
    assert odd_opening("Mimic Colonies", closed, 1) == ""
    ends = [ln("Flame-kin", 0.0, 36.0), ln("They are elemental soldiers created to serve the legion.", 40.0),
            ln("Boros Creatures", 80.0, 38.0), ln("Challenge Creature Source", 120.0), ln("1/8 Mastiff MM", 160.0),
            ln("1/4 Soldier GGR", 200.0)]
    assert "Boros Creatures" in trailing_heading(ends, 1)
    # a table's rows after the entity's own prose, with no heading over them, are its own
    plain = ends[:2] + ends[3:]
    assert trailing_heading(plain, 1) == ""


def test_interior_heading_names_a_small_caps_heading_over_prose_inside_the_text():   # round 4
    from bookextractor.model import Line
    from bookextractor.parsers.sections import interior_heading

    def ln(t, y, h=30.0):
        return Line(t, (100.0, y, 700.0, y + h), h, 1)
    s = "The characters must find a way through the door before the trap springs shut."
    lines = [ln("Describing a Complex Trap", 0.0, 36.0), ln(s, 40.0), ln(s, 80.0), ln("RUNNING A COMPLEX TRAP", 120.0),
             ln(s, 160.0), ln(s, 200.0)]
    assert "RUNNING A COMPLEX TRAP" in interior_heading(lines, 1)
    assert interior_heading(lines[:3] + lines[4:], 1) == ""


def test_odd_opening_flags_a_sentence_left_unfinished_but_not_a_table_row_or_footnote():   # round 4
    from bookextractor.model import Line
    from bookextractor.parsers.sections import odd_opening

    def ln(t, y):
        return Line(t, (100.0, y, 700.0, y + 30.0), 30.0, 1)
    head = ln("Surprise", 0.0)
    done = ln("The DM determines who might be surprised before the fight begins.", 40.0)
    cut = [head, done, ln("Each creature that does not notice a threat is surprised at the start of the", 80.0)]
    assert "not finished" in odd_opening("Surprise", cut, 1)
    for row in ("Pick a lock Varies 20", "*Might involve a rival"):
        assert odd_opening("Surprise", [head, done, ln(row, 80.0)], 1) == "", row
