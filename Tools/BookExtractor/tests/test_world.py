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
    # the guild's own sub-sections stay in; the next guild's text does not
    low = az.text.lower()
    assert "inside the senate" in low and "azorius characters" in low and "enemies and allies" in low
    assert "three capitals" not in low  # a sidebar, stripped by the section reader
    assert not any(l.page >= 39 for l in az.lines), "Azorius runs into the Boros Legion"
    # W13: its sub-headings are set 0.65-0.97 as tall as its own heading, so its end is in doubt: kept, and named
    assert "'Inside the SeNate'" in failed(az)["text-complete"]
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
    dg = failed(one(ents, "Darguun"))["text-complete"]
    assert "'Demon Wastes'" in dg
    kr = failed(one(ents, "Karrnath"))["text-complete"]
    assert "'LHAZAAR PRINCIPALITIES'" in kr
    assert any("Lhazaar" in l.text or "LHAZAAR" in l.text for l in one(ents, "Karrnath").lines)  # kept, not cut


def test_a_real_sub_heading_is_no_doubt():
    """GGR "Involving the Characters" holds its own sub-heading "Cross Purposes", set 0.63x as tall as its heading,
    in another style and no taller than its sub-headings: no doubt, Accepted."""
    ents, _ = _parse("ggr", [124, 125, 126, 127, 128], "ggr", "GGR")
    ic = one(ents, "Involving the Characters")
    assert "cross purposes" in ic.text.lower()
    assert not failed(ic)


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
    """W14: MM p123's Duergar "Actions" (monster action text) is the Monster's, never Lore, even with no claims."""
    ents, _ = _parse("mm", [123], "mm14", "MM")
    assert not [e for e in ents if e.name in ("Actions", "Reactions", "Legendary Actions")]
    assert not [e for e in ents if "Weapon Attack" in e.text or "Hit:" in e.text]


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
