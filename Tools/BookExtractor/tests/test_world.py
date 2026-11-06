from functools import lru_cache
from pathlib import Path

import json

import jsonschema
import pytest

from bookextractor.layout import reading_order
from bookextractor.model import load_ocr_page
from bookextractor.pages import classify_page
from bookextractor.parsers import ParseContext
from bookextractor.parsers.charcommon import squash
from bookextractor.parsers.sections import line_key
from bookextractor.parsers.world import parse_world, section_type

FIX = Path(__file__).parent / "fixtures" / "ocr"
SCHEMAS = Path(__file__).parents[3] / "Schemas" / "canonical"


def schema(name):
    return json.loads((SCHEMAS / f"{name}.schema.json").read_text("utf-8"))


def _parse(book, numbers, slug, source, cache=None):
    pages = {n: load_ocr_page(FIX / book / f"{n:03d}.json", n) for n in numbers}
    body = [l for n in sorted(pages) if classify_page(pages[n]) in ("rules", "art") for l in reading_order(pages[n])]
    ctx = ParseContext(slug=slug, source=source, pages=pages,
                       cache={"claimed_lines": set(), "claimed_names": set(), **(cache or {})})
    return parse_world(body, ctx), body


GGR_CH2 = [12, 28, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 100]


@lru_cache(maxsize=None)
def ggr_guilds():
    return tuple(_parse("ggr", GGR_CH2, "ggr", "GGR")[0])


@lru_cache(maxsize=None)
def erlw_nations():
    return tuple(_parse("erlw", list(range(104, 113)), "erlw", "ERLW")[0])


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
        assert typed.passed and "Chapter 2: Guilds of Ravnica" in typed.reason and "'Guilds'" in typed.reason
        assert set(g.fields) == {"entries"}  # FactionFields prints no category or setting
    az = one(ents, "Azorius Senate")
    assert not failed(az)
    # the guild's own sub-sections stay in; the next guild's text does not
    low = az.text.lower()
    assert "inside the senate" in low and "azorius characters" in low and "enemies and allies" in low
    assert "three capitals" not in low  # a sidebar, stripped by the section reader
    assert "Boros" not in az.text.split("\n")[0]
    assert not any(l.page >= 39 for l in az.lines), "Azorius runs into the Boros Legion"
    # the next guild (its pages here end at p45) ends at the chapter 3 title on p100, a sure end
    boros = one(ents, "Boros Legion")
    assert check(boros, "text-complete").reason == "ends at chapter"
    assert not any(l.page >= 100 for l in boros.lines)


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
    pages, slug = GGR_CH2, "ggr"
    _, body = _parse("ggr", pages, slug, "GGR")
    az = one(ggr_guilds(), "Azorius Senate")
    claimed = {line_key(l) for l in az.lines[5:40]}
    ents, _ = _parse("ggr", pages, slug, "GGR", {"claimed_lines": claimed})
    az2 = one(ents, "Azorius Senate")
    assert not {line_key(l) for l in az2.lines} & claimed
    ents, _ = _parse("ggr", pages, slug, "GGR", {"claimed_names": {squash("Azorius Senate")}})
    assert not [e for e in ents if e.name == "Azorius Senate"]
    ents, _ = _parse("ggr", pages, slug, "GGR", {"claimed_lines": {line_key(az.lines[0])}})
    assert not [e for e in ents if e.name == "Azorius Senate"]


def test_claimed_lines_and_names_are_required():
    pages = {n: load_ocr_page(FIX / "ggr" / f"{n:03d}.json", n) for n in (30, 31)}
    body = [l for n in sorted(pages) for l in reading_order(pages[n])]
    with pytest.raises(KeyError):
        parse_world(body, ParseContext(slug="ggr", source="GGR", pages=pages, cache={"claimed_names": set()}))
    with pytest.raises(KeyError):
        parse_world(body, ParseContext(slug="ggr", source="GGR", pages=pages, cache={"claimed_lines": set()}))


