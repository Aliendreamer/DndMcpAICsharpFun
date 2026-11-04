from collections import Counter

from bookextractor.assemble import _id_and_checks
from bookextractor.checks import Check
from bookextractor.names import build_vocabulary, own_heading_counts, repair_name
from bookextractor.parsers import Parsed

BODY = [
    "you identify the item and identify its properties",
    "an earthquake shakes the ground and an earthquake follows",
    "the creature returns by resurrection and resurrection is rare",
    "the animal friendship spell charms an animal friendship target",
    "conjure woodland beings summons woodland creatures and woodland beings fight",
    "a spell scroll holds a spell and a scroll can be read",
    "oil of sharpness coats a blade with oil and the sharpness of the blade grows",
    "manual of bodily health and a manual of gainful exercise teaches gainful exercise",
    "detection magic and magic detection are not the same; magic is strong",
    "a fireball and a fireball spreads fire; the goblin and the goblin boss attack",
    "wand of wonder; ring of x-ray vision; the ray of x-ray sight",
    "druidcraft druidcraft druid craft rules",
    "of the and",
]
VOCAB = build_vocabulary(BODY * 3)


def fix(name):
    return repair_name(name, VOCAB, own_heading_counts(name))


def test_examples_repaired():
    cases = {
        "Identi Fy": "Identify", "Earthqua Ke": "Earthquake", "Resurr Ection": "Resurrection",
        "Animalfriendship": "Animal Friendship", "Conjure Woodl And Beings": "Conjure Woodland Beings",
        "Spellscroll": "Spell Scroll", "Oilof Sharpness": "Oil of Sharpness",
        "Manualof Gainful Exercise": "Manual of Gainful Exercise",
    }
    for raw, want in cases.items():
        r = fix(raw)
        assert r.name == want and r.repaired and not r.unknown, (raw, r)
        assert r.original == raw


def test_typo_stays_unknown():
    r = fix("Wand of Mgic Detection")
    assert not r.repaired and r.unknown == ["Mgic"]


def test_correct_names_unchanged():
    for n in ["Fireball", "Goblin", "Goblin Boss", "Ring of X-ray Vision"]:
        r = fix(n)
        assert r.name == n and not r.repaired and not r.unknown, (n, r)


def test_lone_letter_never_joined():
    r = fix("Manual of Quickness F Action")
    assert not r.repaired and "F" in r.unknown


def test_compound_not_torn_apart_without_evidence():
    # "druid craft" occurs once next to each other, but "druidcraft" is itself known: untouched
    assert fix("Druidcraft").name == "Druidcraft"
    # unknown glued word whose halves never sit together in the book is flagged, not split
    r = repair_name("Fireglobe", VOCAB, own_heading_counts("Fireglobe"))
    assert not r.repaired and r.unknown == ["Fireglobe"]


def test_own_heading_not_counted():
    vocab = build_vocabulary(["a lone camel stands", "Camel"])
    r = repair_name("Camel", vocab, own_heading_counts("Camel"))
    assert r.unknown == ["Camel"]


def _parsed(name, text="x " * 30):
    return Parsed(type="Spell", name=name, fields={}, text=text, lines=[], checks=[])


def test_assembler_name_checks_and_id():
    p = _parsed("Identi Fy", "IDENTI FY\n" + "body text " * 10)
    eid, checks = _id_and_checks(p, "phb14", Counter(), VOCAB)
    by = {c.name: c for c in checks}
    assert eid == "phb14.spell.identify" and p.name == "Identify"
    assert by["name-repaired"].passed and "'Identi Fy' -> 'Identify'" in by["name-repaired"].reason
    assert by["name-words-known"].passed and p.text.startswith("Identify\n")


def test_assembler_flags_unknown_word():
    p = _parsed("Wand of Mgic Detection")
    _eid, checks = _id_and_checks(p, "dmg14", Counter(), VOCAB)
    bad = next(c for c in checks if c.name == "name-words-known")
    assert not bad.passed and "Mgic" in bad.reason
