import dataclasses
import json
from pathlib import Path

import jsonschema

from bookextractor.layout import reading_order
from bookextractor.model import load_ocr_page
from bookextractor.pages import classify_page
from bookextractor.parsers import ParseContext
from bookextractor.parsers.subclass import parse_subclasses, subclass_checks

FIX = Path(__file__).parent / "fixtures" / "ocr"
SCHEMAS = Path(__file__).parents[3] / "Schemas" / "canonical"
BOOKS = {
    "phb": ("phb14", "PHB", [50, 51]),
    "xge": ("xge", "XGE", [10, 11, 12, 13]),
    "dmg": ("dmg14", "DMG", [96, 97, 98]),
    "ftd": ("ftd", "FTD", [15, 16, 17, 18]),
    "bgg": ("bgg", "BGG", [11, 12]),
    "vrgr": ("vrgr", "VRGR", [29, 30, 31, 32]),
    "egw": ("egw", "EGW", [184, 185, 186, 187]),
    "scag": ("scag", "SCAG", [122, 123]),
    "scag-wizard": ("scag", "SCAG", [142, 143, 144, 146]),
    "tce-armorer": ("tce", "TCE", [15, 16, 17]),
    "phb-paladin": ("phb14", "PHB", [86, 87]),
}


def _squash(s):
    return "".join(c for c in s.lower() if c.isalpha())


def parse(book="phb", mutate_heading=None):
    slug, source, ns = BOOKS[book]
    pages = {n: load_ocr_page(FIX / book.split("-")[0] / f"{n:03d}.json", n) for n in ns}
    body = [l for n in sorted(pages) if classify_page(pages[n]) in ("rules", "art") for l in reading_order(pages[n])]
    if mutate_heading:
        body = [dataclasses.replace(l, text=mutate_heading) if _squash(l.text) == "primalpaths" else l for l in body]
    return parse_subclasses(body, ParseContext(slug=slug, source=source, pages=pages))


def get_sub(name, book="phb", mutate_heading=None):
    return next((p for p in parse(book, mutate_heading) if _squash(p.name) == _squash(name)), None)


def failed(p):
    return [c for c in p.checks if not c.passed]


def check(checks, name):
    return next(c for c in checks if c.name == name)


def schema(name):
    return json.loads((SCHEMAS / f"{name}.schema.json").read_text(encoding="utf-8"))


def test_zealot():
    p = get_sub("Path of the Zealot", book="xge")
    assert p.fields["className"] == "Barbarian" and p.fields["classSource"] == "PHB" and p.fields["shortName"] == "Zealot"
    assert sorted({int(r.split("|")[-1]) for r in p.fields["subclassFeatures"]}) == [3, 6, 10, 14]
    assert any(r.endswith("|Barbarian||Zealot|XGE|3") for r in p.fields["subclassFeatures"])
    assert not failed(p), failed(p)


def test_zealot_refs_follow_5etools():
    p = get_sub("Path of the Zealot", book="xge")
    assert p.fields["subclassFeatures"] == [
        "Path of the Zealot|Barbarian||Zealot|XGE|3", "Fanatical Focus|Barbarian||Zealot|XGE|6",
        "Zealous Presence|Barbarian||Zealot|XGE|10", "Rage beyond Death|Barbarian||Zealot|XGE|14"]
    names = [e["name"] for e in p.fields["entries"]]
    assert names[:2] == ["Divine Fury", "Warrior of the Gods"]


def test_xge_group_reads_every_subclass():
    assert [p.name for p in parse("xge")] == ["Path of the Ancestral Guardian", "Path of the Storm Herald",
                                              "Path of the Zealot"]


def test_berserker_phb():
    p = get_sub("Path of the Berserker")
    assert p.fields["className"] == "Barbarian"
    assert p.fields["subclassFeatures"] == [
        "Path of the Berserker|Barbarian||Berserker||3", "Mindless Rage|Barbarian||Berserker||6",
        "Intimidating Presence|Barbarian||Berserker||10", "Retaliation|Barbarian||Berserker||14"]
    assert not failed(p), failed(p)


def test_group_heading_casing_noise():   # Review Focus 5
    assert get_sub("Path of the Zealot", book="xge", mutate_heading="PRIMAL PATHs")


