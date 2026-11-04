## 1. Extractor package and reader

- [ ] 1.1 Create `Tools/BookExtractor/` Python package (pinned deps: rapidocr 3.4.2, onnxruntime, PyMuPDF, rapidfuzz), pytest setup.
- [ ] 1.2 Page renderer (200 dpi) + RapidOCR reader (EN, PP-OCRv5, CPU threads configurable) with per-page on-disk cache and resume.
- [ ] 1.3 Text-layer reader + per-page quality check; measure the threshold on SRD 5.1 / Basic Rules PDFs.
- [ ] 1.4 Reading order (columns, fragment merge, header/footer removal, hyphen joins) and digit-noise repair; golden-page tests.
- [ ] 1.5 Rules-page classifier (art-only, TOC, index, credits excluded from coverage).

## 2. Parsers and checks

- [ ] 2.1 Monster parser (Armor Class anchor, size line, same-page name, variants) with the monster checks.
- [ ] 2.2 Spell parser (wrapped casting times) with the spell checks.
- [ ] 2.3 Magic item parser (two-line names, artifacts section) with the item checks.
- [ ] 2.4 Common checks (name found, text, numeric noise, unique id), provenance boxes, id slugs, canonical v2 output, recognition report.
- [ ] 2.5 Dev scorer CLI against 5etools (tag expansion, variant collapse); test it never runs in the service.

## 3. Extractor service and container

- [ ] 3.1 HTTP API: submit book, poll progress, fetch result, fetch page image (cached).
- [ ] 3.2 Dockerfile with OCR models baked in; `book-extractor` service in both compose files with read-only `books/` mount and CPU limit; fix `books/canonical` ownership.

## 4. App integration

- [ ] 4.1 `EntityEnvelope` `checks` + `provenance`; `CanonicalJsonSchema` "2"; loader accepts "1" and "2"; regenerate schemas.
- [ ] 4.2 Extractor HTTP client + separate queue lane in `IngestionQueueWorker`.
- [ ] 4.3 Overrides file model, merge (with drift and orphan handling) and disposition from checks.
- [ ] 4.4 Run flow: on success delete old canonical + sidecars + book's `dnd_entities` points, write file + report, ingest Accepted; on failure change nothing.
- [ ] 4.5 `POST /admin/books/{id}/extract-programmatic`; update `.http`, Insomnia, CLAUDE.md (lint clean).

## 5. Admin review

- [ ] 5.1 `User.IsAdmin` + EF migration + `Admin` authorization policy.
- [ ] 5.2 Review service: list flagged/all, override field, approve (rewrite overrides, re-apply, upsert/remove one point).
- [ ] 5.3 Blazor `/review` page: list, entity view with editable fields and failed reasons, page image with boxes.

## 6. Live runs and verification

- [ ] 6.1 Full build + full test suite green.
- [ ] 6.2 Run MM, PHB, DMG one at a time (CPU only); dev scorer meets or beats the spike numbers; record results in `report.md`.
- [ ] 6.3 Live check: a flagged entity is absent from chat; approving it on the admin page makes it retrievable.
