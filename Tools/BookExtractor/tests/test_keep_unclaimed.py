"""Prose no entity claimed is kept: appended to the section it continues, else a flagged Lore (never a stat block)."""
import gzip
import json

import pytest
from regression_run import DATA

from bookextractor.assemble import ALL_TYPES, extract_book
from bookextractor.coverage import reduce_text
from bookextractor.pages_io import page_from_dict

_RAW = json.loads(gzip.decompress((DATA / "fullbook_cases.json.gz").read_bytes()))

# unheld content lines per fixture case, measured before unclaimed prose was kept
_OLD_UNHELD = {
    "chimera": 0,
    "green-hag": 35,
    "ancient-copper-dragon": 0,
    "death-tyrant": 0,
    "demilich": 0,
    "mpmm-drow": 0,
    "mpmm-giff": 0,
    "mpmm-troll": 0,
    "mpmm-warlock": 0,
    "ggr-goblin": 11,
    "myconid": 0,
    "lich": 0,
    "dragon-lore": 0,
    "gnoll-lore": 24,
    "bandit-lore": 1,
    "mummy-lore": 0,
    "eladrin-lore": 28,
    "scc-lore": 22,
    "sidebar-xge": 0,
    "sidebar-hag": 0,
    "sidebar-ggr": 0,
    "tce-wizard": 0,
    "notes-bgg127": 66,
    "notes-bgg134": 0,
    "notes-ggr6": 6,
    "notes-bgg76": 61,
    "notes-bgg156": 0,
    "notes-mpmm79": 0,
    "notes-bgg152": 0,
    "notes-xge8": 56,
    "keep-bgg58": 3,
    "keep-mtf6": 16,
    "keep-mtf86": 2,
    "keep-egw206": 31,
    "keep-ftd11": 29,
    "xge-encounters": 27,
    "tce-tables": 36,
    "ggr-guilds": 234,
    "r4-bgg143": 0,
    "r4-bgg151": 0,
    "r4-bgg177": 0,
    "r4-bgg187": 0,
    "r4-bgg6": 47,
    "r4-xge126": 88,
    "r4-mtf100": 43,
    "r4-dmg14-191": 36,
    "r4-dmg14-216": 0,
    "r4-dmg14-9": 1,
    "r5-xge148": 14,
    "r5-vrgr9": 42,
    "r5-mm14-240": 0,
    "r6-yuan": 0,
    "r6-geetra": 0,
    "r6-shadar": 0,
    "r7-astral": 42,
    "r7-crystal": 0,
    "r7-runic": 0,
    "r7-avalancher": 0,
    "front-phb-index": 91,
    "front-mm14": 0,
    "front-erlw": 0,
    "front-phb-prose": 67,
    "kc-mm154": 22,
    "kc-egw34": 45,
    "kc-phb159": 15,
    "kc-erlw18": 49,
    "kc-mm107": 0,
}


def _result(case: str):
    c = _RAW["cases"][case]
    return extract_book([page_from_dict(_RAW["pages"][f"{c['slug']}:{n}"]) for n in c["pages"]], c["slug"],
                        set(ALL_TYPES))


def _holders(r, snippet: str) -> list[dict]:
    s = reduce_text(snippet)
    return [e for e in r.entities if s in reduce_text(e["canonicalText"])]


def test_unclaimed_paragraph_after_stat_block_is_flagged_lore():
    r = _result("kc-mm154")
    holders = _holders(r, "When the giants' ancient empires fell, Annam")
    assert holders, "the Annam paragraph is held by no entity"
    assert all(e["type"] == "Lore" for e in holders)
    assert [e["name"].lower() for e in holders] == ["giant gods"]  # named after the nearest preceding heading
    assert any(not c["passed"] and c["name"] == "text-complete" and "unclaimed prose" in c["reason"]
               for e in holders for c in e["checks"])


def test_a_stat_block_never_gains_unclaimed_prose():
    r = _result("kc-mm154")
    for e in r.entities:
        if e["type"] == "Monster":
            assert "ancient empires fell" not in e["canonicalText"]


def test_continuation_is_appended_to_the_section_it_continues():
    # mtf p85-86: a trait-table row ends mid-sentence at a column foot; its last words open the next column
    host = _holders(_result("r4-mtf100"), "merriment takes me to the edge of violence")
    assert [e["id"] for e in host] == ["mtf.lore.githzerai-flaws"]
    assert not any(c["name"] == "text-complete" and "unclaimed prose" in c["reason"] and not c["passed"]
                   for c in host[0]["checks"])


