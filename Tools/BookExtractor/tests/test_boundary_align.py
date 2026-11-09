import json
from pathlib import Path

from bookextractor.boundary import BLine
from bookextractor.boundary_align import drop_runs, remove, section_lines


def L(text, col=0):
    return BLine(text=text, page=1, col=col, h=10.0, box=(0, 0, 10, 10))


def test_remove_tail_paragraphs():
    entries = ["Keep this paragraph here.", "Next Heading Text", "Tail body of other section."]
    out = remove(entries, [["Next Heading Text", "Tail body of other section."]])
    assert out == ["Keep this paragraph here."]


def test_remove_mid_paragraph_line():
    assert remove(["alpha beta gamma delta epsilon"], [["gamma delta"]]) == ["alpha beta epsilon"]


def test_repeated_line_takes_first_at_or_after_cursor():
    entries = ["head text", "Entrance", "body", "Entrance"]
    assert remove(entries, [["body"], ["Entrance"]]) == ["head text", "Entrance"]
    assert remove(entries, [["Entrance"]]) == ["head text", "body", "Entrance"]


def test_out_of_order_returns_none():
    assert remove(["head text", "body"], [["body"], ["head text"]]) is None


def test_remove_missing_line_ignored():
    assert remove(["foo bar"], [["nothing here"]]) == ["foo bar"]


def test_remove_whitespace_normalised():
    assert remove(["foo   bar\nbaz qux"], [["bar baz"]]) == ["foo qux"]


def test_remove_hyphen_broken_pair():
    out = remove(["keep this. The crowd was publicizing misled facts. end"],
                 [["The crowd was publicizing mis-", "led facts."]])
    assert out == ["keep this. end"]


def test_remove_bullet_line():
    out = remove(["intro text", "\u2022 Pride's Call rings out", "outro text"], [["\u2022 Pride'S CalL rings out"]])
    assert out == ["intro text", "outro text"]


def test_remove_run_spanning_two_paragraphs():
    out = remove(["keep one. tail of first", "head of second. keep two"],
                 [["tail of first", "head of second."]])
    assert out == ["keep one.", "keep two"]


def test_remove_run_ambiguous_falls_back_to_lines():
    out = remove(["a b c", "x y z", "a b c q"], [["x y z", "a b c q"]])
    assert out == ["a b c"]


def test_remove_list_items_and_empty_list():
    entries = ["intro", {"type": "list", "items": ["one item", "two item"]}]
    assert remove(entries, [["two item"]]) == ["intro", {"type": "list", "items": ["one item"]}]
    assert remove(entries, [["one item", "two item"]]) == ["intro"]
    assert entries[1]["items"] == ["one item", "two item"]


def test_remove_other_dict_shape_ignored():
    e = [{"type": "table", "rows": ["zzz yyy"]}]
    assert remove(e, [["zzz yyy"]]) == e


def test_remove_does_not_mutate_input():
    entries = ["a b c", "d e f"]
    remove(entries, [["d e f"]])
    assert entries == ["a b c", "d e f"]


def test_drop_runs_groups_consecutive():
    assert drop_runs(list("abcde"), {0, 1, 3}) == [["a", "b"], ["d"]]


def test_section_lines_ignores_other_column_text():
    entries = ["The quick brown fox jumps over the lazy dog again and again."]
    lines = [
        L("The quick brown fox jumps over"),
        L("totally unrelated words in the other column", col=1),
        L("the lazy dog again and again."),
    ]
    assert section_lines(lines, entries) == [0, 2]


def test_section_lines_dict_entries_and_short_lines():
    entries = [{"type": "list", "items": ["red green blue", "cyan pink"]}]
    lines = [L("red green"), L("cyan pink"), L("black")]
    assert section_lines(lines, entries) == [0, 1]


def test_regression_fixture_sanity():
    rows = json.loads((Path(__file__).parent / "data" / "boundary_regression.json").read_text())
    hit = 0
    for r in rows[:40]:
        lines = [BLine(l["text"], l["page"], l["col"], l["h"], tuple(l["box"]), l.get("shade", 0.0)) for l in r["lines"]]
        idx = section_lines(lines, r["entries"])
        assert idx == sorted(idx)
        hit += bool(idx)
    assert hit > 0
