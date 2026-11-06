import json
from dataclasses import replace
from functools import lru_cache
from pathlib import Path

import jsonschema

from bookextractor.layout import reading_order
from bookextractor.model import load_ocr_page
from bookextractor.pages import classify_page
from bookextractor.parsers import ParseContext
from bookextractor.parsers.trap import parse_traps, trap_dcs

FIX = Path(__file__).parent / "fixtures" / "ocr"
SCHEMAS = Path(__file__).parents[3] / "Schemas" / "canonical"
BOOKS = {
    # 105-106: Dungeon Hazards, then WILDERNESS; 109-111: Weather (after its table) and Wilderness Hazards, then
    # Foraging; 122-123: Sample Traps; 124-125: an art page and "Chapter 6: Between Adventures" after Sphere of
    # Annihilation
    "dmg": ("dmg14", "DMG", [105, 106, 109, 110, 111, 122, 123, 124, 125]),
    # 115-116: simple traps (a speech-bubble art text beside Net Trap); 119-122: complex traps ("Tempest Effects"
    # table inside Poisoned Tempest), then Designing Complex Traps
    "xge": ("xge", "XGE", [115, 116, 119, 120, 121, 122]),
    # 164-165: Eldritch Storms as lead-ins (an "ELDRITCH STORM" art caption); 170-172: Natural Hazards after a table,
    # "Spell Equivalents of Natural Hazards", then PUZZLES
    "tce": ("tce", "TCE", [164, 165, 170, 171, 172]),
    # 198-199: Sample Haunted Traps (an art caption "The Faceless malice / HAUNTED TRAP / CLAIMS A VICTIM.")
    "vrgr": ("vrgr", "VRGR", [198, 199]),
}


def _pages(book):
    _, _, ns = BOOKS[book]
    return {n: load_ocr_page(FIX / book / f"{n:03d}.json", n) for n in ns}


def _run(book, pages=None):
    slug, source, _ = BOOKS[book]
    pages = pages or _pages(book)
    body = [l for n in sorted(pages) if classify_page(pages[n]) in ("rules", "art") for l in reading_order(pages[n])]
    return parse_traps(body, ParseContext(slug=slug, source=source, pages=pages))


@lru_cache(maxsize=None)
def traps(book):
    return tuple(_run(book))


def get(ps, name):
    hit = [p for p in ps if p.name.lower() == name.lower()]
    assert hit, f"{name} not among {[p.name for p in ps]}"
    return hit[0]


def failed(p):
    return [c for c in p.checks if not c.passed]


def flat(e) -> str:
    if isinstance(e, str):
        return e
    if isinstance(e, list):
        return " ".join(flat(x) for x in e)
    if isinstance(e, dict):
        return " ".join(flat(v) for k, v in e.items() if k in ("name", "entry", "entries", "items"))
    return ""


# ---------- DMG sample traps ----------
def test_collapsing_roof_with_its_printed_dcs():
    p = get(traps("dmg"), "Collapsing Roof")
    assert p.type == "Trap"
    assert p.fields["trapHazType"] == "MECH"
    # "The DC to spot the trip wire is 10. A successful DC 15 Dexterity check using thieves' tools disables ..."
    assert p.fields["detectDc"] == 10
    assert p.fields["disarmDc"] == 15
    assert p.fields["entries"][0].startswith("This trap uses a trip wire to collapse the supports")
    assert p.fields["entries"][-1].endswith("rubble and becomes difficult terrain.")
    assert not failed(p)


def test_dc_printed_in_prose_is_parsed():
    # "The DC is 15 to spot the pressure plate" and "dispel magic (DC 13) cast on the statue destroys the trap"
    p = get(traps("dmg"), "Fire-Breathing Statue")
    assert p.fields["trapHazType"] == "MAG"
    assert (p.fields["detectDc"], p.fields["disarmDc"]) == (15, 13)
    # "A successful DC 20 Intelligence (Investigation) check allows a character to deduce the trap's presence"
    n = get(traps("dmg"), "Poison Needle")
    assert (n.fields["detectDc"], n.fields["disarmDc"]) == (20, 15)


def test_trap_without_printed_disarm_dc_passes_with_the_field_absent():
    p = get(traps("dmg"), "Rolling Sphere")  # spotted at DC 15; slowed, never disarmed, by a check
    assert p.fields["detectDc"] == 15
    assert "disarmDc" not in p.fields
    assert not failed(p)
    h = get(traps("dmg"), "Brown Mold")  # a hazard prints no DC to find or remove it
    assert "detectDc" not in h.fields and "disarmDc" not in h.fields
    assert not failed(h)