def test_feature_levels_mismatch():
    c = check(subclass_checks({"className": "Barbarian", "subclassFeatures": ["A|Barbarian||X|XGE|3", "B|Barbarian||X|XGE|5"]}), "feature-levels-match-class")
    assert not c.passed and "5" in c.reason


def test_feature_levels_backwards():   # two subclasses read as one: the levels restart
    refs = ["A|Fighter||X|XGE|3", "B|Fighter||X|XGE|7", "C|Fighter||X|XGE|15", "D|Fighter||X|XGE|3",
            "E|Fighter||X|XGE|10", "F|Fighter||X|XGE|18"]
    c = check(subclass_checks({"className": "Fighter", "subclassFeatures": refs}), "feature-levels-match-class")
    assert not c.passed and "D (level 3)" in c.reason and "C (level 15)" in c.reason


def test_parent_class_unknown():
    c = check(subclass_checks({"className": "Gunslinger", "subclassFeatures": []}), "parent-class-known")
    assert not c.passed and "Gunslinger" in c.reason


def test_subclass_schema():
    jsonschema.validate(get_sub("Path of the Zealot", book="xge").fields, schema("SubclassFields"))


# ---------- subclasses outside a class chapter: "<Class>: <Subclass>" labels and bare class headings ----------
def test_dmg_class_label_death_domain():   # "Cleric: Death Domain" under "Villainous Class Options"
    p = get_sub("Death Domain", book="dmg")
    assert p.fields["className"] == "Cleric" and p.fields["shortName"] == "Death"
    assert p.fields["subclassFeatures"] == [
        "Death Domain|Cleric||Death|DMG|1", "Channel Divinity: Touch of Death|Cleric||Death|DMG|2",
        "Inescapable Destruction|Cleric||Death|DMG|6", "Divine Strike|Cleric||Death|DMG|8",
        "Improved Reaper|Cleric||Death|DMG|17"]
    assert not failed(p), failed(p)


def test_dmg_class_label_oathbreaker():   # "PALADIN: OATHBREAKER"; Dread Lord's cue is "At 20th-level"
    p = get_sub("Oathbreaker", book="dmg")
    assert p.fields["className"] == "Paladin" and p.fields["shortName"] == "Oathbreaker"
    assert sorted({int(r.split("|")[-1]) for r in p.fields["subclassFeatures"]}) == [3, 7, 15, 20]
    assert not failed(p), failed(p)
    assert [q.name for q in parse("dmg")] == ["Death Domain", "Oathbreaker"]


def test_ftd_label_split_over_two_lines():   # "MONK:" / "Way of the Ascendant Dragon"; TCE-style cues
    p = get_sub("Way of the Ascendant Dragon", book="ftd")
    assert p.fields["className"] == "Monk" and p.fields["shortName"] == "Ascendant Dragon"
    assert sorted({int(r.split("|")[-1]) for r in p.fields["subclassFeatures"]}) == [3, 6, 11, 17]
    assert not failed(p), failed(p)
    d = get_sub("Drakewarden", book="ftd")
    assert d.fields["className"] == "Ranger" and d.fields["subclassFeatures"][0] == "Drakewarden|Ranger||Drakewarden|FTD|3"
    assert not failed(d), failed(d)


def test_bgg_label_same_height_as_body():
    p = get_sub("Path of the Giant", book="bgg")
    assert p.fields["className"] == "Barbarian" and p.fields["shortName"] == "Giant"
    assert sorted({int(r.split("|")[-1]) for r in p.fields["subclassFeatures"]}) == [3, 6, 10, 14]
    assert not failed(p), failed(p)


def test_vrgr_labels():
    assert [q.name for q in parse("vrgr")] == ["College of Spirits", "The Undead"]
    b, w = get_sub("College of Spirits", book="vrgr"), get_sub("The Undead", book="vrgr")
    assert b.fields["className"] == "Bard" and w.fields["className"] == "Warlock"
    assert w.fields["shortName"] == "Undead" and w.fields["subclassFeatures"][0] == "The Undead|Warlock||Undead|VRGR|1"
    assert not failed(b) and not failed(w), (failed(b), failed(w))


