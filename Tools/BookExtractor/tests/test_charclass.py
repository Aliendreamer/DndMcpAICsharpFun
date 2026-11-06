import json
from pathlib import Path

import jsonschema

from bookextractor.layout import reading_order
from bookextractor.model import load_ocr_page
from bookextractor.pages import classify_page
from bookextractor.parsers import ParseContext
from bookextractor.parsers.charclass import class_checks, parse_classes
from bookextractor.tables import Table

FIX = Path(__file__).parent / "fixtures" / "ocr"
SCHEMAS = Path(__file__).parents[3] / "Schemas" / "canonical"
BOOKS = {
    "phb": ("phb14", "PHB", {"Barbarian": [46, 47, 48, 49, 50], "Fighter": [71, 72, 73],
                             "Wizard": [113, 114, 115, 116]}),
    "tce": ("tce", "TCE", {"Artificer": [9, 10, 11, 12, 13, 14, 15]}),
}


def load(book, ns):
    return {n: load_ocr_page(FIX / book / f"{n:03d}.json", n) for n in ns}


def body_of(pages):
    return [l for n in sorted(pages) if classify_page(pages[n]) in ("rules", "art")
            for l in reading_order(pages[n])]


def ctx_for(book, pages):
    slug, source, _ = BOOKS[book]
    return ParseContext(slug=slug, source=source, pages=pages)


def get_class(name, book="phb"):
    pages = load(book, BOOKS[book][2][name])
    return next(p for p in parse_classes(body_of(pages), ctx_for(book, pages)) if p.name == name)


def failed(p):
    return [c for c in p.checks if not c.passed]


def check(checks, name):
    return next(c for c in checks if c.name == name)


def refs(p):
    return [r if isinstance(r, str) else r["classFeature"] for r in p.fields["classFeatures"]]


def schema(name):
    return json.loads((SCHEMAS / f"{name}.schema.json").read_text(encoding="utf-8"))


CTX = ParseContext(slug="phb14", source="PHB", pages={})


def barbarian_lines_without_table_page():
    """The Barbarian pages with the level table's lines removed from page 48 (and from the raw page)."""
    global CTX
    pages = load("phb", BOOKS["phb"][2]["Barbarian"])
    p48 = pages[48]
    p48.lines = [l for l in p48.lines if not (l.box[0] < 700 and l.box[1] < 1320)]
    CTX = ctx_for("phb", pages)
    return body_of(pages)


# --- check fixtures for the pure class_checks tests ---
def _table():
    rows = []
    for lvl in range(1, 21):
        bonus = f"+{2 + (lvl - 1) // 4}"
        feats = {1: "Rage", 2: "Danger Sense", 3: "Primal Path", 6: "Path feature", 10: "Path feature",
                 14: "Path feature"}.get(lvl, "")
        rows.append([f"{lvl}th", bonus, feats])
    return Table(headers=["Level", "Proficiency Bonus", "Features"], rows=rows, page=1, lines=[])


table_ok = _table()
headings_ok = ["Rage", "Danger Sense", "Primal Path"]


def fields_with(hd_faces=12):
    return {"hd": {"number": 1, "faces": hd_faces}, "classFeatures": [
        "Rage|Barbarian||1", "Danger Sense|Barbarian||2",
        {"classFeature": "Primal Path|Barbarian||3", "gainSubclassFeature": True},
        {"classFeature": "Path feature|Barbarian||6", "gainSubclassFeature": True},
        {"classFeature": "Path feature|Barbarian||10", "gainSubclassFeature": True},
        {"classFeature": "Path feature|Barbarian||14", "gainSubclassFeature": True}]}


def text_with(line):
    return f"BARBARIAN\nHit Dice: 1d10 per barbarian level\n{line}\n"


