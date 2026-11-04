import json
from pathlib import Path

from bookextractor.layout import reading_order
from bookextractor.model import load_ocr_page
from bookextractor.parsers.spell import parse_spells, spell_checks

FIX = Path(__file__).parent / "fixtures" / "ocr" / "phb"
SCHEMA = Path(__file__).parents[3] / "Schemas" / "canonical" / "SpellFields.schema.json"
PAGES = [229, 240, 242, 243, 259, 273]


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
                                         "concentration-agrees"]


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
