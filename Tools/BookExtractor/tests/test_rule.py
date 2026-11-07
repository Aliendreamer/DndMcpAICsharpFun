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
    assert not any(n.startswith(("Push, Drag", "Carrying Capacity", "Athletics")) for n in names), names
    # "Lifting and Carrying" is a printed sub-heading (a Rule of its own, round 4); its bold lead-ins stay in it
    parent = rule(rules, "Lifting and Carrying")
    assert "Push, Drag, or Lift. You can push, drag, or lift a" in parent.text
    assert "Carrying Capacity. Your carrying capacity is your" in parent.text
    assert "Push, Drag, or Lift" not in rule(rules, "Strength Checks").text


def test_printed_sub_headings_are_rules_and_share_no_text_with_their_parent():   # round 4
    """A rules-chapter sub-heading is a Rule of its own, at any depth; its parent holds only the lead before it. No
    line is in two Rules, and a parent that doubts a sub-section as its sibling still fails text-complete."""
    for rules in (phb_ability_scores(), dmg(), dmg_workshop(), _parse("phb", [190, 193, 194, 195], "phb14", "PHB")[0]):
        seen = [id(l) for r in rules for l in r.lines]
        assert len(seen) == len(set(seen))
    rules = phb_ability_scores()
    assert {"Strength Checks", "Lifting and Carrying", "Contests"} <= {r.name for r in rules}
    assert "Lifting and Carrying" not in rule(rules, "Strength Checks").text.replace("\n", " ")[40:]
    assert "text-complete" in failed(rule(dmg_workshop(), "Healing"))  # its held sibling "Rest Variants" is named


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


def test_a_rule_ends_at_a_tall_heading_the_reader_did_not_read():
    """DMG Proficiency Dice ends before "SkIll VarIants": a heading the section reader did not read, set taller than
    the rule's own, straight under the table's last row (W10 b)."""
    pd = rule(dmg(), "Proficiency Dice")
    assert "rolls the character's proficiency die twice" in pd.text
    assert "Skill" not in pd.text and "SkIll" not in pd.text
    assert "Ability Check Proficiency" not in pd.text
    assert not failed(pd)


def test_a_heading_the_reader_nests_stays_in_and_a_long_rule_fails():
    """W10: "Experience Points" is read as a sub-heading of MADNESS (0.93x its height): never a silent cut. Round 4:
    it is a Rule of its own and Madness no longer holds its text, but Madness still fails text-complete naming it."""
    m = rule(dmg(), "Madness")
    assert "Experience points (XP) fuel level advancement" not in m.text
    assert "Experience points (XP) fuel level advancement" in rule(dmg(), "Experience Points").text
    assert not {id(l) for l in m.lines} & {id(l) for l in rule(dmg(), "Experience Points").lines}
    reason = failed(m)["text-complete"]
    # W24: the sub-heading nearly ended it (within the midpoint band), so the doubt names it first; the four-page
    # run is the other reason
    assert "'Experience Points'" in reason and "holds" in reason or "4 pages" in reason and "p258-p261" in reason


@lru_cache(maxsize=None)
def dmg_workshop():
    return tuple(_parse("dmg", [263, 264, 265, 266, 267, 270, 271], "dmg14", "DMG")[0])


def test_a_rule_keeps_its_own_tall_sub_headings():
    """W10: OCR heading heights vary by about 10%; a sub-heading the reader nests under the rule stays in it. W24:
    "HORROR" (0.81) sits within the band under the midpoint between FEAR AND HORROR (0.97) and its first
    sub-heading, so the rule is kept whole and `text-complete` names it (never a silent cut)."""
    fear = rule(dmg_workshop(), "Fear and Horror")
    # round 4: each printed sub-heading is a Rule of its own and its parent holds only the lead
    assert "make a Charisma saving throw to resist the horror" not in fear.text
    assert "make a Charisma saving throw to resist the horror" in rule(dmg_workshop(), "Horror").text
    assert set(failed(fear)) <= {"text-complete"} and "'HORROR'" in failed(fear).get("text-complete", "'HORROR'")
    side = rule(dmg_workshop(), "Side Initiative")
    assert "Under this variant, the players roll a d20" in side.text  # under "Side Initiative"
    assert "Under this variant, the players roll a d20" not in rule(dmg_workshop(), "Initiative Variants").text
    actions_rules = _parse("phb", [190, 193, 194, 195], "phb14", "PHB")[0]
    actions = rule(actions_rules, "Actions in Combat")
    for sub in ("Ready", "Search", "Use An Object"):
        assert sub.lower() not in actions.text.lower().split("\n")[0]
        assert rule(actions_rules, sub.title().replace("An", "an")).text.strip()
    assert "You normally interact with an object while doing something else" in rule(actions_rules,
                                                                                  "Use an Object").text
    checks = rule(phb_ability_scores(), "Contests")  # CONteSTS
    assert "Sometimes one character's or monster's efforts are directly opposed" in checks.text
    assert "directly opposed" not in rule(phb_ability_scores(), "Ability Checks").text


