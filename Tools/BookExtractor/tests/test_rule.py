from functools import lru_cache
from pathlib import Path

from bookextractor.layout import reading_order
from bookextractor.model import load_ocr_page
from bookextractor.pages import classify_page
from bookextractor.parsers import ParseContext
import pytest

from bookextractor.parsers.rule import parse_rules

FIX = Path(__file__).parent / "fixtures" / "ocr"


def _pages(book, numbers):
    return {n: load_ocr_page(FIX / book / f"{n:03d}.json", n) for n in numbers}


def _parse(book, numbers, slug, source, cache=None):
    pages = _pages(book, numbers)
    body = [l for n in sorted(pages) if classify_page(pages[n]) in ("rules", "art") for l in reading_order(pages[n])]
    ctx = ParseContext(slug=slug, source=source, pages=pages, cache={"claimed_lines": set(), **(cache or {})})
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
    # the variant's own Point Cost table stays; the Ability Scores and Modifiers table printed at the head of the
    # next column, across the variant's last sentence, is not the variant's
    assert "Ability Score Point Cost" in r.text and "11 3 15 9" in r.text
    assert "Ability Scores and Modifiers" not in r.text
    assert "16-17" not in r.text
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
    assert "When you use this variant, ignore the Strength column" in enc.text
    assert "ignore the Strength column" not in parent.text
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


def test_claimed_lines_are_required():
    """The assembler always says which lines earlier types own; a missing set is an error, never 'none'."""
    pages = _pages("phb", [14])
    body = [l for n in sorted(pages) for l in reading_order(pages[n])]
    with pytest.raises(KeyError):
        parse_rules(body, ParseContext(slug="phb14", source="PHB", pages=pages))


@lru_cache(maxsize=None)
def dmg():
    return tuple(_parse("dmg", [235, 258, 259, 260, 261, 263, 264], "dmg14", "DMG")[0])


def test_a_rule_ends_at_a_tall_heading_in_any_casing():
    """DMG Proficiency Dice ends before "SkIll VarIants" (set taller than body text, straight under a table row);
    Madness ends before "Experience Points" (read by the section reader as a sub-heading of MADNESS)."""
    pd = rule(dmg(), "Proficiency Dice")
    assert "rolls the character's proficiency die twice" in pd.text
    assert "Skill" not in pd.text and "SkIll" not in pd.text
    assert "Ability Check Proficiency" not in pd.text
    m = rule(dmg(), "Madness")
    assert "rid a character of indefinite madness." in m.text
    assert "Experience Points" not in m.text
    assert "Session-Based Advancement" not in m.text
    assert not failed(m)


@lru_cache(maxsize=None)
def xge_downtime():
    return tuple(_parse("xge", list(range(127, 136)), "xge", "XGE")[0])


def test_an_activity_ends_before_the_next_after_a_table_footnote():
    """XGE "TRAINING" and "Work" follow a complications table's footnote ("*Might involve a rival"), not a finished
    sentence: they are activities of their own, not text of Selling a Magic Item."""
    rules = xge_downtime()
    selling = rule(rules, "Selling a Magic Item")
    assert "Training" not in {x.strip() for x in selling.text.split("\n")}
    assert "Given enough free time and the services of an instructor" not in selling.text
    assert "Given enough free time and the services of an instructor" in rule(rules, "Training").text
    assert "Given enough free time" not in rule(rules, "Work").text


def test_a_sidebar_box_ends_at_a_taller_quote_and_drops_running_feet():
    rules, _ = _parse("mm", [268], "mm14", "MM")
    pipes = rule(rules, "Satyr Pipes")
    assert pipes.text.rstrip().endswith("shake the creature awake.")
    assert "Sonnet" not in pipes.text and "TWIXt" not in pipes.text
    fam = rule(_parse("mm", [348], "mm14", "MM")[0], "Familiars")
    assert "APPENDIX" not in fam.text.upper().replace("APPENDIX B", "")
    assert fam.text.rstrip().endswith("or quasit.")
    yug = rule(_parse("mm", [312], "mm14", "MM")[0], "Yugoloth Summoning")
    assert yug.text.rstrip().endswith("to dismiss it.")
    assert "3I1" not in yug.text


def test_a_sidebar_running_off_its_page_mid_sentence_is_not_complete():
    rules, _ = _parse("mm", [257], "mm14", "MM")
    t = rule(rules, "Quaggoth Thonot")
    assert "text-complete" in failed(t) and "overleaf" in failed(t)["text-complete"]
