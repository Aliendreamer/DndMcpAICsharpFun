from pathlib import Path

import pytest

from bookextractor.assemble import ExtractionResult, extract_book, slugify
from bookextractor.model import Line, load_ocr_page
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
    assert ALL_TYPES[:9] == ("Monster", "Spell", "MagicItem", "Class", "Subclass", "Race", "Subrace", "Feat",
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


def _texts(keys, named_at=None, name=""):
    """Each printed line's text in print order; the line at y `named_at` names the stat block."""
    return [(k, f"the {name} stat block" if k[1][1] == named_at else "body text")
            for k in sorted(keys, key=lambda k: k[1][1])]


def test_a_stat_block_printed_inside_a_subclass_is_not_bleed():
    from bookextractor.assemble import shared_text_checks
    sub, mon = _ent("b.subclass.drakewarden", "Subclass"), _ent("b.monster.drake-companion", "Monster")
    mon["name"] = "Drake Companion"
    sk, mk = _keys(0, 200), _keys(50, 140)
    shared_text_checks([sub, mon], [sk, mk], firsts=[(1, (0.0, 0.0, 10.0, 5.0)), (1, (0.0, 50.0, 10.0, 55.0))],
                       texts=[_texts(sk, 10, "Drake Companion"), _texts(mk)])
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
    mon["name"] = "Drake Companion"
    sk, mk = _keys(0, 200), _keys(100, 260)
    shared_text_checks([sub, mon], [sk, mk], firsts=[(1, (0.0, 0.0, 10.0, 5.0)), (1, (0.0, 100.0, 10.0, 105.0))],
                       texts=[_texts(sk, 40, "drake companion"), _texts(mk)])
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


def test_an_unrelated_stat_block_swallowed_by_a_subclass_is_bleed():
    # the host prints the stat block only when its text before the block names the monster (Drakewarden names its
    # Drake Companion); a stat block the host never names is another section's, swallowed by a run-on parse
    from bookextractor.assemble import shared_text_checks
    sub, mon = _ent("b.subclass.drakewarden", "Subclass"), _ent("b.monster.owlbear", "Monster")
    mon["name"] = "Owlbear"
    sk, mk = _keys(0, 200), _keys(50, 140)
    firsts = [(1, (0.0, 0.0, 10.0, 5.0)), (1, (0.0, 50.0, 10.0, 55.0))]
    shared_text_checks([sub, mon], [sk, mk], firsts=firsts, texts=[_texts(sk, 10, "Drake Companion"), _texts(mk)])
    a = next(c for c in sub["checks"] if c["name"] == "text-not-shared")
    assert not a["passed"] and "b.monster.owlbear" in a["reason"], a
    assert not next(c for c in mon["checks"] if c["name"] == "text-not-shared")["passed"]
    # named only after the stat block opens: still not the host's
    sub2, mon2 = _ent("b.subclass.drakewarden", "Subclass"), _ent("b.monster.owlbear", "Monster")
    mon2["name"] = "Owlbear"
    shared_text_checks([sub2, mon2], [sk, mk], firsts=firsts, texts=[_texts(sk, 150, "Owlbear"), _texts(mk)])
    assert not next(c for c in sub2["checks"] if c["name"] == "text-not-shared")["passed"]


def test_glued_feature_name_whose_spaced_pair_is_printed_once_is_split():
    # XGE prints the heading "VeNgeFULAnceSTORS" once and the table cell "Vengeful Ancestors" once: "vengeful" is
    # printed nowhere else, but the spaced pair itself attests both words
    p, cs = _feature_ref_checks(["Vengefulancestors|Barbarian||Ancestral Guardian|XGE|14"],
                                ["VeNgeFULAnceSTORS", "Vengeful Ancestors, Spirit Shield (4d8)", "your ancestors",
                                 "the ancestors", "ancestors"])
    assert p.fields["subclassFeatures"] == ["Vengeful Ancestors|Barbarian||Ancestral Guardian|XGE|14"], p.fields
    assert cs["feature-names-known"].passed


def test_glued_feature_name_with_a_common_word_inside_and_no_split_evidence_is_flagged():
    # TCE prints "UNIVERSALSPEECH" once and never "universal" elsewhere: no split can be proved, but a common word
    # of six or more letters ("speech") glued to five or more letters is a lost space, not a coined word
    p, cs = _feature_ref_checks(["Universalspeech|Bard||Eloquence|TCE|6"],
                                ["UNIVERSALSPEECH", "speech"] + ["your speech is clear"] * 3)
    c = cs["feature-names-known"]
    assert not c.passed and "Universalspeech" in c.reason and "speech" in c.reason, c
    assert p.fields["subclassFeatures"] == ["Universalspeech|Bard||Eloquence|TCE|6"]   # flagged, never guessed


def test_a_coined_word_ending_in_a_line_break_fragment_is_not_flagged():
    # "-ments" / "-tions" / "-ation" are printed as words where a line breaks; they are not a glued common word
    p, cs = _feature_ref_checks(["Beguiling Twist|Ranger||Fey Wanderer|TCE|11", "Fey Reinforcements|Ranger||Fey Wanderer|TCE|11"],
                                ["Beguiling Twist", "Fey Reinforcements", "ments", "ments", "ments", "fey", "fey",
                                 "a reinforce", "twist", "twist", "beguiling"])
    assert cs["feature-names-known"].passed, cs["feature-names-known"]


def test_a_repaired_feature_name_reaches_the_entry_heading():   # TCE Armorer "ExtRAATtaCk"
    from collections import Counter

    from bookextractor.assemble import _id_and_checks
    from bookextractor.layout import reading_order
    from bookextractor.names import build_vocabulary
    from bookextractor.parsers import ParseContext
    from bookextractor.parsers.subclass import parse_subclasses
    ps = {p.number: p for p in pages("tce", 15, 16, 17, 148)}
    body = [l for n in (15, 16, 17) for l in reading_order(ps[n])]
    sub = next(p for p in parse_subclasses(body, ParseContext(slug="tce", source="TCE", pages=ps)) if p.name == "Armorer")
    # the vocabulary is the book's: page 148 (the Warrior sidekick) prints "Extra Attack" spaced
    vocab = build_vocabulary(l.text for n in (15, 16, 17, 148) for l in reading_order(ps[n]))
    _id_and_checks(sub, "tce", Counter(), vocab)
    names = [x["name"] for x in sub.fields["entries"]]
    assert names[4] == "Extra Attack" and "Extraattack" not in names, names
    assert "Extra Attack|Artificer|TCE|Armorer|TCE|5" in sub.fields["subclassFeatures"]


def test_entry_names_are_repaired_and_checked_like_refs():
    # the first-level features are folded into the subclass-named ref, so their entry headings are checked on their own
    p, cs = _feature_ref_checks(["Armorer|Artificer|TCE|Armorer|TCE|3"],
                                ["armorer", "tools of the trade", "tools of the trade", "fullof", "full of stars",
                                 "full of stars"] + ["magigal"])
    p.fields["entries"] = [{"type": "entries", "name": "Fullof Stars", "entries": []},
                           {"type": "entries", "name": "Magigal Armor", "entries": []}]
    from collections import Counter

    from bookextractor.assemble import _feature_name_checks
    from bookextractor.names import build_vocabulary
    vocab = build_vocabulary(["full of stars", "full of stars", "magical armor", "magical armor", "magical", "armor",
                              "Magigal Armor"])
    checks = {c.name: c for c in _feature_name_checks(p, vocab)}
    assert [x["name"] for x in p.fields["entries"]] == ["Full of Stars", "Magigal Armor"]
    assert not checks["feature-names-known"].passed and "Magigal" in checks["feature-names-known"].reason
    assert "Fullof Stars" in checks["feature-name-repaired"].reason


def test_a_monster_name_inside_another_word_does_not_name_the_stat_block():
    # "ape" is inside "shape": an Ape stat block swallowed by a druid subclass is not one it prints
    from bookextractor.assemble import shared_text_checks
    sub, mon = _ent("b.subclass.circle-of-x", "Subclass"), _ent("b.monster.ape", "Monster")
    mon["name"] = "Ape"
    sk, mk = _keys(0, 200), _keys(50, 140)
    firsts = [(1, (0.0, 0.0, 10.0, 5.0)), (1, (0.0, 50.0, 10.0, 55.0))]
    shared_text_checks([sub, mon], [sk, mk], firsts=firsts, texts=[_texts(sk, 10, "Wild Shape"), _texts(mk)])
    assert not next(c for c in sub["checks"] if c["name"] == "text-not-shared")["passed"]
    # the name printed as words names it, whatever the case or a line break between its words
    sub2, mon2 = _ent("b.subclass.circle-of-x", "Subclass"), _ent("b.monster.ape", "Monster")
    mon2["name"] = "Ape"
    shared_text_checks([sub2, mon2], [sk, mk], firsts=firsts, texts=[_texts(sk, 10, "summoned APE,"), _texts(mk)])
    assert next(c for c in sub2["checks"] if c["name"] == "text-not-shared")["passed"]


# ---------- equipment and hazard types (Phase 2b) ----------
NEW_TYPES = ("Weapon", "Armor", "Item", "VehicleMount", "Object", "Trap", "DiseasePoison", "Condition")
PHB_EQUIP_PAGES = (144, 146, 149, 150, 151, 152, 153, 154, 155, 156, 158, 159, 228, 291, 292, 293, 294)
DMG_EQUIP_PAGES = (105, 106, 109, 110, 111, 119, 122, 123, 124, 125, 134, 135, 151, 255, 256, 257, 258, 259, 268)


def test_equipment_and_hazard_types_registered():
    from bookextractor.assemble import ALL_TYPES, _PARSERS
    assert ALL_TYPES[9:17] == NEW_TYPES
    assert set(_PARSERS) == set(ALL_TYPES)


def test_report_types_are_the_extractor_types():
    from bookextractor import report
    from bookextractor.assemble import ALL_TYPES
    assert report.TYPES == ALL_TYPES


def _validate_all(r):
    import json

    import jsonschema
    root = Path(__file__).parents[3] / "Schemas" / "canonical"
    for e in r.entities:
        sch = json.loads((root / f"{e['type']}Fields.schema.json").read_text(encoding="utf-8"))
        jsonschema.validate(e["fields"], sch)


@pytest.fixture(scope="module")
def phb_equipment():
    from bookextractor.assemble import ALL_TYPES
    return extract_book(pages("phb", *PHB_EQUIP_PAGES), "phb14", set(ALL_TYPES))


@pytest.fixture(scope="module")
def dmg_equipment():
    from bookextractor.assemble import ALL_TYPES
    return extract_book(pages("dmg", *DMG_EQUIP_PAGES), "dmg14", set(ALL_TYPES))


def test_phb_equipment_ids_and_schemas(phb_equipment):
    r = phb_equipment
    for eid in ("phb14.weapon.longsword", "phb14.armor.chain-mail", "phb14.item.crowbar", "phb14.condition.poisoned"):
        by_id(r, eid)
    ids = [e["id"] for e in r.entities]
    assert len(ids) == len(set(ids))
    _validate_all(r)
    for t in ("Weapon", "Armor", "Item", "Condition", "DiseasePoison"):
        assert r.report["counts"][t] > 0, t


def test_dmg_equipment_and_hazard_ids_and_schemas(dmg_equipment):
    r = dmg_equipment
    types = {e["type"] for e in r.entities}
    assert {"Weapon", "Item", "VehicleMount", "Object", "Trap", "DiseasePoison"} <= types, types
    ids = [e["id"] for e in r.entities]
    assert len(ids) == len(set(ids))
    _validate_all(r)
    by_id(r, "dmg14.vehiclemount.apparatus-of-kwalish")
    by_id(r, "dmg14.diseasepoison.burnt-othur-fumes")
    by_id(r, "dmg14.trap.collapsing-roof")


def test_text_not_shared_runs_over_new_types(phb_equipment, dmg_equipment):
    for r in (phb_equipment, dmg_equipment):
        for e in r.entities:
            if e["type"] in NEW_TYPES:
                assert any(c["name"] == "text-not-shared" for c in e["checks"]), e["id"]


def test_new_types_sharing_lines_with_another_type_fail():
    from bookextractor.assemble import shared_text_checks
    item, trap = _ent("b.item.x", "Item"), _ent("b.trap.y", "Trap")
    shared_text_checks([item, trap], [_keys(0, 60), _keys(40, 90)])
    assert not next(c for c in item["checks"] if c["name"] == "text-not-shared")["passed"]
    assert not next(c for c in trap["checks"] if c["name"] == "text-not-shared")["passed"]


def test_no_printed_row_is_emitted_as_two_types(phb_equipment, dmg_equipment):
    # each table row is one entity: a DMG poison row is a DiseasePoison, not also an Item; the Apparatus of Kwalish
    # (MagicItem hosting its VehicleMount stat block) is the one intended overlap
    for r in (phb_equipment, dmg_equipment):
        names = {}
        for e in r.entities:
            if e["type"] in NEW_TYPES:
                names.setdefault(e["name"].lower(), set()).add(e["type"])
        twice = {n: ts for n, ts in names.items() if len(ts) > 1}
        assert not twice, twice
    poisons = [e for e in dmg_equipment.entities if e["type"] == "DiseasePoison"]
    items = {e["name"].lower() for e in dmg_equipment.entities if e["type"] == "Item"}
    assert poisons and not any(p["name"].lower() in items for p in poisons)


def _row_checks(name, t="Weapon", table_row=True, cell_ok=True, vocab_lines=("a club and an axe",) * 3):
    from collections import Counter

    from bookextractor.assemble import _id_and_checks
    from bookextractor.checks import Check
    from bookextractor.names import build_vocabulary
    from bookextractor.parsers import Parsed
    p = Parsed(type=t, name=name, fields={}, text=f"{name}\n" + "cell " * 10, lines=[],
               checks=[Check("cost-read", cell_ok, "" if cell_ok else "cost cell blank")], table_row=table_row)
    _e, checks = _id_and_checks(p, "phb14", Counter(), build_vocabulary(list(vocab_lines)))
    return p, {c.name: c for c in checks}


def test_a_table_row_label_is_attested_by_its_table():
    # P4: Greatclub, Flail, Maul are printed once, as a row label
    for name in ("Greatclub", "Flail", "Maul"):
        _p, cs = _row_checks(name, vocab_lines=["a great hero", "the fail", "mail", "main", "great", "club"] * 3)
        assert cs["name-words-known"].passed, (name, cs["name-words-known"])
        assert "table" in cs["name-words-known"].reason


def test_a_table_row_label_whose_cells_fail_is_not_attested():
    _p, cs = _row_checks("Flail", cell_ok=False, vocab_lines=["the fail", "fail"] * 3)
    assert not cs["name-words-known"].passed


def test_a_prose_entity_name_is_not_attested_by_a_table():
    _p, cs = _row_checks("Flail", table_row=False, vocab_lines=["the fail", "fail"] * 3)
    assert not cs["name-words-known"].passed


def test_a_glued_table_row_label_with_adjacent_pair_evidence_is_repaired():
    # "Unarmedstrike": the book prints "unarmed strike" spaced, so repair_name splits the glue
    p, cs = _row_checks("Unarmedstrike",
                        vocab_lines=["an unarmed strike", "unarmed strikes", "unarmed", "strike"] * 3)
    assert p.name == "Unarmed Strike" and cs["name-words-known"].passed, (p.name, cs["name-words-known"])


def test_a_glued_table_row_label_printed_spaced_but_not_split_is_flagged():
    # "pole vaulted" is printed spaced (an inflection spaced_pair counts) but never "pole vault" itself, so
    # repair_name has no adjacent-pair evidence to split on; the label is still a lost space, not attested
    p, cs = _row_checks("Polevault", t="Item",
                        vocab_lines=["they pole vaulted", "pole", "vault", "a pole", "the vault"] * 3)
    assert p.name == "Polevault"
    c = cs["name-words-known"]
    assert not c.passed and "printed spaced as 'pole vault'" in c.reason, c


def test_a_table_row_label_with_a_non_letter_inside_is_not_attested():   # P17: Gr€ataxe is an OCR misread
    for name in ("Gr€ataxe", "Gre@taxe"):
        _p, cs = _row_checks(name, vocab_lines=["a great hero", "great", "axe"] * 3)
        c = cs["name-words-known"]
        assert not c.passed and name in c.reason and "table" not in c.reason, c


def test_a_table_row_label_needs_a_letter():   # P17 tightening of P15: a bare "7" is not a name
    _p, cs = _row_checks("7", t="Item")
    assert not cs["name-found"].passed and not cs["name-words-known"].passed


def test_name_words_reasons_drop_trailing_punctuation():   # "Bread,, loaf" read badly
    _p, cs = _row_checks("Breab, loaf", t="Item", cell_ok=False, table_row=False,
                         vocab_lines=["bread", "bread", "bread", "a loaf", "loaf"] * 3)
    c = cs["name-words-known"]
    assert not c.passed and c.reason == "unknown words in name: Breab", c


def test_a_table_row_label_may_start_with_a_digit():   # P15: "10-foot pole" is printed
    _p, cs = _row_checks("10-foot pole", t="Item", vocab_lines=["a foot pole", "foot pole"] * 3)
    assert cs["name-found"].passed
    _p, cs = _row_checks("10-foot pole", t="Item", table_row=False, vocab_lines=["a foot pole"] * 3)
    assert not cs["name-found"].passed


def test_a_vehicle_stat_block_inside_its_magic_item_is_not_bleed():   # P10: Apparatus of Kwalish
    from bookextractor.assemble import shared_text_checks
    for t in ("VehicleMount", "Object"):
        item, veh = _ent("dmg14.magicitem.apparatus-of-kwalish", "MagicItem"), _ent("dmg14.x.apparatus", t)
        veh["name"] = "Apparatus of Kwalish"
        ik, vk = _keys(0, 200), _keys(50, 140)
        firsts = [(1, (0.0, 0.0, 10.0, 5.0)), (1, (0.0, 50.0, 10.0, 55.0))]
        shared_text_checks([item, veh], [ik, vk], firsts=firsts,
                           texts=[_texts(ik, 10, "Apparatus of Kwalish"), _texts(vk)])
        assert not item["checks"], item   # the MagicItem's disposition is untouched
        assert next(c for c in veh["checks"] if c["name"] == "text-not-shared")["passed"]
    # a stat block the item never names before it is bleed
    item, veh = _ent("dmg14.magicitem.x", "MagicItem"), _ent("dmg14.vehiclemount.galley", "VehicleMount")
    veh["name"] = "Galley"
    shared_text_checks([item, veh], [ik, vk], firsts=firsts, texts=[_texts(ik, 10, "Apparatus"), _texts(vk)])
    assert not next(c for c in veh["checks"] if c["name"] == "text-not-shared")["passed"]
    assert not next(c for c in item["checks"] if c["name"] == "text-not-shared")["passed"]


def _short_row_checks(table_row, cell_ok):
    from collections import Counter

    from bookextractor.assemble import _id_and_checks
    from bookextractor.checks import Check
    from bookextractor.names import build_vocabulary
    from bookextractor.parsers import Parsed
    p = Parsed(type="Item", name="Bell", fields={}, text="Bell\n1 gp\n- lb.", lines=[],
               checks=[Check("cost-read", cell_ok, "" if cell_ok else "cost cell blank")], table_row=table_row)
    _e, checks = _id_and_checks(p, "phb14", Counter(), build_vocabulary(["a bell rings", "the bell"] * 3))
    return {c.name: c for c in checks}


def test_a_short_table_row_whose_cells_read_has_its_text():
    # PHB "Bell | 1 gp | -": the row's printed cells are all the text the book has for it
    c = _short_row_checks(table_row=True, cell_ok=True)["text-present"]
    assert c.passed and "cells" in c.reason, c


def test_a_short_table_row_whose_cells_fail_still_lacks_text():
    assert not _short_row_checks(table_row=True, cell_ok=False)["text-present"].passed


def test_a_short_prose_entity_still_lacks_text():
    assert not _short_row_checks(table_row=False, cell_ok=True)["text-present"].passed


def test_equipment_row_sharing_a_few_lines_with_another_type_is_bleed():
    # a 1-3 line equipment row never reaches 10 shared lines: the gate for the Phase 2b types is relative to the
    # smaller entity (at least half its lines, at least 2, at most the old 9)
    from bookextractor.assemble import shared_text_checks
    row, bg = _ent("b.item.rope", "Item"), _ent("b.background.sailor", "Background")
    shared_text_checks([row, bg], [_keys(0, 3), _keys(1, 40)])
    assert not next(c for c in row["checks"] if c["name"] == "text-not-shared")["passed"]
    # the background keeps its own (older) gate: its disposition does not change
    assert next(c for c in bg["checks"] if c["name"] == "text-not-shared")["passed"]
    trap, sub = _ent("b.trap.pits", "Trap"), _ent("b.subclass.x", "Subclass")
    shared_text_checks([trap, sub], [_keys(0, 40), _keys(31, 80)])  # 9 lines: the old gate let it pass
    assert not next(c for c in trap["checks"] if c["name"] == "text-not-shared")["passed"]
    assert next(c for c in sub["checks"] if c["name"] == "text-not-shared")["passed"]
    item, weapon = _ent("b.item.bullets", "Item"), _ent("b.weapon.pistol", "Weapon")
    shared_text_checks([item, weapon], [_keys(0, 2), _keys(0, 3)])  # two Phase 2b types: both fail
    assert not any(c["passed"] for e in (item, weapon) for c in e["checks"] if c["name"] == "text-not-shared")


def test_one_shared_line_or_old_type_pairs_keep_the_old_gate():
    from bookextractor.assemble import shared_text_checks
    row, bg = _ent("b.item.rope", "Item"), _ent("b.background.sailor", "Background")
    shared_text_checks([row, bg], [_keys(0, 3), _keys(2, 40)])  # one line of a 3-line row
    assert all(c["passed"] for e in (row, bg) for c in e["checks"] if c["name"] == "text-not-shared")
    sub, bg2 = _ent("b.subclass.x", "Subclass"), _ent("b.background.y", "Background")
    shared_text_checks([sub, bg2], [_keys(0, 4), _keys(1, 40)])  # 3 lines between character types: unchanged
    assert all(c["passed"] for e in (sub, bg2) for c in e["checks"] if c["name"] == "text-not-shared")


WORLD_TYPES = ("God", "Rule", "Plane", "Faction", "Location", "Lore")


def test_world_types_registered_after_the_earlier_types():
    # God and Rule run before the world section types, which see their claims
    from bookextractor.assemble import ALL_TYPES, _PARSERS
    assert ALL_TYPES[17:] == WORLD_TYPES
    assert set(_PARSERS) == set(ALL_TYPES)


@pytest.fixture(scope="module")
def phb_world():
    from bookextractor.assemble import ALL_TYPES
    return extract_book(pages("phb", 14, 19, 20, 21, 22, 300), "phb14", set(ALL_TYPES))


def test_phb_god_and_rule_ids_and_schemas(phb_world):
    r = phb_world
    anubis = by_id(r, "phb14.god.anubis")
    assert anubis["fields"]["pantheon"] == "Egyptian"
    rule = by_id(r, "phb14.rule.customizing-ability-scores")
    assert rule["name"] == "Customizing Ability Scores"
    ids = [e["id"] for e in r.entities]
    assert len(ids) == len(set(ids))
    _validate_all(r)
    assert r.report["counts"]["God"] > 0 and r.report["counts"]["Rule"] > 0


def test_text_not_shared_runs_over_world_types(phb_world):
    for e in phb_world.entities:
        if e["type"] in WORLD_TYPES:
            assert any(c["name"] == "text-not-shared" for c in e["checks"]), e["id"]


def test_no_printed_line_is_both_an_earlier_and_a_world_entity(monkeypatch):
    # the world parsers take the claimed lines and names of every earlier type: no world entity holds a printed
    # line an earlier entity (the Dwarf race, p19-22) holds, and no two world entities share one
    from bookextractor import assemble
    from bookextractor.assemble import ALL_TYPES, _line_keys
    got: list[tuple[str, set]] = []
    for t, f in list(assemble._PARSERS.items()):
        def wrap(body, ctx, f=f, t=t):
            out = f(body, ctx)
            got.extend((t, _line_keys(p.lines)) for p in out)
            return out
        monkeypatch.setitem(assemble._PARSERS, t, wrap)
    extract_book(pages("phb", 14, 19, 20, 21, 22, 300), "phb14", set(ALL_TYPES))
    old = set().union(*(k for t, k in got if t not in WORLD_TYPES))
    new = [k for t, k in got if t in WORLD_TYPES]
    assert old and new and any(t == "Race" for t, _ in got)
    assert all(not (k & old) for k in new)
    assert all(not (a & b) for i, a in enumerate(new) for b in new[i + 1:])


def test_claims_reach_the_world_parsers(monkeypatch):
    # every earlier entity's printed lines and names, as printed, are claimed before each world type parses: God
    # sees the earlier types', Rule sees God's too, and the world sections see Rule's too
    from bookextractor import assemble
    from bookextractor.parsers import Parsed
    seen = {}

    def emit(t, name, page):
        line = Line(name, (0.0, 0.0, 10.0, 5.0), 5.0, page)

        def parse(body, ctx):
            seen[t] = (set(ctx.cache["claimed_lines"]), set(ctx.cache["claimed_names"]))
            return [Parsed(type=t, name=name, fields={}, text=name + " x" * 20, lines=[line])]
        return parse

    def world(body, ctx):
        seen["world"] = (set(ctx.cache["claimed_lines"]), set(ctx.cache["claimed_names"]))
        return []

    monkeypatch.setitem(assemble._PARSERS, "Race", emit("Race", "Dwarf", 19))
    monkeypatch.setitem(assemble._PARSERS, "God", emit("God", "Anubis", 20))
    monkeypatch.setitem(assemble._PARSERS, "Rule", emit("Rule", "Variant: Encumbrance", 21))
    monkeypatch.setattr(assemble, "parse_world", world)
    assemble.extract_book(pages("phb", 19), "phb14", {"Race", "God", "Rule", "Lore"})
    key = lambda n: (n, (0.0, 0.0, 10.0, 5.0))  # noqa: E731
    assert seen["God"] == ({key(19)}, {"Dwarf"})
    assert seen["Rule"] == ({key(19), key(20)}, {"Dwarf", "Anubis"})
    assert seen["world"] == ({key(19), key(20), key(21)}, {"Dwarf", "Anubis", "Variant: Encumbrance"})


def test_world_entity_sharing_lines_with_an_earlier_type_fails_alone():
    # one-sided: the earlier entity's disposition never changes for a world entity
    from bookextractor.assemble import shared_text_checks
    cls, lore = _ent("b.class.x", "Class"), _ent("b.lore.y", "Lore")
    shared_text_checks([cls, lore], [_keys(0, 60), _keys(30, 90)])
    assert next(c for c in cls["checks"] if c["name"] == "text-not-shared")["passed"]
    bad = next(c for c in lore["checks"] if c["name"] == "text-not-shared")
    assert not bad["passed"] and "b.class.x" in bad["reason"]
    mon = _ent("b.monster.z", "Monster")
    shared_text_checks([mon, _ent("b.lore.y", "Lore")], [_keys(0, 60), _keys(30, 90)])
    assert not any(c["name"] == "text-not-shared" for c in mon["checks"])


def test_two_world_entities_sharing_lines_both_fail():
    from bookextractor.assemble import shared_text_checks
    rule, lore = _ent("b.rule.x", "Rule"), _ent("b.lore.y", "Lore")
    shared_text_checks([rule, lore], [_keys(0, 60), _keys(30, 90)])
    assert not next(c for c in rule["checks"] if c["name"] == "text-not-shared")["passed"]
    assert not next(c for c in lore["checks"] if c["name"] == "text-not-shared")["passed"]


def test_a_magic_item_a_god_names_is_hosted():
    from bookextractor.assemble import shared_text_checks
    host = [((1, (0.0, float(y), 10.0, float(y) + 5)), "the god's blade is the Sword of Kas" if y == 0 else "x")
            for y in range(0, 40)]
    keys = [{k for k, _ in host}, {k for k, _ in host[10:]}]
    god, item = _ent("b.god.vecna", "God"), _ent("b.magicitem.sword-of-kas", "MagicItem")
    item["name"] = "Sword of Kas"
    shared_text_checks([god, item], keys, [host[0][0], host[10][0]], [host, host[10:]])
    assert next(c for c in god["checks"] if c["name"] == "text-not-shared")["passed"]
    other = _ent("b.magicitem.wand-of-orcus", "MagicItem")
    other["name"] = "Wand of Orcus"
    god = _ent("b.god.vecna", "God")
    shared_text_checks([god, other], keys, [host[0][0], host[10][0]], [host, host[10:]])
    assert not next(c for c in god["checks"] if c["name"] == "text-not-shared")["passed"]


def _world(name, t, page, y, **fields):
    from bookextractor.parsers import Parsed
    line = Line(name, (0.0, float(y), 10.0, float(y) + 5), 5.0, page)
    return Parsed(type=t, name=name, fields=fields, text=name + " text " * 10, lines=[line])


def _ids(parsed, sections=()):
    from bookextractor.assemble import _world_ids
    from bookextractor.parsers import ParseContext
    ctx = ParseContext(slug="b", source="B", pages={}, cache={"sections": list(sections)})
    out = _world_ids(parsed, [[] for _ in parsed], "b", ctx, [])
    return [eid for eid, _ in out], [next(c for c in cs if c.name == "unique-id") for _, cs in out]


def _section(title, parents, lines, level=2):
    from bookextractor.parsers.sections import Section
    return Section(title=title, level=level, parents=parents, lines=lines, page=lines[0].page, complete=True,
                   end_reason="")


def test_a_repeated_god_name_takes_its_pantheon_everywhere():   # W4
    gods = [_world("Lugh", "God", 1, 0, pantheon="Celtic"), _world("Lugh", "God", 2, 0, pantheon="Gods of Eberron"),
            _world("Anubis", "God", 3, 0, pantheon="Egyptian")]
    ids, checks = _ids(gods)
    assert ids == ["b.god.lugh-celtic", "b.god.lugh-gods-of-eberron", "b.god.anubis"]
    assert all(c.passed for c in checks) and "Celtic" in checks[0].reason


def test_a_repeated_heading_takes_its_parent_heading_everywhere():   # W11
    a, b, c = (_world("Crime", "Lore", 1, 10), _world("Crime", "Lore", 5, 10), _world("Breland", "Location", 1, 0))
    secs = [_section("Breland", ["Nations of Khorvaire"], c.lines + a.lines, level=1),
            _section("Crime", ["Nations of Khorvaire", "Breland"], a.lines),
            _section("Karrnath", ["Nations of Khorvaire"], [Line("Karrnath", (0, 0, 10, 5), 5.0, 5)] + b.lines, 1),
            _section("Crime", ["Nations of Khorvaire", "Karrnath"], b.lines)]
    ids, checks = _ids([a, b], secs)
    assert ids == ["b.lore.breland-crime", "b.lore.karrnath-crime"]
    assert all(x.passed for x in checks) and "'Breland'" in checks[0].reason
    assert _ids([c], secs)[0] == ["b.location.breland"]


def test_a_heading_repeated_under_one_parent_takes_its_page_then_an_ordinal():   # W19: TCE p100
    a, b = _world("Religious Order Contact", "Lore", 100, 10), _world("Religious Order Contact", "Lore", 101, 10)
    secs = [_section("Religious Order Contact", ["Patrons", "Religious Order"], a.lines),
            _section("Religious Order Contact", ["Patrons", "Religious Order"], b.lines)]
    ids, checks = _ids([a, b], secs)
    assert ids == ["b.lore.religious-order-religious-order-contact-p100",
                   "b.lore.religious-order-religious-order-contact-p101"]
    assert all(c.passed for c in checks)
    c, d = _world("Religious Order Contact", "Lore", 100, 10), _world("Religious Order Contact", "Lore", 100, 40)
    secs = [_section("Religious Order Contact", ["Patrons", "Religious Order"], c.lines),
            _section("Religious Order Contact", ["Patrons", "Religious Order"], d.lines)]
    ids, checks = _ids([c, d], secs)
    assert ids == ["b.lore.religious-order-religious-order-contact-p100-1",
                   "b.lore.religious-order-religious-order-contact-p100-2"]
    assert len(set(ids)) == 2 and not any(x.passed for x in checks)
    assert _ids([c, d], secs)[0] == ids  # deterministic


def test_a_repeated_heading_with_no_parent_section_takes_its_page():   # W11 parent None -> W19 page
    a, b = _world("Currency", "Lore", 3, 10), _world("Currency", "Lore", 7, 10)
    ids, checks = _ids([a, b])  # no section holds their lines: no parent heading to name
    assert ids == ["b.lore.currency-p3", "b.lore.currency-p7"]
    assert all(c.passed for c in checks) and "parent" not in checks[0].reason


def test_a_chapter_parent_is_named_without_its_chapter_prefix():
    a, b = _world("Introduction", "Lore", 1, 0), _world("Introduction", "Lore", 9, 0)
    secs = [_section("Introduction", ["Chapter 2: Guilds of Ravnica"], a.lines, level=1),
            _section("Introduction", ["Appendix A: Conditions"], b.lines, level=1)]
    ids, checks = _ids([a, b], secs)
    assert ids == ["b.lore.guilds-of-ravnica-introduction", "b.lore.conditions-introduction"]
    assert "'Guilds of Ravnica'" in checks[0].reason


def test_a_rule_id_naming_a_caption_parent_fails_entry_level():   # W21
    from bookextractor.assemble import _world_ids
    from bookextractor.parsers import ParseContext
    cap = Line("Stronghold Cost Time", (0, 0, 10, 5), 5.0, 128)
    row = Line("Abbey 50,000 gp 400 days", (0, 10, 10, 15), 5.0, 128)
    good = Line("Spellcasters live here. They study.", (0, 10, 10, 15), 5.0, 130)
    a, b = _world("Carousing", "Rule", 128, 20), _world("Carousing", "Rule", 130, 20)
    secs = [_section("Stronghold Cost Time", ["Between Adventures"], [cap, row] + a.lines, level=1),
            _section("Towers", ["Between Adventures"], [Line("Towers", (0, 0, 10, 5), 5.0, 130), good] + b.lines, 1)]
    ctx = ParseContext(slug="b", source="B", pages={}, cache={"sections": secs})
    out = _world_ids([a, b], [[], []], "b", ctx, [])
    assert [i for i, _ in out] == ["b.rule.stronghold-cost-time-carousing", "b.rule.towers-carousing"]
    bad = [c for c in out[0][1] if c.name == "entry-level"]
    assert bad and not bad[0].passed and "'Stronghold Cost Time'" in bad[0].reason and "'Carousing'" in bad[0].reason
    assert not [c for c in out[1][1] if c.name == "entry-level"]


@pytest.fixture(scope="module")
def dmg_downtime():
    from bookextractor.assemble import ALL_TYPES
    return extract_book(pages("dmg", 125, 126, 127, 128, 129, 130, 131), "dmg14", set(ALL_TYPES))


def _entry_level(e):
    return next(c for c in e["checks"] if c["name"] == "entry-level")


def test_dmg_caption_parent_never_lets_a_world_entity_through(dmg_downtime):   # W21: DMG p128
    r = dmg_downtime
    # the "Stronghold Cost Time" caption read as a level-1 heading, over the downtime activities: nothing on p128-131
    # is Accepted as a world entity under it or its "Carousing" table title
    late = [e for e in r.entities if e["type"] in WORLD_TYPES and e["type"] != "Rule" and e["page"] >= 128]
    assert late and not any(all(c["passed"] for c in e["checks"]) for e in late)
    # a section under a real parent with prose of its own is still an entry
    assert _entry_level(by_id(r, "dmg14.lore.campaign-tracking"))["passed"]


def test_vrgr_domains_are_read_as_their_own_sections():   # W21-W23: VRGR Falkovnia, Kalakeri, Mordent
    """The domain titles (a display title over a short "Domain of ..." / "Darklord: ..." block) are read, and the
    "DEMENTLIEU" map label over its "MAP 3.4" caption is not: no entity after Dementlieu's pages is filed under it."""
    from bookextractor.assemble import ALL_TYPES
    r = extract_book(pages("vrgr", 96, 97, 98, 101, 124, 125, 144, 145), "vrgr", set(ALL_TYPES))
    world = [e for e in r.entities if e["type"] in WORLD_TYPES]
    assert not [e["id"] for e in world if e["page"] >= 101 and e["fields"].get("category") == "Dementlieu"]
    cats = {e["fields"].get("category") for e in world if e["page"] >= 101}
    assert {"Falkovnia", "Kalakeri", "Mordent"} <= cats
    mordent = by_id(r, "vrgr.lore.mordent-noteworthy-features")
    assert "Mordent" in mordent["canonicalText"]
    kalakeri = by_id(r, "vrgr.lore.kalakeri-settlements-and-sites")
    assert "Kalakeri" in kalakeri["canonicalText"]


def test_ftd_section_filed_under_the_previous_chapter_fails():   # W22 (c): FTD p65
    from bookextractor.assemble import ALL_TYPES
    r = extract_book(pages("ftd", 33, 64, 65), "ftd", set(ALL_TYPES))
    lairs = next(e for e in r.entities if e["name"] == "Dragon Lairs")
    c = _entry_level(lairs)
    assert not c["passed"] and "another chapter's title" in c["reason"] and "Dragons in Play" in c["reason"]
    # the right-column entry under the new chapter (W25): set clearly smaller than the misfiled opener "Dragon
    # Lairs", it may be that opener's sub-section (it is: the Lair Location table sits inside Dragon Lairs)
    c = _entry_level(next(e for e in r.entities if e["name"] == "Lair Location"))
    assert not c["passed"] and "chapter opener" in c["reason"] and "'Dragon Lairs'" in c["reason"]


def test_egw_coast_entries_nest_under_the_coast():   # W22 (d), W23: EGW Menagerie Coast
    from bookextractor.assemble import ALL_TYPES
    # W24: the coast is a container as a member of a group of long listings (its span over `_LISTING_PAGES`), so
    # the fixture runs to p70
    r = extract_book(pages("egw", *range(59, 71)), "egw", set(ALL_TYPES))
    named = {e["name"]: e for e in r.entities if e["type"] in WORLD_TYPES}
    # W23: the end midpoint uses the widest sub-heading so far, so the city heading DARKTOW no longer ends the coast
    for n in ("Copia Lake", "Darktow Isle", "Darktow"):
        assert named[n]["fields"]["category"] == "Menagerie Coast", n
    for n in ("Copia Lake", "Darktow Isle"):
        assert _entry_level(named[n])["passed"], n
    # W25: Darktow holds "Doralle Woods" (1.00) and "Diver's Grave" (0.88), set like the coast's cities (median
    # 0.98): it may hold a sibling, so its place is doubted (review load, never a wrong category)
    assert "set like its parent's siblings" in _entry_level(named["Darktow"])["reason"]
    # the coast's entries the reader still nests under Darktow are set like Darktow's siblings: doubted
    for n in ("Diver's Grave", "Doralle Woods"):
        c = _entry_level(named[n])
        assert not c["passed"] and "set like the siblings" in c["reason"] and "'Darktow'" in c["reason"], n


def test_garbage_heading():   # W22 (b)
    from bookextractor.parsers.world import garbage_heading
    for t in ("Q Yes B3", "Gp Pp", "N O P Q R S T", "3 4 5"):
        assert garbage_heading(t), t
    for t in ("Dementlieu", "Rank 1: Official", "Level 1", "Chapter 2: Guilds of Ravnica", "The Lost"):
        assert not garbage_heading(t), t


def _sec(title, parents, lines, level, size=1.0, end=""):
    from bookextractor.parsers.sections import Section
    return Section(title=title, level=level, parents=parents, lines=lines, page=lines[0].page, complete=True,
                   end_reason=end, size=size)


def _ln(text, page, y):
    return Line(text, (0.0, float(y), 10.0, float(y) + 5), 5.0, page)


def test_a_doubted_parent_doubts_every_section_under_it():   # W22 (a)
    from bookextractor.parsers.world import placement_doubts
    g, c, d = _ln("Q Yes B3", 1, 0), _ln("Starting the Adventure", 1, 10), _ln("Meeting in the Mists", 1, 20)
    prose = [_ln("The characters meet a stranger in the mists.", 1, y) for y in (11, 21)]
    top = _sec("Q Yes B3", ["Chapter 5: Adventure"], [g, c, prose[0], d, prose[1]], 1)
    kid = _sec("Starting the Adventure", ["Chapter 5: Adventure", "Q Yes B3"], [c, prose[0], d, prose[1]], 2)
    grand = _sec("Meeting in the Mists", ["Chapter 5: Adventure", "Q Yes B3", "Starting the Adventure"],
                 [d, prose[1]], 3)
    out = placement_doubts([top, kid, grand], {})
    assert "'Q Yes B3'" in out[id(top)] and "no heading" in out[id(top)]  # W24: its own heading is doubted too
    assert "'Q Yes B3'" in out[id(kid)] and "no heading" in out[id(kid)]
    assert "is doubted" in out[id(grand)] and "'Starting the Adventure'" in out[id(grand)] \
        and "'Q Yes B3'" in out[id(grand)]


def test_a_section_printing_a_sub_heading_twice_doubts_everything_under_it():   # W22 (a), W23
    from bookextractor.parsers.world import placement_doubts
    text = lambda p, y: _ln("Mist rolls over the quiet village every night.", p, y)  # noqa: E731
    heads = [("Chateaufaux", 1), ("Noteworthy Features", 2), ("Noteworthy Features", 5), ("Klorr", 6)]
    hl = [(_ln(t, p, 0), text(p, 1)) for t, p in heads]
    top_h = _ln("Dementlieu", 1, -10)
    lines = [top_h, text(1, -5)] + [x for pair in hl for x in pair]
    top = _sec("Dementlieu", ["Chapter 3: Domains"], lines, 1, size=2.0)
    kids = [_sec(t, ["Chapter 3: Domains", "Dementlieu"], list(pair), 2) for (t, _), pair in zip(heads, hl)]
    out = placement_doubts([top] + kids, {})
    # W23: the parent printing a sub-heading twice is itself unverified, and every section under it inherits that
    assert "prints its sub-heading 'Noteworthy Features' more than once" in out[id(top)]
    assert "printed 2 times" in out[id(kids[1])] and "printed 2 times" in out[id(kids[2])]
    for k in (kids[0], kids[3]):
        assert "is doubted" in out[id(k)] and "'Dementlieu'" in out[id(k)]


def test_a_section_whose_page_opens_another_chapter_is_doubted():   # W22 (c)
    from bookextractor.parsers.charcommon import squash
    from bookextractor.parsers.world import placement_doubts
    a = _sec("Dragon Lairs", ["Chapter 3: Dragons in Play"], [_ln("Dragon Lairs", 65, 0)], 1)
    b = _sec("Lair Location", ["Chapter 4: Lairs and Hoards"], [_ln("Lair Location", 65, 0)], 1)
    c = _sec("Dragon Hoards", ["Chapter 3: Dragons in Play"], [_ln("Dragon Hoards", 60, 0)], 1)
    out = placement_doubts([a, b, c], {65: {squash("Chapter 4: Lairs and Hoards")}})
    assert "another chapter's title" in out[id(a)] and "'Chapter 3: Dragons in Play'" in out[id(a)]
    assert out[id(b)] == "" and out[id(c)] == ""


def test_a_child_set_like_its_parents_siblings_is_doubted():   # W23 (b): SCAG Clerics under Bards
    from bookextractor.parsers.world import placement_doubts
    text = lambda p: _ln("Bards travel the realms and sing of old deeds.", p, 50)  # noqa: E731
    barb = _sec("Barbarians", ["Chapter 5: Classes"], [_ln("Barbarians", 120, 0), text(120)], 1, size=1.54)
    bl = [_ln("Bards", 123, 0), text(123)]
    sub = [_ln("College of Lore", 124, 0), text(124)]
    cl = [_ln("Clerics", 126, 0), text(126)]
    bards = _sec("Bards", ["Chapter 5: Classes"], bl + sub + cl, 1, size=1.94)
    lore = _sec("College of Lore", ["Chapter 5: Classes", "Bards"], sub, 2, size=0.9)
    clerics = _sec("Clerics", ["Chapter 5: Classes", "Bards"], cl, 2, size=1.48)
    out = placement_doubts([barb, bards, lore, clerics], {})
    assert "set like the siblings" in out[id(clerics)] and "'Barbarians'" in out[id(clerics)] \
        and "'Bards'" in out[id(clerics)]
    assert out[id(lore)] == ""


def test_a_clipped_parent_heading_is_doubted():   # W23 (f)
    from bookextractor.parsers.world import clipped_heading, placement_doubts
    assert clipped_heading("Fizban's Treasury of") and clipped_heading("Welcome to") and clipped_heading("Church of the")
    assert not clipped_heading("Treasury of Dragons") and not clipped_heading("Chapter 2: Guilds of Ravnica")
    a = _sec("Seven Secrets of Ravenloft", ["Welcome to"], [_ln("Seven Secrets of Ravenloft", 7, 0)], 1)
    assert "clipped" in placement_doubts([a], {})[id(a)] and "'Welcome to'" in placement_doubts([a], {})[id(a)]


def test_a_close_end_run_stops_at_a_sibling_set_larger_than_its_sub_headings():   # W23 (d): MM Chuul
    from bookextractor.parsers.world import placement_doubts
    p = ["Monsters"]
    line = lambda t, pg: _ln(t, pg, 0)  # noqa: E731
    chuul_l, sub_l = line("Chuul", 41), line("Chuul Lair", 41)
    chuul = _sec("Chuul", p, [chuul_l, sub_l], 1, size=2.0, end="close: stops at 'COUATL'")
    sub = _sec("Chuul Lair", p + ["Chuul"], [sub_l], 2, size=1.0)
    small = _sec("Crawling Claw", p, [line("Crawling Claw", 42)], 1, size=1.05)
    big = _sec("Couatl", p, [line("Couatl", 43)], 1, size=1.67)
    after = _sec("Cyclops", p, [line("Cyclops", 44)], 1, size=1.05)
    out = placement_doubts([chuul, sub, small, big, after], {})
    assert "'Chuul'" in out[id(small)]
    assert out[id(big)] == "" and out[id(after)] == ""  # the run ends at Couatl, set larger than Chuul's sub-headings


def test_section_end_uses_the_widest_sub_heading_so_far():   # W23 (a)
    from bookextractor.parsers.sections import _ends
    # Menagerie Coast 1.51; Barren Shores 0.78; Brokenbank 1.14; Darktow 1.15; Marrow Valley 1.56
    ends = _ends([1.51, 0.78, 1.14, 1.15, 1.56])
    assert ends[0][0] == 4  # the coast runs to Marrow Valley, not to Darktow


def test_a_rule_whose_parent_is_doubted_fails_entry_level():   # W23 (e)
    from bookextractor.assemble import _rule_parent_doubt
    from bookextractor.parsers import Parsed
    sec = _sec("Q Yes B3", ["Chapter 5"], [_ln("Q Yes B3", 9, 0), _ln("A sentence of prose is here.", 9, 10)], 1)
    p = Parsed(type="Rule", name="Demon Summoning", fields={}, text="x", lines=[_ln("Demon Summoning", 9, 20)])
    why = _rule_parent_doubt(p, sec, False, {id(sec): "its page prints another chapter's title"})
    assert "'Q Yes B3'" in why and "is doubted" in why
    assert _rule_parent_doubt(p, sec, False, {}) == ""


def test_a_section_after_another_entitys_sibling_section_is_doubted():   # W23: EGW Commandments of Avandra
    from bookextractor.parsers.sections import line_key
    from bookextractor.parsers.world import placement_doubts
    p = ["Chapter 1: Story of Wildemount", "Pantheon of Exandria"]
    god = _sec("Avandra, the Change Bringer", p, [_ln("Avandra, the Change Bringer", 21, 0)], 2, size=0.64)
    cmd = _sec("Commandments of Avandra", p, [_ln("Commandments of Avandra", 22, 0)], 2, size=0.63)
    big = _sec("Betrayer Gods", p, [_ln("Betrayer Gods", 27, 0)], 2, size=0.88)
    out = placement_doubts([god, cmd], {}, {line_key(god.lines[0])})
    assert "'Avandra, the Change Bringer'" in out[id(cmd)] and "another entity's section" in out[id(cmd)]
    assert placement_doubts([god, big], {}, {line_key(god.lines[0])})[id(big)] == ""
    assert placement_doubts([god, cmd], {})[id(cmd)] == ""


def test_a_top_heading_printed_twice_in_its_chapter_is_doubted():   # W23: XGE "MONK"
    from bookextractor.parsers.world import placement_doubts
    p = ["Chapter 1: Character Options"]
    a = _sec("Monk", p, [_ln("Monk", 31, 0)], 1)
    b = _sec("Monk", p, [_ln("Monk", 68, 0)], 1)
    c = _sec("Origins", p, [_ln("Origins", 62, 0)], 1)
    out = placement_doubts([a, c, b], {})
    assert "printed 2 times at the top of its chapter" in out[id(b)] and out[id(c)] == ""


def test_a_section_naming_a_god_is_a_part_of_the_gods_text():   # W23: EGW Commandments of Melora
    from bookextractor.parsers.world import placement_doubts
    p = ["Chapter 1: Story of Wildemount", "Pantheon of Exandria"]
    cmd = _sec("Commandments of Melora", p, [_ln("Commandments of Melora", 25, 0)], 2)
    other = _sec("Prime Deities", p, [_ln("Prime Deities", 21, 0)], 2)
    out = placement_doubts([other, cmd], {}, set(), {"Melora", "Avandra, the Change Bringer"})
    assert "names the god 'Melora'" in out[id(cmd)] and out[id(other)] == ""


def test_a_sibling_ending_its_predecessor_barely_over_the_midpoint_is_doubted():   # W23: XGE Origins
    from bookextractor.parsers.world import placement_doubts
    p = ["Chapter 1: Character Options"]
    tl, sb = _ln("This Is Your Life", 62, 0), _ln("Section by Section", 62, 10)
    tiyl = _sec("This Is Your Life", p, [tl, sb], 1, size=1.04)
    sub = _sec("Section by Section", p + ["This Is Your Life"], [sb], 2, size=0.44)
    origins = _sec("Origins", p, [_ln("Origins", 62, 20)], 1, size=0.77)
    big = _sec("Wizard", p, [_ln("Wizard", 70, 0)], 1, size=1.4)
    out = placement_doubts([tiyl, sub, origins], {})
    assert "barely over the midpoint" in out[id(origins)] and "'This Is Your Life'" in out[id(origins)]
    assert placement_doubts([tiyl, sub, big], {})[id(big)] == ""


def test_a_close_end_run_stays_in_its_own_sibling_group():   # W24: MM's top sections never run into an appendix
    from bookextractor.parsers.world import placement_doubts
    line = lambda t, pg: _ln(t, pg, 0)  # noqa: E731
    chuul_l, sub_l = line("Chuul", 41), line("Chuul Lair", 41)
    chuul = _sec("Chuul", [], [chuul_l, sub_l], 1, size=2.0, end="close: stops at 'COUATL'")
    sub = _sec("Chuul Lair", ["Chuul"], [sub_l], 2, size=1.0)
    small = _sec("Crawling Claw", [], [line("Crawling Claw", 42)], 1, size=1.05)
    appendix = _sec("Blink Dog", ["Appendix A: Miscellaneous Creatures"], [line("Blink Dog", 340)], 1, size=1.05)
    out = placement_doubts([chuul, sub, small, appendix], {})
    assert "'Chuul'" in out[id(small)] and out[id(appendix)] == ""


def test_every_sibling_after_an_owned_section_is_doubted_to_the_first_larger_one():   # W24 (v): DMG Larva
    from bookextractor.parsers.sections import line_key
    from bookextractor.parsers.world import placement_doubts
    p = ["Chapter 2: Creating a Multiverse"]
    ys = iter(range(0, 1000, 20))
    mk = lambda t, pg, size: _sec(t, p, [_ln(t, pg, next(ys)), _ln("The plane is a place of endless war.", pg, next(ys))], 1, size=size)  # noqa: E731
    larva, gehenna, hells = mk("Larva", 63, 1.08), mk("Gehenna", 63, 1.12), mk("The Nine Hells", 64, 0.92)
    acheron, mechanus, other = mk("Acheron", 66, 0.94), mk("Mechanus", 66, 1.1), mk("Other Planes", 67, 1.5)
    after = mk("Material Plane", 68, 1.0)
    out = placement_doubts([larva, gehenna, hells, acheron, mechanus, other, after], {}, {line_key(larva.lines[0])})
    for s in (gehenna, acheron, mechanus):
        assert "'Larva'" in out[id(s)] and "another entity's section" in out[id(s)], s.title
    assert "'Larva'" in out[id(hells)] or "midpoint" in out[id(hells)]
    assert out[id(other)] == "" and out[id(after)] == ""  # the run ends at the first sibling set clearly larger


def test_a_taller_copy_of_a_heading_on_its_page_doubts_the_siblings_set_smaller():   # W24 (vi): SCC p5
    from bookextractor.parsers.world import _taller_copies, placement_doubts
    p = ["A Curriculum of Chaos"]
    foot = Line("WELCOME TO STRIXHAVEN", (0.0, 2430.0, 300.0, 2466.0), 36.2, 5)
    display = Line("Welcome to Strixhaven", (494.0, 1221.0, 1500.0, 1298.0), 77.5, 5)
    prose = Line("Strixhaven is a university of magic on the plane of Arcavios.", (0.0, 2470.0, 300.0, 2500.0), 30.0, 5)
    about = Line("About This Book", (199.0, 119.0, 600.0, 170.0), 51.7, 6)
    world = Line("StrixHaven's World", (213.0, 116.0, 700.0, 176.0), 60.7, 7)
    big = Line("Chapter Two Starts Here", (213.0, 116.0, 900.0, 200.0), 84.0, 8)
    welcome = _sec("Welcome to Strixhaven", p, [foot, prose], 1, size=0.49)
    a = _sec("About This Book", p, [about], 1, size=1.25)
    w = _sec("Strixhaven's World", p, [world], 1, size=1.5)
    b = _sec("Chapter Two Starts Here", p, [big], 1, size=2.0)
    body = [display, foot, prose, about, world, big]
    assert _taller_copies([welcome, a, w, b], body) == {id(welcome): 77.5}
    out = placement_doubts([welcome, a, w, b], {}, body=body)
    for s in (a, w):
        assert "taller copy (77.5)" in out[id(s)] and "'Welcome to Strixhaven'" in out[id(s)], s.title
    assert out[id(b)] == "" and out[id(welcome)] == ""
    assert placement_doubts([welcome, a, w, b], {})[id(a)] == ""  # without the page lines, no copy is known


def test_the_top_sections_after_a_misfiled_chapter_opener_are_doubted():   # W25: SCC "RUNNING THE ADVENTURES"
    from bookextractor.parsers.charcommon import squash
    from bookextractor.parsers.world import placement_doubts
    old, new = ["Chapter 2: Character Options"], ["Chapter 3: School Is in Session"]
    opener = _sec("Running the Adventures", old, [_ln("Running the Adventures", 42, 0)], 1, size=1.9)
    kids = [_sec(t, new, [_ln(t, 42 + k, 10)], 1, size=1.1) for k, t in
            enumerate(("A Strixhaven Campaign", "Character Creation", "Standalone Adventures"))]
    big = _sec("Extracurriculars", new, [_ln("Extracurriculars", 46, 0)], 1, size=1.8)
    later = _sec("Jobs", new, [_ln("Jobs", 47, 0)], 1, size=1.1)
    out = placement_doubts([opener] + kids + [big, later], {42: {squash("Chapter 3: School Is in Session")}})
    assert "another chapter's title" in out[id(opener)]
    for k in kids:
        assert "chapter opener" in out[id(k)] and "'Running the Adventures'" in out[id(k)], k.title
    assert out[id(big)] == "" and out[id(later)] == ""  # the run ends at the first top section as large


def test_a_section_holding_a_sibling_sized_part_is_doubted_but_its_other_parts_are_not():   # W25
    from bookextractor.parsers.world import held_siblings, placement_doubts
    p = ["Chapter 5: Classes"]
    bl, cl, lore = _ln("Bards", 123, 0), _ln("Clerics", 126, 0), _ln("College of Lore", 124, 0)
    text = _ln("Bards travel the realms and sing of old deeds.", 123, 10)
    barb = _sec("Barbarians", p, [_ln("Barbarians", 120, 0)], 1, size=1.54)
    fighters = _sec("Fighters", p, [_ln("Fighters", 130, 0)], 1, size=1.5)
    bards = _sec("Bards", p, [bl, text, lore, cl], 1, size=1.94)
    college = _sec("College of Lore", p + ["Bards"], [lore], 2, size=0.9)
    clerics = _sec("Clerics", p + ["Bards"], [cl], 2, size=1.48)
    secs = [barb, bards, college, clerics, fighters]
    assert [c.title for c in held_siblings(bards, secs)] == ["Clerics"]
    out = placement_doubts(secs, {})
    assert "holds 'Clerics'" in out[id(bards)] and "set like its parent's siblings" in out[id(bards)]
    assert out[id(college)] == ""  # the held-sibling doubt is the section's own, never inherited
    assert "set like the siblings" in out[id(clerics)]


def test_a_zero_size_section_counts_its_tall_peers_as_doubtful():   # Task 7 r1: ledger 67 (a)
    from bookextractor.parsers.world import _doubt
    from tests.test_world import PROSE, _synthetic
    spec = [("Religious Order Contact", 30.0, 1), (PROSE, 30.0, None), ("Contact Peer", 26.0, 2),
            (PROSE, 30.0, None), ("Contact Other", 26.0, 2), (PROSE, 30.0, None), (PROSE, 30.0, None)]
    sec, own, starts = _synthetic(spec)
    sec.size = 0.0
    doubts = _doubt(sec, own, 1, starts, {id(own[2]): 0.5, id(own[4]): 0.5})
    assert any("no measured size" in d for d in doubts), doubts


def test_run_style_tolerates_ocr_casing_noise_and_counts_only_peers_over_prose():   # Task 7 r1: ledger 67 (b), (c)
    from bookextractor.parsers.world import _doubt, _run_style, _style
    from tests.test_world import PROSE, _synthetic
    assert _run_style("The Cog CarnIval") == "title" and _run_style("RANgeR") == "caps"
    assert _style("The Sword Coast and the North") == "title"  # small words in lower case keep the style
    assert _run_style("Gnome Ideals") == "title" and _run_style("The Hidden Rune") == "title"
    # exactly three same-styled peers over prose fire the run
    spec = [("MONK", 40.0, 1), (PROSE, 30.0, None)]  # a caps heading: its title-case peers are not "styled like it"
    for name in ("The Cog CarnIval", "The Iron Market", "The Old Mill"):
        spec += [(name, 31.0, 2), (PROSE, 30.0, None), (PROSE, 30.0, None)]
    sec, own, starts = _synthetic(spec)
    doubts = _doubt(sec, own, 1, starts, {id(l): 0.6 for l in own if id(l) in starts and l is not own[0]})
    assert any("run of 3 peer sub-headings" in d for d in doubts), doubts
    # a peer over a table, not prose, does not count: two over prose are no run
    spec = [("MONK", 40.0, 1), (PROSE, 30.0, None), ("The Cog CarnIval", 31.0, 2), (PROSE, 30.0, None),
            ("The Iron Market", 31.0, 2), ("d6 Result", 30.0, None), ("1 A ring", 30.0, None), ("2 A coin", 30.0, None),
            ("3 A key", 30.0, None), ("The Old Mill", 31.0, 2), (PROSE, 30.0, None), (PROSE, 30.0, None)]
    sec, own, starts = _synthetic(spec)
    doubts = _doubt(sec, own, 1, starts, {id(l): 0.6 for l in own if id(l) in starts and l is not own[0]})
    assert not any("run of" in d for d in doubts), doubts
