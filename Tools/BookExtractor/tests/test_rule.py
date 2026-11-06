from functools import lru_cache
from pathlib import Path

from bookextractor.layout import reading_order
from bookextractor.model import load_ocr_page
from bookextractor.pages import classify_page
from bookextractor.parsers import ParseContext
from bookextractor.parsers.rule import parse_rules

FIX = Path(__file__).parent / "fixtures" / "ocr"


def _pages(book, numbers):
    return {n: load_ocr_page(FIX / book / f"{n:03d}.json", n) for n in numbers}


def _parse(book, numbers, slug, source, cache=None):
    pages = _pages(book, numbers)
    body = [l for n in sorted(pages) if classify_page(pages[n]) in ("rules", "art") for l in reading_order(pages[n])]
    ctx = ParseContext(slug=slug, source=source, pages=pages, cache=dict(cache or {}))
    return parse_rules(body, ctx), ctx


@lru_cache(maxsize=None)
def phb_variant():
    return tuple(_parse("phb", [14], "phb14", "PHB")[0])


@lru_cache(maxsize=None)
def phb_ability_scores():
    return tuple(_parse("phb", [174, 175, 176, 177], "phb14", "PHB")[0])


@lru_cache(maxsize=None)
def phb_downtime():
    return tuple(_parse("phb", [187, 188], "phb14", "PHB")[0])


def rule(rules, name):
    hit = [r for r in rules if r.name == name]
    assert hit, f"{name} not among {[r.name for r in rules]}"
    assert len(hit) == 1, f"{name} emitted {len(hit)} times"
    return hit[0]


def failed(r):
    return {c.name: c.reason for c in r.checks if not c.passed}


def test_customizing_ability_scores_is_a_variant_rule():
    """Spec: a Rule named "Customizing Ability Scores" with ruleType V; its text ends before the next heading."""
    r = rule(phb_variant(), "Customizing Ability Scores")
    assert r.type == "Rule"
    assert r.fields["ruleType"] == "V"
    assert "You have 27 points to spend" in r.text
    assert "set of numbers between those extremes." in r.text
    # "4. Describe Your Character" is the next step's heading: the variant's text stops before it
    assert "Describe Your Character" not in r.text
    assert "Your character needs a name" not in r.text
    assert not failed(r)
    assert all(isinstance(e, (str, dict)) for e in r.fields["entries"])


def test_rule_type_only_from_a_printed_label():
    rules = phb_ability_scores()
    # an entry-level section of a rules chapter prints no Variant/Optional label
    plain = rule(rules, "Advantage and Disadvantage")
    assert "ruleType" not in plain.fields
    enc = rule(rules, "Encumbrance")
    assert enc.fields["ruleType"] == "V"
    assert rule(rules, "Skills with Different Abilities").fields["ruleType"] == "V"


def test_a_nested_variant_is_not_in_its_parents_text():
    rules = phb_ability_scores()
    parent = rule(rules, "Using Each Ability")
    enc = rule(rules, "Encumbrance")
    assert "If you use this variant" not in parent.text or "If you use this variant" in enc.text
    assert not {id(l) for l in parent.lines} & {id(l) for l in enc.lines}


def test_a_bold_lead_in_stays_in_its_parent():
    """Review Focus 4: "Lifting and Carrying." / "Push, Drag, or Lift." are bold lead-ins inside the Strength
    rules, not Rules of their own."""
    rules = phb_ability_scores()
    names = {r.name for r in rules}
    assert not any(n.startswith(("Lifting and Carrying", "Push, Drag", "Carrying Capacity", "Athletics"))
                   for n in names), names
    parent = rule(rules, "Using Each Ability")
    assert "Push, Drag, or Lift. You can push, drag, or lift a" in parent.text
    assert "Carrying Capacity. Your carrying capacity is your" in parent.text


def test_downtime_activities_are_rules():
    rules = phb_downtime()
    crafting = rule(rules, "Crafting")
    assert "You can craft nonmagical objects" in crafting.text
    assert "at a total cost of 750 gp." in crafting.text
    # "Practicing a Profession" is printed as a heading the section reader did not read: its own activity
    assert "You can work between adventures" not in crafting.text
    prof = rule(rules, "Practicing a Profession")
    assert "You can work between adventures" in prof.text
    assert "wealthy lifestyle instead." in prof.text
    for n in ("Recuperating", "Researching", "Training"):
        r = rule(rules, n)
        assert "ruleType" not in r.fields
    assert "learning a new" in rule(rules, "Training").text
    # an activity's lines belong to no other rule
    owner: dict[int, str] = {}
    for r in rules:
        for l in r.lines:
            assert id(l) not in owner, f"{l.text!r} in {owner.get(id(l))} and {r.name}"
            owner[id(l)] = r.name


def test_a_variant_sidebar_is_a_rule_bounded_by_its_box():
    """PHB "Variant: Equipment Sizes" is a sidebar, stripped from the section text: it is read from the unstripped
    lines and ends with its box."""
    rules, _ = _parse("phb", [145], "phb14", "PHB")
    r = rule(rules, "Equipment Sizes")
    assert r.fields["ruleType"] == "V"
    assert r.text.startswith("Variant: Equipment Sizes")
    assert "In most campaigns, you can use or wear any equipment" in r.text
    assert "based on the extent of the alterations required." in r.text
    # the column's text after the box is the Armor section's
    assert "The Armor table shows the cost" not in r.text
    assert "Armor Proficiency" not in r.text
    assert not failed(r)
    # Equipment is no rules chapter: its sections are not rules
    assert "Armor and Shields" not in {x.name for x in rules}


def test_lines_claimed_by_an_earlier_type_are_not_a_rules():
    """A rule never takes the printed lines of an entity an earlier parser emitted (ctx.cache["claimed_lines"])."""
    rules, _ = _parse("phb", [187, 188], "phb14", "PHB")
    crafting = rule(rules, "Crafting")
    claimed = {(l.page, tuple(round(x, 1) for x in l.box)) for l in crafting.lines[1:4]}
    again, _ = _parse("phb", [187, 188], "phb14", "PHB", cache={"claimed_lines": claimed})
    c2 = rule(again, "Crafting")
    assert not {(l.page, tuple(round(x, 1) for x in l.box)) for l in c2.lines} & claimed
    # a heading an earlier type owns is no rule
    head = {(l.page, tuple(round(x, 1) for x in l.box)) for l in crafting.lines[:1]}
    gone, _ = _parse("phb", [187, 188], "phb14", "PHB", cache={"claimed_lines": head})
    assert "Crafting" not in {r.name for r in gone}


def test_a_section_without_a_sure_end_fails_text_complete():
    rules = phb_downtime()
    # Training is the last activity on the last fixture page: the text runs to the end of what was read
    t = rule(rules, "Training")
    assert "text-complete" in failed(t)
    assert "Training" in failed(t)["text-complete"]
