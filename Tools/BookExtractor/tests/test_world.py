import json
from functools import lru_cache
from pathlib import Path

import jsonschema
import pytest

from bookextractor.layout import reading_order
from bookextractor.model import load_ocr_page
from bookextractor.pages import classify_page
from bookextractor.parsers import ParseContext
from bookextractor.parsers.charcommon import character_body, median_height
from bookextractor.parsers.sections import line_key
from bookextractor.parsers.world import parse_world, section_type

FIX = Path(__file__).parent / "fixtures" / "ocr"
SCHEMAS = Path(__file__).parents[3] / "Schemas" / "canonical"


def schema(name):
    return json.loads((SCHEMAS / f"{name}.schema.json").read_text("utf-8"))


def _load(book, numbers, slug, source):
    pages = {n: load_ocr_page(FIX / book / f"{n:03d}.json", n) for n in numbers}
    body = [l for n in sorted(pages) if classify_page(pages[n]) in ("rules", "art") for l in reading_order(pages[n])]
    return pages, body


def _parse(book, numbers, slug, source, cache=None):
    pages, body = _load(book, numbers, slug, source)
    ctx = ParseContext(slug=slug, source=source, pages=pages,
                       cache={"claimed_lines": set(), "claimed_names": set(), **(cache or {})})
    return parse_world(body, ctx), body


GGR_CH2 = [12, 28, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 100]
# "Chapter 2: Khorvaire Gazetteer": Aundair, Breland, Cyre, Darguun (with the Demon Wastes), and Karrnath (with the
# Lhazaar Principalities) up to Mror Holds
ERLW_NATIONS = list(range(104, 115)) + [118, 119, 120, 121]


@lru_cache(maxsize=None)
def ggr_guilds():
    return tuple(_parse("ggr", GGR_CH2, "ggr", "GGR")[0])


@lru_cache(maxsize=None)
def erlw_nations():
    return tuple(_parse("erlw", ERLW_NATIONS, "erlw", "ERLW")[0])


def one(ents, name):
    hit = [e for e in ents if e.name == name]
    assert len(hit) == 1, f"{name}: {len(hit)} among {[e.name for e in ents]}"
    return hit[0]


def failed(e):
    return {c.name: c.reason for c in e.checks if not c.passed}


def check(e, name):
    return next(c for c in e.checks if c.name == name)


def test_ggr_guild_sections_are_factions():
    """Spec: each guild chapter's guild section is a Faction whose type-from-context names the enclosing heading."""
    ents = ggr_guilds()
    for guild in ("Azorius Senate", "Boros Legion"):
        g = one(ents, guild)
        assert g.type == "Faction"
        typed = check(g, "type-from-context")
        assert typed.passed and "'Chapter 2: Guilds of Ravnica'" in typed.reason and "'guilds'" in typed.reason
        assert set(g.fields) == {"entries"}  # FactionFields prints no category or setting
    az = one(ents, "Azorius Senate")
    # round 4: the guild's own sub-sections ("Inside the Senate", "Azorius Characters") are entries of their own and
    # the guild holds only its lead; the next guild's text is in neither
    low = az.text.lower()
    assert "inside the senate" not in low and "azorius characters" not in low
    assert "three capitals" not in low  # a sidebar, stripped by the section reader
    assert not any(l.page >= 39 for l in az.lines), "Azorius runs into the Boros Legion"
    inside = one(ents, "Inside the Senate")
    assert inside.type == "Lore" and not {id(l) for l in az.lines} & {id(l) for l in inside.lines}
    assert inside.fields["category"] == "Azorius Senate"
    # a sub-section of a Faction entry is its part: the entry-level check keeps it from being Accepted
    assert "entry-level" in failed(inside) and "Faction entry" in failed(inside)["entry-level"]
    assert "azorius characters" in one(ents, "Azorius Characters").text.lower()
    # W16: its sub-headings are peers of one another, set alike: no doubt on the guild
    assert not failed(az)
    boros = one(ents, "Boros Legion")
    assert not any(l.page >= 100 for l in boros.lines)  # it ends at the chapter 3 title


def test_guild_membership_is_lore():
    """A child of "Guilds of Ravnica" that is no proper name, set smaller than the guilds, is a topic: Lore."""
    gm = one(ggr_guilds(), "Guild Membership")
    assert gm.type == "Lore" and gm.fields["category"] == "Guilds of Ravnica"
    assert check(gm, "type-from-context").passed


