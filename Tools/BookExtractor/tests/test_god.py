from functools import lru_cache
from pathlib import Path

from bookextractor.assemble import shared_text_checks, _line_keys
from bookextractor.layout import reading_order
from bookextractor.model import Line, load_ocr_page
from bookextractor.pages import classify_page
from bookextractor.parsers import ParseContext
from bookextractor.parsers.god import _own_lines, parse_gods
from bookextractor.parsers.sections import Section, read_sections
from bookextractor.parsers.item import parse_items

FIX = Path(__file__).parent / "fixtures" / "ocr"


def _pages(book, numbers):
    return {n: load_ocr_page(FIX / book / f"{n:03d}.json", n) for n in numbers}


def _parse(book, pages, slug=None, source=None):
    body = [l for n in sorted(pages) if classify_page(pages[n]) in ("rules", "art") for l in reading_order(pages[n])]
    ctx = ParseContext(slug=slug or book, source=source or book.upper(), pages=pages)
    return parse_gods(body, ctx), body


@lru_cache(maxsize=None)
def phb():
    return tuple(_parse("phb", _pages("phb", [300]), "phb14", "PHB")[0])


def god(gods, name):
    hit = [g for g in gods if g.name == name]
    assert hit, f"{name} not among {[g.name for g in gods]}"
    return hit[0]


def failed(g):
    return {c.name: c.reason for c in g.checks if not c.passed}


def test_phb_anubis_from_the_egyptian_table():
    """Spec: phb14.god.anubis has pantheon Egyptian, alignment [L, N], domains with Death, symbol Black jackal."""
    a = god(phb(), "Anubis")
    assert a.fields["pantheon"] == "Egyptian"
    assert a.fields["alignment"] == ["L", "N"]
    assert "Death" in a.fields["domains"]
    assert a.fields["symbol"] == "Black jackal"
    assert a.fields["province"] == "god of judgment and death"
    assert a.table_row and not failed(a)
    # the Norse table below it on the same page has its own title
    assert god(phb(), "Odin").fields["pantheon"] == "Norse"
    # a label wrapped onto a second line ("Hathor, goddess of love, music, and" / "motherhood")
    assert god(phb(), "Hathor").fields["province"] == "goddess of love, music, and motherhood"
    assert len([g for g in phb() if g.fields["pantheon"] == "Egyptian"]) == 14


def test_a_misread_domain_fails_naming_it():
    """Spec / Review Focus 2: a "Tempset" domain cell fails domains-known, naming the word."""
    pages = _pages("phb", [300])
    p = pages[300]
    aegir = next(l for l in p.lines if l.text.startswith("Aegir,"))
    cell = next(l for l in p.lines if l.text == "Tempest" and abs(l.box[1] - aegir.box[1]) < aegir.height)
    cell.text = "Tempset"
    g = god(_parse("phb", pages, "phb14", "PHB")[0], "Aegir")
    assert "domains-known" in failed(g)
    assert "'Tempset'" in failed(g)["domains-known"]
    assert "Tempset" in g.fields["domains"]


def test_alignment_and_symbol_checks_name_the_cell():
    pages = _pages("phb", [300])
    p = pages[300]
    bast = next(l for l in p.lines if l.text.startswith("Bast,"))
    al = next(l for l in p.lines if l.text == "CG" and abs(l.box[1] - bast.box[1]) < bast.height)
    al.text = "CC"
    cat = next(l for l in p.lines if l.text == "Cat")
    p.lines.remove(cat)
    g = god(_parse("phb", pages, "phb14", "PHB")[0], "Bast")
    assert "'CC'" in failed(g)["alignment-known"]
    assert "blank" in failed(g)["symbol-present"]
    assert g.fields["alignment"] == [] and "symbol" not in g.fields


@lru_cache(maxsize=None)
def egw():
    gods, body = _parse("egw", _pages("egw", [21, 22]), "egw", "EGW")
    return tuple(gods), body


