import json
from pathlib import Path

from bookextractor.layout import reading_order
from bookextractor.model import load_ocr_page
from bookextractor.parsers.spell import parse_spells, spell_checks

FIX = Path(__file__).parent / "fixtures" / "ocr" / "phb"
SCHEMA = Path(__file__).parents[3] / "Schemas" / "canonical" / "SpellFields.schema.json"
PAGES = [223, 224, 229, 231, 232, 233, 234, 240, 242, 243, 244, 247, 257, 259, 268, 269, 270, 273, 290, 291]


def lines(*ns):
    out = []
    for n in ns:
        out += reading_order(load_ocr_page(FIX / f"{n:03d}.json", n))
    return out


def get(name, *ns):
    return next(p for p in parse_spells(lines(*ns)) if p.name == name)


def failed(p):
    return [c for c in p.checks if not c.passed]


def test_fireball():
    p = get("Fireball", 242, 243)
    f = p.fields
    assert f["level"] == 3 and f["school"] == "V"
    assert f["time"] == [{"number": 1, "unit": "action"}]
    assert f["range"] == {"type": "point", "distance": {"type": "feet", "amount": 150}}
    assert f["components"] == {"v": True, "s": True, "m": "a tiny ball of bat guano and sulfur"}
    assert f["duration"] == [{"type": "instant"}]
    assert f["ritual"] is False and f["concentration"] is False
    assert f["entries"] and f["entriesHigherLevel"]
    assert not failed(p), failed(p)
    assert [c.name for c in p.checks] == ["level-school", "casting-time", "range", "components", "duration",
                                         "concentration-agrees",
                                         "entries-complete", "higher-level-split"]


def test_counterspell_wrapped_time():
    p = get("Counterspell", 229)
    t = p.fields["time"][0]
    assert t["unit"] == "reaction" and "see a creature within 60 feet" in t["condition"]
    assert next(c for c in p.checks if c.name == "casting-time").passed
    assert not failed(p), failed(p)


def test_faerie_fire_found():
    p = get("Faerie Fire", 240)
    assert p.fields["level"] == 1 and p.fields["school"] == "V"
    assert p.fields["components"] == {"v": True}
    assert not failed(p), failed(p)


def test_revivify_found():
    p = get("Revivify", 273)
    assert p.fields["level"] == 3 and p.fields["school"] == "C"
    assert p.fields["components"]["m"].startswith("diamonds worth 300 gp")
    assert not failed(p), failed(p)


def test_mass_spells_with_unspaced_level_line():
    assert get("Mass Cure Wounds", 259).fields["level"] == 5
    assert get("Mass Heal", 259).fields["level"] == 9
    assert not failed(get("Mass Cure Wounds", 259)) and not failed(get("Mass Heal", 259))


def test_concentration_agrees():
    p = get("Faerie Fire", 240)
    assert p.fields["duration"] == [{"type": "timed", "duration": {"type": "minute", "amount": 1},
                                     "concentration": True}]
    assert p.fields["concentration"] is True
    assert next(c for c in p.checks if c.name == "concentration-agrees").passed
    p.fields["concentration"] = False
    assert not next(c for c in spell_checks(p) if c.name == "concentration-agrees").passed


def test_wrapped_components_and_range_shape():
    p = get("Control Weather", 229)
    assert p.fields["components"]["m"] == "burning incense and bits of earth and wood mixed in water"
    assert p.fields["range"] == {"type": "radius", "distance": {"type": "miles", "amount": 5}}
    assert not failed(p), failed(p)


def test_lines_are_source_lines_and_span_pages():
    assert {l.page for l in get("Fireball", 242, 243).lines} == {242, 243}
    assert get("Fireball", 242).lines[0].text.lower() == "fireball"
    spans = [q for q in parse_spells(lines(*PAGES)) if len({l.page for l in q.lines}) > 1]
    assert spans, "expected at least one spell continuing onto the next page"


def test_every_fixture_spell_matches_schema():
    import jsonschema
    schema = json.loads(SCHEMA.read_text(encoding="utf-8"))
    spells = parse_spells(lines(*PAGES))
    assert len(spells) >= 8
    for p in spells:
        jsonschema.validate(p.fields, schema)


