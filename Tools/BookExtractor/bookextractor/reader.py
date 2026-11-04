"""Read a PDF into Pages: cached OCR, or the embedded text layer when it is good enough."""
from __future__ import annotations

import hashlib
import json
import os
import re
import tempfile
from concurrent.futures import ProcessPoolExecutor, as_completed
from pathlib import Path
from typing import Callable

import numpy as np
import pymupdf

from .model import Fragment, Line, Page, clean, load_ocr_page

DPI = 200
_SCALE = DPI / 72

# Quality = share of text-layer words that are dictionary-like (letters only, len >= 2, has a vowel),
# capped at SCAN_LAYER_CAP when a raster image covers most of the page. Why the cap: the 2014 scans
# carry an OCR text layer whose errors are letter substitutions ("existenee", "aetion") that a
# vowel/letters test cannot see - their raw word share is 0.83-0.87 mean, no different from
# born-digital books - but every scan page is a full-page image and no born-digital page is.
# Measured on books/ (pages with >= 5 layer words; raw word share, mean / p5 / min):
#   born-digital  System Reference Document 5.1: 0.845 / 0.678 / 0.385  (0 full-page images)
#                 Player's Basic Rules:          0.883 / 0.731 / 0.346  (1 full-page image: cover)
#                 DM's Basic Rules:              0.771 / 0.675 / 0.657  (1 full-page image: cover)
#   2014 scans    PHB 0.874 / 0.712 / 0.333 | MM 0.833 / 0.693 / 0.222 | DMG 0.867 / 0.672 / 0.000
#                 (all pages full-page images -> capped to 0.5)
# Threshold 0.6 passes ~98% of born-digital pages (the rest -> OCR) and rejects every scan.
LAYER_QUALITY_THRESHOLD = 0.6
SCAN_LAYER_CAP = 0.5
SCAN_IMAGE_COVERAGE = 0.6
# Layers with fewer words than this are not trusted regardless of quality (art / near-empty pages).
MIN_LAYER_WORDS = 5

_WORD = re.compile(r"^[A-Za-z]{2,}$")
_VOWEL = re.compile(r"[aeiouyAEIOUY]")
_EDGE = re.compile(r"^[^\w]+|[^\w]+$")


