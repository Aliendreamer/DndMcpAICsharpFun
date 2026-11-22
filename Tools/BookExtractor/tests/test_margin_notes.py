"""Margin notes (the writers' asides) are kept in `notes` of the entity on their page, never in its canonicalText."""
import gzip
import json

from regression_run import DATA

from bookextractor.pages_io import extract_pages, page_from_dict

_RAW = json.loads(gzip.decompress((DATA / "fullbook_cases.json.gz").read_bytes()))


def _entities(case: str) -> list[dict]:
    c = _RAW["cases"][case]
    return extract_pages([page_from_dict(_RAW["pages"][f"{c['slug']}:{n}"]) for n in c["pages"]], c["slug"])


def _notes(case: str, page: int) -> list[dict]:
    return [n for e in _entities(case) for n in e.get("notes", []) if n["page"] == page]


def test_a_signed_note_keeps_its_text_and_signature():
    notes = _notes("notes-bgg127", 127)
    note = next(n for n in notes if n["text"].startswith("Most giants know better than to make regular use"))
    assert note["by"] == "Diancastra"
    assert note["text"].endswith("death rune.")
    assert "Diancastra" not in note["text"]


def test_the_note_is_on_the_entity_with_the_most_text_on_its_page_and_not_in_its_canonical_text():
    ents = _entities("notes-bgg127")
    holder = next(e for e in ents if any(n["text"].startswith("Most giants know") for n in e.get("notes", [])))
    assert holder["page"] == 127
    assert "Most giants know better" not in holder["canonicalText"]
    assert holder["type"] == "Monster"  # the stat block holds the most text on p127


def test_body_lines_stay_with_the_body_when_the_ocr_interleaves_them_with_a_note():
    ents = _entities("notes-bgg134")
    notes = [n for e in ents for n in e.get("notes", []) if n["page"] == 134]
    assert notes
    body = " ".join(e["canonicalText"] for e in ents if e["page"] == 134)
    for n in notes:
        assert n["text"] not in body
        assert "Long ago, a band of frost giants" not in n["text"]
    assert "Long ago, a band of frost giants" in body


def test_a_foreword_is_no_margin_note():
    # ggr p6: the italic letter is most of its page; it stays left out for the coverage scan
    ents = _entities("notes-ggr6")
    assert not [e for e in ents if e["page"] == 6 and e["name"].startswith("Margin note")]
    assert not [n for e in ents for n in e.get("notes", []) if n["page"] == 6]


def test_a_note_on_a_page_no_entity_holds_becomes_a_flagged_lore():
    ents = _entities("notes-bgg127")
    lone = [e for e in ents if e["name"].startswith("Margin note") or " note, p" in e["name"]]
    for e in lone:
        assert e["type"] == "Lore" and any(c["name"] == "margin-note" and not c["passed"] for c in e["checks"])


def test_body_text_set_a_little_large_is_no_pull_quote_and_no_note():
    # bgg p76: the lines of "Shifting the Shroud" grow taller down a scaled page; they stay in the entity's text
    ents = _entities("notes-bgg76")
    magic = next(e for e in ents if e["id"] == "bgg.lore.forest-crystal-magic")
    assert "Intelligence (Arcana) check. A creature who takes control" in magic["canonicalText"]
    assert not [n for e in ents for n in e.get("notes", []) if n["page"] == 76]


def test_only_tall_hand_set_asides_are_notes_not_chapter_openers_or_table_cells():
    for case in ("notes-bgg127", "notes-bgg134", "notes-bgg76"):
        for e in _entities(case):
            for n in e.get("notes", []):
                assert "presents giant enclaves" not in n["text"], (case, n)