def test_parts_with_different_dcs_leave_the_field_absent_and_say_so():
    p = get(traps("dmg"), "Pits")  # simple pit spotted at DC 10, hidden pit at DC 15
    assert "detectDc" not in p.fields
    c = [c for c in p.checks if c.name == "trap-dcs-parsed"][0]
    assert c.passed and "10, 15" in c.reason


def test_unreadable_dc_fails_the_check():
    fields, check = trap_dcs("A successful DC lX Wisdom (Perception) check reveals the trap.")
    assert "detectDc" not in fields
    assert not check.passed and "lX" in check.reason
    fields, check = trap_dcs("A successful DC lS Wisdom (Perception) check reveals the trap.")  # OCR "15"
    assert fields == {"detectDc": 15} and check.passed


def test_saving_throw_dc_is_not_a_detect_or_disarm_dc():
    fields, check = trap_dcs("Each creature must succeed on a DC 13 Dexterity saving throw or be revealed.")
    assert fields == {} and check.passed


def test_sample_traps_end_at_the_next_heading_and_the_chapter():
    ps = traps("dmg")
    names = [p.name for p in ps if p.fields.get("trapHazType") in ("MECH", "MAG")]
    assert names == ["Collapsing Roof", "Falling Net", "Fire-Breathing Statue", "Pits", "Poison Darts",
                     "Poison Needle", "Rolling Sphere", "Sphere of Annihilation"]
    s = get(ps, "Sphere of Annihilation")
    assert s.text.endswith("A successful dispel magic (DC 18) removes this enchantment.")
    assert "Chapter 6" not in s.text and "CAMPAIGN" not in s.text
    f = get(ps, "Falling Net")
    assert "CHAPTER 5" not in f.text  # the running foot between the columns


# ---------- DMG hazards ----------
def test_dmg_hazards_by_section():
    ps = traps("dmg")
    kinds = {p.name: p.fields.get("trapHazType") for p in ps if p.fields.get("trapHazType") not in ("MECH", "MAG")}
    assert kinds == {
        "Brown Mold": None, "Green Slime": None, "Webs": None, "Yellow Mold": None,  # Dungeon Hazards: no kind
        "Extreme Cold": "WTH", "Extreme Heat": "WTH", "Strong Wind": "WTH", "Heavy Precipitation": "WTH",
        "Desecrated Ground": "WLD", "Frigid Water": "WLD", "Quicksand": "WLD", "Razorvine": "WLD",
        "Slippery Ice": "WLD", "Thin Ice": "WLD",
    }
    assert "trapHazType" not in get(ps, "Webs").fields


def test_hazard_text_ends_at_the_next_section():
    ps = traps("dmg")
    thin = get(ps, "Thin Ice")
    assert flat(thin.fields["entries"]).endswith("All creatures on broken ice fall through.")
    assert "Foraging" not in thin.text and "FORAGING" not in thin.text
    precip = get(ps, "Heavy Precipitation")  # HIGH ALTITUDE is a section of its own, not a weather hazard
    assert "altitude" not in precip.text.lower()
    assert all(p.name != "High Altitude" for p in ps)
    mold = get(ps, "Yellow Mold")
    assert flat(mold.fields["entries"]).endswith("instantly destroys one patch of yellow mold.")


# ---------- XGE simple and complex traps ----------
def test_simple_trap_fields():
    p = get(traps("xge"), "Bear Trap")
    assert p.fields["trapHazType"] == "SMPL"
    assert p.fields["rating"] == [{"tier": 1, "threat": "dangerous"}]
    assert p.fields["entries"] == ["A bear trap resembles a set of iron jaws that springs shut when stepped on, "
                                   "clamping down on a creature's leg. The trap is spiked in the ground, leaving the "
                                   "victim immobilized."]
    assert p.fields["trigger"] == ["A creature that steps on the bear trap triggers it."]
    assert p.fields["effect"][0].startswith("The trap makes an attack against the triggering creature.")
    assert p.fields["countermeasures"] == ["A successful DC 10 Wisdom (Perception) check reveals the trap. A "
                                           "successful DC 10 Dexterity check using thieves' tools disables it."]
    assert (p.fields["detectDc"], p.fields["disarmDc"]) == (10, 10)
    assert not failed(p)


