from bookextractor.assemble import extract_book
from bookextractor.coverage import is_content_line, measure, reduce_text
from test_assemble import pages

LINE = "The ancient wyrm coils around its hoard of gold."


def test_unheld_line_listed_with_page():
    cov = measure([(5, LINE, 0.95)], ["something else entirely"], [])
    assert cov.unheld == [(5, LINE)] and cov.content_lines == 1


def test_short_or_low_score_lines_are_not_content():
    assert not is_content_line("a" * 29, 0.99)
    assert is_content_line("a" * 30, 0.99)
    assert not is_content_line(LINE, 0.89)
    assert measure([(1, "a" * 29, 1.0), (1, LINE, 0.89)], [], []).content_lines == 0


def test_hyphenated_and_digit_repaired_lines_are_held():
    hy = "The creature rises from the deep, its abys-"
    dig = "The target takes ld6 fire damage on a failed save"
    held = ["The creature rises from the deep, its abyssal wings", "The target takes 1d6 fire damage on a failed save"]
    assert measure([(1, hy, 1.0), (1, dig, 1.0)], held, []).unheld == []


def test_left_out_line_is_held():
    assert measure([(3, LINE, 1.0)], [], [(3, "x " + LINE + " y")]).unheld == []
    assert measure([(4, LINE, 1.0)], [], [(3, LINE)]).unheld == [(4, LINE)]


def test_line_held_only_by_notes_is_held():
    # notes are passed as held texts alongside the entity text
    assert measure([(1, LINE, 1.0)], ["body text", LINE], []).unheld == []


def test_reduce_text():
    assert reduce_text("Hello, World-42!") == "helloworld42"


def test_extract_book_result_has_coverage():
    r = extract_book(pages("mm", 167), "mm14", {"Monster"})
    assert {"contentLines", "unheld", "unheldLines"} <= set(r.coverage)
    assert r.coverage["contentLines"] >= 1