def test_world_text_is_not_shared_and_never_an_earlier_types():
    ents = ggr_guilds()
    keys = [line_key(l) for e in ents for l in e.lines]
    assert len(keys) == len(set(keys))
    # the end of chapter 1 is the Circle of Spores subclass: once the subclass owns its lines (as the assembler
    # records them), none of its feature headings is a world section
    spores = {k for e in ents if e.lines[0].page == 28 for k in (line_key(l) for l in e.lines)}
    assert spores, "the fixture's p28 subclass features read as sections when nothing owns them"
    ents, _ = _parse("ggr", GGR_CH2, "ggr", "GGR", {"claimed_lines": spores})
    assert not [e for e in ents if e.lines[0].page == 28]


def test_claimed_lines_and_names_are_owned_by_earlier_types():
    az = one(ggr_guilds(), "Azorius Senate")
    claimed = {line_key(l) for l in az.lines[5:40]}
    ents, _ = _parse("ggr", GGR_CH2, "ggr", "GGR", {"claimed_lines": claimed})
    assert not {line_key(l) for l in one(ents, "Azorius Senate").lines} & claimed
    # a claimed name, as printed: a plural "s" and a trailing parenthetical do not hide it
    for name in ("Azorius Senate", "Azorius Senates", "Azorius Senate (Guild)"):
        ents, _ = _parse("ggr", GGR_CH2, "ggr", "GGR", {"claimed_names": {name}})
        assert not [e for e in ents if e.name == "Azorius Senate"], name
    ents, _ = _parse("ggr", GGR_CH2, "ggr", "GGR", {"claimed_lines": {line_key(az.lines[0])}})
    assert not [e for e in ents if e.name == "Azorius Senate"]


def test_claimed_lines_and_names_are_required():
    pages = {n: load_ocr_page(FIX / "ggr" / f"{n:03d}.json", n) for n in (30, 31)}
    body = [l for n in sorted(pages) for l in reading_order(pages[n])]
    with pytest.raises(KeyError):
        parse_world(body, ParseContext(slug="ggr", source="GGR", pages=pages, cache={"claimed_names": set()}))
    with pytest.raises(KeyError):
        parse_world(body, ParseContext(slug="ggr", source="GGR", pages=pages, cache={"claimed_lines": set()}))


def test_erlw_nation_fixture_keeps_its_container_heading():
    """Fixture fragility, guarded: the "NATioNS OF" / "K HORVAIRE" heading (56.51 tall) survives the 2x median
    art-text cut (`charcommon._LARGE`) only while the fixture's median line height stays at 28.25. With fewer pages
    (104-110: median 27.17) the heading is dropped and the nations lose their typing parent. Fail loudly here, not as a
    puzzling Lore in the next test, if the median edge moves."""
    pages, body = _load("erlw", ERLW_NATIONS, "erlw", "ERLW")
    med = median_height(character_body(body, ParseContext(slug="erlw", source="ERLW", pages=pages)))
    head = next(l for l in body if l.text.strip() == "NATioNS OF")
    assert head.height <= 2.0 * med, (f"'NATioNS OF' ({head.height:.3f}) is cut as art text at 2 x median "
                                      f"{med:.3f}: the fixture's median line height moved")


def test_erlw_nation_is_a_location():
    """A nation under "Nations of Khorvaire" (a container of the nations, over its sub-sections) is a Location with
    the book's setting; the container's lead is its own entity, typed by the chapter title."""
    ents = erlw_nations()
    br = one(ents, "Breland")
    assert br.type == "Location"
    typed = check(br, "type-from-context")
    assert typed.passed and "'nations'" in typed.reason and "'Nations of K Horvaire'" in typed.reason
    assert br.fields["category"] == "Nations of K Horvaire"
    assert br.fields["setting"] == "Eberron"
    assert "Capital: Wroat" in br.text
    au = one(ents, "Aundair")
    assert au.type == "Location" and "Breland" not in au.text.split("\n")[0]
    assert not {line_key(l) for l in au.lines} & {line_key(l) for l in br.lines}
    lead = one(ents, "Nations of K Horvaire")
    assert lead.type == "Lore" and lead.fields["settingContext"] == "Eberron"
    assert lead.fields["category"] == "Khorvaire Gazetteer"
    assert "Capital: Fairhaven" not in lead.text  # Aundair's, not the lead's


