import json
import shutil
from pathlib import Path

import numpy as np
import pymupdf
import pytest

from bookextractor import reader as reader_mod
from bookextractor.pages import classify_page
from bookextractor.reader import BookReader, layer_quality, pdf_sha256

FIX = Path(__file__).parent / "fixtures" / "ocr" / "mm"


class _Result:
    def __init__(self, boxes=None, txts=(), scores=()):
        self.boxes, self.txts, self.scores = boxes, txts, scores


def _blank_pdf(path: Path, pages: int) -> Path:
    doc = pymupdf.open()
    for _ in range(pages):
        doc.new_page(width=300, height=300)
    doc.save(path)
    return path


def _seed(cache: Path, pdf: Path, numbers: list[int]) -> None:
    out = cache / pdf_sha256(pdf) / "ocr"
    out.mkdir(parents=True)
    for n in numbers:
        shutil.copy(FIX / "167.json", out / f"{n:03d}.json")


def test_cache_hit_skips_ocr(tmp_path):
    pdf = _blank_pdf(tmp_path / "b.pdf", 1)
    _seed(tmp_path / "cache", pdf, [1])

    def boom():
        raise AssertionError("OCR engine must not be created")

    pages = BookReader(pdf, tmp_path / "cache", processes=1, engine_factory=boom).read()
    assert len(pages) == 1 and pages[0].source == "ocr" and pages[0].number == 1
    assert len(pages[0].lines) > 10


def test_resume_after_interrupt(tmp_path):
    pdf = _blank_pdf(tmp_path / "b.pdf", 3)
    _seed(tmp_path / "cache", pdf, [1, 2])
    calls = []

    def factory():
        def eng(img):
            calls.append(img.shape)
            return _Result()
        return eng

    progress = []
    pages = BookReader(pdf, tmp_path / "cache", processes=1, engine_factory=factory).read(
        lambda d, t: progress.append((d, t)))
    assert len(calls) == 1
    assert [p.number for p in pages] == [1, 2, 3]
    assert progress[-1] == (3, 3)
    saved = json.loads((tmp_path / "cache" / pdf_sha256(pdf) / "ocr" / "003.json").read_text())
    assert set(saved) == {"w", "h", "lines"}
    assert not list((tmp_path / "cache").rglob("*.tmp"))


def test_blank_page_no_crash(tmp_path):
    pdf = _blank_pdf(tmp_path / "b.pdf", 1)
    factory = lambda: (lambda img: _Result(boxes=None))
    pages = BookReader(pdf, tmp_path / "cache", processes=1, engine_factory=factory).read()
    assert pages[0].lines == []
    assert classify_page(pages[0]) == "blank"


def test_ocr_result_saved_in_spike_format(tmp_path):
    pdf = _blank_pdf(tmp_path / "b.pdf", 1)
    box = np.array([[1, 2], [50, 2], [50, 20], [1, 20]], dtype=np.float32)
    factory = lambda: (lambda img: _Result(np.array([box]), ("Hello",), (0.9,)))
    pages = BookReader(pdf, tmp_path / "cache", processes=1, engine_factory=factory).read()
    assert pages[0].lines[0].text == "Hello"
    saved = json.loads((tmp_path / "cache" / pdf_sha256(pdf) / "ocr" / "001.json").read_text())
    assert saved["lines"][0] == {"box": [[1.0, 2.0], [50.0, 2.0], [50.0, 20.0], [1.0, 20.0]],
                                 "t": "Hello", "s": pytest.approx(0.9)}


def _text_pdf(path: Path) -> Path:
    doc = pymupdf.open()
    page = doc.new_page()
    y = 80
    for line in ["The goblin attacks the weary traveller", "with a rusty shortsword and a sneer",
                 "Armor Class 15 and Hit Points 7", "Wizards cast spells from their books"]:
        page.insert_text((72, y), line, fontsize=12)
        y += 20
    doc.save(path)
    return path


