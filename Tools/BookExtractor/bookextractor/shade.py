"""Per-line background shade versus the page's paper colour (detects shaded sidebars)."""
from __future__ import annotations

from pathlib import Path

import numpy as np
import pymupdf

from .model import Box

_PAD_Y = 3.0  # OCR-scale pixels
_PAD_X = 6.0


def _background(img: np.ndarray, box: Box, scale: float) -> np.ndarray:
    """Mean colour of the brightest 30% of pixels in a band around the box."""
    h, w = img.shape[:2]
    x0 = max(0, int((box[0] - _PAD_X) * scale))
    y0 = max(0, int((box[1] - _PAD_Y) * scale))
    x1 = min(w, int((box[2] + _PAD_X) * scale) + 1)
    y1 = min(h, int((box[3] + _PAD_Y) * scale) + 1)
    region = img[y0:y1, x0:x1].reshape(-1, 3).astype(float)
    if len(region) == 0:
        return np.array([255.0, 255.0, 255.0])
    lum = region.mean(1)
    return region[lum >= np.percentile(lum, 70)].mean(0)


def line_shades(pdf: Path, page: int, boxes: list[Box], ocr_dpi: int = 200,
                dpi: int = 100) -> list[float]:
    """Max-channel |background - paper| per box; paper = per-page median background.

    `page` is 1-based; boxes are in `ocr_dpi` pixels and scaled to the render dpi.
    """
    if not boxes:
        return []
    with pymupdf.open(pdf) as doc:
        pix = doc[page - 1].get_pixmap(dpi=dpi)
        img = np.frombuffer(pix.samples, dtype=np.uint8).reshape(pix.h, pix.w, pix.n)[:, :, :3]
    scale = dpi / ocr_dpi
    bgs = np.array([_background(img, b, scale) for b in boxes])
    paper = np.median(bgs, axis=0)
    return [float(np.abs(b - paper).max()) for b in bgs]
