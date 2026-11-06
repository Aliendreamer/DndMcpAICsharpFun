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
    assert r["counts"]["Spell"] == 10 and r["counts"]["Monster"] == 0 and r["counts"]["Class"] == 0


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


def _parsed(fields, text="x " * 30):
    from bookextractor.parsers import Parsed
    return Parsed(type="Spell", name="Fireball", fields=fields, text=text, lines=[], checks=[])


def _numeric(fields, text="x " * 30):
    from collections import Counter

    from bookextractor.names import build_vocabulary
    from bookextractor.assemble import _id_and_checks
    p = _parsed(fields, text)
    _eid, checks = _id_and_checks(p, "b", Counter(), build_vocabulary(["fireball fireball"] * 2))
    return p, next(c for c in checks if c.name == "numeric-clean")


def test_digit_noise_repaired_before_numeric_clean():
    p, c = _numeric({"entries": ["The target takes 8ld8 fire damage... or ld8 here, 33I"]},
                    text="Fireball\nIt deals ld8 damage. " * 3)
    assert "1d8" in p.fields["entries"][0] and "331" in p.fields["entries"][0]
    assert "ld8" not in p.text and "1d8" in p.text
    assert c.passed, c.reason


def test_unrepairable_numeric_still_fails():
    # a numeric field holding letters repair cannot turn into a number
    _p, c = _numeric({"hp": {"average": "7x", "formula": "2d6"}})
    assert not c.passed and "average" in c.reason
    _p, c = _numeric({"cr": "1/Q"})
    assert not c.passed


def test_dmg_coverage_counts_tables_and_excludes_maps_and_index():
    r = extract_book(pages("dmg", 95, 137, 163, 310, 317), "dmg14", {"MagicItem"})
    assert r.report["pagesRules"] == 2 and r.report["pagesCovered"] == 2


# ---------- character types (Phase 2a) ----------
CHAR_SCHEMAS = {"Class": "ClassFields", "Subclass": "SubclassFields", "Race": "RaceFields",
                "Subrace": "SubraceFields", "Feat": "FeatFields", "Background": "BackgroundFields"}
PHB_CHAR_PAGES = (19, 20, 21, 22, 46, 47, 48, 49, 50, 51, 128, 129, 166, 167, 168)


def test_character_types_registered():
    from bookextractor.assemble import ALL_TYPES
    assert ALL_TYPES == ("Monster", "Spell", "MagicItem", "Class", "Subclass", "Race", "Subrace", "Feat",
                         "Background")


def test_phb_character_ids_and_schemas():
    import json

    import jsonschema
    from bookextractor.assemble import ALL_TYPES
    r = extract_book(pages("phb", *PHB_CHAR_PAGES), "phb14", set(ALL_TYPES))
    for eid in ("phb14.class.barbarian", "phb14.race.dwarf", "phb14.subrace.dwarf-hill-dwarf",
                "phb14.background.acolyte", "phb14.feat.heavily-armored"):
        by_id(r, eid)
    assert by_id(r, "phb14.subrace.dwarf-hill-dwarf")["name"] == "Hill Dwarf"
    ids = [e["id"] for e in r.entities]
    assert len(ids) == len(set(ids))
    root = Path(__file__).parents[3] / "Schemas" / "canonical"
    for e in r.entities:
        if e["type"] in CHAR_SCHEMAS:
            sch = json.loads((root / f"{CHAR_SCHEMAS[e['type']]}.schema.json").read_text(encoding="utf-8"))
            jsonschema.validate(e["fields"], sch)
    assert r.report["counts"]["Class"] == 1 and r.report["counts"]["Subrace"] >= 2


def test_races_parsed_once_for_race_and_subrace(monkeypatch):
    from bookextractor import assemble
    from bookextractor.parsers import race
    calls = []
    real = race.parse_races

    def counting(body, ctx):
        calls.append(1)
        return real(body, ctx)
    monkeypatch.setattr(assemble, "parse_races", counting)
    r = extract_book(pages("phb", 19, 20, 21, 22), "phb14", {"Race", "Subrace"})
    assert len(calls) == 1
    assert {e["type"] for e in r.entities} == {"Race", "Subrace"}


def _feature_checks(t: str, fields: dict, vocab_text: str):
    from collections import Counter

    from bookextractor.assemble import _id_and_checks
    from bookextractor.names import build_vocabulary
    from bookextractor.parsers import Parsed
    p = Parsed(type=t, name="Bard", fields=fields, text="Bard " + "x " * 30, lines=[])
    _eid, checks = _id_and_checks(p, "phb14", Counter(), build_vocabulary([vocab_text] * 3))
    return p, {c.name: c for c in checks}