def test_egw_bare_class_heading():   # "FIGHTER" heading, then "Eco Knight" (OCR) named by "3rd-level Echo Knight feature"
    assert [q.name for q in parse("egw")] == ["Echo Knight", "Chronurgy Magic", "Graviturgy Magic"]
    e = get_sub("Echo Knight", book="egw")
    assert e.fields["className"] == "Fighter" and e.fields["shortName"] == "Echo Knight"
    assert sorted({int(r.split("|")[-1]) for r in e.fields["subclassFeatures"]}) == [3, 7, 10, 15, 18]
    g = get_sub("Graviturgy Magic", book="egw")
    assert g.fields["className"] == "Wizard" and g.fields["shortName"] == "Graviturgy"
    assert not failed(e) and not failed(g), (failed(e), failed(g))


def test_label_with_unknown_class_fails_parent_check():   # never guess the parent class
    pages = {n: load_ocr_page(FIX / "bgg" / f"{n:03d}.json", n) for n in (11, 12)}
    body = [l for n in sorted(pages) for l in reading_order(pages[n])]
    body = [dataclasses.replace(l, text="Gunslinger: Path of the Giant") if _squash(l.text) == "barbarianpathofthegiant"
            else l for l in body]
    p = next(q for q in parse_subclasses(body, ParseContext(slug="bgg", source="BGG", pages=pages))
             if _squash(q.name) == "pathofthegiant")
    assert "className" not in p.fields and not check(p.checks, "parent-class-known").passed
    jsonschema.validate(p.fields, schema("SubclassFields"))


def test_spell_range_label_is_not_a_ranger_subclass():   # TCE "Range: Self" fuzzily resembles "Ranger: ..."
    pages = {n: load_ocr_page(FIX / "dmg" / f"{n:03d}.json", n) for n in (96, 97)}
    body = [l for n in sorted(pages) for l in reading_order(pages[n])]
    body = [dataclasses.replace(l, text="Range: Self") if _squash(l.text) == "clericdeathdomain" else l for l in body]
    assert [q.name for q in parse_subclasses(body, ParseContext(slug="dmg14", source="DMG", pages=pages))] == \
        ["Oathbreaker"]


def test_egw_cue_name_correction_is_recorded():   # R11: a cue-based rename passes but is never silent
    e = get_sub("Echo Knight", book="egw")
    c = check(e.checks, "name-repaired")
    assert c.passed and "Eco Knight" in c.reason and "Echo Knight" in c.reason and "cues" in c.reason
    assert not any(c.name == "name-repaired" for c in get_sub("Chronurgy Magic", book="egw").checks)


def test_scag_battlerager_is_a_subclass():
    p = get_sub("Path of the Battlerager", book="scag")
    assert p.fields["className"] == "Barbarian" and p.fields["shortName"] == "Battlerager"
    assert sorted({int(r.split("|")[-1]) for r in p.fields["subclassFeatures"]}) == [3, 6, 10, 14]


def test_scag_totem_warrior_options_are_not_a_subclass():
    # "If you follow the Path of the Totem Warrior from the Player's Handbook, you have access to the options
    # presented here": new totem options for a PHB subclass, not a subclass SCAG prints
    assert get_sub("Path of the Totem Warrior", book="scag") is None
    assert [p.name for p in parse("scag")] == ["Path of the Battlerager"]


def _song_of_victory(unhead=False):
    slug, source, ns = BOOKS["scag-wizard"]
    pages = {n: load_ocr_page(FIX / "scag" / f"{n:03d}.json", n) for n in ns}
    body = [l for n in sorted(pages) if classify_page(pages[n]) in ("rules", "art") for l in reading_order(pages[n])]
    if unhead:  # the wrapped cantrips heading not read as a heading at all: the chapter title must still end it
        body = [dataclasses.replace(l, text=l.text.lower()) if _squash(l.text).startswith(("cantripsfor", "warlocksand"))
                else l for l in body]
    subs = parse_subclasses(body, ParseContext(slug=slug, source=source, pages=pages))
    p = next(p for p in subs if p.name == "Bladesinging")
    last = p.fields["entries"][-1]
    assert last["name"] == "Song of Victory"
    return " ".join(last["entries"])


