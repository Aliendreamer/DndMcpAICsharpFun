"""Task 8 fix wave: categories, pull-quotes and swallowed headings never accepted; honest section credit."""
from pathlib import Path

from bookextractor.checks import Check
from bookextractor.layout import reading_order
from bookextractor.model import Line, load_ocr_page
from bookextractor.names import build_vocabulary
from bookextractor.parsers import Parsed
from bookextractor.parsers.sections import Section, drop_pull_quotes, swallowed_heading

FIX = Path(__file__).parent / "fixtures" / "ocr"


def _l(text, y, h=30.0, page=1, x=100.0):
    return Line(text, (x, float(y), x + 400.0, float(y) + h), h, page)


# ---- 1. category words are read like name words -------------------------------------------------------------

def _cat(cat, raw, vocab_lines):
    from bookextractor.assemble import _unseen_long_word_check
    vocab = build_vocabulary(vocab_lines)
    p = Parsed(type="Lore", name="Special Traits", fields={"category": cat}, text="",
               lines=[_l("SPECIAL TRAITS", 0)], category_raw=raw)
    return _unseen_long_word_check(p, [Check("name-words-known", True, "")], vocab)


def test_a_very_long_category_word_printed_nowhere_else_fails_category_words_known():   # MTF Demoniccambions
    cs = _cat("Culturalmelancholy", "CULTURALMELANCHOLY", ["CULTURALMELANCHOLY", "a cultural melancholy sets in"])
    assert [c.name for c in cs if not c.passed] == ["category-words-known"]


def test_a_category_the_book_prints_spaced_elsewhere_fails():   # SCAG Citadeladbar
    cs = _cat("Citadeladbar", "CITADELADBAR", ["CITADELADBAR", "the citadel adbar stands", "citadel adbar again"])
    assert [c.name for c in cs if not c.passed] == ["category-words-known"]


def test_a_category_with_a_capital_inside_and_no_other_print_fails():   # GGR "A PreCArIOUS PeACe"
    cs = _cat("Aprecarious Peace", "APREcarIouS PEacE", ["APREcarIouS PEacE", "a peace of sorts"])
    assert [c.name for c in cs if not c.passed] == ["category-words-known"]


def test_an_ordinary_category_passes():
    cs = _cat("Racial Traits", "RACIAL TRAITS", ["RACIAL TRAITS", "racial traits"])
    assert all(c.passed for c in cs)


# ---- 2. a peer or lead-in is no category -----------------------------------------------------------------------

def _sec(title, parents, lines, level, size, end=""):
    return Section(title=title, level=level, parents=parents, lines=lines, page=lines[0].page, complete=not end,
                   end_reason=end, size=size)


def test_a_parent_that_held_a_peer_is_no_sure_category():   # PHB Age holding Size
    from bookextractor.parsers.world import placement_doubts
    ch = ["Chapter 2: Races", "Choosing a Race"]
    prose = lambda y: _l("The dwarves live a long life indeed.", y)  # noqa: E731
    age = _sec("Age", ch, [_l("Age", 0), prose(10), _l("Size", 20), prose(30)], 2, 0.74,
               end="holds 'SIZE' (p18, size 0.66), not clearly a sub-heading of its own")
    size = _sec("Size", [*ch, "Age"], [age.lines[2], age.lines[3]], 3, 0.66)
    out = placement_doubts([age, size], {})
    assert "peer" in out[id(size)] and "'Age'" in out[id(size)]


def test_a_parent_set_like_a_childless_sibling_is_a_peer_lead_in():   # DMG Town over City
    from bookextractor.parsers.world import placement_doubts
    ch = ["Chapter 1", "Settlements", "Size"]
    prose = lambda y: _l("A town is a busy place to live in.", y)  # noqa: E731
    village = _sec("Village", ch, [_l("Village", 0), prose(10)], 3, 0.65)
    town = _sec("Town", ch, [_l("Town", 20), prose(30), _l("City", 40), prose(50)], 3, 0.63)
    city = _sec("City", [*ch, "Town"], [town.lines[2], town.lines[3]], 4, 0.54)
    out = placement_doubts([village, town, city], {})
    assert "peer lead-in" in out[id(city)]


def test_a_real_enclosing_heading_is_still_a_category():
    from bookextractor.parsers.world import placement_doubts
    ch = ["Chapter 1"]
    prose = lambda y: _l("A town is a busy place to live in.", y)  # noqa: E731
    top = _sec("Settlements", ch, [_l("Settlements", 0), prose(10), _l("Size", 20), prose(30)], 1, 1.5)
    sub = _sec("Size", [*ch, "Settlements"], [top.lines[2], top.lines[3]], 2, 0.9)
    assert placement_doubts([top, sub], {})[id(sub)] == ""


# ---- 3. margin pull-quotes ---------------------------------------------------------------------------------------

def test_the_mtf_margin_note_is_dropped_from_the_long_view():   # MTF p41
    L = reading_order(load_ocr_page(FIX / "mtf" / "041.json", 41))
    i = next(k for k, l in enumerate(L) if l.text == "The Long View")
    own = L[i:]
    assert any("elves of Oerth" in l.text for l in own)
    kept = drop_pull_quotes(own)
    text = " ".join(l.text for l in kept)
    assert "elves of Oerth" not in text and "There is evidence of" not in text and "humanity ybost" not in text
    assert "When enemies threaten to invade" in text and "ingrained in elves" in text