def test_a_nation_running_on_into_the_next_fails():
    """W13: the reader nests the next nation under a nation; the text is kept whole and text-complete fails, naming
    the sibling's heading: "Demon Wastes" (0.93x) inside Darguun, "LHAZAAR PRINCIPALITIES" (0.89x) inside Karrnath."""
    ents = erlw_nations()
    # W23: the reader ends Darguun at "Demon Wastes" (set nearer Darguun's size than its widest sub-heading), so
    # Darguun no longer holds it; the run-on into the unread "LHAZAAR PRINCIPALITIES" is still a doubt
    assert not any("demon wastes" in l.text.lower() for l in one(ents, "Darguun").lines[1:])
    kr = failed(one(ents, "Karrnath"))["text-complete"]
    assert "'LHAZAAR PRINCIPALITIES'" in kr
    # round 4: Karrnath holds only its lead; the unread heading's text is in the last sub-section it runs into,
    # and no line is in two entries
    assert any("Lhazaar" in l.text or "LHAZAAR" in l.text for e in ents for l in e.lines)  # kept, not cut
    seen = [id(l) for e in ents for l in e.lines]
    assert len(seen) == len(set(seen))


def test_a_real_sub_heading_is_no_doubt():
    """GGR "Involving the Characters" holds its own sub-heading "Cross Purposes", set 0.63x as tall as its heading,
    in another style and no taller than its sub-headings: no doubt, Accepted."""
    ents, _ = _parse("ggr", [124, 125, 126, 127, 128], "ggr", "GGR")
    ic = one(ents, "Involving the Characters")
    assert "cross purposes" in ic.text.lower()
    assert not failed(ic)


def test_printed_sub_headings_are_entries_and_no_line_is_in_two_entries():   # round 4
    """A section with printed sub-headings holds only its lead; each sub-heading is an entry of its own, typed by its
    nearest enclosing heading, and the entry and its sub-sections never share a line (the assembler's text-not-shared
    gate would fail both). Sub-headings nested deeper become entries too (ERLW Aundair > Arcanix)."""
    for ents in (ggr_guilds(), erlw_nations()):
        seen = [id(l) for e in ents for l in e.lines]
        assert len(seen) == len(set(seen))
    ents = erlw_nations()
    arcanix = one(ents, "Arcanix")
    assert arcanix.type == "Lore" and arcanix.fields["category"] == "Aundairian Characters"
    chars = one(ents, "Aundairian Characters")
    assert chars.type == "Lore" and chars.fields["category"] == "Aundair"
    # the sub-heading of a Location entry is its part (and its own place is in doubt here): never Accepted
    assert "entry-level" in failed(chars)


def test_vrgr_domain_of_dread_is_typed_by_its_parent():
    """Darkon, a main entry under "Chapter 3: Domains of Ravenloft" printed as a proper name, is a Plane; "Currency",
    a topic set smaller under the same chapter title, is Lore (W12)."""
    ents, _ = _parse("vrgr", [62, 65, 89, 90], "vrgr", "VRGR")
    dk = one(ents, "Darkon")
    assert dk.type == "Plane"
    typed = check(dk, "type-from-context")
    assert typed.passed and "'domains'" in typed.reason and "'Chapter 3: Domains of Ravenloft'" in typed.reason
    assert dk.fields["category"] == "Domains of Ravenloft"
    assert set(dk.fields) == {"category", "entries"}  # PlaneFields prints no setting
    cur = one(ents, "Currency")
    assert cur.type == "Lore" and check(cur, "type-from-context").passed
    assert "not printed as a proper name" in check(cur, "type-from-context").reason
    # Darkon's own sub-sections are grandchildren of the chapter: typed by "Darkon", which names no group
    assert one(ents, "Martira Bay").type == "Lore"


def test_types_come_only_from_a_direct_plural_parent():
    # the head noun of the parent decides; a conflict fails naming both words
    etype, category, typed = section_type(["Chapter 4: Creating Adventures", "Guild Locations"])
    assert not typed.passed
    assert "'Guild' (Faction)" in typed.reason and "'Locations' (Location)" in typed.reason
    assert etype == "Location" and category == "Guild Locations"
    # a singular word types nothing: ERLW Sharn's "Healing" is Lore
    etype, category, typed = section_type(["Chapter 3: Sharn, City of Towers"])
    assert etype == "Lore" and typed.passed and category == "Sharn, City of Towers"
    # distant ancestors type nothing: a grandchild of "Guilds of Ravnica"
    etype, _, typed = section_type(["Chapter 2: Guilds of Ravnica", "Azorius Senate"])
    assert etype == "Lore" and typed.passed
    # a gerund names an activity, not a group; "Realms" is the Forgotten Realms; a divine domain is a cleric's
    assert section_type(["Chapter 2: Creating Domains of Dread"])[0] == "Lore"
    assert section_type(["Chapter 1: Welcome to the Realms"])[0] == "Lore"
    assert section_type(["Chapter 3: Classes", "Divine Domains"])[0] == "Lore"
    # the plural head noun of the direct parent, for a proper-named main entry
    assert section_type(["Chapter 2: Creating a Multiverse", "The Outer Planes"])[0] == "Plane"
    assert section_type(["Chapter 2: Creating a Multiverse", "The Outer Planes"], proper=False)[0] == "Lore"
    assert section_type(["Chapter 2: Creating a Multiverse", "The Outer Planes"], main=False)[0] == "Lore"
    assert section_type(["Chapter 3: Domains of Ravenloft", "Featured Domains"],
                        repeated="Noteworthy Features")[0] == "Lore"