def test_scag_bladesinging_ends_at_the_wrapped_cantrips_heading():
    # "Cantrips foR SorcereRs," / "WARLoCKS, AND WIZARDS": a heading wrapped after a comma
    text = _song_of_victory()
    assert text.endswith("while your Bladesong is active."), text[-300:]


def test_a_subclass_section_never_runs_past_a_chapter_title():
    text = _song_of_victory(unhead=True)
    assert "CHAPTER 5" not in text.upper() and "background feature" not in text, text[-300:]


def _last_feature(name, book):
    p = get_sub(name, book=book)
    last = p.fields["entries"][-1]
    return p, last["name"], " ".join(last["entries"])


def test_ftd_last_labelled_subclass_ends_at_the_section_heading():
    # "Perfected Bond" is followed by "Heroes of the Dragon", then the Draconic Feats: none of it is the feature's
    p, name, text = _last_feature("Drakewarden", "ftd")
    assert name == "Perfected Bond"
    assert "Heroes of the Dragon" not in text and "draconic flavor" not in text, text[-300:]
    assert "Heroes of the Dragon" not in p.text


def test_vrgr_last_labelled_subclass_ends_at_the_backgrounds_heading():
    p, name, text = _last_feature("The Undead", "vrgr")
    assert name == "Spirit Projection"
    assert text.endswith("until you finish a long rest."), text[-300:]
    assert "BACKGROUNDS" not in p.text and "haunted one" not in p.text


def test_bgg_last_labelled_subclass_ends_at_the_backgrounds_heading():
    p, name, text = _last_feature("Path of the Giant", "bgg")
    assert name == "Demiurgic Colossus"
    assert text.endswith("increases to 2d6."), text[-300:]
    assert "Giant Foundling" not in p.text and "foundling" not in text.lower()


def _synthetic(spec):
    """Lines on one page from (text, height) pairs, stacked top to bottom."""
    from bookextractor.model import Line
    out, y = [], 0.0
    for t, h in spec:
        out.append(Line(text=t, box=(100.0, y, 700.0, y + h), height=float(h), page=1))
        y += h + 4
    return out


_PROSE = ("Some text about this option and how it is used in play.", 30)


def _fighter_feature(name, cue):
    return [(name, 30), (f"{cue}, you gain a benefit that lasts until you finish a long rest.", 30), _PROSE]


def test_height_only_heading_whose_features_continue_the_levels_continues_the_subclass():
    # a taller heading with no naming evidence ("Terrain Notes", a stat block's name) inside a subclass whose
    # features carry on from the previous subclass's last level is not a subclass of its own
    spec = [("Martial Archetypes", 50), _PROSE, _PROSE, ("Champion", 40), _PROSE, _PROSE]
    spec += _fighter_feature("Improved Critical", "Beginning at 3rd level")
    spec += _fighter_feature("Remarkable Athlete", "Starting at 7th level")
    spec += _fighter_feature("Additional Fighting Style", "At 10th level")
    spec += [("Terrain Notes", 40), _PROSE, _PROSE]
    spec += _fighter_feature("Superior Critical", "Starting at 15th level")
    spec += _fighter_feature("Survivor", "At 18th level")
    subs = parse_subclasses(_synthetic(spec), ParseContext(slug="x", source="XGE", pages={}))
    assert [p.name for p in subs] == ["Champion"]
    assert sorted({int(r.split("|")[-1]) for r in subs[0].fields["subclassFeatures"]}) == [3, 7, 10, 15, 18]
    assert not failed(subs[0]), failed(subs[0])


def test_height_only_heading_whose_features_restart_starts_a_new_subclass():
    spec = [("Martial Archetypes", 50), _PROSE, _PROSE, ("Champion", 40), _PROSE, _PROSE]
    spec += _fighter_feature("Improved Critical", "Beginning at 3rd level")
    spec += _fighter_feature("Remarkable Athlete", "Starting at 7th level")
    spec += [("Battle Master", 40), _PROSE, _PROSE]
    spec += _fighter_feature("Combat Superiority", "At 3rd level")
    spec += _fighter_feature("Know Your Enemy", "Starting at 7th level")
    subs = parse_subclasses(_synthetic(spec), ParseContext(slug="x", source="XGE", pages={}))
    assert [p.name for p in subs] == ["Champion", "Battle Master"]
    # neither holds the other's features, and both fail on their missing levels instead of merging
    assert [r.split("|")[0] for r in subs[1].fields["subclassFeatures"]] == ["Battle Master", "Know Your Enemy"]
    assert all(not check(p.checks, "feature-levels-match-class").passed for p in subs)


