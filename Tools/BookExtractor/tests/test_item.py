import json
from pathlib import Path

import jsonschema

from bookextractor.layout import reading_order
from bookextractor.model import load_ocr_page
from bookextractor.parsers.item import item_checks, parse_items

FIX = Path(__file__).parent / "fixtures" / "ocr" / "dmg"
SCHEMA = Path(__file__).parents[3] / "Schemas" / "canonical" / "MagicItemFields.schema.json"
PAGES = sorted(int(p.stem) for p in FIX.glob("*.json"))


def lines(*ns):
    out = []
    for n in ns:
        out += reading_order(load_ocr_page(FIX / f"{n:03d}.json", n))
    return out


def get(name, *ns):
    return next(p for p in parse_items(lines(*ns)) if p.name == name)


def failed(p):
    return [c for c in p.checks if not c.passed]


def test_two_line_name():
    p = get("Amulet of Proof Against Detection and Location", 150)
    f = p.fields
    assert f["rarity"] == "uncommon" and f["reqAttune"] is True
    assert f["type"] == "wondrous item" and f["wondrous"] is True
    assert f["entries"] == ["While wearing this amulet, you are hidden from divination magic. You can't be targeted "
                            "by such magic or perceived through magical scrying sensors."]
    assert not failed(p), failed(p)
    assert [c.name for c in p.checks][:2] == ["type-parsed", "rarity-parsed"]


def test_artifact_section():
    # the printed heading is "Eye and Hand of Vecna" (one entry; the Eye and the Hand are not separate headings)
    p = get("Eye and Hand of Vecna", 224, 225)
    assert p.fields["rarity"] == "artifact" and p.fields["reqAttune"] is True
    assert p.fields["type"] == "wondrous item"
    assert not failed(p), failed(p)
    assert p.fields["entries"][0].startswith("Seldom is the name of Vecna")
    assert not any("Orb of Dragonkind" in e for e in p.fields["entries"])


def test_artifact_names_and_rarity():
    names = {p.name: p for p in parse_items(lines(224, 225, 226))}
    assert {"Eye and Hand of Vecna", "Orb of Dragonkind", "Sword of Kas"} <= set(names)
    assert all(p.fields["rarity"] == "artifact" for p in names.values())


def test_spell_scroll_found():
    p = next(p for p in parse_items(lines(200, 201)) if p.name.lower().replace(" ", "") == "spellscroll")
    assert p.fields["type"] == "scroll" and p.fields["rarity"] == "varies"
    assert not failed(p), failed(p)
    text = " ".join(p.fields["entries"])
    assert "9th Legendary 19 +11" in text and "successfully copied" in text  # the table and the text after it


def test_figurine_found():
    p = get("Figurine of Wondrous Power", 169)
    assert p.fields["rarity"] == "varies" and p.fields["wondrous"] is True
    assert not failed(p), failed(p)


def test_stone_of_good_luck_found():
    p = get("Stone of Good Luck (Luckstone)", 205)
    assert p.fields["rarity"] == "uncommon" and p.fields["reqAttune"] is True
    assert not failed(p), failed(p)


def test_no_doubled_names():
    for p in parse_items(lines(*PAGES)):
        words = p.name.lower().split()
        pairs = [tuple(words[i:i + 2]) for i in range(len(words) - 1)]
        assert len(pairs) == len(set(pairs)), p.name


def test_multi_rarity_variants_and_wrapped_type_line():
    p = get("Ammunition, +1, +2, or +3", 150)
    assert p.fields["rarity"] == "varies"
    assert p.fields["variants"] == [{"name": "+1", "rarity": "uncommon", "bonus": 1},
                                    {"name": "+2", "rarity": "rare", "bonus": 2},
                                    {"name": "+3", "rarity": "very rare", "bonus": 3}]
    assert not failed(p), failed(p)


def test_type_line_with_late_rarity_and_separate_attunement_line():
    sharp = get("Sword of Sharpness", 206)  # "Weapon (any sword that deals slashing damage), very / rare (...)"
    assert sharp.fields["rarity"] == "very rare" and sharp.fields["reqAttune"] is True
    vorpal = get("Vorpal Sword", 209)  # rarity on the line after the comma
    assert vorpal.fields["rarity"] == "legendary"
    mage = get("Wand of the War Mage, +1, +2, or +3", 212)  # "(requires attunement by a spellcaster)" on its own line
    assert mage.fields["reqAttune"] == "by a spellcaster" and mage.fields["rarity"] == "varies"
    for p in (sharp, vorpal, mage):
        assert not failed(p), failed(p)


def test_type_line_wrapping_before_the_last_bonus_keeps_attunement():
    # TCE All-Purpose Tool: "..., very rare" / "(+3) (requires attunement by an artificer)" lost the attunement
    from bookextractor.model import Line
    texts = ["All-Purpose Tool", "Wondrous item, uncommon (+1), rare (+2), very rare",
             "(+3) (requires attunement by an artificer)",
             "This simple screwdriver can transform into a variety of tools.",
             "While holding this tool, you gain a bonus to artificer spells."]
    ls = [Line(t, (150, 1000 + 40 * k, 750, 1030 + 40 * k), 30, 40) for k, t in enumerate(texts)]
    [p] = parse_items(ls)
    assert p.fields["reqAttune"] == "by an artificer"
    assert [v["bonus"] for v in p.fields["variants"]] == [1, 2, 3]
    assert p.fields["entries"][0].startswith("This simple screwdriver")