def test_a_lone_tall_line_or_a_heading_is_not_a_pull_quote():
    body = [_l("The Heading", 0, 45.0)] + [_l(f"ordinary prose line number {k} of text", 40 + 30 * k) for k in range(8)]
    body.insert(4, _l("a lone drop word that is tall", 150, 50.0))
    assert len(drop_pull_quotes(body)) == len(body)


# ---- 4. swallowed headings ---------------------------------------------------------------------------------------

def _body(*texts):
    return [_l("Name Here", 0)] + [_l(t, 40 + 30 * k) for k, t in enumerate(texts)]


def test_text_ending_on_a_label_fails():   # DMG Domains of Dread "OPTIONAL RULE:"
    ls = _body("The Shadowfell holds horrific demiplanes ruled by dark lords of old.", "OPTIONAL RULE:")
    assert "label" in swallowed_heading("Domains of Dread", ls, 1)


def test_text_ending_on_a_table_header_with_no_rows_fails():   # DMG Monsters and Critical Hits
    ls = _body("A monster scores a critical hit like a player character does today.", "Creature Size on Squares and Hexes",
               "TINY SmaLl or Medium LARGE")
    assert "heading-like" in swallowed_heading("Monsters and Critical Hits", ls, 1)


def test_text_opening_mid_table_fails():   # TCE "Oolp Effect"
    ls = _body("73-78 One character in the region gains the ability to cure afflictions.", "79-84 Another effect holds.")
    assert "middle of a table" in swallowed_heading("Oolp Effect", ls, 1)


def test_an_inner_capitals_label_fails_but_the_entitys_own_word_does_not():   # DMG alien technology FIREARMS
    ls = _body("20 or higher One success; character has advantage on", "FIREARMS", "Renaissance Item Cost Damage", "Pistol 250 gp")
    assert "FIREARMS" in swallowed_heading("Figuring Out Alien Technology", ls, 1)
    ls = _body("Prose that uses the rule here, in the usual way.", "FIREARMS", "Pistol 250 gp")
    assert swallowed_heading("Firearms", ls, 1) == ""


def test_a_heading_like_its_own_name_inside_the_text_fails():   # XGE Grassland Encounters (Levels 6-10)
    ls = _body("OOLP Encounter", "01 1 hobgoblin captain with 1d4 hobgoblins", "Grassland Encounters (Levels 6-10)",
               "OOLP Encounter", "01 1 owlbear")
    assert "like its own name" in swallowed_heading("Grassland Encounters (Levels 1-5)", ls, 1)
    ls = _body("Dice Examples", "Improvising Damage", "1d10 Burned by coals", "2d10 Being struck")
    assert swallowed_heading("Improvising Damage", ls, 1) == ""


def test_the_lead_ins_of_two_other_entities_fail():   # GGR Random Guilds
    ls = _body("Here is a table you can use in those situations.", "10 Simic Combine",
               "Golgari Swarm. An elf lich named Jarad guides the", "Gruul Clans. Raging against civilization and its",
               "Izzet League. Led by the dragon Niv-Mizzet, the Izzet")
    claimed = {"Golgari Swarm", "Gruul Clans", "Izzet League"}
    assert "lead-ins" in swallowed_heading("Random Guilds", ls, 1, claimed)
    assert swallowed_heading("Random Guilds", ls, 1) == ""


# ---- 5. honest section credit ------------------------------------------------------------------------------------

def test_a_namesake_without_the_nodes_text_is_not_credited():
    from bookextractor import score as sc
    node = {"name": "Age", "entries": ["Dwarves mature at the same rate as humans, but they are considered young."]}
    assert sc._holds_opening("Age\nDwarves mature at the same rate as humans, but they are considered young until.", node)
    assert not sc._holds_opening("Age\nAn unrelated paragraph about the aging of magical items in a hoard.", node)
    assert not sc._holds_opening("anything", {"name": "x", "entries": []})
    texts = [("b.lore.a", "Age. Something else entirely, about the passing years."),
             ("b.lore.b", "Intro\nAge. Dwarves mature at the same rate as humans, but they are considered young.")]
    assert sc._printed_inside("Age", texts) == "b.lore.a"  # the old test: any mention
    assert sc._printed_inside("Age", texts, node) == "b.lore.b"  # credited: only where the node's text is


def test_an_entitys_own_table_title_label_or_part_is_not_foreign():   # DMG/XGE own tables and parts
    ls = _body("Prose about the points here, in the usual way.", "SPell Points by Level", "1st 2 3")
    assert swallowed_heading("Spell Points", ls, 1) == ""
    ls = _body("Prose about the features here, in the usual way.", "NPC FEATURES", "Strength 10")
    assert swallowed_heading("Creating NPCs from Scratch", ls, 1) == ""
    ls = _body("Prose about the activity here, in the usual way.", "COMPLICATIONS", "01 A thing")
    assert swallowed_heading("Resolving Activities", ls, 1) == ""
    ls = _body("A trait sentence here, in the usual way.", "Innate Spellcasting (Psionics). The thri-kreen's innate", "x y")
    assert swallowed_heading("Thri-Kreen Weapons and Psionics", ls, 1) == ""