def pdf_sha256(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def _is_scan_page(page: pymupdf.Page) -> bool:
    """True when one raster image covers most of the page (the layer is then an OCR overlay)."""
    area = page.rect.width * page.rect.height
    for info in page.get_image_info():
        r = pymupdf.Rect(info["bbox"]) & page.rect
        if not r.is_empty and r.width * r.height / area > SCAN_IMAGE_COVERAGE:
            return True
    return False


def layer_quality(page: pymupdf.Page) -> float:
    """0-1 share of text-layer words that look like real words. 0.0 when there is no layer."""
    words = [_EDGE.sub("", w[4]) for w in page.get_text("words")]
    words = [w for w in words if w]
    if not words:
        return 0.0
    good = sum(1 for w in words if _WORD.match(w) and _VOWEL.search(w))
    share = good / len(words)
    return min(share, SCAN_LAYER_CAP) if _is_scan_page(page) else share


def _layer_page(page: pymupdf.Page, number: int) -> Page:
    lines: list[Line] = []
    for block in page.get_text("dict")["blocks"]:
        for ln in block.get("lines", []):
            text = clean("".join(s["text"] for s in ln["spans"]))
            if not text:
                continue
            x0, y0, x1, y1 = (v * _SCALE for v in ln["bbox"])
            box = (x0, y0, x1, y1)
            lines.append(Line(text=text, box=box, height=y1 - y0, page=number,
                              frags=[Fragment(text=text, score=1.0, box=box)]))
    rect = page.rect
    return Page(number=number, width=round(rect.width * _SCALE), height=round(rect.height * _SCALE),
                lines=lines, source="layer")


def default_engine_factory():
    """Build a RapidOCR engine (PP-OCRv5 English, CPU)."""
    from rapidocr import LangRec, OCRVersion, RapidOCR
    threads = int(os.environ.get("BOOKEXTRACTOR_OCR_THREADS", "4"))
    return RapidOCR(params={"Rec.lang_type": LangRec.EN, "Rec.ocr_version": OCRVersion.PPOCRV5,
                            "EngineConfig.onnxruntime.intra_op_num_threads": threads})


def _atomic_write(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp = tempfile.mkstemp(dir=path.parent, suffix=".tmp")
    try:
        with os.fdopen(fd, "wb") as f:
            f.write(data)
        os.replace(tmp, path)
    except BaseException:
        Path(tmp).unlink(missing_ok=True)
        raise


def _ocr_to_cache(engine, doc: pymupdf.Document, index: int, out: Path) -> None:
    pix = doc[index].get_pixmap(dpi=DPI)
    img = np.frombuffer(pix.samples, dtype=np.uint8).reshape(pix.h, pix.w, pix.n)[:, :, :3]
    r = engine(img)
    lines = [] if r.boxes is None else [
        {"box": [[float(x) for x in p] for p in b], "t": t, "s": float(s)}
        for b, t, s in zip(r.boxes, r.txts, r.scores)]
    _atomic_write(out, json.dumps({"w": pix.w, "h": pix.h, "lines": lines}).encode("utf-8"))


_worker: dict = {}


def _worker_init(pdf: str, factory, threads: int) -> None:
    os.environ["BOOKEXTRACTOR_OCR_THREADS"] = str(threads)
    _worker["doc"] = pymupdf.open(pdf)
    _worker["engine"] = factory()


def _worker_run(index: int, out: str) -> int:
    _ocr_to_cache(_worker["engine"], _worker["doc"], index, Path(out))
    return index


class BookReader:
    def __init__(self, pdf_path: Path, cache_dir: Path, threads: int = 4, processes: int = 2,
                 engine_factory: Callable[[], Callable] = default_engine_factory):
        self.pdf_path = Path(pdf_path)
        self.threads = threads
        self.processes = processes
        self.engine_factory = engine_factory
        self.sha = pdf_sha256(self.pdf_path)
        self.root = Path(cache_dir) / self.sha
        self.ocr_dir = self.root / "ocr"
        self.png_dir = self.root / "png"

    def _ocr_path(self, number: int) -> Path:
        return self.ocr_dir / f"{number:03d}.json"

    def read(self, progress: Callable[[int, int], None] | None = None) -> list[Page]:
        with pymupdf.open(self.pdf_path) as doc:
            return self._read(doc, progress)

    def _read(self, doc: pymupdf.Document, progress) -> list[Page]:
        total = len(doc)
        pages: dict[int, Page] = {}
        todo: list[int] = []
        done = 0

        def tick() -> None:
            if progress:
                progress(done, total)

        for i in range(total):
            n = i + 1
            if self._ocr_path(n).exists():
                pages[n] = load_ocr_page(self._ocr_path(n), n)
            else:
                layer = None
                if layer_quality(doc[i]) >= LAYER_QUALITY_THRESHOLD:
                    layer = _layer_page(doc[i], n)
                if layer is not None and len(layer.lines) >= 1 and \
                        sum(len(ln.text.split()) for ln in layer.lines) >= MIN_LAYER_WORDS:
                    pages[n] = layer
                else:
                    todo.append(i)
                    continue
            done += 1
            tick()

        if todo:
            os.environ["BOOKEXTRACTOR_OCR_THREADS"] = str(self.threads)
            engine = self.engine_factory()  # warm in the parent: avoids a cold model-download race
            if self.processes <= 1:
                for i in todo:
                    _ocr_to_cache(engine, doc, i, self._ocr_path(i + 1))
                    pages[i + 1] = load_ocr_page(self._ocr_path(i + 1), i + 1)
                    done += 1
                    tick()
            else:
                del engine
                with ProcessPoolExecutor(max_workers=self.processes, initializer=_worker_init,
                                         initargs=(str(self.pdf_path), self.engine_factory,
                                                   self.threads)) as pool:
                    futures = [pool.submit(_worker_run, i, str(self._ocr_path(i + 1))) for i in todo]
                    try:
                        for fut in as_completed(futures):
                            i = fut.result()
                            pages[i + 1] = load_ocr_page(self._ocr_path(i + 1), i + 1)
                            done += 1
                            tick()
                    except BaseException:
                        for f in futures:
                            f.cancel()  # finished pages stay cached; pending ones are dropped
                        raise
        return [pages[n] for n in sorted(pages)]

    def render_png(self, page: int) -> bytes:
        path = self.png_dir / f"{page:03d}.png"
        if path.exists():
            return path.read_bytes()
        with pymupdf.open(self.pdf_path) as doc:
            data = doc[page - 1].get_pixmap(dpi=DPI).tobytes("png")
        _atomic_write(path, data)
        return data