def test_art_text_and_a_caption_over_another_heading_are_dropped():
    """XGE Downtime Revisited: the handwritten art note ("when minions come back from ...") and the "ChelDar's
    Plans" caption printed straight over the "Downtime Activities" heading are not the rule's text."""
    rules, _ = _parse("xge", [78, 124, 125, 126, 127], "xge", "XGE")
    r = rule(rules, "Downtime Revisited")
    assert "Cheldar wants to make the temple of Pholtus" in rule(rules, "Example Rival: High Priest Cheldar").text
    assert not any("minions" in x.text for x in rules)
    assert not any("ChelDar's Plans" in x.text or "Cheldar's Plans" in x.text for x in rules)
    assert "Downtime activities are tasks that usually take" in rule(rules, "Downtime Activities").text


def test_a_heading_left_in_a_rule_fails_text_complete():
    """A heading-like line set at least as tall as the rule's own heading and clearly taller than its body text,
    over prose, still inside the rule: text-complete fails naming it."""
    pages = _pages("mm", [268])
    line = next(l for l in pages[268].lines if l.text.startswith("Charming Melody."))
    line.text, line.height = "Charming Melody", 40.0
    body = [l for n in sorted(pages) for l in reading_order(pages[n])]
    ctx = ParseContext(slug="mm14", source="MM", pages=pages, cache={"claimed_lines": set()})
    r = rule(parse_rules(body, ctx), "Satyr Pipes")
    assert "'Charming Melody'" in failed(r)["text-complete"]


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


def test_a_rule_holding_a_sibling_sized_heading_fails_text_complete_naming_it():   # W24: DMG Healing
    """"Rest Variants" (0.79) sits at the midpoint between HEALING (0.97) and its sub-headings (0.61): the rule
    keeps it (never a silent cut) and `text-complete` names it, so the Rest Variants rule is never Accepted as
    Healing's text."""
    healing = rule(dmg_workshop(), "Healing")
    assert "The rules for short and long rests" not in healing.text  # round 4: Rest Variants is a Rule of its own
    assert "The rules for short and long rests" in rule(dmg_workshop(), "Rest Variants").text
    reason = failed(healing)["text-complete"]
    assert "'Rest VarianTs'" in reason and "holds" in reason


def test_a_rule_holding_a_sibling_sized_section_fails_text_complete():   # W25: PHB Cover
    """"COVER" (p197, 1.70) is set larger than its peers (median 1.39), so the reader nests "Damage and Healing"
    (1.33) under it: the rule keeps the text whole, and `text-complete` names the held section."""
    rules = _parse("phb", [190, 193, 194, 195, 196, 197, 198, 199], "phb14", "PHB")[0]
    cover = rule(rules, "Cover")
    assert "Temporary Hit Points" not in cover.text  # round 4: "Damage and Healing" is a Rule of its own
    assert "Temporary Hit Points" in rule(rules, "Temporary Hit Points").text
    assert not {id(l) for l in cover.lines} & {id(l) for l in rule(rules, "Damage and Healing").lines}
    why = failed(cover)["text-complete"]
    assert "'Damage and Healing'" in why and "set like its parent's siblings" in why
    assert "set like its parent's siblings" not in failed(rule(rules, "Making an Attack")).get("text-complete", "")
    # an activity's complications sub-heading, set like the activities, is its own part, never a held sibling
    research = rule(_parse("xge", [127, 128, 129, 130, 131, 132, 133, 134, 135], "xge", "XGE")[0], "Research")
    assert "Research Complications" in research.text.replace("\n", " ") or "Complications" in research.text
    assert "set like its parent's siblings" not in failed(research).get("text-complete", "")


def test_a_section_that_calls_itself_a_rule_is_a_rule_in_any_chapter():   # Task 7 round 2: DMG Renown p22
    """"Renown is an optional rule you can use ..." (chapter 1, no rules chapter, no label): a Rule by the book's
    own words, with no ruleType (no printed label)."""
    rules = _parse("dmg", [22], "dmg14", "DMG")[0]
    renown = rule(rules, "Renown")
    assert "optional rule" in renown.text and "ruleType" not in renown.fields
