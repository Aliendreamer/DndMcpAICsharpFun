"""An art credit set sideways in the margin never ends a stat block or enters its text (scc Daemogoth, erlw Bone Knight)."""
from bookextractor.model import Fragment, Line
from bookextractor.parsers.monster import parse_monsters

H = 28.0


def _line(text: str, y: float, x0: float = 100, x1: float = 700, height: float = H) -> Line:
    box = (x0, y, x1, y + height)
    return Line(text, box, height, 1, [Fragment(text, 1.0, box)])


def _credit(text: str, y: float) -> Fragment:
    return Fragment(text, 1.0, (60, y, 72, y + 260))


BLOCK = [
    "Small construct, unaligned", "Armor Class 13", "Hit Points 7 (2d6)", "Speed 30 ft.",
    "STR DEX CON INT WIS CHA", "10 (+0) 10 (+0) 10 (+0) 10 (+0) 10 (+0) 10 (+0)",
    "Senses passive Perception 10", "Languages Common", "Challenge 1/8 (25 XP)", "Actions",
    "Greatsword. Melee Weapon Attack: +7 to hit, reach 5 ft., one",
]
REST = ["target. Hit: 11 (2d6 + 4) slashing damage.",
        "Longbow. Ranged Weapon Attack: +4 to hit, range 150/600 ft.,", "one target. Hit: 5 (1d8 + 1) piercing damage."]


def _page(credit_row: bool) -> list[Line]:
    lines = [_line("BONE KNIGHT", 100, height=44)]
    y = 150.0
    for t in BLOCK:
        lines.append(_line(t, y))
        y += 32
    if credit_row:
        f = _credit("TOMAS GIORELLO", y - 10)
        lines.append(Line(f.text, f.box, f.box[3] - f.box[1], 1, [f]))
    for t in REST:
        lines.append(_line(t, y))
        y += 32
    return lines


def _action(lines: list[Line]) -> list[dict]:
    (m,) = parse_monsters(lines)
    assert "GIORELLO" not in m.text
    return m.fields["action"]


def test_credit_line_does_not_end_the_block():
    actions = _action(_page(credit_row=True))
    assert [a["name"] for a in actions] == ["Greatsword", "Longbow"]
    assert actions[0]["entries"][0].endswith("one target. Hit: 11 (2d6 + 4) slashing damage.")


def test_credit_merged_into_a_row_is_cut_from_it():
    lines = _page(credit_row=False)
    k = next(i for i, l in enumerate(lines) if l.text.startswith("target. Hit"))
    l, f = lines[k], _credit("TOMAS GIORELLO", lines[k].box[1])
    lines[k] = Line(f"{f.text} {l.text}", l.box, l.height, 1, [f, *l.frags])
    actions = _action(lines)
    assert actions[0]["entries"][0].endswith("one target. Hit: 11 (2d6 + 4) slashing damage.")


def test_upright_caps_heading_still_ends_the_block():
    lines = _page(credit_row=False) + [_line("BONE KNIGHT LORE", 720, height=44), _line("The knights ride.", 780)]
    (m,) = parse_monsters(lines)
    assert "LORE" not in m.text and "knights ride" not in m.text


def _tail(lines: list[Line]) -> str:
    return _action(lines)[-1]["entries"][0]


def test_chapter_foot_tab_letter_and_running_foot_are_no_text():
    for foot in ("CHAPTER 6 | BESTIARY", "328 APPENDIX'A: MISOELLANEOUS CREATURES", "S", "DEVILS", "14.7"):
        lines = _page(credit_row=False) + [_line(foot, 800, height=24)]
        assert _tail(lines).endswith("piercing damage."), foot


def test_tab_letter_merged_into_the_last_row_is_cut():
    lines = _page(credit_row=False)
    l = lines[-1]
    f = Fragment("M", 1.0, (790, l.box[1], 815, l.box[3]))
    lines[-1] = Line(f"{l.text} M", l.box, l.height, 1, [*l.frags, f])
    assert _tail(lines).endswith("piercing damage.")


def test_next_blocks_doubled_name_is_not_this_blocks_text():
    lines = _page(credit_row=False) + [_line("BLiNK DoG", 660), _line("BLINK DOG", 700, height=44)]
    lines += [_line(t, 760 + 32 * k) for k, t in enumerate(BLOCK)]
    first, second = parse_monsters(lines)
    assert first.fields["action"][-1]["entries"][0].endswith("piercing damage.")
    assert second.name == "Blink Dog"


def test_caps_line_set_large_still_ends_the_block_at_the_page_foot():
    lines = _page(credit_row=False) + [_line("GOBLIN LORE", 800, height=44)]
    (m,) = parse_monsters(lines)
    assert "GOBLIN" not in m.text
