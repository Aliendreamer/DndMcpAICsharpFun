"""A chapter opening's large first letter is put back in front of the opening (OCR reads it as a line of its own)."""
import gzip
import json
import re

from regression_run import DATA

from bookextractor.pages_io import extract_pages, page_from_dict

_RAW = json.loads(gzip.decompress((DATA / "fullbook_cases.json.gz").read_bytes()))


def _text(case: str) -> str:
    c = _RAW["cases"][case]
    pages = [page_from_dict(_RAW["pages"][f"{c['slug']}:{n}"]) for n in c["pages"]]
    return "\n".join(e["canonicalText"] for e in extract_pages(pages, c["slug"]))


def test_drop_cap_letter_restored():
    t = _text("drop-tce8")
    assert "when you make your d&d" in t.lower()
    assert not re.search(r"\bhen you make", t, re.I)


def test_magic_opening_restored():
    assert "magic is everywhere" in _text("drop-tce106").lower()


def test_tab_letter_not_prefixed():
    t = _text("tab-mm107")
    assert not re.search(r"^D(he|ra|the)\w*", t, re.M)
    assert "The most gregarious" in t and "DThe" not in t


def _line(text, box, page=1):
    from bookextractor.model import Fragment, Line
    return Line(text=text, box=box, height=box[3] - box[1], page=page, frags=[Fragment(text, 0.99, box)])


def _body():
    return [_line(f"body text line number {i}", (300, 400 + 40 * i, 800, 430 + 40 * i)) for i in range(6)]


def _vocab():
    from bookextractor.names import build_vocabulary
    return build_vocabulary(["magic is everywhere"])


def test_restore_unit_tall_tab_letter_without_adjacent_line_is_unchanged():
    from bookextractor.assemble import _restore_drop_caps
    lines = [_line("D", (1500, 300, 1560, 440)), *_body()]
    assert [l.text for l in _restore_drop_caps(lines, _vocab())] == [l.text for l in lines]


def test_restore_unit_capitalised_non_vocabulary_word_is_not_prefixed():
    from bookextractor.assemble import _restore_drop_caps
    lines = [_line("D", (100, 300, 250, 440)), _line("Zzyx is a word", (260, 305, 800, 335)), *_body()]
    assert "DZzyx" not in " ".join(l.text for l in _restore_drop_caps(lines, _vocab()))
    assert any(l.text == "D" for l in _restore_drop_caps(lines, _vocab()))


def test_restore_unit_body_height_letter_is_ignored():
    from bookextractor.assemble import _restore_drop_caps
    lines = [_line("A", (100, 300, 130, 330)), _line("AGiC is here", (140, 300, 800, 330)), *_body()]
    assert [l.text for l in _restore_drop_caps(lines, _vocab())] == [l.text for l in lines]


def test_restore_unit_positive_case():
    from bookextractor.assemble import _restore_drop_caps
    lines = [_line("M", (100, 300, 250, 440)), _line("AGiC is everywhere", (260, 305, 800, 335)), *_body()]
    out = [l.text for l in _restore_drop_caps(lines, _vocab())]
    assert "MAGiC is everywhere" in out and "M" not in out


def test_restore_unit_low_score_letter_without_vocabulary_support_is_skipped():
    from bookextractor.assemble import _restore_drop_caps
    from bookextractor.model import Fragment
    cap = _line("S", (100, 300, 250, 440))
    cap.frags = [Fragment("S", 0.64, cap.box)]
    lines = [cap, _line("he Dungeon Master", (260, 305, 800, 335)), *_body()]
    out = [l.text for l in _restore_drop_caps(lines, _vocab())]
    assert "he Dungeon Master" in out and "S" in out
    cap.frags = [Fragment("S", 0.99, cap.box)]
    assert "She Dungeon Master" in [l.text for l in _restore_drop_caps(lines, _vocab())]


def _unsure(letter, score):
    from bookextractor.model import Fragment
    cap = _line(letter, (100, 300, 250, 440))
    cap.frags = [Fragment(letter, score, cap.box)]
    return cap


def _words_vocab():
    from bookextractor.names import build_vocabulary
    return build_vocabulary(["she said the the the the then when when when when having"])


def test_restore_unit_unsure_letter_that_is_not_the_likeliest_word_is_skipped():
    from bookextractor.assemble import _restore_drop_caps
    # "she" is a book word, but "the" is likelier: the S at 0.64 is a misread T
    lines = [_unsure("S", 0.64), _line("he Dungeon Master", (260, 305, 800, 335)), *_body()]
    out = [l.text for l in _restore_drop_caps(lines, _words_vocab())]
    assert "he Dungeon Master" in out and "S" in out


def test_restore_unit_unsure_letter_that_is_the_likeliest_word_is_kept():
    from bookextractor.assemble import _restore_drop_caps
    lines = [_unsure("W", 0.60), _line("hen you make", (260, 305, 800, 335)), *_body()]
    assert "When you make" in [l.text for l in _restore_drop_caps(lines, _words_vocab())]
    lines = [_unsure("H", 0.51), _line("aving been", (260, 305, 800, 335)), *_body()]
    assert "Having been" in [l.text for l in _restore_drop_caps(lines, _words_vocab())]


def test_restore_unit_unsure_letter_before_a_capitalised_opening_needs_only_the_book_word():
    from bookextractor.assemble import _restore_drop_caps
    from bookextractor.names import build_vocabulary
    v = build_vocabulary(["saving throw " * 50, "having delved"])  # "saving" outranks "having", as in the PHB
    lines = [_unsure("H", 0.51), _line("AVING DELVED INTO THE DEPTHS OF", (260, 305, 800, 335)), *_body()]
    assert "HAVING DELVED INTO THE DEPTHS OF" in [l.text for l in _restore_drop_caps(lines, v)]