@pytest.mark.parametrize("case,snippet", [
    ("kc-egw34", "According to the teachings of the Kryn and the Umavi"),
    ("kc-erlw18", "Kalashtar are humanoids bound to spirits"),
    ("kc-phb159", "Wretched. You live in inhumane conditions"),
])
def test_kryn_kalashtar_lifestyle_text_is_held(case, snippet):
    assert _holders(_result(case), snippet), f"{snippet!r} is held by no entity"


def test_fixture_cases_unheld_does_not_rise():
    rose = {}
    for case, old in _OLD_UNHELD.items():
        new = _result(case).coverage["unheld"]
        if new > old:
            rose[case] = (old, new)
    assert not rose, rose


def _synthetic(run_texts: list[str], host_last_entry):
    """A host Lore whose last printed line directly precedes an unclaimed run on the same page."""
    from collections import Counter

    from bookextractor.assemble import _keep_unheld, _line_keys
    from bookextractor.model import Fragment, Line, Page
    from bookextractor.names import build_vocabulary

    def line(text: str, y: float) -> Line:
        box = (100.0, y, 700.0, y + 30.0)
        return Line(text=text, box=box, height=30.0, page=1, frags=[Fragment(text, 0.99, box)])

    host_lines = [line("The guild keeps a ledger of every contract it takes on and every", 100.0),
                  line("debt it is owed by the families of the city", 140.0)]
    run = [line(t, 180.0 + 40.0 * i) for i, t in enumerate(run_texts)]
    host = {"id": "x.lore.guild", "type": "Lore", "name": "Guild", "page": 1,
            "canonicalText": "Guild\nThe guild keeps a ledger of every contract it takes on and every debt it is owed "
                             "by the families of the city",
            "fields": {"entries": ["The guild keeps a ledger ...", host_last_entry]}, "checks": [], "notes": []}
    entities, entity_lines = [host], [host_lines]
    claimed = set(_line_keys(host_lines))
    _keep_unheld(entities, entity_lines, {1: host_lines + run}, [], [], set(), True, [_line_keys(host_lines)],
                 [None], [[]], "x", Counter(), build_vocabulary([], []), {1: Page(1, 1000, 1000, host_lines + run, "ocr")},
                 claimed, set())
    return entities


def test_capitalised_paragraph_after_a_table_ending_host_is_a_flagged_lore():
    ents = _synthetic(["The treasury is guarded by four old veterans of the city watch.",
                       "They rotate shifts at every dusk and every dawn."], {"type": "table", "rows": []})
    assert len(ents) == 2 and ents[0]["fields"]["entries"][-1] == {"type": "table", "rows": []}
    assert "treasury" not in ents[0]["canonicalText"]
    assert ents[1]["type"] == "Lore"
    assert any(c["name"] == "text-complete" and not c["passed"] and "unclaimed prose" in c["reason"]
               for c in ents[1]["checks"])


def test_lower_case_continuation_after_a_table_ending_host_still_appends():
    ents = _synthetic(["and the vault beneath it is sealed with three separate locks.",
                       "Each lock needs its own key to open."], {"type": "table", "rows": []})
    assert len(ents) == 1 and "sealed with three separate locks" in ents[0]["canonicalText"]


def test_headed_run_is_never_appended():
    ents = _synthetic(["The Vault", "and the vault beneath it is sealed with three separate locks.",
                       "Each lock needs its own key to open."], "ends mid sentence and")
    assert len(ents) == 2 and ents[1]["name"] == "The Vault"


def _line_at(text: str, page: int, y: float, x0: float = 100.0):
    from bookextractor.model import Fragment, Line
    box = (x0, y, x0 + 600.0, y + 30.0)
    return Line(text=text, box=box, height=30.0, page=page, frags=[Fragment(text, 0.99, box)])


def _assemble(pages: dict[int, list], host_lines: list, host_text: str, host_entries=None):
    """A Lore host holding `host_lines` (its text `host_text`), over the pages' printed lines: the entities after
    _keep_unheld ran on the unclaimed rest."""
    from collections import Counter

    from bookextractor.assemble import _keep_unheld, _line_keys
    from bookextractor.model import Page
    from bookextractor.names import build_vocabulary

    host = {"id": "x.lore.host", "type": "Lore", "name": "Host", "page": host_lines[0].page,
            "canonicalText": "Host\n" + host_text, "fields": {"entries": host_entries or [host_text]},
            "checks": [], "notes": [], "provenance": {"pages": []}}
    by_number = {n: Page(n, 1000, 1000, ls, "ocr") for n, ls in pages.items()}
    entities, entity_lines = [host], [host_lines]
    _keep_unheld(entities, entity_lines, pages, [], [], set(), True, [_line_keys(host_lines)], [None], [[]], "x",
                 Counter(), build_vocabulary([], []), by_number, set(_line_keys(host_lines)), set())
    return entities