def test_height_only_heading_overlapping_the_previous_levels_is_not_merged():
    # features at levels the previous subclass already reached (its cue lost on the first feature) are not
    # folded into it: the merge needs every level to come after the previous subclass's last
    spec = [("Martial Archetypes", 50), _PROSE, _PROSE, ("Champion", 40), _PROSE, _PROSE]
    spec += _fighter_feature("Improved Critical", "Beginning at 3rd level")
    spec += _fighter_feature("Remarkable Athlete", "Starting at 7th level")
    spec += _fighter_feature("Additional Fighting Style", "At 10th level")
    spec += [("Battle Master", 40), _PROSE, _PROSE, ("Combat Superiority", 30), _PROSE]  # cue lost
    spec += _fighter_feature("Know Your Enemy", "Starting at 7th level")
    subs = parse_subclasses(_synthetic(spec), ParseContext(slug="x", source="XGE", pages={}))
    assert [p.name for p in subs] == ["Champion", "Battle Master"]
    assert not any("Know Your Enemy" in r for r in subs[0].fields["subclassFeatures"])


def _all_text(p):
    return json.dumps(p.fields.get("entries")) + "\n" + p.text


def test_an_art_credit_ocr_merged_into_a_body_line_is_not_kept():   # TCE: "whenever BRIAN VALEZA" (sideways credit)
    p = get_sub("Armorer", book="tce-armorer")
    assert "VALEZA" not in _all_text(p), [e for e in p.fields["entries"] if "VALEZA" in json.dumps(e)]
    # the parser keeps the printed (OCR-glued) heading; the assembler repairs it (test_assemble)
    extra = next(e for e in p.fields["entries"] if e["name"] == "Extraattack")
    assert "whenever you take the Attack action" in json.dumps(extra), extra


def test_art_credit_rule_keeps_horizontal_all_caps_lines():
    from bookextractor.model import Fragment, Line
    from bookextractor.parsers.charcommon import without_art_credits
    head = Line("ARMORER SPELLS", (100, 10, 400, 40), 30, 1, [Fragment("ARMORER SPELLS", 1.0, (100, 10, 400, 40))])
    body = Line("whenever BRIAN VALEZA", (100, 50, 1534, 153), 30, 1,
                [Fragment("whenever", 1.0, (100, 50, 400, 80)), Fragment("BRIAN VALEZA", 1.0, (1516, 50, 1534, 153))])
    out = without_art_credits([head, body])
    assert [l.text for l in out] == ["ARMORER SPELLS", "whenever"]
    assert out[1].box == body.box   # printed-line identity (page, box) is kept for the text-bleed gate


def test_a_sidebar_inside_a_subclass_feature_is_not_kept():   # PHB "Breaking Your Oath" inside Channel Divinity
    p = get_sub("Oath of Devotion", book="phb-paladin")
    text = _all_text(p)
    assert "Breaking Your Oath" not in text and "Oathbreaker paladin option" not in text, text
    cd = json.dumps(next(e for e in p.fields["entries"] if e["name"] == "Channel Divinity"))
    # the feature's sentence the sidebar interrupted reads on: "It also can't take" / "reactions."
    assert "It also can't take reactions." in cd, cd
    assert not failed(p), failed(p)


def test_a_sidebar_is_dropped_but_a_table_in_the_feature_is_kept():   # VRGR College of Spirits
    p = get_sub("College of Spirits", book="vrgr")
    text = _all_text(p)
    assert "Storytellers, like bards" not in text   # the "Spirit Tales" sidebar
    assert "Tale of the Brute" in text and "Tale of the Mind-Bender" in text.replace("ofthe", "of the")


def test_scag_bladesinging_sidebars_are_not_kept():   # "Mage Sigils", "Bladesinger Styles"
    p = get_sub("Bladesinging", book="scag-wizard")
    text = _all_text(p)
    assert "Mage Sigils" not in text and "Bladesinger Styles".lower() not in text.lower(), text[-800:]
