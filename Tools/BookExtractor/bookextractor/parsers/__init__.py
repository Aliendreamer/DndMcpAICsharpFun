"""Entity parsers. Each turns reading-ordered Lines into Parsed records."""
from __future__ import annotations

from dataclasses import dataclass, field

from ..checks import Check
from ..model import Line, Page


@dataclass
class Parsed:
    type: str
    name: str
    fields: dict
    text: str
    lines: list[Line]
    checks: list[Check] = field(default_factory=list)
    variant_of: str | None = None
    # the name is a printed table row's label cell: the table attests it when the row's own checks pass (P4)
    table_row: bool = False
    # the printed text of the parent heading's first line (a world section's category), for the run-together test
    category_raw: str = ""


@dataclass
class ParseContext:
    """What a parser may know about the book being parsed beyond its reading-ordered body lines."""
    slug: str
    source: str  # 5etools source key, e.g. "PHB"
    pages: dict[int, Page]  # raw, un-ordered pages by number
    cache: dict = field(default_factory=dict)  # per-extraction results shared between types (races)
