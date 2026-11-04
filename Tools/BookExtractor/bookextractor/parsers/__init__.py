"""Entity parsers. Each turns reading-ordered Lines into Parsed records."""
from __future__ import annotations

from dataclasses import dataclass, field

from ..checks import Check
from ..model import Line


@dataclass
class Parsed:
    type: str
    name: str
    fields: dict
    text: str
    lines: list[Line]
    checks: list[Check] = field(default_factory=list)
    variant_of: str | None = None