def test_erlw_nation_is_a_location():
    """A nation under "Nations of Khorvaire" (a container of the nations, over its sub-sections) is a Location with
    the book's setting; the container's lead is its own entity, typed by the chapter title."""
    ents = erlw_nations()
    br = one(ents, "Breland")
    assert br.type == "Location"
    typed = check(br, "type-from-context")
    assert typed.passed and "'Nations'" in typed.reason and "Nations of K Horvaire" in typed.reason
    assert br.fields["category"] == "Nations of K Horvaire"
    assert br.fields["setting"] == "Eberron"
    assert "Capital: Wroat" in br.text
    assert not failed(br)
    au = one(ents, "Aundair")
    assert au.type == "Location" and "Breland" not in au.text.split("\n")[0]
    assert not {line_key(l) for l in au.lines} & {line_key(l) for l in br.lines}
    lead = one(ents, "Nations of K Horvaire")
    assert lead.type == "Lore" and lead.fields["settingContext"] == "Eberron"
    assert lead.fields["category"] == "Khorvaire Gazetteer"
    assert "Capital: Fairhaven" not in lead.text  # Aundair's, not the lead's


def test_vrgr_domain_of_dread_is_typed_by_its_context():
    """A domain of dread is a Plane or a Location, per the context rule: Darkon is printed under "Chapter 3: Domains
    of Ravenloft", whose "Domains" is a plane word."""
    ents, _ = _parse("vrgr", [62, 89, 90], "vrgr", "VRGR")
    dk = one(ents, "Darkon")
    assert dk.type == "Plane"
    typed = check(dk, "type-from-context")
    assert typed.passed and "'Domains'" in typed.reason and "Chapter 3: Domains of Ravenloft" in typed.reason
    assert dk.fields["category"] == "Domains of Ravenloft"
    assert set(dk.fields) == {"category", "entries"}  # PlaneFields prints no setting


def test_conflicting_context_words_fail():
    etype, category, typed = section_type(["Chapter 4: Creating Adventures", "Guild Locations"])
    assert not typed.passed
    assert "'Guild' (Faction)" in typed.reason and "'Locations' (Location)" in typed.reason
    assert etype == "Location" and category == "Guild Locations"
    # the nearest heading with a type word decides; an outer one is not consulted
    etype, category, typed = section_type(["Chapter 2: Guilds of Ravnica", "Azorius Senate"])
    assert etype == "Faction" and typed.passed and category == "Guilds of Ravnica"
    etype, category, typed = section_type(["Chapter 1: Story of Wildemount", "History of Wildemount"])
    assert etype == "Lore" and typed.passed and category == "History of Wildemount"
    # the Forgotten Realms are no plane; a divine domain is a cleric feature
    assert section_type(["Chapter 1: Welcome to the Realms"])[0] == "Lore"
    assert section_type(["Chapter 3: Classes", "Divine Domains"])[0] == "Lore"


def test_a_lost_chapter_title_types_nothing():
    """Without the chapter 2 title page the guilds sit under a chapter title the OCR did not read: nothing printed
    encloses them, so type-from-context fails and no category is guessed."""
    ents, _ = _parse("ggr", [n for n in GGR_CH2 if n != 30], "ggr", "GGR")
    az = one(ents, "Azorius Senate")
    assert az.type == "Lore" and az.fields["category"] == ""
    assert "type-from-context" in failed(az) and "text-complete" in failed(az)


def test_fields_validate_against_the_world_schemas():
    ents = list(ggr_guilds()) + list(erlw_nations()) + list(_parse("vrgr", [62, 89, 90], "vrgr", "VRGR")[0])
    ents += _parse("ggr", [n for n in GGR_CH2 if n != 30], "ggr", "GGR")[0]
    assert {e.type for e in ents} == {"Faction", "Location", "Lore", "Plane"}
    for e in ents:
        jsonschema.validate(e.fields, schema(f"{e.type}Fields"))
