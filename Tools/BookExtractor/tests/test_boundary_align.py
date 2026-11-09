import json
from pathlib import Path

from bookextractor.boundary import BLine
from bookextractor.boundary_align import remove, section_lines


def L(text, col=0):
    return BLine(text=text, page=1, col=col, h=10.0, box=(0, 0, 10, 10))


def test_remove_tail_paragraphs():
    entries = ["Keep this paragraph here.", "Next Heading Text", "Tail body of other section."]
    out = remove(entries, ["Next Heading Text", "Tail body of other section."])
    assert out == ["Keep this paragraph here."]


def test_remove_mid_paragraph_line():
    out = remove(["alpha beta gamma delta epsilon"], ["gamma delta"])
    assert out == ["alpha beta epsilon"]


def test_remove_ambiguous_returns_none():
    assert remove(["foo bar", "x foo bar y"], ["foo bar"]) is None


def test_remove_missing_returns_none():
    assert remove(["foo bar"], ["nothing here"]) is None


def test_remove_whitespace_normalised():
    assert remove(["foo   bar\nbaz qux"], ["bar baz"]) == ["foo qux"]


def test_remove_list_items_and_empty_list():
    entries = ["intro", {"type": "list", "items": ["one item", "two item"]}]
    out = remove(entries, ["two item"])
    assert out == ["intro", {"type": "list", "items": ["one item"]}]
    out2 = remove(entries, ["one item", "two item"])
    assert out2 == ["intro"]
    assert entries[1]["items"] == ["one item", "two item"]  # not mutated


def test_remove_other_dict_shape_not_found():
    assert remove([{"type": "table", "rows": ["zzz yyy"]}], ["zzz yyy"]) is None


def test_remove_does_not_mutate_input():
    entries = ["a b c", "d e f"]
    remove(entries, ["d e f"])
    assert entries == ["a b c", "d e f"]


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
