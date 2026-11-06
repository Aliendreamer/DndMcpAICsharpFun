import subprocess
import sys
import time
from pathlib import Path

import pymupdf
import pytest
from fastapi.testclient import TestClient

from bookextractor import service
from bookextractor.model import load_ocr_page

FIX = Path(__file__).parent / "fixtures" / "ocr" / "mm"


class FakeReader:
    gate = None

    def __init__(self, pdf, cache, threads=4, processes=2, **_):
        self.pdf, self.cache = pdf, cache

    def read(self, progress=None):
        if FakeReader.gate:
            FakeReader.gate.wait(5)
        pages = [load_ocr_page(FIX / "167.json", 167)] if (FIX / "167.json").exists() else []
        if progress:
            progress(len(pages), len(pages))
        return pages

    def render_png(self, page):
        return BookReaderReal(self.pdf, self.cache).render_png(page)


BookReaderReal = service.BookReader


@pytest.fixture
def client(tmp_path, monkeypatch):
    books = tmp_path / "books"
    books.mkdir()
    doc = pymupdf.open()
    doc.new_page(width=200, height=200)
    doc.save(books / "a.pdf")
    monkeypatch.setattr(service, "BookReader", FakeReader)
    FakeReader.gate = None
    return TestClient(service.create_app(books, tmp_path / "cache"))


def wait_done(c, job_id):
    for _ in range(100):
        s = c.get(f"/jobs/{job_id}").json()
        if s["status"] in ("done", "failed"):
            return s
        time.sleep(0.05)
    raise AssertionError("job did not finish")


def test_health(client):
    assert client.get("/health").status_code == 200


def test_job_lifecycle(client):
    r = client.post("/jobs", json={"pdfPath": "a.pdf", "bookSlug": "mm14", "types": ["Monster"]})
    assert r.status_code == 202
    job = r.json()["jobId"]
    s = wait_done(client, job)
    assert s["status"] == "done" and s["pagesDone"] == s["pagesTotal"]
    body = client.get(f"/jobs/{job}/result").json()
    assert set(body) == {"entities", "report"}
    assert any(e["id"] == "mm14.monster.goblin" for e in body["entities"])


def test_result_404_until_done(client):
    FakeReader.gate = __import__("threading").Event()
    job = client.post("/jobs", json={"pdfPath": "a.pdf", "bookSlug": "mm14"}).json()["jobId"]
    assert client.get(f"/jobs/{job}/result").status_code == 404
    FakeReader.gate.set()
    wait_done(client, job)
    assert client.get("/jobs/nope").status_code == 404


def test_path_traversal_rejected(client):
    for p in ("../etc/passwd", "/etc/passwd", "missing.pdf"):
        assert client.post("/jobs", json={"pdfPath": p, "bookSlug": "x"}).status_code == 400
    assert client.get("/page-image", params={"pdfPath": "../etc/passwd", "page": 1}).status_code == 400


def test_second_job_conflict(client):
    import threading
    FakeReader.gate = threading.Event()
    first = client.post("/jobs", json={"pdfPath": "a.pdf", "bookSlug": "mm14"})
    assert first.status_code == 202
    assert client.post("/jobs", json={"pdfPath": "a.pdf", "bookSlug": "mm14"}).status_code == 409
    FakeReader.gate.set()
    wait_done(client, first.json()["jobId"])
    assert client.post("/jobs", json={"pdfPath": "a.pdf", "bookSlug": "mm14"}).status_code == 202


def test_failed_job_reports_error(client, monkeypatch):
    def boom(self, progress=None):
        raise RuntimeError("bad pdf")
    monkeypatch.setattr(FakeReader, "read", boom)
    job = client.post("/jobs", json={"pdfPath": "a.pdf", "bookSlug": "x"}).json()["jobId"]
    s = wait_done(client, job)
    assert s["status"] == "failed" and "bad pdf" in s["error"]


def test_page_image_png(client):
    r = client.get("/page-image", params={"pdfPath": "a.pdf", "page": 1})
    assert r.status_code == 200 and r.headers["content-type"] == "image/png"
    assert r.content[:4] == b"\x89PNG"
    assert client.get("/page-image", params={"pdfPath": "a.pdf", "page": 9}).status_code == 400


def test_service_never_imports_score():
    out = subprocess.run([sys.executable, "-c",
                          "import sys, bookextractor.service; print('bookextractor.score' in sys.modules)"],
                         capture_output=True, text=True, check=True)
    assert out.stdout.strip() == "False"


def test_default_types_include_character_types():
    for t in ("Class", "Subclass", "Race", "Subrace", "Feat", "Background"):
        assert t in service.ALL_TYPES


def test_default_types_include_equipment_and_hazard_types():
    for t in ("Weapon", "Armor", "Item", "VehicleMount", "Object", "Trap", "DiseasePoison", "Condition"):
        assert t in service.ALL_TYPES