def test_stat_block_parts_are_no_world_sections():
    """W14: MM p123's Duergar "Actions" (monster action text) is the Monster's, never Lore, even with no claims; W16:
    a section that holds stat block lines is kept and flagged, never dropped."""
    ents, _ = _parse("mm", [123], "mm14", "MM")
    # W24: never dropped in silence: emitted, and `entry-level` fails naming it a stat block's part
    parts = [e for e in ents if e.name in ("Actions", "Reactions", "Legendary Actions")]
    assert parts and all("stat block's part" in failed(e).get("entry-level", "") for e in parts)
    for e in ents:
        if "Weapon Attack:" in e.text:
            assert "stat block" in failed(e).get("text-complete", ""), e.name


def test_a_stat_block_inside_lore_is_flagged_not_dropped():
    """W16: EGW "Story Overview" prints an NPC's stat block inside its lore: the section stays, text-complete names
    the block's lines."""
    ents, _ = _parse("egw", [236, 237], "egw", "EGW")
    # round 4: the block's own heading ("Actions") is a sub-section, so the block is named in that entry, and the
    # section holding it still fails text-complete (its heading "STAHLMASTER" is in doubt)
    assert "text-complete" in failed(one(ents, "Story Overview"))
    reason = failed(one(ents, "Actions"))["text-complete"]
    assert "stat block" in reason and "Melee Weapon Attack" in reason


def test_prose_naming_an_attack_is_no_stat_block():
    """W16: the stat block's own forms only, case as printed, two within a few lines: prose that names "a melee
    weapon attack" (or one stray "Hit:") keeps its section whole and Accepted."""
    from bookextractor.model import Line
    from bookextractor.parsers.world import _stat_block

    def L(t, y):
        return Line(text=t, box=(0.0, float(y), 500.0, float(y) + 30.0), height=30.0, page=1)

    prose_lines = [L("Champions of the arena", 0), L("A gladiator who makes a melee weapon attack against a", 40),
                   L("rival earns the crowd's favour. Hit: the crowd roars.", 80)]
    assert _stat_block(prose_lines) == []
    block = prose_lines + [L("Slam. Melee Weapon Attack: +6 to hit, reach 5 ft.,", 120),
                           L("one target. Hit: 9 (1d10 + 4) bludgeoning damage.", 160)]
    assert len(_stat_block(block)) == 2


def test_a_recurring_sub_heading_is_no_entry():
    """W16: ERLW "Symbol" (p144), a sub-heading every faith prints, read as an entry under "Khorvaire": entry-level
    fails naming its recurrence (and its RITES / TEMPLES siblings fail text-complete)."""
    ents, _ = _parse("erlw", [104, 141, 142, 143, 144, 145, 146, 147, 148], "erlw", "ERLW")
    sym = next(e for e in ents if e.name == "Symbol" and e.lines[0].page == 144)
    assert "recurs" in failed(sym)["entry-level"]
    # W23: "Church of the" / "SILVER FLAME" is read as one heading; the faith after it, set like its siblings, is
    # doubted as its sub-section
    assert any(e.name == "Church of the Silver Flame" for e in ents)
    cults = check(one(ents, "Cults of the Dragon Below"), "entry-level")
    assert not cults.passed and "'Church of the Silver Flame'" in cults.reason and "siblings" in cults.reason


def test_a_lost_chapter_title_types_nothing():
    """Without the chapter 2 title page the guilds sit under a chapter title the OCR did not read: nothing printed
    encloses them, so type-from-context fails and no category is guessed (absent, not empty)."""
    ents, _ = _parse("ggr", [n for n in GGR_CH2 if n != 30], "ggr", "GGR")
    az = one(ents, "Azorius Senate")
    assert az.type == "Lore" and "category" not in az.fields
    assert "type-from-context" in failed(az) and "text-complete" in failed(az)


def test_fields_validate_against_the_world_schemas():
    ents = list(ggr_guilds()) + list(erlw_nations()) + list(_parse("vrgr", [62, 65, 89, 90], "vrgr", "VRGR")[0])
    ents += _parse("ggr", [n for n in GGR_CH2 if n != 30], "ggr", "GGR")[0]
    assert {e.type for e in ents} == {"Faction", "Location", "Lore", "Plane"}
    for e in ents:
        jsonschema.validate(e.fields, schema(f"{e.type}Fields"))