def test_simple_traps_levels_and_ends():
    ps = traps("xge")
    simple = {p.name: p.fields["rating"][0]["tier"] for p in ps if p.fields["trapHazType"] == "SMPL"}
    assert simple == {"Bear Trap": 1, "Crossbow Trap": 1, "Falling Portcullis": 1, "Fiery Blast": 2, "Net Trap": 1,
                      "Pit Trap": 1, "Poison Needle": 1, "Scything Blade": 2, "Sleep of Ages": 3}  # "level 1116"
    net = get(ps, "Net Trap")
    assert "hilarious" not in net.text  # the speech-bubble art text beside it
    sleep = get(ps, "Sleep of Ages")
    assert sleep.text.endswith("destroys the trap.")
    # an Arcana check disables it; the spell's DC 19 counts only when no check is printed
    assert sleep.fields["disarmDc"] == 20


def test_complex_trap_elements():
    ps = traps("xge")
    p = get(ps, "Path of Blades")
    assert p.fields["trapHazType"] == "CMPX"
    assert p.fields["rating"] == [{"tier": 1, "threat": "dangerous"}]
    assert p.fields["initiative"] == 3  # "acts on initiative count 20 and initiative count 10"
    active = p.fields["eActive"]
    assert active[0].startswith("The Path of Blades includes a set of whirling blades")
    assert [i["name"] for i in active[1]["items"]] == ["Whirling Blades (Initiative 20)",
                                                       "Crushing Pillars (Initiative 10)",
                                                       "Rune of Fear (Initiative 10)"]
    assert [i["name"] for i in p.fields["eDynamic"][1]["items"]] == ["Blades Accelerate", "Rune's Defense"]
    assert [i["name"] for i in p.fields["eConstant"][1]["items"]] == ["Whirling Blades", "Rune of Fear"]
    assert [i["name"] for i in p.fields["countermeasures"][1]["items"]] == [
        "Whirling Blades", "Intelligence (Investigation), DC 15", "Attack",
        "Dexterity check using thieves' tools, DC 15", "Crushing Pillars", "Rune of Fear"]
    doom = get(ps, "Sphere of Crushing Doom")
    assert (doom.fields["initiative"], doom.fields["initiativeNote"]) == (1, "but see the dynamic element below")
    assert doom.text.endswith("but the characters can escape.")
    tempest = get(ps, "Poisoned Tempest")  # its "Tempest Effects" table caption is not a heading
    assert tempest.fields["rating"] == [{"tier": 3, "threat": "deadly"}]
    assert "Disable the Statues" in [i["name"] for i in tempest.fields["countermeasures"][1]["items"]]
    assert "Designing" not in tempest.text and "DesigNInG" not in tempest.text


# ---------- TCE and VRGR ----------
def test_eldritch_storms_from_lead_ins():
    ps = traps("tce")
    storms = [p.name for p in ps if p.fields.get("trapHazType") == "EST"]
    assert storms == ["Flaywind", "Flame Storm", "Necrotic Tempest", "Thrym's Howl"]
    howl = get(ps, "Thrym's Howl")  # continues after the "ELDRITCH STORM" caption on the next page
    assert flat(howl.fields["entries"]).endswith("Thrym's howl typically lasts 2d10 hours.")
    assert "ELD" not in howl.text and "Emotional" not in howl.text
    assert get(ps, "Flaywind").fields["detectDc"] == 15  # "DC 15 ... check allows a character to recognize"


def test_natural_hazards_after_a_table():
    ps = traps("tce")
    wld = [p.name for p in ps if p.fields.get("trapHazType") == "WLD"]
    assert wld == ["Avalanches", "Falling Into Water", "Falling Onto a Creature"]
    fall = get(ps, "Falling Onto a Creature")
    assert "Spell Equivalents" not in fall.text and "Puzzle" not in fall.text


def test_haunted_traps():
    ps = traps("vrgr")
    assert {p.name: p.fields["hauntBonus"] for p in ps} == {
        "Danse Macabre": "+4", "Faceless Malice": "+2", "Icon of the Lower Aerial Kingdoms": "+6",
        "Morbid Memory": "+0"}
    assert all(p.fields["trapHazType"] == "HAUNT" for p in ps)
    danse = get(ps, "Danse Macabre")
    assert "Faceless malice" not in danse.text and "VICTIM" not in danse.text.upper()
    assert "resist the trap or to stop dancing" in flat(danse.fields["entries"])
    assert "SURVIVORS" not in get(ps, "Morbid Memory").text.upper()