_GUARD = "The guards of the vault keep watch over it through every long night"
_ON_A = "and the keys to the vault hang on a hook by the door of the old"
_ON_B = "watch house, where nobody who is not a guard is ever allowed to go"
_ON_C = "in; the captain of the watch sleeps in the room beside it every night."


def test_paragraph_continuing_over_two_column_breaks_ends_in_one_host():
    last = _line_at(_GUARD, 1, 100.0)
    p2 = [_line_at(_ON_A, 2, 100.0)]
    p3 = [_line_at(_ON_B, 3, 100.0), _line_at(_ON_C, 3, 140.0)]
    ents = _assemble({1: [last], 2: p2, 3: p3}, [last], _GUARD)
    assert len(ents) == 1
    assert _ON_A in ents[0]["canonicalText"] and _ON_C in ents[0]["canonicalText"]
    assert [p["page"] for p in ents[0]["provenance"]["pages"]] == [1, 2, 3]


def test_paragraph_after_a_fresh_lore_continues_into_it():
    head = _line_at("Guard Duty", 1, 100.0)
    a = _line_at("The guards of the vault keep watch over it through every long night.", 1, 140.0)
    b = _line_at("and the keys to the vault hang on a hook by the door of the old watch house.", 2, 100.0)
    ents = _assemble({1: [head, a], 2: [b]}, [_line_at("Something else entirely held by the host.", 9, 0.0)],
                     "Something else entirely held by the host.")
    lores = [e for e in ents if e["name"] != "Host"]
    assert len(lores) == 1 and "hang on a hook" in lores[0]["canonicalText"]
    assert [p["page"] for p in lores[0]["provenance"]["pages"]] == [1, 2]


def _vault_page(heading_lines: list[str]):
    held = [_line_at(t, 1, 100.0 + 40 * i) for i, t in enumerate(heading_lines)]
    run = [_line_at("The treasury is guarded by four old veterans of the city watch.", 1, 100.0 + 40 * len(held)),
           _line_at("They rotate shifts at every dusk and every dawn of the week.", 1, 140.0 + 40 * len(held))]
    return held, run


@pytest.mark.parametrize("bad", ["Guard Duty (Optional)", "OLP Contents", "OOLP Treasure", "Location Description"])
def test_flagged_lore_is_never_named_after_a_table_header_or_debris(bad):
    held, run = _vault_page(["The Vault", "Its door has stood shut for a hundred years now.", bad])
    if bad == "Location Description":  # a header row over short cells
        cells = [_line_at(t, 1, 300.0 + 40 * i) for i, t in enumerate(["cellar locked", "stair open", "attic barred"])]
        held += cells
        run = [_line_at(l.text, 1, 600.0 + 40 * i) for i, l in enumerate(run)]
    ents = _assemble({1: [*held, *run]}, held, " ".join(l.text for l in held) + ".")
    lore = [e for e in ents if e["name"] != "Host"]
    assert lore and lore[0]["name"] == "The Vault"


def test_two_runs_with_the_same_heading_on_a_page_are_one_lore():
    heading = _line_at("The Vault", 1, 100.0)
    run1 = [_line_at("The treasury is guarded by four old veterans of the city watch.", 1, 140.0),
            _line_at("They rotate shifts at every dusk and every dawn of the week.", 1, 180.0)]
    mid = _line_at("door locked barred chained sealed shut", 1, 220.0)
    run2 = [_line_at("The captain keeps the only key on a chain around his neck at all times.", 1, 260.0),
            _line_at("Nobody has ever seen him take it off, not even to bathe or sleep.", 1, 300.0)]
    ents = _assemble({1: [heading, *run1, mid, *run2]}, [mid, heading], "The Vault door locked barred chained sealed shut.")
    lore = [e for e in ents if e["name"] != "Host"]
    assert len(lore) == 1 and "only key" in lore[0]["canonicalText"] and "four old veterans" in lore[0]["canonicalText"]