def test_attunement_condition_is_kept():
    answering = get("Sword of Answering", 206)  # (sic: the OCR reads "alinment")
    assert answering.fields["reqAttune"] == "by a creature with the same alinment as the sword"
    assert get("Necklace of Prayer Beads", 182, 183).fields["reqAttune"] == "by a cleric, druid, or paladin"


def test_item_does_not_swallow_the_next_item():
    ps = {p.name: p for p in parse_items(lines(159, 160, 162, 163, 164))}
    deck = ps["Deck of Many Things"]
    assert deck.lines[-1].page == 164
    assert not any("Defender" in e for e in deck.fields["entries"])
    assert "Defender" in ps  # it is its own item
    cube = ps["Cube of Force"]  # its table header ("Face Charges Effect", a tall line) stays in the item
    assert "Face Charges Effect" in " ".join(cube.fields["entries"])


def test_item_continues_across_empty_page():
    ps = {p.name: p for p in parse_items(lines(185, 186, 187))}
    hole = ps["Portable Hole"]
    assert {l.page for l in hole.lines} == {185, 187}
    assert not failed(hole), failed(hole)
    ps = {p.name: p for p in parse_items(lines(197, 198, 199))}
    rope = ps["Rope of Entanglement"]
    assert {l.page for l in rope.lines} == {197, 199}
    assert not failed(rope), failed(rope)
    assert "Saddle of the Cavalier" in ps


def test_art_page_labels_are_not_body_text():
    deck = get("Deck of Many Things", 162, 163, 164)
    text = " ".join(deck.fields["entries"])
    assert "DONJON" not in text and "JESTER" not in text


def test_item_does_not_swallow_a_following_section():
    ps = {p.name: p for p in parse_items(lines(218, 219, 220, 221))}
    whelm = ps["Whelm"]
    assert {l.page for l in whelm.lines} == {218}
    assert not any("must be destroyed" in e for e in whelm.fields["entries"])
    axe = ps["Axe of the Dwarvish Lords"]
    assert not any("Destroying Artifacts" in e for e in axe.fields["entries"])


def test_table_endings_are_complete():
    for name, pages in (("Robe of Useful Items", (195,)), ("Elemental Gem", (167, 168))):
        p = get(name, *pages)
        assert next(c for c in p.checks if c.name == "entries-complete").passed, name


def test_lines_are_source_lines():
    src = lines(150)
    p = next(p for p in parse_items(src) if p.name.startswith("Amulet of Proof"))
    assert p.lines and all(any(l is s for s in src) for l in p.lines)
    assert p.lines[0].text.startswith("Amulet of Proof")


def test_every_fixture_item_matches_schema():
    schema = json.loads(SCHEMA.read_text(encoding="utf-8"))
    validator = jsonschema.Draft4Validator(schema)
    items = parse_items(lines(*PAGES))
    assert len(items) > 70
    for p in items:
        errs = list(validator.iter_errors(p.fields))
        assert not errs, (p.name, [e.message for e in errs])


def test_checks_flag_truncation_and_mismatches():
    p = get("Amulet of Proof Against Detection and Location", 150)
    p.fields["entries"] = ["While wearing this amulet, you are hidden from"]
    p.fields["rarity"] = "rare"
    p.fields["reqAttune"] = None
    bad = {c.name for c in item_checks(p) if not c.passed}
    assert {"entries-complete", "rarity-parsed", "attunement-agrees"} <= bad
    p.fields["type"] = "armor"
    assert "type-parsed" in {c.name for c in item_checks(p) if not c.passed}


def test_sidebar_label_is_not_part_of_the_item_name():
    # MTF sidebars title items "Magic Item: Infernal Tack"
    from bookextractor.parsers.item import _name
    assert _name("Magic Item: Infernal Tack") == "Infernal Tack"
    assert _name("MAGIC ITEM: GREATER SILVER SWORD") == "Greater Silver Sword"


def test_sidebar_item_ends_where_the_column_text_resumes():
    # MTF prints Greater Silver Sword in an inset sidebar; below it the column's githyanki lore resumes
    # further left ("riders and fulfill their terms ..."), which is not part of the item
    mtf = Path(__file__).parent / "fixtures" / "ocr" / "mtf"
    src = []
    for n in (90, 91):
        src += reading_order(load_ocr_page(mtf / f"{n:03d}.json", n))
    sword = next(p for p in parse_items(src) if p.name == "Greater Silver Sword")
    assert sword.lines[-1].text == "body, instead of dealing damage."
    assert "riders" not in sword.text and "Astral Skiff" not in sword.text
    assert not failed(sword), failed(sword)