def test_unreadable_level_fails_rating_check():
    pages = _pages("xge")
    pg = pages[115]
    hit = [l for l in pg.lines if l.text == "Simple trap (level 1-4, dangerous threat)"]
    assert hit
    i = pg.lines.index(hit[0])
    pg.lines[i] = replace(hit[0], text="Simple trap (level 1-7, dangerous threat)",
                          frags=[replace(hit[0].frags[0], text="Simple trap (level 1-7, dangerous threat)")])
    p = get(_run("xge", pages), "Bear Trap")
    assert "rating" not in p.fields
    assert [c.name for c in failed(p)] == ["trap-rating-parsed"]


def test_schema():
    schema = json.loads((SCHEMAS / "TrapFields.schema.json").read_text())
    for book in BOOKS:
        for p in traps(book):
            jsonschema.validate(p.fields, schema)


# ---------- fix round 1 (P11) ----------
def _synthetic(rows):
    """Body lines from (text, width per character in line heights or None for prose) rows, one column."""
    from bookextractor.model import Fragment, Line, Page
    h, y, lines = 30.0, 100.0, []
    for text, w in rows:
        x1 = 100 + len(text) * (w if w else 0.4) * h
        box = (100.0, y, x1, y + h)
        lines.append(Line(text=text, box=box, height=h, page=1, frags=[Fragment(text, 1.0, box)]))
        y += 40 if w is None else 70
    page = Page(number=1, width=1500, height=2100, lines=lines, source="ocr")
    return parse_traps(lines, ParseContext(slug="x", source="X", pages={1: page}))


_PROSE = "The ground here is soft and creatures sink into it slowly."


def test_section_stops_at_a_heading_narrower_than_any_absolute_threshold():
    ps = _synthetic([("Wilderness Hazards", 0.75), (_PROSE, None), ("Mud Pit", 0.62), (_PROSE, None),
                     ("Old Roads", 0.69), (_PROSE, None), (_PROSE, None)])
    assert [p.name for p in ps] == ["Mud Pit"]
    assert ps[0].fields["trapHazType"] == "WLD"
    assert not failed(ps[0])
    assert "Old Roads" not in ps[0].text


def test_thin_margin_fails_hazard_section_bounded():
    ps = _synthetic([("Wilderness Hazards", 0.75), (_PROSE, None), ("Mud Pit", 0.62), (_PROSE, None),
                     ("Sinking Sand", 0.66), (_PROSE, None), ("Old Roads", 0.69), (_PROSE, None)])
    assert [p.name for p in ps] == ["Mud Pit", "Sinking Sand"]
    for p in ps:
        c = [c for c in failed(p) if c.name == "hazard-section-bounded"]
        assert c and "Old Roads" in c[0].reason and "0.690" in c[0].reason and "0.660" in c[0].reason


def test_section_running_to_the_end_of_the_text_fails_hazard_section_bounded():
    ps = _synthetic([("Wilderness Hazards", 0.75), (_PROSE, None), ("Mud Pit", 0.62), (_PROSE, None)])
    assert [p.name for p in ps] == ["Mud Pit"]
    assert [c.name for c in failed(ps[0])] == ["hazard-section-bounded"]


def test_dmg_and_tce_hazard_sections_are_bounded():
    for book in ("dmg", "tce"):
        for p in traps(book):
            assert not [c for c in failed(p) if c.name == "hazard-section-bounded"], p.name


def test_unread_initiative_fails_its_check():
    pages = _pages("xge")
    pg = pages[119]
    for old, new in (("Initiative. The trap acts on initiative count 20 and ini-", "Initiative. The trap acts "
                      "whenever it likes and"), ("tiative count 10.", "never sleeps.")):
        hit = [l for l in pg.lines if l.text == old]
        assert hit, old
        i = pg.lines.index(hit[0])
        pg.lines[i] = replace(hit[0], text=new, frags=[replace(hit[0].frags[0], text=new)])
    p = get(_run("xge", pages), "Path of Blades")
    assert "initiative" not in p.fields
    c = [c for c in failed(p) if c.name == "trap-initiative-parsed"]
    assert c and "whenever it likes" in c[0].reason
    assert [c for c in get(traps("xge"), "Path of Blades").checks if c.name == "trap-initiative-parsed"][0].passed


def test_sentence_both_detecting_and_disarming_records_neither():
    fields, check = trap_dcs("A DC 15 Investigation check reveals how the trap can be disabled.")
    assert fields == {}
    assert not check.passed and "reveals how the trap can be disabled" in check.reason