def test_feature_ref_glue_repaired_from_vocabulary():
    p, cs = _feature_checks("Class", {"classFeatures": [
        "Song of Rest|Bard||2",
        {"classFeature": "Bard College|Bard||3", "gainSubclassFeature": True},
        "Magical Secretsof|Bard||10"]}, "bard song of rest college magical secrets of")
    assert p.fields["classFeatures"][2] == "Magical Secrets of|Bard||10"
    assert p.fields["classFeatures"][1] == {"classFeature": "Bard College|Bard||3", "gainSubclassFeature": True}
    assert cs["feature-name-repaired"].passed and "Secretsof" in cs["feature-name-repaired"].reason
    assert cs["feature-names-known"].passed


def test_feature_ref_unknown_word_fails_naming_it():
    _p, cs = _feature_checks("Subclass", {"subclassFeatures": [
        "College of Lore|Bard||Lore||3", "Additional Magigal Secrets|Bard||Lore||6"]},
        "bard college of lore additional magical secrets")
    c = cs["feature-names-known"]
    assert not c.passed and "Additional Magigal Secrets" in c.reason and "Magigal" in c.reason


def test_feature_names_check_only_on_class_and_subclass():
    _p, cs = _feature_checks("Feat", {"entries": []}, "bard")
    assert "feature-names-known" not in cs


def test_feature_die_annotation_is_not_a_word():
    p, cs = _feature_checks("Class", {"classFeatures": ["Song of Rest (d6)|Bard||2", "Song of Rest (d8)|Bard||9"]},
                            "bard song of rest")
    assert cs["feature-names-known"].passed, cs["feature-names-known"].reason
    assert p.fields["classFeatures"] == ["Song of Rest (d6)|Bard||2", "Song of Rest (d8)|Bard||9"]


def test_feature_dice_count_annotation_is_not_a_word():   # TCE sidekick table: "Inspiring Help (1d6)"
    refs = ["Inspiring Help (1d6)|Expert|TCE|11", "Inspiring Help (2d6)|Expert|TCE|20"]
    _p, cs = _feature_checks("Class", {"classFeatures": refs}, "expert inspiring help")
    assert cs["feature-names-known"].passed, cs["feature-names-known"].reason


def test_feature_word_printed_in_heading_and_once_more_is_known():
    from collections import Counter

    from bookextractor.assemble import _id_and_checks
    from bookextractor.names import build_vocabulary
    from bookextractor.parsers import Parsed
    # "hearth" (one letter from the common "heart") is printed twice in all: its feature heading and one more
    # line; "magigal" only once
    vocab = build_vocabulary(["Hearth of Shadow", "a hearth fire", "Magigal Ambush"]
                             + ["swashbuckler heart of shadow magical ambush"] * 3)
    p = Parsed(type="Subclass", name="Swashbuckler", fields={"subclassFeatures": [
        "Hearth of Shadow|Rogue||Swashbuckler|XGE|17"]}, text="Swashbuckler " + "x " * 30, lines=[])
    _e, checks = _id_and_checks(p, "xge", Counter(), vocab)
    assert next(c for c in checks if c.name == "feature-names-known").passed
    p.fields = {"subclassFeatures": ["Magigal Ambush|Rogue||Swashbuckler|XGE|9"]}
    _e, checks = _id_and_checks(p, "xge", Counter(), vocab)
    c = next(c for c in checks if c.name == "feature-names-known")
    assert not c.passed and "Magigal" in c.reason


def test_subclass_cue_name_repair_recorded_once():   # R11: the parser's cue-based rename reaches the entity
    r = extract_book(pages("egw", 184, 185, 186, 187), "egw", {"Subclass"})
    e = by_id(r, "egw.subclass.echo-knight")
    reps = [c for c in e["checks"] if c["name"] == "name-repaired"]
    assert len(reps) == 1 and reps[0]["passed"]
    assert "Eco Knight" in reps[0]["reason"] and "Echo Knight" in reps[0]["reason"]


def test_parser_and_vocabulary_name_repairs_merge_into_one_check():
    from collections import Counter

    from bookextractor.assemble import _id_and_checks
    from bookextractor.checks import Check
    from bookextractor.names import build_vocabulary
    from bookextractor.parsers import Parsed
    p = Parsed(type="Subclass", name="Spellscroll", fields={}, text="Spellscroll\n" + "body text " * 5, lines=[])
    p.checks = [Check("name-repaired", True, "'Spel Scroll' -> 'Spellscroll' (from 2 cues)")]
    vocab = build_vocabulary(["a spell scroll and a spell scroll and the spell scroll"] * 3)
    _, checks = _id_and_checks(p, "b", Counter(), vocab)
    reps = [c for c in checks if c.name == "name-repaired"]
    assert len(reps) == 1 and "from 2 cues" in reps[0].reason and "then 'Spellscroll' -> 'Spell Scroll'" in reps[0].reason