def test_barbarian():
    p = get_class("Barbarian")
    f = p.fields
    assert f["hd"] == {"number": 1, "faces": 12} and f["proficiency"] == ["str", "con"]
    assert f["subclassTitle"] == "Primal Path" and "Rage|Barbarian||1" in f["classFeatures"]
    assert {"classFeature": "Primal Path|Barbarian||3", "gainSubclassFeature": True} in f["classFeatures"]
    assert not failed(p), failed(p)
    assert [c.name for c in p.checks] == ["hit-die-consistent", "level-table-read", "proficiency-bonus-steps",
                                         "features-match-table"]


def test_barbarian_starting_proficiencies():
    sp = get_class("Barbarian").fields["startingProficiencies"]
    assert sp == {"armor": ["light", "medium", "shield"], "weapons": ["simple", "martial"],
                  "skills": [{"choose": {"from": ["animal handling", "athletics", "intimidation", "nature",
                                                  "perception", "survival"], "count": 2}}]}


def test_barbarian_features_by_level():
    p = get_class("Barbarian")
    assert "Brutal Critical (1 die)|Barbarian||9" in p.fields["classFeatures"]
    gains = [int(r["classFeature"].rsplit("|", 1)[1]) for r in p.fields["classFeatures"] if isinstance(r, dict)]
    assert gains == [3, 6, 10, 14]
    assert p.fields["multiclassing"] is None
    names = [e["name"] for e in p.fields["entries"]]
    assert "Reckless Attack" in names and "Primal Champion" in names


def test_barbarian_text_stops_at_subclass_group():
    p = get_class("Barbarian")
    assert "Mindless Rage" not in p.text and "FRENZY" not in p.text and "PrIMAl CHamPIon" in p.text


def test_fighter_features_by_level():
    assert "Action Surge|Fighter||2" in get_class("Fighter").fields["classFeatures"]


def test_fighter_garbled_ordinal_keeps_level():
    assert "Indomitable|Fighter||9" in get_class("Fighter").fields["classFeatures"]


def test_wizard_passes_every_check_but_the_printed_spelling_disagreement():
    assert [c.name for c in failed(get_class("Wizard"))] == ["features-match-table"]


def test_artificer_tce_source():
    assert any(r.startswith("Magical Tinkering|Artificer|TCE|1") for r in refs(get_class("Artificer", book="tce")))


def test_artificer_multiclassing_printed():
    p = get_class("Artificer", book="tce")
    assert p.fields["multiclassing"]["requirements"] == {"int": 13}
    assert p.fields["proficiency"] == ["con", "int"]
    assert not failed(p), failed(p)


def test_hit_die_disagreement():   # spec scenario: Hit Dice 1d10 but "12 + your Constitution modifier"
    c = check(class_checks(fields_with(hd_faces=10), table_ok, headings_ok,
                           text_with("Hit Points at 1st Level: 12 + your Constitution modifier")),
              "hit-die-consistent")
    assert not c.passed and "1d10" in c.reason and "12" in c.reason


def test_hit_die_agreement():
    c = check(class_checks(fields_with(hd_faces=12), table_ok, headings_ok,
                           text_with("Hit Points at 1st Level: 12 + your Constitution modifier")),
              "hit-die-consistent")
    assert c.passed, c.reason


def test_proficiency_bonus_step_wrong():
    t = _table()
    t.rows[4][1] = "+2"
    c = check(class_checks(fields_with(), t, headings_ok, text_with("")), "proficiency-bonus-steps")
    assert not c.passed and "5" in c.reason and "+3" in c.reason


def test_feature_heading_missing():
    c = check(class_checks(fields_with(), table_ok, ["Rage"], text_with("")), "features-match-table")
    assert not c.passed and "Danger Sense" in c.reason


def test_unreadable_table_still_produced():   # Review Focus 1
    p = parse_classes(barbarian_lines_without_table_page(), CTX)[0]
    assert p.name == "Barbarian" and not check(p.checks, "level-table-read").passed \
        and "Barbarian" in check(p.checks, "level-table-read").reason