def test_egw_prose_god_takes_its_section_as_text():
    """A prose deity section ("Avandra, the Change Bringer") gives the table's Avandra its entries; the table title
    "Prime Deities" is a rank (category), so the pantheon comes from the "Pantheon of Exandria" heading."""
    gods, _ = egw()
    a = god(gods, "Avandra")
    assert a.fields["pantheon"] == "Exandria"
    assert a.fields["category"] == "Prime Deities"
    assert a.fields["alignment"] == ["C", "G"]
    assert a.fields["domains"] == ["Nature", "Trickery"]
    assert a.fields["province"] == "Change, freedom, luck"
    assert a.fields["symbol"] == "Woman's profile embossed on a gold coin or pendant"
    text = " ".join(x for x in a.fields["entries"] if isinstance(x, str))
    assert text.startswith('Also known as "She Who Makes the Path," Avandra')
    assert "the new year until the sun rises." in text
    # its text stops at its own end: no table row, no other god, no commandments of another deity
    assert "Bahamut" not in text and "Woman's profile" not in text
    assert not failed(a)
    # footnote markers dropped, merged "Alignment Province" header cut into two columns
    b = god(gods, "Bahamut")
    assert b.fields["domains"] == ["Life", "Order", "War"] and b.fields["alignment"] == ["L", "G"]
    assert b.fields["province"] == "Honor, justice"


def test_a_god_section_never_absorbs_a_hosted_magic_item():
    """Review Focus 5: a magic item printed inside a god's section (the DMG's Adamantine Armor -- heading, type line,
    description -- set in Avandra's column where her commandments are printed) ends the god's text at the item's
    heading: the god never holds the item's lines, so the text-not-shared gate has nothing to flag."""
    pages = _pages("egw", [21, 22])
    dmg = load_ocr_page(FIX / "dmg" / "150.json", 150)
    item = sorted((l for l in dmg.lines if l.box[0] < 300 and 1060 <= l.box[1] <= 1240),
                  key=lambda l: l.box[1])
    assert item[0].text == "AdaMaNtIne ArMoR" and item[1].text.startswith("Armor (medium or heavy")
    p22 = pages[22]
    # the left column's commandments block (y 650-1000) is replaced by the item, line for line
    p22.lines = [l for l in p22.lines if not (l.box[2] < p22.width / 2 and 650 < l.box[1] < 1000)]
    dy = 680 - item[0].box[1]
    p22.lines += [Line(text=l.text, box=(l.box[0] - 60, l.box[1] + dy, l.box[2] - 60, l.box[3] + dy),
                       height=l.height, page=22, frags=l.frags) for l in item]
    gods, body = _parse("egw", pages, "egw", "EGW")
    a = god(gods, "Avandra")
    text = " ".join(x for x in a.fields["entries"] if isinstance(x, str))
    assert "the new year until the sun rises." in text
    assert "adamantine" not in text.lower() and "critical hit" not in text
    items = [i for i in parse_items(body) if "adamantine" in i.name.lower()]
    assert items, "the hosted magic item is still read as a magic item"
    keys = [_line_keys(a.lines), _line_keys(items[0].lines)]
    assert not keys[0] & keys[1]
    ents = [{"id": "egw.god.avandra", "type": "God", "name": a.name, "checks": []},
            {"id": "egw.magicitem.adamantine-armor", "type": "MagicItem", "name": items[0].name, "checks": []}]
    shared_text_checks(ents, keys)
    assert [c["passed"] for c in ents[0]["checks"] if c["name"] == "text-not-shared"] == [True]


def test_a_nested_hosted_item_ends_the_god_text_at_its_heading():
    """Review Focus 5, when the section reader nests the item under the god (sizes noisy, as SCAG nests Jergal under
    Hoar): the god's own lines stop at the item's heading, above its type line; with no item, nothing is cut."""
    pages = _pages("egw", [21, 22])
    _, body = egw()
    sec = next(x for x in read_sections(body, ParseContext(slug="egw", source="EGW", pages=pages))
               if x.title.startswith("Avandra"))
    item = [l for l in reading_order(load_ocr_page(FIX / "dmg" / "150.json", 150))
            if l.box[0] < 300 and 1060 <= l.box[1] <= 1240]
    assert item[0].text == "AdaMaNtIne ArMoR"
    hosting = Section(title=sec.title, level=sec.level, parents=sec.parents, lines=sec.lines + item, page=sec.page,
                      complete=True, end_reason="heading")
    own = _own_lines(hosting, [hosting])
    assert own == sec.lines
    assert _own_lines(sec, [sec]) == sec.lines