def test_prismatic_wall_spans_empty_page():
    p = get("Prismatic Wall", 268, 269, 270)
    assert {l.page for l in p.lines} == {268, 270}
    assert len(p.fields["entries"]) >= 8 and p.fields["entries"][-1].endswith(".")
    assert not failed(p), failed(p)
    assert get("Produce Flame", 270).fields["level"] == 0


def test_last_spell_does_not_absorb_appendix():
    p = get("Zone of Truth", 290, 291)
    text = " ".join(p.fields["entries"])
    assert "PARALYZED" not in text and "INVISIBLE" not in text and "Conditions" not in text
    assert text.endswith("boundaries of the truth.")
    assert not failed(p), failed(p)


def test_inline_higher_levels_are_split():
    for name, pages in (("Color Spray", (223, 224)), ("Flaming Sphere", (243, 244)), ("Longstrider", (257,))):
        p = get(name, *pages)
        hl = p.fields["entriesHigherLevel"]
        assert hl and hl[0]["entries"][0].startswith("When you cast this spell using a spell slot"), name
        assert not any("At Higher Level" in e for e in p.fields["entries"]), name
        assert not failed(p), failed(p)


def test_art_page_inside_text_flow_is_skipped():
    p = get("Detect Thoughts", 232, 233, 234)
    assert len(p.fields["entries"]) >= 3 and p.fields["entries"][-1].endswith(".")
    assert not failed(p), failed(p)


def test_bullet_list_ending_is_complete():
    p = get("Greater Restoration", 247)
    assert "hit point maximum" in p.fields["entries"][-1] and not failed(p), failed(p)


def test_new_checks_flag_truncation_and_mismatches():
    p = get("Fireball", 242)  # page 243 missing: description cut off, higher levels lost
    assert not next(c for c in p.checks if c.name == "entries-complete").passed
    q = get("Faerie Fire", 240)
    q.fields["range"] = {"type": "point", "distance": {"type": "feet", "amount": 90}}
    q.fields["components"] = {"s": True}
    named = {c.name: c.passed for c in spell_checks(q)}
    assert not named["range"] and not named["components"]
    r = get("Fireball", 242, 243)
    r.fields["entriesHigherLevel"] = []
    r.fields["entries"].append("At Higher Levels. more")
    assert not next(c for c in spell_checks(r) if c.name == "higher-level-split").passed


def test_mass_spells_and_revivify_schools_match_the_printed_level_line():
    # the book prints conjuration for all three (5etools lists Mass Cure Wounds / Mass Heal as evocation
    # and Revivify as necromancy); the parse follows the printed page
    assert get("Mass Cure Wounds", 259).fields["school"] == "C"
    assert get("Mass Heal", 259).fields["school"] == "C"
    assert get("Revivify", 273).fields["school"] == "C"


def test_level_school_fails_when_school_forced_wrong():
    for name, page in (("Mass Cure Wounds", 259), ("Mass Heal", 259), ("Revivify", 273)):
        p = get(name, page)
        assert next(c for c in p.checks if c.name == "level-school").passed
        p.fields["school"] = "N"  # a valid code, but not the word the page prints
        assert not next(c for c in spell_checks(p) if c.name == "level-school").passed
    q = get("Fireball", 242, 243)
    q.fields["level"] = 4
    assert not next(c for c in spell_checks(q) if c.name == "level-school").passed


def test_crusaders_mantle_range_is_self_as_printed():
    p = get("Crusader's Mantle", 231)
    assert p.fields["range"] == {"type": "point", "distance": {"type": "self"}}
    assert not failed(p), failed(p)


def test_range_type_is_checked_against_the_printed_type():
    p = get("Crusader's Mantle", 231)
    for wrong in ({"type": "point", "distance": {"type": "touch"}},
                  {"type": "point", "distance": {"type": "sight"}},
                  {"type": "radius", "distance": {"type": "feet", "amount": 30}},
                  {"type": "special"}):
        p.fields["range"] = wrong
        assert not next(c for c in spell_checks(p) if c.name == "range").passed, wrong
    q = get("Revivify", 273)  # printed "Touch"
    q.fields["range"] = {"type": "point", "distance": {"type": "self"}}
    assert not next(c for c in spell_checks(q) if c.name == "range").passed
    f = get("Fireball", 242, 243)  # printed "150 feet": a miles reading must fail
    f.fields["range"] = {"type": "point", "distance": {"type": "miles", "amount": 150}}
    assert not next(c for c in spell_checks(f) if c.name == "range").passed