def test_class_fields_validate_schema():
    jsonschema.validate(get_class("Barbarian").fields, schema("ClassFields"))
    jsonschema.validate(get_class("Artificer", book="tce").fields, schema("ClassFields"))


def test_level_cue_disagrees_with_table():
    f = fields_with()
    f["entries"] = [{"type": "entries", "name": "Danger Sense",
                     "entries": ["At 3rd level, you gain an uncanny sense of when things nearby aren't as they should be."]}]
    c = check(class_checks(f, table_ok, headings_ok, text_with("")), "features-match-table")
    assert not c.passed and "Danger Sense" in c.reason and "3rd" in c.reason and "[2]" in c.reason


def test_monk_with_dropped_level_cell():
    p = next(p for p in parse_classes(*_book("phb", [76, 77, 78, 79, 80])) if p.name == "Monk")
    assert "Deflect Missiles|Monk||3" in p.fields["classFeatures"] and "Perfect Self|Monk||20" in p.fields["classFeatures"]
    assert not failed(p), failed(p)


def _book(book, ns):
    pages = load(book, ns)
    return body_of(pages), ctx_for(book, pages)


def test_empty_first_level_fails():
    f = fields_with()
    f["classFeatures"] = f["classFeatures"][1:]
    c = check(class_checks(f, table_ok, headings_ok, text_with("")), "features-match-table")
    assert not c.passed and "level 1 lists no features" in c.reason


def test_table_cells_on_a_body_page_are_not_feature_headings():
    pages = load("phb", BOOKS["phb"][2]["Barbarian"])
    # the table page 48 is in the body, and its table is read AFTER the Hit Dice block (a table printed in the
    # column after "Class Features", as other books lay it out)
    body = []
    for n in sorted(pages):
        lines = reading_order(pages[n])
        if n == 48:
            table = [l for l in lines if l.box[0] < 700 and l.box[1] < 1320]
            lines = [l for l in lines if l not in table] + table
        body += lines
    p = next(p for p in parse_classes(body, ctx_for("phb", pages)) if p.name == "Barbarian")
    rage = next(e for e in p.fields["entries"] if e["name"] == "Rage")
    assert "In battle, you fight with primal ferocity" in " ".join(rage["entries"])
    sense = next(e for e in p.fields["entries"] if e["name"] == "Danger Sense")   # a standalone table cell line
    assert "uncanny sense" in " ".join(sense["entries"])
    assert not failed(p), failed(p)


def test_wizard_ref_uses_the_heading_spelling():
    # p114's table prints "Signature Spell"; the feature heading on p116 prints "Signature Spells"
    p = get_class("Wizard")
    assert "Signature Spells|Wizard||20" in p.fields["classFeatures"]
    assert "Signature Spell|Wizard||20" not in p.fields["classFeatures"]
    assert any(e["name"] == "Signature Spells" for e in p.fields["entries"])
    c = check(p.checks, "features-match-table")
    assert not c.passed and "'Signature Spell'" in c.reason and "'Signature Spells'" in c.reason


def test_table_and_heading_spelling_disagree_fails():
    c = check(class_checks(fields_with(), table_ok, ["Rage", "Danger Senses", "Primal Path"], text_with("")),
              "features-match-table")
    assert not c.passed and "'Danger Sense'" in c.reason and "'Danger Senses'" in c.reason


def test_heading_differs_only_by_case_or_note_passes():
    t = _table()
    t.rows[0][2] = "Rage (2/rest)"
    c = check(class_checks(fields_with(), t, ["RAGE", "DaNGER SENSE", "Primal Path"], text_with("")),
              "features-match-table")
    assert c.passed, c.reason


SIDEKICK_PAGES = [143, 144, 145, 146, 147, 148, 149]


def sidekicks():
    pages = load("tce", SIDEKICK_PAGES)
    return {p.name: p for p in parse_classes(body_of(pages), ctx_for("tce", pages))}


