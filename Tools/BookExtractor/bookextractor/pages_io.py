"""Page <-> dict serialisation and a subset-extraction entry point (fixtures built from a few pages)."""
from __future__ import annotations

from .assemble import ALL_TYPES, extract_book
from .model import Fragment, Line, Page


def page_to_dict(p: Page) -> dict:
    return {
        "number": p.number, "width": p.width, "height": p.height, "source": p.source,
        "lines": [
            {"text": l.text, "box": list(l.box), "height": l.height,
             "frags": [{"text": f.text, "score": f.score, "box": list(f.box)} for f in l.frags]}
            for l in p.lines
        ],
    }


def page_from_dict(d: dict) -> Page:
    number = d["number"]
    lines = [
        Line(text=l["text"], box=tuple(l["box"]), height=l["height"], page=number,
             frags=[Fragment(text=f["text"], score=f["score"], box=tuple(f["box"])) for f in l["frags"]])
        for l in d["lines"]
    ]
    return Page(number=number, width=d["width"], height=d["height"], lines=lines, source=d["source"])


def extract_pages(pages: list[Page], book_slug: str) -> list[dict]:
    """Extract entities from a page subset; all types run because types claim lines from each other."""
    return extract_book(pages, book_slug, set(ALL_TYPES)).entities
