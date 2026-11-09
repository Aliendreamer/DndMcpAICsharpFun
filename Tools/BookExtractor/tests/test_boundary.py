from pathlib import Path

import pytest

from bookextractor.boundary import BLine, Cut, cut, find_anchor, headlike
from bookextractor.shade import line_shades

BODY = "This is an ordinary body line of running text that is long."


def L(text, h=20, page=1, col=0, shade=0.0):
    return BLine(text, page, col, h, (0, 0, 100, h), shade)


def base(name="Sorcerer"):
    return [L(name, 30), L(BODY), L(BODY), L(BODY)]


def test_cut_at_sibling_heading():
    lines = base() + [L("Warlock", 30), L(BODY)]
    c = cut(lines, "Sorcerer", 1)
    assert c.drop == {4, 5} and c.start == 4 and c.rule == "heading"


def test_smaller_subheading_kept():
    lines = base() + [L("Origins", 24), L(BODY)]
    assert cut(lines, "Sorcerer", 1) == Cut(set(), None, None)


def test_no_anchor_on_first_page_no_cut():
    lines = [L(BODY), L(BODY), L("Sorcerer", 30, page=2), L("Warlock", 30, page=2)]
    assert find_anchor(lines, "Sorcerer", 1) is None
    assert cut(lines, "Sorcerer", 1).drop == set()


def test_two_anchors_no_cut():
    lines = base() + [L("Sorcerer", 30), L(BODY), L("Warlock", 30)]
    assert cut(lines, "Sorcerer", 1).drop == set()


def test_odd_casing_anchors():
    lines = base("RisinG From ThE LAsT War") + [L("Warlock", 30)]
    assert find_anchor(lines, "Rising from the Last War", 1) == 0
    assert find_anchor(lines, "rising-from-the-last-war-p12", 1) == 0
    assert cut(lines, "Rising from the Last War", 1).drop == {4}


def test_sub_heading_idx_never_cut():
    lines = base() + [L("Warlock", 30), L(BODY)]
    assert cut(lines, "Sorcerer", 1, sub_heading_idx={4}).drop == set()


def test_later_page_heading_cuts():
    lines = base() + [L(BODY, page=2), L("Warlock", 30, page=2, col=1)]
    assert cut(lines, "Sorcerer", 1).drop == {5}


def test_shaded_block_with_heading_dropped():
    lines = base() + [L("Sidebar Title", 26, shade=30), L(BODY, shade=30), L(BODY)]
    c = cut(lines, "Sorcerer", 1)
    assert c.drop == {4, 5} and c.start == 4 and c.rule == "shaded"


def test_read_aloud_box_kept():
    lines = [L("Sorcerer", 30), L(BODY), L("Read this, the following is read:"),
             L("Dark Hall", 26, shade=30), L(BODY, shade=30)]
    assert cut(lines, "Sorcerer", 1).drop == set()


def test_map_labels_dropped():
    lines = base() + [L("MAP 3", 20), L("1 square = 5 feet"), L("Side View"), L(BODY)]
    c = cut(lines, "Sorcerer", 1)
    assert c.drop == {4, 5, 6} and c.rule == "map" and c.start == 4


def test_table_rows_not_headings():
    assert not headlike("3 | House Dimir")
    assert not headlike("10 Simic Combine")
    lines = base("Guilds") + [L("10 Simic Combine", 30), L("3 | House Dimir", 30)]
    assert cut(lines, "Guilds", 1).drop == set()


def test_headlike_basics():
    assert headlike("Warlock")
    assert not headlike("Ends with a period.")
    assert not headlike("one two three four five six seven eight")


def test_line_shades_smoke():
    pdfs = sorted(Path(__file__).resolve().parents[3].glob("books/*.pdf"))
    if not pdfs:
        pytest.skip("no pdf in books/")
    out = line_shades(pdfs[0], 1, [(100.0, 100.0, 600.0, 150.0), (100.0, 200.0, 600.0, 250.0)])
    assert len(out) == 2 and all(0 <= v <= 255 for v in out)


# ---- approvable gate ----
from bookextractor.boundary import approvable  # noqa: E402

_B = ["Opening paragraph " + "word " * 20, "Second para " + "word " * 20]
_C = Cut({5}, 5, "heading")


def test_approvable_ok():
    assert approvable(_B, _B[:1] + ["tail"], _C, "X", [{"name": "text-complete", "passed": False}])


def test_approvable_rejects_low_tokens_kept():
    assert not approvable(_B, ["Opening"], _C, "X", [])


def test_approvable_rejects_no_rule():
    assert not approvable(_B, _B[:1], Cut({5}, 5, None), "X", [])


def test_approvable_rejects_empty_drop_or_unchanged():
    assert not approvable(_B, _B[:1], Cut(set(), None, "heading"), "X", [])
    assert not approvable(_B, list(_B), _C, "X", [])


def test_approvable_rejects_empty_after():
    assert not approvable(_B, [], _C, "X", [])


def test_approvable_waives_entry_level():
    assert approvable(_B, _B[:1] + ["tail"], _C, "X", [{"name": "entry-level", "passed": False}])


def test_approvable_rejects_table_cut_failure():
    assert not approvable(_B, _B[:1] + ["tail"], _C, "X", [{"name": "table-cut", "passed": False}])


def test_approvable_trim_inside_first_entry_keeps_opening():
    first = "Opening paragraph " + "word " * 20
    assert approvable([first, "x " * 5], [first[:-10]], _C, "X", [])


def test_waivable_failures():
    from bookextractor.boundary import waivable_failures
    cs = [{"name": "entry-level", "passed": False}, {"name": "text-complete", "passed": True},
          {"name": "table-cut", "passed": False}]
    assert waivable_failures(cs) == ["entry-level"]


def test_approvable_rejects_opening_cut():
    assert not approvable(_B, [_B[1]], _C, "X", [])


def test_approvable_rejects_other_failing_check():
    assert not approvable(_B, _B[:1] + ["tail"], _C, "X", [{"name": "stat-block", "passed": False}])


def test_map_rule_keeps_prose_line():
    lines = base() + [L("Map 4.1 depicts a region of Annam's Cradle with"), L(BODY)]
    c = cut(lines, "Sorcerer", 1)
    assert not c.drop
