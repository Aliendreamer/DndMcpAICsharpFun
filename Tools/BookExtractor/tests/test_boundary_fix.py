import json
from pathlib import Path

import pytest

from bookextractor import boundary_fix

BODY = [f"The old keep stands upon hill number {w} and watches the valley below." for w in
        ("one", "two", "three", "four", "five", "six")]
TAIL_SHORT = [f"Merchants of the second town trade fur {w} every autumn." for w in ("alpha", "beta")]
TAIL_LONG = [f"Merchants of the second town trade fur {w} every autumn season." for w in
             ("alpha", "beta", "gamma", "delta", "epsilon", "zeta", "eta", "theta", "iota", "kappa", "lambda", "mu")]
HASH = "ab" * 32


def _page(root: Path, n: int, tail: list[str]):
    rows = [("Zorgkeep", 40)] + [(t, 20) for t in BODY] + [("Other Town", 40)] + [(t, 20) for t in tail]
    lines, y = [], 100
    for t, h in rows:
        lines.append({"box": [[100, y], [800, y], [800, y + h], [100, y + h]], "t": t, "s": 0.99})
        y += h + 15
    d = root / HASH / "ocr"
    d.mkdir(parents=True, exist_ok=True)
    (d / f"{n:03d}.json").write_text(json.dumps({"w": 1000, "h": 2000, "lines": lines}))


def _entity(eid, tail, checks=None, **kw):
    entries = [" ".join(BODY[:3]), " ".join(BODY[3:]), "Other Town", " ".join(tail)]
    e = {"id": eid, "type": "Location", "name": "Zorgkeep", "disposition": "Accepted", "needsReview": True,
         "canonicalText": "\n".join(["Zorgkeep"] + entries), "fields": {"entries": entries},
         "checks": checks if checks is not None else [{"name": "text-complete", "passed": False, "reason": "x"}],
         "provenance": {"pages": [{"page": 5, "boxes": []}]}}
    e.update(kw)
    return e


@pytest.fixture
def env(tmp_path, monkeypatch):
    monkeypatch.setattr(boundary_fix, "line_shades", lambda pdf, p, boxes, **k: [0.0] * len(boxes))
    canon, books, ocr = tmp_path / "canonical", tmp_path / "books", tmp_path / "ocr"
    canon.mkdir()
    books.mkdir()

    def make(ents, slug="bk", tail=TAIL_SHORT, overrides=None, page=True):
        if page:
            _page(ocr, 5, tail)
        (canon / f"{slug}.json").write_text(json.dumps({"schemaVersion": "2", "book": {"fileHash": HASH},
                                                        "entities": ents}))
        if overrides:
            (canon / f"{slug}.overrides.json").write_text(json.dumps(overrides))
        return canon, books, ocr
    return make


def _ov(canon, slug="bk"):
    p = canon / f"{slug}.overrides.json"
    return json.loads(p.read_text()) if p.exists() else None


def test_trim_writes_both_fields(env):
    canon, books, ocr = env([_entity("bk.location.zorgkeep", TAIL_SHORT)])
    r = boundary_fix.run(canon, books, ocr, None, False)["bk"]
    assert (r["candidates"], r["trimmed"]) == (1, 1), r
    ov = _ov(canon)["overrides"]
    by = {o["field"]: o for o in ov}
    assert set(by) == {"fields.entries", "canonicalText"}
    new = by["fields.entries"]["value"]
    assert "Other Town" not in " ".join(new) and by["fields.entries"]["extracted"][2] == "Other Town"
    assert by["canonicalText"]["value"] == "\n".join(["Zorgkeep"] + new)
    assert r["no-pdf-shade"] == 1


def test_approves_small_trim(env):
    canon, books, ocr = env([_entity("bk.location.zorgkeep", TAIL_SHORT)])
    r = boundary_fix.run(canon, books, ocr, None, False)["bk"]
    assert (r["approved"], r["flagged"]) == (1, 0)
    a = _ov(canon)["approvals"]
    assert a[0]["entityId"] == "bk.location.zorgkeep" and a[0]["waivedChecks"] == ["text-complete"]


def test_large_cut_not_approved(env):
    canon, books, ocr = env([_entity("bk.location.zorgkeep", TAIL_LONG)], tail=TAIL_LONG)
    r = boundary_fix.run(canon, books, ocr, None, False)["bk"]
    assert (r["trimmed"], r["approved"], r["flagged"]) == (1, 0, 1)
    assert _ov(canon)["approvals"] == [] and len(_ov(canon)["overrides"]) == 2


def test_other_failing_check_blocks_approval(env):
    checks = [{"name": "text-complete", "passed": False, "reason": ""}, {"name": "type-fit", "passed": False, "reason": ""}]
    canon, books, ocr = env([_entity("bk.location.zorgkeep", TAIL_SHORT, checks=checks)])
    r = boundary_fix.run(canon, books, ocr, None, False)["bk"]
    assert (r["trimmed"], r["approved"], r["flagged"]) == (1, 0, 1)


def test_skips_approved_and_overridden(env):
    ents = [_entity("bk.location.a", TAIL_SHORT), _entity("bk.location.b", TAIL_SHORT),
            _entity("bk.location.c", TAIL_SHORT)]
    ov = {"overrides": [{"entityId": "bk.location.a", "field": "canonicalText", "value": "v", "extracted": "e"}],
          "approvals": [{"entityId": "bk.location.b", "reason": "r", "at": "t", "waivedChecks": []}]}
    canon, books, ocr = env(ents, overrides=ov)
    r = boundary_fix.run(canon, books, ocr, None, False)["bk"]
    assert r["skipped"] == {"already-overridden": 1, "approved": 1} and r["trimmed"] == 1
    out = _ov(canon)
    assert len(out["overrides"]) == 3 and len(out["approvals"]) == 2  # existing preserved, new appended


def test_skip_books(env):
    canon, books, ocr = env([_entity("sys.location.a", TAIL_SHORT)], slug="system-reference-document")
    assert boundary_fix.run(canon, books, ocr, None, False) == {}
    (canon / "x.report.json").write_text("{}")
    assert boundary_fix.run(canon, books, ocr, ["nope"], False) == {}


def test_missing_ocr_page_skipped_and_counted(env):
    canon, books, ocr = env([_entity("bk.location.zorgkeep", TAIL_SHORT)], page=False)
    r = boundary_fix.run(canon, books, ocr, None, False)["bk"]
    assert r["skipped"] == {"missing-ocr-page": 1} and r["trimmed"] == 0
    assert _ov(canon) is None


def test_render_mismatch_skipped(env):
    e = _entity("bk.location.zorgkeep", TAIL_SHORT)
    e["canonicalText"] += "\nextra"
    canon, books, ocr = env([e])
    assert boundary_fix.run(canon, books, ocr, None, False)["bk"]["skipped"] == {"render-mismatch": 1}


def test_dry_run_writes_nothing(env):
    canon, books, ocr = env([_entity("bk.location.zorgkeep", TAIL_SHORT)])
    r = boundary_fix.run(canon, books, ocr, None, True)["bk"]
    assert r["trimmed"] == 1 and _ov(canon) is None