def _synthetic(spec):
    """(Section, own lines, starts, sizes) from [(text, height, reader level or None)], one line per entry, 40 apart
    on page 1; the first entry is the section's heading (level 1)."""
    from bookextractor.model import Line
    from bookextractor.parsers.sections import Section

    lines = [Line(text=t, box=(100.0, 40.0 * k, 700.0, 40.0 * k + h), height=h, page=1)
             for k, (t, h, _) in enumerate(spec)]
    starts = {id(l): lv for l, (_, _, lv) in zip(lines, spec) if lv is not None}
    sec = Section(title=spec[0][0], level=1, parents=["Chapter 1: Character Options"], lines=lines, page=1,
                  complete=True, end_reason="heading", size=1.0)
    return sec, lines, starts


PROSE = "the character's training began in a monastery far from the city walls."


def test_a_run_of_same_styled_peers_is_doubted():
    """W18: XGE "Monk" (title case) holds PALADIN, RANgeR, ROGUE, ... each over its own "d6 I became a ..." table and
    prose: a run of three or more same-styled peers (OCR casing noise aside) inside one section is doubted, whatever
    their sizes (0.59-0.72x: under the reader-size cut)."""
    from bookextractor.parsers.world import _doubt

    spec = [("Monk", 40.0, 1), (PROSE, 30.0, None), (PROSE, 30.0, None)]
    for name in ("PALADIN", "RANgeR", "ROGUE", "SORCeRER"):
        spec += [(name, 31.0, 2), (PROSE, 30.0, None), (PROSE, 30.0, None)]
    sec, own, starts = _synthetic(spec)
    sizes = {id(l): 0.6 for l in own if id(l) in starts and l is not own[0]}
    doubts = _doubt(sec, own, 1, starts, sizes)
    assert any("run of 4 peer sub-headings" in d and "'RANgeR'" in d for d in doubts), doubts


def test_a_pair_or_a_mixed_run_of_sub_headings_is_no_doubt():
    """W18: two peers ("Inside the Senate", "Azorius Characters") or three peers in different styles are the
    section's own sub-headings: no run doubt."""
    from bookextractor.parsers.world import _doubt

    pair = [("AZORIUS SENATE", 54.0, 1), (PROSE, 30.0, None), ("Inside the Senate", 34.0, 2), (PROSE, 30.0, None),
            (PROSE, 30.0, None), ("Azorius Characters", 34.0, 2), (PROSE, 30.0, None), (PROSE, 30.0, None)]
    sec, own, starts = _synthetic(pair)
    assert not _doubt(sec, own, 1, starts, {id(l): 0.6 for l in own if id(l) in starts})
    mixed = pair + [("NOTABLE MEMBERS", 34.0, 2), (PROSE, 30.0, None), (PROSE, 30.0, None)]
    sec, own, starts = _synthetic(mixed)
    assert not [d for d in _doubt(sec, own, 1, starts, {id(l): 0.6 for l in own if id(l) in starts})
                if "run of" in d]


def test_a_zero_size_section_and_an_unread_heading_raise_nothing():
    """W18 minor: a section whose reader size is 0.0, holding a heading the reader did not read beside a peer, is
    checked without a KeyError and without a size comparison."""
    from bookextractor.parsers.world import _doubt

    spec = [("Religious Order Contact", 30.0, 1), (PROSE, 30.0, None), ("Contact Peer", 26.0, 2),
            (PROSE, 30.0, None), ("Contact", 26.0, None), (PROSE, 30.0, None), (PROSE, 30.0, None)]
    sec, own, starts = _synthetic(spec)
    sec.size = 0.0
    doubts = _doubt(sec, own, 1, starts, {id(own[2]): 0.5})
    assert not [d for d in doubts if "'Contact' (p1" in d and "reader size" in d]


def test_a_table_row_read_as_a_heading_is_no_entry():   # W24 (iv): PHB "Drow (+1) Human (+1)" p13
    """An ability-score summary row ("Drow (+1) Human (+1)") read as a section is doubted by its own heading: a
    signed number is a table's token, never a heading's word."""
    from bookextractor.parsers.world import garbage_heading

    assert garbage_heading("Drow (+1) Human (+1)") and garbage_heading("Dwarf (+2) Half-orc (+1)")
    assert not garbage_heading("Weapon (+1)") is None  # a call, not a crash: parenthesised modifiers are rows
    assert not garbage_heading("Level 1-4") and not garbage_heading("Beyond 1st Level")
    # Task 7 round 2: a heading over table rows is a table's caption, never a section: the row is no entity at all
    ents, _ = _parse("phb", [13], "phb14", "PHB")
    assert not [e for e in ents if e.name == "Drow (+1) Human (+1)"]


