from pathlib import Path

from bookextractor.layout import fix_digits, join_hyphens, reading_order
from bookextractor.model import Fragment, Line, load_ocr_page

FIX = Path(__file__).parent / "fixtures" / "ocr"


def goblin():
    return load_ocr_page(FIX / "mm" / "167.json", 167)


def test_fix_digits_dice():
    assert fix_digits("45 (6dlO + 12)") == "45 (6d10 + 12)"


def test_fix_digits_leaves_words():
    assert fix_digits("Slashing Bludgeoning") == "Slashing Bludgeoning"


def test_fix_digits_cr():
    assert fix_digits("Challenge l/4") == "Challenge 1/4"


def test_reading_order_columns():
    page = goblin()
    lines = reading_order(page)
    texts = [l.text for l in lines]
    name = texts.index("GOBLIN")
    ac = next(i for i, t in enumerate(texts) if i > name and "Armor Class" in t)
    hp = next(i for i, t in enumerate(texts) if i > name and "Hit Points" in t)
    assert name < ac < hp
    mid = page.width / 2
    for l in lines:
        assert not (l.box[0] < mid - 50 and l.box[2] > mid + 50), l.text


def test_ability_row_order():
    texts = [l.text for l in reading_order(goblin())]
    assert "8 (-1) 14 (+2) 10 (+0) 10 (+0) 8 (-1) 8 (-1)" in texts


def test_headers_footers_removed():
    texts = [l.text for l in reading_order(goblin())]
    assert "GOBLINS" not in texts
    assert "166" not in texts


def test_running_foot_with_its_page_number_is_dropped_whole():
    # the foot is one merged row "166 GOBLINS" / "SLAADI 277"; it used to survive and end a stat block's text
    import re
    page = goblin()
    feet = [l.text for l in reading_order(page) if l.box[1] > 0.95 * page.height]
    assert not any(re.fullmatch(r"(?:\d{1,3} )?[A-Z][A-Z '&-]{2,40}(?: \d{1,3})?", t) or t.isdigit() for t in feet)
    assert any(l.box[1] > 0.95 * page.height and "GOBLINS" in l.text for l in page.lines)  # the foot is on the page


def test_join_hyphens():
    f = Fragment("x", 1.0, (0, 0, 1, 1))
    a = Line("a long wo-", (0, 0, 10, 10), 10, 1, [f])
    b = Line("rd here", (0, 12, 10, 22), 10, 1, [f])
    out = join_hyphens([a, b])
    assert [l.text for l in out] == ["a long word here"]


def test_fix_digits_letter_digits():
    assert fix_digits("1B") == "18"
    assert fix_digits("Sd6") == "5d6"
    assert fix_digits("1S") == "15"
    assert fix_digits("Challenge O") == "Challenge 0"
    assert fix_digits("Bludgeoning") == "Bludgeoning"


def test_fix_digits_fraction_needs_digit():
    assert fix_digits("I/O") == "I/O"
    assert fix_digits("l/4") == "1/4"


def test_edge_rule_keeps_entry_headings():
    names = ["goblin", "clay golem", "faerie dragon", "lizard king", "demilich", "counterspell",
             "fireball", "faerie fire", "revivify", "amulet of proof", "eye of vecna",
             "spell scroll", "figurine of wondrous power", "arcane grimoire"]
    for f in sorted(FIX.glob("*/*.json")):
        page = load_ocr_page(f, int(f.stem))
        for l in page.lines:
            if any(n in l.text.lower() for n in names) and l.box[1] <= 0.95 * page.height:  # not a running foot
                # headings must not be dropped by the edge-noise rule (merged rows excluded)
                merged = any(l.text in k.text for k in reading_order(page))
                assert merged, (f, l.text)
