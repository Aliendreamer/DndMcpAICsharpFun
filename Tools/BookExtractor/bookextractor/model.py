"""Page model: OCR fragments merged into lines, grouped into pages."""
from __future__ import annotations

import json
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Literal

Box = tuple[float, float, float, float]  # x0, y0, x1, y1 in 200-dpi pixels

_DASH = str.maketrans({"−": "-", "–": "-", "—": "-", "’": "'", "‘": "'",
                       "“": '"', "”": '"', "（": "(", "）": ")", "：": ":", "，": ","})


def clean(text: str) -> str:
    """Normalise unicode dashes/quotes/brackets and collapse whitespace."""
    return re.sub(r"\s+", " ", text.translate(_DASH)).strip()


@dataclass
class Fragment:
    text: str
    score: float
    box: Box


@dataclass
class Line:
    text: str
    box: Box
    height: float
    page: int
    frags: list[Fragment] = field(default_factory=list)


@dataclass
class Page:
    number: int
    width: int
    height: int
    lines: list[Line]
    source: Literal["ocr", "layer"]


def load_ocr_page(path: Path, number: int) -> Page:
    """Load a spike-format OCR JSON ({w,h,lines:[{box,t,s}]}). Lines are raw, unordered."""
    data = json.loads(Path(path).read_text(encoding="utf-8"))
    lines: list[Line] = []
    for item in data["lines"]:
        text = clean(item["t"])
        if not text:
            continue
        xs = [p[0] for p in item["box"]]
        ys = [p[1] for p in item["box"]]
        box: Box = (min(xs), min(ys), max(xs), max(ys))
        frag = Fragment(text=text, score=float(item.get("s", 1.0)), box=box)
        lines.append(Line(text=text, box=box, height=box[3] - box[1], page=number, frags=[frag]))
    return Page(number=number, width=int(data["w"]), height=int(data["h"]), lines=lines, source="ocr")