def _glued_checks(vocab_lines):
    from collections import Counter

    from bookextractor.assemble import _id_and_checks
    from bookextractor.names import build_vocabulary
    from bookextractor.parsers import Parsed
    vocab = build_vocabulary(vocab_lines + ["swashbuckler"] * 3)
    p = Parsed(type="Subclass", name="Swashbuckler", fields={"subclassFeatures": [
        "Elegantmaneuver|Rogue||Swashbuckler|SCAG|13"]}, text="Swashbuckler " + "x " * 30, lines=[])
    _e, checks = _id_and_checks(p, "scag", Counter(), vocab)
    return p, {c.name: c for c in checks}


def test_glued_feature_name_split_on_an_inflected_pair():
    # SCAG prints "EleGanTManeuVeR" (OCR-glued) and "elegant maneuvers" in the body: the pair evidence is inflected
    p, cs = _glued_checks(["EleGanTManeuVeR", "elegant maneuvers that fend off harm", "an elegant style",
                           "a maneuver", "maneuvers"])
    assert p.fields["subclassFeatures"][0].startswith("Elegant Maneuver|"), p.fields
    assert cs["feature-names-known"].passed and cs["feature-name-repaired"].passed


def test_glued_feature_name_of_two_known_words_without_evidence_is_flagged():
    p, cs = _glued_checks(["EleGanTManeuVeR", "an elegant style", "elegant", "a maneuver", "maneuver"])
    c = cs["feature-names-known"]
    assert not c.passed and "Elegantmaneuver" in c.reason, c


def test_subclass_name_repair_reaches_short_name_and_refs():   # TCE "Feywanderer" -> "Fey Wanderer"
    r = extract_book(pages("tce", 59, 60), "tce", {"Subclass"})
    e = by_id(r, "tce.subclass.fey-wanderer")
    assert e["name"] == "Fey Wanderer" and e["fields"]["shortName"] == "Fey Wanderer"
    refs = e["fields"]["subclassFeatures"]
    assert refs[0] == "Fey Wanderer|Ranger||Fey Wanderer|TCE|3"
    assert all(r.split("|")[3] == "Fey Wanderer" for r in refs), refs
    rep = next(c for c in e["checks"] if c["name"] == "name-repaired")
    assert rep["passed"] and "shortName 'Feywanderer' -> 'Fey Wanderer'" in rep["reason"], rep["reason"]


def test_subclass_name_repair_rederives_a_prefixed_short_name():
    from collections import Counter

    from bookextractor.assemble import _id_and_checks
    from bookextractor.names import build_vocabulary
    from bookextractor.parsers import Parsed
    fields = {"className": "Sorcerer", "classSource": "PHB", "shortName": "Clockworksoul",
              "subclassFeatures": ["Clockworksoul|Sorcerer||Clockworksoul|TCE|1",
                                   "Bastion of Law|Sorcerer||Clockworksoul|TCE|6"]}
    p = Parsed(type="Subclass", name="Clockworksoul", fields=fields, text="Clockworksoul\n" + "body text " * 5,
               lines=[])
    vocab = build_vocabulary(["a clockwork soul and the clockwork soul of a bastion of law"] * 3)
    _, checks = _id_and_checks(p, "tce", Counter(), vocab)
    assert p.name == "Clockwork Soul" and p.fields["shortName"] == "Clockwork Soul"
    assert p.fields["subclassFeatures"] == ["Clockwork Soul|Sorcerer||Clockwork Soul|TCE|1",
                                            "Bastion of Law|Sorcerer||Clockwork Soul|TCE|6"]
    assert any(c.name == "name-repaired" and "shortName" in c.reason for c in checks)


def _ent(eid, t):
    return {"id": eid, "type": t, "checks": []}


def _keys(lo, hi, page=1):
    return {(page, (0.0, float(y), 10.0, float(y) + 5)) for y in range(lo, hi)}


def test_entities_of_different_types_sharing_printed_lines_both_fail():
    from bookextractor.assemble import shared_text_checks
    sub, bg = _ent("b.subclass.giant", "Subclass"), _ent("b.background.giant-foundling", "Background")
    shared_text_checks([sub, bg], [_keys(0, 60), _keys(48, 90)])
    a = next(c for c in sub["checks"] if c["name"] == "text-not-shared")
    b = next(c for c in bg["checks"] if c["name"] == "text-not-shared")
    assert not a["passed"] and "b.background.giant-foundling" in a["reason"] and "12" in a["reason"], a
    assert not b["passed"] and "b.subclass.giant" in b["reason"], b


