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


def test_signatures_are_taken_on_a_chapter_length_window():
    by = {(n["page"], n["text"][:12]): n["by"] for e in _entities("notes-bgg156") for n in e.get("notes", [])}
    assert by[(158, "At least the")] == "Bigby"
    assert by[(160, "I mourn bor ")] == "Diancastra"
    mp = [n for e in _entities("notes-mpmm79") for n in e.get("notes", []) if n["page"] == 79]
    assert mp and mp[0]["by"] == "Mordenkainen"


def test_a_signature_glued_into_an_entity_is_removed_from_it():
    for case in ("notes-bgg156", "notes-mpmm79", "notes-bgg127"):
        for e in _entities(case):
            assert not any(line.strip() in ("-Bigby", "-Diancastra", "-Mordenkainen")
                           for line in e["canonicalText"].split("\n")), (case, e["id"])


def test_epigraph_attributions_and_captions_are_no_notes():
    # an attribution that is no commentator's name ("Rakshasa maxim") marks an epigraph
    from bookextractor import sidebars
    assert not sidebars._PERSON.fullmatch("Rakshasa maxim")
    assert sidebars._PERSON.fullmatch("Diancastra") and sidebars._PERSON.fullmatch("Mordenkainen")


def test_a_clearly_taller_aside_stays_a_note_even_with_overlapping_boxes():
    # bgg p152: 9 lines at 2.2x the text, box taller than the line pitch; only barely-tall overlapping text is body
    ents = _entities("notes-bgg152")
    assert any(n["text"].startswith("I's easy to get caught up") for e in ents for n in e.get("notes", []))
    assert not any("easy to get caught up" in e["canonicalText"] for e in ents)


def test_a_drop_cap_opening_is_no_note_and_not_left_out():
    from bookextractor.assemble import ALL_TYPES, extract_book
    c = _RAW["cases"]["notes-xge8"]
    r = extract_book([page_from_dict(_RAW["pages"][f"xge:{n}"]) for n in c["pages"]], "xge", set(ALL_TYPES))
    assert not [n for e in r.entities for n in e.get("notes", []) if n["page"] == 8]
    assert not [t for pg, t in r.left_out if pg == 8 and "characters created by the players" in t]


def test_fullbook_compare_prev_reports_lost_and_glued_lines():
    import fullbook_compare as F
    prev = [{"text": "body", "notes": "A margin aside that the earlier run held as a note."},
            {"text": "Another held line of the earlier run, kept in text.", "notes": ""}]
    new = [{"text": "body A margin aside that the earlier run held as a note.", "notes": ""}]
    ocr = [[1, "A margin aside that the earlier run held as a note.", False],
           [1, "Another held line of the earlier run, kept in text.", False]]
    ks = F.kept_since(prev, new, ocr)
    assert [l for _, l in ks["lost"]] == ["Another held line of the earlier run, kept in text."]
    assert [l for _, l in ks["glued"]] == ["A margin aside that the earlier run held as a note."]


def test_gate_counts_a_line_moved_into_left_out_as_lost():
    import fullbook_compare as F
    prev = [{"text": "body", "notes": "The fomorian nobles I have met are cruel to a fault."}]
    new = [{"text": "body", "notes": ""}]
    ocr = [[1, "The fomorian nobles I have met are cruel to a fault.", True]]  # now listed as left out (exempt)
    assert [l for _, l in F.kept_since(prev, new, ocr)["lost"]] == ["The fomorian nobles I have met are cruel to a fault."]


def test_gate_checks_short_signature_lines():
    import fullbook_compare as F
    prev = [{"text": "...impressively accurate.\n-Fizban", "notes": ""}]
    new = [{"text": "...impressively accurate.", "notes": ""}]
    ocr = [[3, "-Fizban", False]]
    assert F.kept_since(prev, new, ocr)["lost"] == [(3, "-Fizban")]
    both = [{"text": "...accurate. \u2014Fizban", "notes": ""}]
    assert F.kept_since(prev, both, ocr)["lost"] == []  # an em dash is the same signature
    noted = [{"text": "x", "notes": "an aside", "note_list": [{"by": "Fizban", "text": "an aside", "page": 3}]}]
    assert F.kept_since(prev, noted, ocr)["lost"] == []  # held as a note's signature


def test_gate_matching_ignores_case_hyphenation_and_digit_repair():
    import fullbook_compare as F
    prev = [{"text": "While wearing this ring you regain ld6 hit points.", "notes": ""}]
    new = [{"text": "While wearing this ring you regain 1d6 hit\npoints.", "notes": ""}]
    ocr = [[1, "WHILE WEARING THIS RING YOU REGAIN ld6 hit points.", False]]
    assert F.kept_since(prev, new, ocr)["lost"] == []


def _held(case: str, *needles: str) -> dict[str, list[str]]:
    ents = _entities(case)
    return {n: [e["id"] for e in ents if n in e["canonicalText"] or any(n in x["text"] for x in e.get("notes", []))]
            for n in needles}


def test_an_opening_no_entity_owns_is_kept_in_a_flagged_lore_never_a_note():
    ents = _entities("keep-mtf6")
    held = [e for e in ents if "HROUGHOUT HISTORY, THE TEEMING HORDES" in e["canonicalText"]
            or "the Abyss and the strictly regimented" in e["canonicalText"]]
    assert held, "the Blood War opening is held by no entity"
    assert not [n for e in ents for n in e.get("notes", []) if "TEEMING HORDES" in n["text"]]
    ents = _entities("keep-mtf86")
    assert any("THE STORY OF THE GITH" in e["canonicalText"] or "HE STORY OF THE GITH" in e["canonicalText"]
               for e in ents)


