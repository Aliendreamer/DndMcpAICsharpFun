import json
from dataclasses import replace
from functools import lru_cache
from pathlib import Path

import jsonschema

from bookextractor.layout import reading_order
from bookextractor.model import load_ocr_page
from bookextractor.pages import classify_page
from bookextractor.parsers import ParseContext
from bookextractor.parsers.hazard_rules import CONDITIONS, parse_conditions, parse_diseases_poisons

FIX = Path(__file__).parent / "fixtures" / "ocr"
SCHEMAS = Path(__file__).parents[3] / "Schemas" / "canonical"
BOOKS = {
    # 256-257: Sample Diseases (Cackle Fever, Sewer Plague, Sight Rot); 257: the Poisons table and Sample Poisons;
    # 258: the descriptions to Wyvern Poison, then Purchasing Poison and MADNESS; 259: Going Mad
    "dmg": ("dmg14", "DMG", [256, 257, 258, 259]),
    # 144: "Chapter 5: Equipment"; 151: the Adventuring Gear table ("Poison, basic (vial) | 100 gp", weight dash
    # dropped); 154: "Poison, Basic." description; 228: Contagion's diseases as lead-ins, then Contingency;
    # 291-293: Appendix A (art captions "BLINDED", "CHARMED" ... set beside the pictures); 294: Appendix B
    "phb": ("phb14", "PHB", [144, 151, 152, 153, 154, 228, 291, 292, 293, 294]),
    # 154-155: "The Gnawing Plague" with Transmission / Symptoms / Immunity lead-ins, then Cycle of the Plague
    "vrgr": ("vrgr", "VRGR", [154, 155]),
    # 126: "Frigid Woe." lead-in under Disease Laboratory, then AEORIAN HUNTERS
    "egw": ("egw", "EGW", [126]),
}


def _pages(book):
    _, _, ns = BOOKS[book]
    return {n: load_ocr_page(FIX / book / f"{n:03d}.json", n) for n in ns}


def _ctx(book, pages):
    slug, source, _ = BOOKS[book]
    body = [l for n in sorted(pages) if classify_page(pages[n]) in ("rules", "art") for l in reading_order(pages[n])]
    return body, ParseContext(slug=slug, source=source, pages=pages)


@lru_cache(maxsize=None)
def dps(book):
    return tuple(parse_diseases_poisons(*_ctx(book, _pages(book))))


@lru_cache(maxsize=None)
def conditions():
    return tuple(parse_conditions(*_ctx("phb", _pages("phb"))))


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
        return " ".join(flat(v) for k, v in e.items() if k in ("name", "entry", "entries", "items", "rows"))
    return ""


# ---------- poisons ----------
def test_burnt_othur_fumes():
    p = get(dps("dmg"), "Burnt Othur Fumes")
    assert p.type == "DiseasePoison"
    assert p.fields["poisonTypes"] == ["inhaled"]
    assert p.fields["value"] == 50000
    assert p.fields["entries"] == [
        "A creature subjected to this poison must succeed on a DC 13 Constitution saving throw or take 10 (3d6) "
        "poison damage, and must repeat the saving throw at the start of each of its turns. On each successive "
        "failed save, the character takes 3 (1d6) poison damage. After three successful saves, the poison ends."]
    assert not failed(p)


def test_all_dmg_poisons_with_types_prices_and_ends():
    ps = [p for p in dps("dmg") if "poisonTypes" in p.fields]
    assert len(ps) == 14
    assert get(ps, "Midnight Tears").fields["value"] == 150000  # "1,500 gp"
    assert get(ps, "Assassin's Blood").fields["value"] == 15000  # "150 gP"
    # the last description ends before "Purchasing Poison"; one over a running foot reads through it
    assert flat(get(ps, "Wyvern Poison").fields["entries"]).endswith("or half as much damage on a successful one.")
    assert flat(get(ps, "Purple Worm Poison").fields["entries"]).endswith("on a successful one.")
    assert "CHAPTER" not in get(ps, "Purple Worm Poison").text
    assert all(not failed(p) for p in ps), [(p.name, failed(p)) for p in ps if failed(p)]


def _burnt_row(old, new):
    """The DMG pages with the Burnt othur fumes row's `old` cell printed as `new`."""
    pages = _pages("dmg")
    p = pages[257]
    label = next(l for l in p.lines if l.text == "Burnt othur fumes")
    row = lambda l: abs((l.box[1] + l.box[3]) / 2 - (label.box[1] + label.box[3]) / 2) < 0.5 * label.height
    pages[257] = replace(p, lines=[replace(l, text=new) if l.text == old and row(l) else l for l in p.lines])
    return pages


def _poison_page(cell):
    return _burnt_row("500 gp", cell)


def test_poison_price_printed_as_dash_passes():
    p = get(parse_diseases_poisons(*_ctx("dmg", _poison_page("—"))), "Burnt Othur Fumes")
    assert "value" not in p.fields
    assert not failed(p)


def test_unreadable_poison_price_fails():
    p = get(parse_diseases_poisons(*_ctx("dmg", _poison_page("5O0 gq"))), "Burnt Othur Fumes")
    assert "value" not in p.fields
    assert [c.name for c in failed(p)] == ["cost-valid"]
    assert "Burnt othur fumes" in failed(p)[0].reason


