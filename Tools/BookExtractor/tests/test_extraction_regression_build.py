import json
import shutil
from pathlib import Path

import pymupdf

from bookextractor.reader import pdf_sha256
from extraction_regression_build import build

EMPTY = {"pages": {}, "entries": []}

FIX = Path(__file__).parent / "fixtures" / "ocr" / "mm" / "167.json"
HASH = "abc123"


def _entity(eid="mm14:monster:x", page=167, text="orig", fields=None):
    return {"id": eid, "type": "monster", "name": "X", "page": page, "canonicalText": text,
            "fields": fields or {"ac": 10}, "checks": [], "provenance": {"pages": [{"page": page, "boxes": []}]}}


def _setup(tmp_path, entities, overrides=None, approvals=None, with_ocr=True):
    books = tmp_path / "books"
    (books / "canonical").mkdir(parents=True)
    (books / "canonical" / "mm14.json").write_text(json.dumps(
        {"schemaVersion": "2", "book": {"fileHash": HASH}, "entities": entities}))
    (books / "canonical" / "mm14.overrides.json").write_text(json.dumps(
        {"overrides": overrides or [], "approvals": approvals or []}))
    (books / "canonical" / "mm14.report.json").write_text("{}")
    cache = tmp_path / "cache"
    if with_ocr:
        d = cache / HASH / "ocr"
        d.mkdir(parents=True)
        shutil.copy(FIX, d / "167.json")
    return books, cache


def _ov(eid, field, value):
    return {"entityId": eid, "field": field, "value": value, "extracted": "e", "reason": "r", "at": "t"}


def test_override_value_is_expected(tmp_path):
    books, cache = _setup(tmp_path, [_entity()], overrides=[
        _ov("mm14:monster:x", "canonicalText", "fixed"), _ov("mm14:monster:x", "fields.ac", 15)])
    out, skipped = build(books, cache, EMPTY)
    entries = out["entries"]
    assert skipped == []
    e = entries[0]
    assert e["expected"] == {"canonicalText": "fixed", "fields": {"ac": 15}}
    assert e["overridden"] == ["canonicalText", "fields.ac"]
    assert e["pages"] == [167] and e["ctx_pages"] == [167]


def test_approved_entity_expects_extraction(tmp_path):
    books, cache = _setup(tmp_path, [_entity()], approvals=[{"entityId": "mm14:monster:x", "reason": "r", "at": "t"}])
    out, _ = build(books, cache, EMPTY)
    entries = out["entries"]
    assert entries[0]["expected"]["canonicalText"] == "orig"
    assert entries[0]["overridden"] == []


def test_existing_entry_is_never_rewritten(tmp_path):
    books, cache = _setup(tmp_path, [_entity()], overrides=[_ov("mm14:monster:x", "canonicalText", "new")])
    old = {"id": "mm14:monster:x", "expected": {"canonicalText": "old", "fields": {}}}
    existing = {"pages": {"mm14:5": {"number": 5}}, "entries": [old]}
    out, _ = build(books, cache, existing)
    assert out == existing


def test_missing_page_is_skipped_and_named(tmp_path):
    books, cache = _setup(tmp_path, [_entity()], overrides=[_ov("mm14:monster:x", "name", "Y")], with_ocr=False)
    out, skipped = build(books, cache, EMPTY)
    assert out["entries"] == []
    assert any("mm14:monster:x" in s for s in skipped)


def test_unknown_entity_is_skipped_and_named(tmp_path):
    books, cache = _setup(tmp_path, [_entity()], overrides=[_ov("mm14:monster:gone", "name", "Y")])
    out, skipped = build(books, cache, EMPTY)
    assert out["entries"] == [] and any("mm14:monster:gone" in s for s in skipped)


def test_layer_page_read_from_pdf(tmp_path):
    doc = pymupdf.open()
    doc.new_page().insert_text((72, 72), "Hello fixture")
    pdf = tmp_path / "books" / "b.pdf"
    (tmp_path / "books").mkdir()
    doc.save(pdf)
    h = pdf_sha256(pdf)
    books = tmp_path / "books"
    (books / "canonical").mkdir()
    ent = _entity(page=1)
    (books / "canonical" / "mm14.json").write_text(json.dumps(
        {"schemaVersion": "2", "book": {"fileHash": h}, "entities": [ent]}))
    (books / "canonical" / "mm14.overrides.json").write_text(json.dumps(
        {"overrides": [], "approvals": [{"entityId": ent["id"], "reason": "r", "at": "t"}]}))
    out, skipped = build(books, tmp_path / "nocache", EMPTY)
    assert skipped == []
    page = out["pages"]["mm14:1"]
    assert out["entries"][0]["ctx_pages"] == [1] and page["source"] == "layer"
    assert any("Hello" in l["text"] for l in page["lines"])


def test_auto_verified_only_entity_is_not_a_candidate(tmp_path):
    books, cache = _setup(tmp_path, [_entity()], overrides=[
        {**_ov("mm14:monster:x", "name", "Y"), "reason": "Verified against 5etools: ok"}])
    out, skipped = build(books, cache, EMPTY)
    assert out["entries"] == [] and skipped == []


def test_context_expands_one_page_each_side(tmp_path):
    books, cache = _setup(tmp_path, [_entity()], approvals=[{"entityId": "mm14:monster:x", "reason": "r", "at": "t"}])
    for n in (166, 168, 169):
        shutil.copy(FIX, cache / HASH / "ocr" / f"{n}.json")
    out, _ = build(books, cache, EMPTY)
    e = out["entries"][0]
    assert e["pages"] == [167] and e["ctx_pages"] == [166, 167, 168]
    assert set(out["pages"]) == {"mm14:166", "mm14:167", "mm14:168"}


def test_provenance_pages_are_unioned(tmp_path):
    ent = _entity()
    ent["provenance"]["pages"].append({"page": 168, "boxes": []})
    books, cache = _setup(tmp_path, [ent], approvals=[{"entityId": ent["id"], "reason": "r", "at": "t"}])
    shutil.copy(FIX, cache / HASH / "ocr" / "168.json")
    out, _ = build(books, cache, EMPTY)
    assert out["entries"][0]["pages"] == [167, 168]


def test_entity_without_pages_is_skipped_and_named(tmp_path):
    ent = _entity()
    ent["page"] = None
    ent["provenance"] = None
    books, cache = _setup(tmp_path, [ent], approvals=[{"entityId": ent["id"], "reason": "r", "at": "t"}])
    out, skipped = build(books, cache, EMPTY)
    assert out["entries"] == [] and any(ent["id"] in s for s in skipped)