def test_tce_sidekick_classes_named_from_their_headings():
    assert sorted(sidekicks()) == ["Expert", "Spellcaster", "Warrior"]


def test_expert_sidekick_fields():
    p = sidekicks()["Expert"]
    f = p.fields
    assert f["hd"] is None and f["subclassTitle"] is None and f["proficiency"] is None
    assert refs(p)[:2] == ["Bonus Proficiencies|Expert|TCE|1", "Helpful|Expert|TCE|1"]
    assert "Inspiring Help (1d6)|Expert|TCE|11" in refs(p) and "Sharp Mind|Expert|TCE|18" in refs(p)
    assert not any(isinstance(r, dict) for r in f["classFeatures"])
    names = [e["name"] for e in f["entries"]]
    assert names[:3] == ["Bonus Proficiencies", "Helpful", "Cunning Action"] and "Sharp Mind" in names
    assert not failed(p), failed(p)
    jsonschema.validate(f, schema("ClassFields"))


def test_warrior_sidekick_first_grant_marker_dropped():
    r = refs(sidekicks()["Warrior"])
    assert "Second Wind|Warrior|TCE|2" in r and "Extra Attack|Warrior|TCE|6" in r and "Indomitable|Warrior|TCE|11" in r
    assert "Second Wind (2 uses)|Warrior|TCE|20" in r and "Extra Attack (2 extra)|Warrior|TCE|15" in r
    assert not failed(sidekicks()["Warrior"]), failed(sidekicks()["Warrior"])


def test_spellcaster_sidekick_passes_and_stops_before_the_next_section():
    p = sidekicks()["Spellcaster"]
    assert "Focused Casting|Spellcaster|TCE|20" in refs(p) and not failed(p), failed(p)
    w = sidekicks()["Warrior"]
    assert "Indomitable" in w.text and "PARLE" not in w.text.upper()


def test_feature_label_level_disagrees_with_table():
    f = fields_with()
    f["entries"] = [{"type": "entries", "name": "Danger Sense",
                     "entries": ["4th-level Barbarian feature", "You gain an uncanny sense."]}]
    c = check(class_checks(f, table_ok, headings_ok, text_with("")), "features-match-table")
    assert not c.passed and "Danger Sense" in c.reason and "[2]" in c.reason


def _sidekick_hit_die(text):
    return check(class_checks({"hd": None, "classFeatures": fields_with()["classFeatures"]}, table_ok, headings_ok,
                              text, name="Expert", sidekick=True), "hit-die-consistent")


def test_sidekick_printed_hit_dice_with_colon_unread_fails():
    c = _sidekick_hit_die("EXPERT\nHit Dice: 1d8 per expert level\n")
    assert not c.passed and "Hit Dice: 1d8" in c.reason


def test_sidekick_printed_hit_dice_without_colon_unread_fails():
    c = _sidekick_hit_die("EXPERT\nHitDice 1d8 per expert level\n")
    assert not c.passed and "1d8" in c.reason


def test_sidekick_without_hit_dice_passes():
    assert _sidekick_hit_die("EXPERT\nA sidekick gains the following class features.\n").passed


def test_sidekick_printed_hit_dice_garbled_token_fails():
    for token in ("ld", "Id"):
        c = _sidekick_hit_die(f"EXPERT\nHit Dice: {token} per expert level\n")
        assert not c.passed and f"Hit Dice: {token}" in c.reason


def test_sidekick_printed_hit_dice_space_no_colon_fails():
    c = _sidekick_hit_die("EXPERT\nHit Dice 1d8 per expert level\n")
    assert not c.passed and "Hit Dice 1d8" in c.reason


def test_sidekick_prose_about_hit_dice_passes():
    assert _sidekick_hit_die("EXPERT\nyou can spend Hit Dice to heal\n").passed