def test_a_tables_title_read_as_a_heading_is_no_entry():   # W24: ERLW "Villain You Know" d6 table p186
    """The d6 table "Villain You Know" is printed as a sibling of "The Villain You Know": its heading is a table's
    title (a die column header straight under it), and `entry-level` fails naming it."""
    from bookextractor.model import Line
    from bookextractor.parsers.world import table_title

    ents, _ = _parse("erlw", [185, 186], "erlw", "ERLW")
    table = one(ents, "Villain You Know")
    why = failed(table)["entry-level"]
    assert "table's title" in why and "'d6 Villain'" in why
    assert "entry-level" not in failed(one(ents, "The Villain You Know"))
    rows = [Line(t, (0.0, 10.0 * k, 100.0, 10.0 * k + 5), 5.0, 1) for k, t in enumerate(
        ["Random Encounters", "1 A patrol of guards stops the party for questions.",
         "2-3 A merchant offers a bargain on stolen goods.", "4 Nothing happens, and the night is quiet."])]
    assert "numbered table rows" in table_title(rows, "Random Encounters")
    prose = [Line(t, (0.0, 10.0 * k, 100.0, 10.0 * k + 5), 5.0, 1) for k, t in enumerate(
        ["Random Encounters", "1. Roll on the table below once per day of travel.", "The guards are wary of strangers."])]
    assert table_title(prose, "Random Encounters") == ""  # a numbered step is no table row


def test_siblings_after_a_stat_block_are_doubted_to_the_first_larger_one():   # W24 (v): DMG Acheron p66
    """The Larva stat block (p63) ends "Outer Planes" and sets Gehenna, The Nine Hells, Acheron and Mechanus beside
    it: every sibling after the stat block set no larger than it is doubted, not just the next (with no claims, the
    stat block's own lines mark it)."""
    ents, _ = _parse("dmg", [61, 62, 63, 64, 65, 66], "dmg14", "DMG")
    for name in ("Gehenna", "Acheron", "Mechanus"):
        why = failed(one(ents, name))["entry-level"]
        assert "'Larva'" in why, (name, why)  # after the stat block, or set like Larva's siblings: both name it
    assert "entry-level" in failed(one(ents, "The Nine Hells"))  # barely over Gehenna's midpoint


def test_siblings_under_a_taller_copy_of_a_heading_are_doubted():   # W24 (vi): SCC "Welcome to Strixhaven" p5
    """The reader read the running foot "WELCOME TO STRIXHAVEN" (36 tall) as the heading; the display title (77.5)
    is on the same page. "About This Book" (p6) and "Strixhaven's World" (p7), set smaller than that copy, may be its
    sub-sections: doubted, with everything under them."""
    ents, _ = _parse("scc", [5, 6, 7], "scc", "SCC")
    for name in ("About This Book", "Strixhaven's World"):
        why = failed(one(ents, name))["entry-level"]
        # Task 7: the unread display line rule names the same title first; the taller-copy run stands behind it
        assert "77.5" in why and "'Welcome to Strixhaven'" in why, (name, why)


def test_a_section_with_prose_never_vanishes():   # W24 (i): VRGR domains, MM "Actions", a book's opening sections
    """A domain's display block ("Domain of ...", "Darklord: ...") is no prose straight under its title: the domain
    is emitted all the same, with `entry-level` failing for it, never dropped in silence. A top section with no
    chapter title over it is emitted too (`type-from-context` fails)."""
    ents, _ = _parse("vrgr", [96, 97, 98, 101, 124, 125, 144, 145], "vrgr", "VRGR")
    for name in ("Falkovnia", "Kalakeri", "Mordent"):
        e = one(ents, name)
        assert "the Greying Wildlands" not in e.text and len(e.text) > 500
        assert "no printed parent heading" in failed(e).get("type-from-context", "")
    ents, _ = _parse("egw", [111, 112, 113, 115], "egw", "EGW")
    wild = one(ents, "Greying Wildlands")   # a top section before any chapter title in the fixture
    assert "no printed parent heading" in failed(wild)["type-from-context"]