def test_layer_used_when_quality_passes(tmp_path):
    pdf = _text_pdf(tmp_path / "t.pdf")

    def boom():
        raise AssertionError("OCR must not run")

    assert layer_quality(pymupdf.open(pdf)[0]) > 0.8
    pages = BookReader(pdf, tmp_path / "cache", processes=1, engine_factory=boom).read()
    assert pages[0].source == "layer"
    assert pages[0].lines[0].text.startswith("The goblin")
    assert pages[0].width == round(595 * 200 / 72)
    assert pages[0].height == round(842 * 200 / 72)


def test_layer_quality_low_for_garbage(tmp_path):
    doc = pymupdf.open()
    page = doc.new_page()
    page.insert_text((72, 100), "xz qk bcd 12 ~~ ## fgh jkl 99 pqr", fontsize=12)
    assert layer_quality(page) < 0.2
    assert layer_quality(pymupdf.open().new_page()) == 0.0


def test_render_png_cached(tmp_path):
    pdf = _blank_pdf(tmp_path / "b.pdf", 1)
    r = BookReader(pdf, tmp_path / "cache", processes=1)
    data = r.render_png(1)
    assert data[:8] == b"\x89PNG\r\n\x1a\n"
    assert (tmp_path / "cache" / pdf_sha256(pdf) / "png" / "001.png").read_bytes() == data
    assert r.render_png(1) == data


def _stub_engine(img):
    return _Result(np.array([[[1, 2], [50, 2], [50, 20], [1, 20]]], dtype=np.float32), ("Hi",), (0.9,))


def stub_factory():
    return _stub_engine


def failing_factory():
    def eng(img):
        raise RuntimeError("engine failure")
    return eng


def test_multiprocess_pool_path(tmp_path):
    pdf = _blank_pdf(tmp_path / "b.pdf", 4)
    seen = []
    pages = BookReader(pdf, tmp_path / "cache", threads=1, processes=2, engine_factory=stub_factory).read(
        lambda d, t: seen.append((d, t)))
    assert [p.number for p in pages] == [1, 2, 3, 4]
    assert all(p.source == "ocr" and p.lines[0].text == "Hi" for p in pages)
    assert seen[-1] == (4, 4)
    assert len(list((tmp_path / "cache" / pdf_sha256(pdf) / "ocr").glob("*.json"))) == 4


def test_worker_error_surfaces(tmp_path):
    from concurrent.futures.process import BrokenProcessPool  # noqa: F401
    pdf = _blank_pdf(tmp_path / "b.pdf", 3)
    r = BookReader(pdf, tmp_path / "cache", threads=1, processes=2, engine_factory=failing_factory)
    with pytest.raises(RuntimeError, match="engine failure"):
        r.read()


def test_threads_honoured_in_process(tmp_path, monkeypatch):
    pdf = _blank_pdf(tmp_path / "b.pdf", 1)
    got = []
    def factory():
        import os
        got.append(os.environ["BOOKEXTRACTOR_OCR_THREADS"])
        return lambda img: _Result()
    BookReader(pdf, tmp_path / "cache", threads=3, processes=1, engine_factory=factory).read()
    assert got == ["3"]
    monkeypatch.delenv("BOOKEXTRACTOR_OCR_THREADS", raising=False)


def test_scan_page_layer_is_capped(tmp_path):
    pix = pymupdf.Pixmap(pymupdf.csRGB, pymupdf.IRect(0, 0, 100, 100), False)
    pix.set_rect(pix.irect, (255, 255, 255))
    doc = pymupdf.open()
    page = doc.new_page(width=300, height=300)
    page.insert_image(page.rect, pixmap=pix)
    page.insert_text((20, 100), "The goblin attacks the weary traveller with a rusty sword", fontsize=8)
    assert layer_quality(page) <= reader_mod.SCAN_LAYER_CAP < reader_mod.LAYER_QUALITY_THRESHOLD