def test_unknown_poison_type_fails():
    got = get(parse_diseases_poisons(*_ctx("dmg", _burnt_row("Inhaled", "Inhal3d"))), "Burnt Othur Fumes")
    assert "poisonTypes" not in got.fields
    assert any(c.name == "poison-type-known" and "Inhal3d" in c.reason for c in failed(got))


def test_phb_basic_poison_from_the_gear_table():
    p = get(dps("phb"), "Poison, basic (vial)")
    assert p.fields["value"] == 10000
    assert "poisonTypes" not in p.fields  # the PHB prints no type
    assert p.fields["entries"][0].startswith("You can use the poison in this vial")
    assert p.fields["entries"][-1].endswith("retains potency for 1 minute before drying.")
    assert not failed(p)


# ---------- diseases ----------
def test_dmg_diseases_and_their_ends():
    ds = [p for p in dps("dmg") if "poisonTypes" not in p.fields]
    assert sorted(p.name for p in ds) == ["Cackle Fever", "Sewer Plague", "Sight Rot"]
    sight = get(ds, "Sight Rot")
    assert flat(sight.fields["entries"]).endswith("the ointment cures the disease entirely.")
    assert "POISONS" not in sight.text and "Given their insidious" not in sight.text
    assert get(ds, "Cackle Fever").fields["entries"][0].startswith("This disease targets humanoids")
    assert all(not failed(p) for p in ds)


def test_phb_contagion_diseases_stop_at_the_next_spell():
    ds = [p for p in dps("phb") if p.name != "Poison, basic (vial)"]
    assert [p.name for p in ds] == ["Blinding Sickness", "Filth Fever", "Flesh Rot", "Mindfire", "Seizure",
                                    "Slimy Doom"]
    slimy = get(ds, "Slimy Doom")
    assert slimy.fields["entries"] == [
        "The creature begins to bleed uncontrollably. The creature has disadvantage on Constitution checks and "
        "Constitution saving throws. In addition, whenever the creature takes damage, it is stunned until the end "
        "of its next turn."]
    assert "Contingency" not in slimy.text


def test_vrgr_gnawing_plague_heading_with_its_lead_ins():
    ds = dps("vrgr")
    assert [p.name for p in ds] == ["The Gnawing Plague"]
    text = flat(ds[0].fields["entries"])
    assert text.startswith("The Gnawing Plague, also known as")
    assert "Transmission. The Gnaws is spread" in text
    assert text.endswith("mune to the Gnawing Plague.") or text.endswith("immune to the Gnawing Plague.")
    assert "At any given time" not in text  # "Cycle of the Plague" is the next section


def test_egw_frigid_woe_lead_in():
    ds = dps("egw")
    assert [p.name for p in ds] == ["Frigid Woe"]
    assert flat(ds[0].fields["entries"]).endswith("ending all symptoms and effects of the disease instantly.")
    assert "AEORIAN" not in ds[0].text.upper().replace("AEOR'S", "")


# ---------- conditions ----------
def test_all_15_conditions_pass_condition_known():
    cs = conditions()
    assert sorted(p.name.lower() for p in cs) == sorted(CONDITIONS)
    for p in cs:
        assert p.type == "Condition"
        assert [c for c in p.checks if c.name == "condition-known" and c.passed], p.name


def test_unconscious_ends_at_appendix_b():
    p = get(conditions(), "Unconscious")
    text = flat(p.fields["entries"])
    assert text.endswith("within 5 feet of the creature.")
    assert "Gods" not in p.text and "GodS" not in p.text and "RELIGION" not in p.text.upper()
    assert "UNCOnSCIOUS" not in p.text and "PRONE" not in p.text  # art captions


def test_art_captions_are_not_text():
    charmed = get(conditions(), "Charmed")
    assert "BLINDED" not in charmed.text
    assert flat(charmed.fields["entries"]).endswith("to interact socially with the creature.")
    assert "PETRIFIED" not in get(conditions(), "Incapacitated").text


def test_condition_bullets_and_exhaustion_table():
    blinded = get(conditions(), "Blinded")
    assert blinded.fields["entries"][0]["type"] == "list"
    assert len(blinded.fields["entries"][0]["items"]) == 2
    ex = get(conditions(), "Exhaustion")
    tab = [e for e in ex.fields["entries"] if isinstance(e, dict) and e.get("type") == "table"]
    assert len(tab) == 1 and len(tab[0]["rows"]) == 6
    assert tab[0]["rows"][-1] == ["6", "Death"]
    # the OCR lost the first row's "1": the row is kept and the check names it
    assert tab[0]["rows"][0] == ["", "Disadvantage on ability checks"]
    assert [c.name for c in failed(ex)] == ["table-rows-read"]


def test_unknown_condition_heading_fails():
    pages = _pages("phb")
    p = pages[293]
    pages[293] = replace(p, lines=[replace(l, text="STUNNEX") if l.text == "STUNNED" and l.box[0] < 200 else l
                                   for l in p.lines])
    got = get(parse_conditions(*_ctx("phb", pages)), "Stunnex")
    assert any(c.name == "condition-known" and "Stunnex" in c.reason for c in failed(got))


def test_schemas():
    dp = json.loads((SCHEMAS / "DiseasePoisonFields.schema.json").read_text())
    co = json.loads((SCHEMAS / "ConditionFields.schema.json").read_text())
    for book in BOOKS:
        for p in dps(book):
            jsonschema.validate(p.fields, dp)
    for p in conditions():
        jsonschema.validate(p.fields, co)