def _tree(spec, chapter="Chapter 3: Gazetteer"):
    """[(title, level, page, n_lines)] in reading order -> Section list sharing one line list, nested by level."""
    from bookextractor.model import Line
    from bookextractor.parsers.sections import Section

    lines, secs, stack = [], [], []
    for title, level, page, n in spec:
        head = Line(title, (0.0, 0.0, 100.0, 5.0), 5.0, page)
        body = [Line("the quiet town sits on the river and trades in furs and salt.", (0.0, 10.0 * k, 100.0, 10.0 * k + 5),
                     5.0, page) for k in range(1, n)]
        start = len(lines)
        lines += [head] + body
        stack = stack[:level - 1]
        secs.append((Section(title=title, level=level, parents=[chapter] + [s.title for s in stack], lines=[],
                             page=page, complete=True, end_reason="heading", size=2.0 - 0.5 * level), start))
        stack.append(secs[-1][0])
    for k, (s, start) in enumerate(secs):
        end = next((st for o, st in secs[k + 1:] if o.level <= s.level), len(lines))
        s.lines = lines[start:end]
    return [s for s, _ in secs]


def test_a_sibling_group_of_long_listings_is_a_group_of_containers():   # W24 (vii)
    """Three regions, each over nine pages with three places under it, are containers alike; three six-page
    domains with three parts each are entries alike, the longest included; a heading that lists entries by its own
    plural type word is a container whatever its length."""
    from bookextractor.parsers.world import _entries

    long_ = []
    for r, region in enumerate(("Menagerie Coast", "Marrow Valley", "Zemni Fields")):
        p = 1 + 12 * r
        long_ += [(region, 1, p, 4)] + [(f"{region} {t}", 2, p + 4 * k + 1, 4) for k, t in enumerate(("Harbor", "Market", "Temple"))]
    secs = _tree(long_)
    kinds = {e.section.title: e.lead for e in _entries(secs, lambda *a, **k: False)}
    assert all(kinds[r] for r in ("Menagerie Coast", "Marrow Valley", "Zemni Fields"))  # leads: containers
    assert "Menagerie Coast Market" in kinds
    short = []
    for r, dom in enumerate(("Barovia", "Borca", "Dementlieu")):
        p = 1 + 6 * r
        short += [(dom, 1, p, 4)] + [(f"{dom} Part {k}", 2, p + 2 * k, 4) for k in range(3)]
    secs = _tree(short)
    kinds = {e.section.title: e.lead for e in _entries(secs, lambda *a, **k: False)}
    assert not any(kinds.get(d) for d in ("Barovia", "Borca", "Dementlieu")) and "Barovia Part 1" not in kinds
    listing = _tree([("Other Domains of Dread", 1, 1, 4), ("Klorr", 2, 1, 4), ("Ghastria", 2, 2, 4), ("G'henna", 2, 2, 4),
                     ("Mist Wanderers", 1, 3, 4)])
    kinds = {e.section.title: e.lead for e in _entries(listing, lambda *a, **k: False)}
    assert kinds["Other Domains of Dread"] and "Klorr" in kinds and not kinds["Mist Wanderers"]
    # a chapter's only top heading holds the entries (PHB Appendix B: "D&D Pantheons" over its pantheons)
    lone = _tree([("D&D Pantheons", 1, 1, 4), ("The Forgotten Realms", 2, 1, 4), ("Greyhawk", 2, 2, 4),
                  ("Dragonlance", 2, 2, 4)], chapter="Appendix B: Gods of the Multiverse")
    kinds = {e.section.title: e.lead for e in _entries(lone, lambda *a, **k: False)}
    assert kinds["D&D Pantheons"] and "Greyhawk" in kinds


def test_a_section_with_a_sibling_sized_part_is_doubted_itself():   # W25: PHB Fantasy-Historical Pantheons
    """"Fantasy-Historical Pantheons" (0.71) is read under "D&D Pantheons", and its parts (Celtic 0.54 ... Norse
    0.61) are set like the pantheons beside it (median 0.58): it sits a level too deep, so its place (and its
    category "D&d Pantheons") is doubted; its parts keep their own place."""
    ents, _ = _parse("phb", [293, 294, 295, 296, 297, 298, 299, 300], "phb14", "PHB")
    fhp = one(ents, "Fantasy-Historical Pantheons")
    why = failed(fhp)["entry-level"]
    assert "'The Celtic Pantheon'" in why and "set like its parent's siblings" in why and "level too deep" in why
    assert "is doubted" not in failed(one(ents, "The Greek Pantheon")).get("entry-level", "")  # not inherited


def test_a_containers_one_sentence_lead_is_kept_and_flagged():   # W25: DMG Other Planes p67
    ents, _ = _parse("dmg", [61, 62, 63, 64, 65, 66, 67, 68], "dmg14", "DMG")
    lead = one(ents, "Other Planes")
    assert "A variety of realms exist" in lead.text
    assert "one prose line only" in failed(lead)["entry-level"]
    assert one(ents, "Demiplanes").fields.get("category") == "Other Planes"