def test_a_few_shared_lines_or_the_same_type_pass():
    from bookextractor.assemble import shared_text_checks
    race, sub, other = _ent("b.race.elf", "Race"), _ent("b.subrace.elf-high", "Subrace"), _ent("b.race.dwarf", "Race")
    shared_text_checks([race, sub, other], [_keys(0, 50), _keys(45, 80), _keys(0, 50)])
    assert all(c["passed"] for e in (race, sub, other) for c in e["checks"] if c["name"] == "text-not-shared")
    assert any(c["name"] == "text-not-shared" for c in race["checks"])


def test_a_stat_block_printed_inside_a_subclass_is_not_bleed():
    from bookextractor.assemble import shared_text_checks
    sub, mon = _ent("b.subclass.drakewarden", "Subclass"), _ent("b.monster.drake-companion", "Monster")
    shared_text_checks([sub, mon], [_keys(0, 200), _keys(50, 140)])
    assert all(c["passed"] for c in sub["checks"] if c["name"] == "text-not-shared")
    assert not mon["checks"]   # a Phase 1 entity gets the check only when it fails


def test_phase1_types_sharing_lines_are_not_gated():
    from bookextractor.assemble import shared_text_checks
    mon, item = _ent("b.monster.giant-fly", "Monster"), _ent("b.magicitem.wand-of-orcus", "MagicItem")
    shared_text_checks([mon, item], [_keys(0, 200), _keys(50, 140)])
    assert not mon["checks"] and not item["checks"]


def test_a_stat_block_starting_inside_a_subclass_and_running_past_it_is_not_bleed():
    # v19: the Drake Companion stat block opens inside Drakewarden and its parse runs on past the subclass;
    # the subclass printed it, the over-run is the monster's own (already failing) problem
    from bookextractor.assemble import shared_text_checks
    sub, mon = _ent("b.subclass.drakewarden", "Subclass"), _ent("b.monster.drake-companion", "Monster")
    mk = _keys(100, 260)
    shared_text_checks([sub, mon], [_keys(0, 200), mk], firsts=[(1, (0.0, 0.0, 10.0, 5.0)),
                                                                  (1, (0.0, 100.0, 10.0, 105.0))])
    assert all(c["passed"] for c in sub["checks"] if c["name"] == "text-not-shared")
    # a monster that opens before the subclass and runs into it is still bleed
    sub2, mon2 = _ent("b.subclass.x", "Subclass"), _ent("b.monster.y", "Monster")
    shared_text_checks([sub2, mon2], [_keys(100, 300), _keys(0, 200)], firsts=[(1, (0.0, 100.0, 10.0, 105.0)),
                                                                                (1, (0.0, 0.0, 10.0, 5.0))])
    assert not next(c for c in sub2["checks"] if c["name"] == "text-not-shared")["passed"]


def _feature_ref_checks(refs, vocab_lines):
    from collections import Counter

    from bookextractor.assemble import _id_and_checks
    from bookextractor.names import build_vocabulary
    from bookextractor.parsers import Parsed
    vocab = build_vocabulary(vocab_lines + ["armorer"] * 3)
    p = Parsed(type="Subclass", name="Armorer", fields={"subclassFeatures": list(refs)},
               text="Armorer " + "x " * 30, lines=[])
    _e, checks = _id_and_checks(p, "tce", Counter(), vocab)
    return p, {c.name: c for c in checks}


def test_glued_feature_name_printed_glued_in_several_headings_is_split_on_the_spaced_pair():
    # TCE prints the small-caps heading "ExtRAATtaCk" glued three times (Armorer, Battle Smith, Bladesinging), so
    # the glued word is "known"; the body prints "extra attack" more often than that, so the glue is a misread
    p, cs = _feature_ref_checks(["Extraattack|Artificer|TCE|Armorer|TCE|5"],
                                ["ExtRAATtaCk"] * 3 + ["the Extra Attack feature", "an extra attack", "extra attacks",
                                                       "extra damage", "an attack roll"])
    assert p.fields["subclassFeatures"] == ["Extra Attack|Artificer|TCE|Armorer|TCE|5"], p.fields
    assert cs["feature-name-repaired"].passed and "Extraattack" in cs["feature-name-repaired"].reason
    assert cs["feature-names-known"].passed


def test_a_compound_printed_far_more_often_than_its_spaced_pair_is_kept():
    # PHB prints "spellcasting" ~150 times and "spell casting" once: the compound is the real word
    p, cs = _feature_ref_checks(["Spellcasting|Artificer|TCE|Armorer|TCE|5"],
                                ["spellcasting ability"] * 20 + ["spell casting", "a spell", "casting time"])
    assert p.fields["subclassFeatures"] == ["Spellcasting|Artificer|TCE|Armorer|TCE|5"]
    assert "feature-name-repaired" not in cs and cs["feature-names-known"].passed
