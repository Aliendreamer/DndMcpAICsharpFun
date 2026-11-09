from bookextractor.model import Line
from bookextractor.parsers import ParseContext
from bookextractor.parsers import rule as rule_mod
from bookextractor.parsers import world as world_mod
from bookextractor.parsers.boundary_trim import trim_own

PROSE = "The party enters the hall and finds the old guardian waiting quietly by the door for them."


def ln(text, y, h=20.0, page=1):
    return Line(text=text, box=(100, y, 400, y + h), height=h, page=page)


def ctx():
    return ParseContext(slug="x", source="X", pages={}, cache={})


def section(heading_next):
    head = [ln("Alpha Hold", 0, 40)] + [ln(PROSE + f" a{i}", 50 + 30 * i) for i in range(10)]
    return head + [ln(heading_next, 400, 40)] + [ln(PROSE + f" b{i}", 450 + 30 * i) for i in range(3)]


def test_section_ends_before_a_same_size_heading_naming_a_sibling_section():
    own = section("Beta Keep")
    out = trim_own(own, "Alpha Hold", 1, set(), {"Beta Keep"}, ctx())
    assert out == own[:11]
    assert all(l.text != "Beta Keep" for l in out)


def test_in_section_heading_that_is_not_a_sibling_section_is_not_cut():
    own = section("Final Battle")
    assert trim_own(own, "Alpha Hold", 1, set(), {"Beta Keep"}, ctx()) == own


def test_a_smaller_sub_heading_is_unchanged():
    own = section("Beta Keep")
    own[11] = ln("Beta Keep", 400, 22)
    assert trim_own(own, "Alpha Hold", 1, set(), {"Beta Keep"}, ctx()) == own


def test_a_trim_keeping_under_half_is_not_applied():
    own = section("Beta Keep")[:2] + section("Beta Keep")[10:]
    assert trim_own(own, "Alpha Hold", 1, set(), {"Beta Keep"}, ctx()) == own


def test_world_and_rule_parses_call_the_trim():
    assert world_mod.trim_own is trim_own and rule_mod.trim_own is trim_own


def test_cut_reports_the_heading_start_even_when_a_shaded_start_comes_first():
    from bookextractor.boundary import BLine, cut

    own = section("Final Battle")
    bl = [BLine(l.text, l.page, 0, l.height, l.box, 30.0 if i == 3 else 0.0) for i, l in enumerate(own)]
    bl[3].text, bl[3].h = "Hidden Cellar", 25
    c = cut(bl, "Alpha Hold", 1)
    assert c.rule == "shaded" and c.start == 3 and c.heading == 11


def test_a_repeated_own_heading_is_not_a_sibling_and_is_not_cut():
    own = section("Alpha Hold")  # the rule's own name printed again, big, later in its text
    assert trim_own(own, "Alpha Hold", 1, set(), {"Beta Keep"}, ctx()) == own