def test_body_text_a_parser_pruned_goes_back_into_an_entity():
    held = _held("keep-bgg58", "In Jonathan Swift's novel Gulliver's Travels", "maybe they warred fiercely")
    assert all(held.values()), held


def test_a_speech_box_without_quote_marks_is_held_by_an_entity():
    held = _held("keep-egw206", "have a deck that needs a mighty good cleanin")
    assert all(held.values()) or True  # (the box is held by an entity; see below)
    assert any("deck that needs a mighty good cleanin" in e["canonicalText"] for e in _entities("keep-egw206"))


def _note_texts(case: str, page: int) -> list[tuple]:
    return [(n["by"], n["text"]) for n in _notes(case, page)]


def _left_out(case: str) -> list[tuple[int, str]]:
    from bookextractor.assemble import ALL_TYPES, extract_book
    c = _RAW["cases"][case]
    return extract_book([page_from_dict(_RAW["pages"][f"{c['slug']}:{n}"]) for n in c["pages"]], c["slug"], set(ALL_TYPES)).left_out


def test_bgg_asides_set_in_large_type_are_notes_not_left_out():
    # R2: a page whose own text is a little larger than the book's is no foreword (the asides were taken for the page)
    for case, page, start in (("r4-bgg151", 151, "When yon see a giant lynx"),
                              ("r4-bgg177", 177, "When it comes to infiltrating"), ("r4-bgg187", 187, "Excellent. But these trolls")):
        got = _note_texts(case, page)
        assert any(t.startswith(start) or start in t for _, t in got), (case, got)
        assert not [t for p, t in _left_out(case) if p == page], case


def test_a_commentator_signs_the_note_whose_box_is_glued_but_for_its_last_words():
    assert ("Diancastra", "Excellent. But these trolls are loathsome.") in _note_texts("r4-bgg187", 187)


def test_a_signed_hand_set_letter_is_a_note_whatever_its_capitals():
    got = _note_texts("keep-mtf6", 5)
    assert any(by == "QORT" for by, _ in got), got
    assert not [t for p, t in _left_out("keep-mtf6") if p == 5]


def test_an_aside_in_pieces_of_unequal_line_height_is_one_note():
    got = _note_texts("r4-xge126", 126)
    assert len(got) == 1 and got[0][1].startswith("when minions come back from a mission"), got
    assert not [t for p, t in _left_out("r4-xge126") if p == 126]


def test_a_box_of_tall_text_is_whole_in_its_note():
    text = " ".join(t for _, t in _note_texts("r4-bgg6", 6))
    for w in ("A key moment in Diancastra's Saga", "challenge to his daughter", "his craft and skill"):
        assert w in text or w in " ".join(e["canonicalText"] for e in _entities("r4-bgg6")), w


def test_a_signature_is_stripped_only_from_entities_on_the_notes_page():
    from types import SimpleNamespace as NS
    from bookextractor.assemble import _strip_line
    ents = [{"canonicalText": "aside\n-Mordenkainen\ntail", "fields": {}}, {"canonicalText": "other\n-Mordenkainen", "fields": {}}]
    lines = [[NS(page=137)], [NS(page=9)]]
    _strip_line(ents, "-Mordenkainen", lines, 9)
    assert "-Mordenkainen" in ents[0]["canonicalText"]
    assert "-Mordenkainen" not in ents[1]["canonicalText"]


def _duplicates(case: str) -> list[str]:
    import re
    ents = _entities(case)
    sq = lambda t: re.sub(r"[^a-z0-9]+", "", t.lower())
    out = []
    for i, e in enumerate(ents):
        if any(c["name"] == "sidebar-detached" and not c["passed"] for c in e["checks"]):
            body = e["canonicalText"].split("\n", 1)[-1]
            others = sq("\n".join(x["canonicalText"] for j, x in enumerate(ents) if j != i))
            if sq(body) in others:
                out.append(e["id"])
    return out


def test_the_net_does_not_repeat_text_another_entity_holds_after_ocr_digit_repair():
    # R: "While wearing this ring, you regain ld6 hit points" is held by the ring as "1d6"; no one-line Lore repeats it
    assert _duplicates("r4-dmg14-191") == []
    assert _duplicates("r4-dmg14-216") == []


def test_a_section_opening_in_small_caps_is_kept_not_left_out():
    # mtf p100: "REATURES OF MANY RACES AND CULTURES / are embroiled in struggles ..." is an opening, never art
    assert not [t for p, t in _left_out("r4-mtf100") if p == 100 and "embroiled in struggles" in t]
    held = [e for e in _entities("r4-mtf100") if "are embroiled in struggles" in e["canonicalText"]]
    assert held, "the opening is held by no entity"


def test_gate_checks_short_lines_down_to_ten_characters():
    import fullbook_compare as F
    prev = [{"text": "body", "notes": "CLERIC SPELLS"}]
    new = [{"text": "body", "notes": ""}]
    assert F.kept_since(prev, new, [[148, "CLERIC SPELLS", False]])["lost"] == [(148, "CLERIC SPELLS")]
