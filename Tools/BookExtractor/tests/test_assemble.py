from pathlib import Path

import pytest

from bookextractor.assemble import ExtractionResult, extract_book, slugify
from bookextractor.model import load_ocr_page
from bookextractor.report import build_report

FIX = Path(__file__).parent / "fixtures" / "ocr"


def pages(book, *ns):
    return [load_ocr_page(FIX / book / f"{n:03d}.json", n) for n in ns]


def by_id(r: ExtractionResult, eid: str) -> dict:
    return next(e for e in r.entities if e["id"] == eid)


def test_slugify():
    assert slugify("Faerie Dragon (Red)") == "faerie-dragon-red"


def test_ids():
    assert by_id(extract_book(pages("mm", 167), "mm14", {"Monster"}), "mm14.monster.goblin")
    assert by_id(extract_book(pages("phb", 242, 243), "phb14", {"Spell"}), "phb14.spell.fireball")
    r = extract_book(pages("dmg", 150), "dmg14", {"MagicItem"})
    assert by_id(r, "dmg14.magicitem.amulet-of-proof-against-detection-and-location")["type"] == "MagicItem"


def test_entity_shape():
    e = by_id(extract_book(pages("mm", 167), "mm14", {"Monster"}), "mm14.monster.goblin")
    assert set(e) == {"id", "type", "name", "page", "canonicalText", "fields", "checks", "provenance"}
    assert e["page"] == 167 and e["name"] == "Goblin" and e["canonicalText"]
    names = {c["name"] for c in e["checks"]}
    assert {"name-found", "text-present", "numeric-clean", "unique-id", "name-words-known"} <= names
    assert all(set(c) == {"name", "passed", "reason"} for c in e["checks"])


def test_duplicate_id_suffixed():
    # the same page twice yields every entity twice
    r = extract_book(pages("mm", 167) + pages("mm", 167), "mm14", {"Monster"})
    a, b = by_id(r, "mm14.monster.goblin"), by_id(r, "mm14.monster.goblin-2")
    assert next(c for c in a["checks"] if c["name"] == "unique-id")["passed"]
    second = next(c for c in b["checks"] if c["name"] == "unique-id")
    assert not second["passed"] and "goblin" in second["reason"]


def test_provenance_two_pages():
    e = by_id(extract_book(pages("phb", 242, 243), "phb14", {"Spell"}), "phb14.spell.fireball")
    prov = e["provenance"]["pages"]
    assert [p["page"] for p in prov] == [242, 243]
    assert all(p["boxes"] for p in prov)
    box = prov[0]["boxes"][0]
    assert set(box) == {"x0", "y0", "x1", "y1"} and box["x1"] > box["x0"] and box["y1"] > box["y0"]


def test_report_warning():
    ents = [{"id": f"b.spell.s{i}", "type": "Spell",
             "checks": [{"name": "c", "passed": i != 0, "reason": ""}]} for i in range(10)]
    r = build_report("b", 10, 10, 10, ents)
    assert r["passingShare"] == pytest.approx(0.9) and r["coverageShare"] == 1.0
    assert len(r["warnings"]) == 1 and "90.0%" in r["warnings"][0] and "under 95%" in r["warnings"][0]
    assert r["failed"] == [{"id": "b.spell.s0", "checks": ["c"]}]
    assert r["counts"] == {"Monster": 0, "Spell": 10, "MagicItem": 0}


def test_non_rules_pages_excluded():
    # title art, credits, table of contents, index are not rules pages
    r = extract_book(pages("mm", 2, 3, 4, 167, 352), "mm14", {"Monster"})
    assert r.report["pagesTotal"] == 5 and r.report["pagesRules"] == 1
    assert r.report["pagesCovered"] == 1 and r.report["coverageShare"] == 1.0
    assert r.report["warnings"] == [] or all("passing" in w for w in r.report["warnings"])


def test_real_ocr_glue_repaired():
    r = extract_book(pages("dmg", 200, 201), "dmg14", {"MagicItem"})
    e = by_id(r, "dmg14.magicitem.spell-scroll")
    rep = next(c for c in e["checks"] if c["name"] == "name-repaired")
    assert rep["passed"] and "Spellscroll" in rep["reason"]
    assert e["canonicalText"].startswith("Spell Scroll")


@pytest.mark.parametrize("book,ns,t,schema", [
    ("mm", (167, 169, 134, 206, 50, 338), "Monster", "MonsterFields"),
    ("phb", (229, 240, 242, 243, 259, 273), "Spell", "SpellFields"),
    ("dmg", (150, 200, 201, 224, 225, 226), "MagicItem", "MagicItemFields"),
])
def test_fields_validate_schema(book, ns, t, schema):
    import json

    import jsonschema
    path = Path(__file__).parents[3] / "Schemas" / "canonical" / f"{schema}.schema.json"
    sch = json.loads(path.read_text(encoding="utf-8"))
    r = extract_book(pages(book, *ns), f"{book}14", {t})
    assert r.entities
    for e in r.entities:
        jsonschema.validate(e["fields"], sch)