def test_the_opener_run_starts_from_any_misfiled_section():   # Task 7 r1: VRGR Horror Monsters p224
    """HORROR MONSTERS (p224, left column) is read before the chapter title "Monsters of Ravenloft" (right column
    top), so it is filed under the chapter before (here: a lost one). Its parts Notorious Monsters ... Monstrous
    Minions follow as the new chapter's top sections: all doubted as the opener's sub-sections."""
    ents, _ = _parse("vrgr", [224, 225, 226], "vrgr", "VRGR")
    for name in ("Notorious Monsters", "Describing Monsters", "Monstrous Tactics", "Monstrous Traits",
                 "Monstrous Minions"):
        why = failed(one(ents, name))["entry-level"]
        assert "chapter opener" in why and "'Horror Monsters'" in why, (name, why)


def test_top_sections_after_an_unread_display_heading_are_doubted():   # Task 7 r1: ERLW "RACES" p18
    """"RACES" (p18, 63 tall, 2.2x the median) is set over its own prose but the text stripper drops it as art text:
    the top sections after it set clearly smaller (Changelings ... Dwarves, 45-48 tall) may be its sub-sections,
    and are doubted naming it."""
    from bookextractor.model import Line
    from bookextractor.parsers.sections import display_lines

    ents, _ = _parse("erlw", [18, 19, 20, 21], "erlw", "ERLW")
    for name in ("Dragonborn", "Dwarves"):
        why = failed(one(ents, name))["entry-level"]
        assert "display line 'RACES'" in why and "63.0" in why, (name, why)
    # a drop cap, a headline and a chapter title are no display heading; a tall heading over prose is
    def ln(t, h, y, page=1):
        return Line(t, (100.0, y, 700.0, y + h), h, page)
    body = [ln("humanoids of all kinds walk the lands of this world in peace.", 30.0, 200.0 + 40 * k) for k in range(8)]
    B = [ln("A", 180.0, 10.0)] + body + [ln("UNDER SIEGE?", 72.0, 600.0), ln("the city fell to the invaders last night.", 30.0, 680.0),
         ln("RACES", 63.0, 800.0), ln("the races of this world are presented in alphabetical order here.", 30.0, 870.0),
         ln("CHARACTER", 60.0, 1000.0), ln("the chapter opens with the basics of character creation.", 30.0, 1070.0)]
    out = display_lines(B, {id(B[-2]): "Chapter 1: Character Creation"})
    assert [l.text for l in out] == ["RACES"]


def test_recurring_sub_headings_fold_into_their_entry():   # W17 (Task 7 round 2): EGW Government / Crime
    """"Government", "Crime" and "Geography" print under every city of the gazetteer: they are parts of each city's
    text, never entities of their own, and the city's text keeps them."""
    ents, _ = _parse("egw", list(range(59, 71)), "egw", "EGW")
    assert not [e for e in ents if e.name in ("Government", "Crime", "Geography")]
    darktow = one(ents, "Darktow")
    heads = {l.text.strip().lower() for l in darktow.lines}
    assert "government" in heads and "crime" in heads, sorted(heads)[:20]  # the folded parts' own heading lines


EGW_BLIGHTSHORE = list(range(146, 159)) + [161, 162]
EGW_SPIRITS = [218, 236, 237] + list(range(251, 267))


def test_folded_sub_heading_must_be_contiguous_with_its_parent_blightshore():   # W17 round 3
    """"Government" / "Crime" / "Geography" are Blightshore's folded parts only where nothing else is printed between
    them and the parent's text. Pages 151-156 (Blightshore's own sub-sections) stand between its p150 text and the
    parts printed on p157-158, which are ROTTHOLD's: they never join Blightshore."""
    ents, _ = _parse("egw", EGW_BLIGHTSHORE, "egw", "EGW")
    b = one(ents, "Blightshore")
    assert sorted({l.page for l in b.lines}) == [150], sorted({l.page for l in b.lines})
    assert "Rotthold" not in b.text and "Government" not in b.text


def test_folded_sub_heading_must_be_contiguous_with_its_parent_unwelcome_spirits():   # W17 round 3
    """The adventure's "Story Overview" / "Setup" parts fold only while contiguous; the pieces on p264 after the area
    text (p252-263), and the art caption on p265, never join the adventure's text."""
    ents, _ = _parse("egw", EGW_SPIRITS, "egw", "EGW")
    u = one(ents, "Unwelcome Spirits")
    assert sorted({l.page for l in u.lines}) == [251], sorted({l.page for l in u.lines})
    assert "ACHERON" not in u.text.upper()
