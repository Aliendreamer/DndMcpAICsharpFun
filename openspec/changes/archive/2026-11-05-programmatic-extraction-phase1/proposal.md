## Why

Our structured book data comes from a small local model (qwen3:8b) that invents values. The 2014 Monster Manual file has
CR wrong for 122 of 373 reviewed monsters, and 5etools is bolted on to patch the gaps. A spike on 2026-11-04 showed that
plain code reading the books with CPU OCR beats the model on every measure: MM 419/450 monsters found with AC, HP and CR
at 100%; PHB 357/361 spells; DMG 243/250 magic items. Its self-checks caught every OCR misread it let through. This is
Phase 1 of the programmatic-extraction program (four phases): build the reader and verification, cover monsters,
spells and magic items, and replace the old PHB/MM/DMG data with the new output.

## What Changes

- New **book extractor**: a Python service in its own container (plus a CLI for development scoring) that renders each
  page, runs OCR (RapidOCR, PP-OCRv5 English, CPU only) or uses the PDF text layer when it passes a quality check,
  rebuilds reading order, and parses Monster, Spell and MagicItem entries by rules. No LLM anywhere.
- **Self-checks** on every entity (e.g. HP equals the average of its hit dice, all four spell fields present). An entity
  that passes all checks is accepted automatically. Any failure marks it NeedsReview with a reason per failed check,
  and it stays out of the chat until the admin approves it.
- **Recognition report** per run: share of entities passing all checks and share of rules pages covered. Warning
  below 95%.
- **Canonical envelope fields** `checks` and `provenance` (pages and page boxes); canonical schema version 1 → 2.
- **Overrides file** per book (`books/canonical/<slug>.overrides.json`): admin corrections and approvals, applied on top
  of every run, so re-runs never wipe them. A re-run that changes a value under an override is flagged.
- **Admin review page** in the Blazor UI: list of flagged entities, browse any entity, see its values next to the book
  page image with its region highlighted, override any field with a reason, approve. Guarded by a new `IsAdmin` flag on
  `User`.
- New admin endpoint to queue a programmatic extraction run for a book.
- **BREAKING (data):** the old `phb14`, `mm14` and `dmg14` canonical files and their sidecars are deleted, and those
  books' old points are removed from `dnd_entities`. The new output is the only data. Types the extractor doesn't cover
  yet (classes, races, backgrounds, feats and the rest) are missing from the chat until Phases 2–3. The owner accepted
  this.
- Development scorer (5etools as answer key only, never read by the app) to prove the numbers.

## Capabilities

### New Capabilities

- `programmatic-book-extraction`: the extractor service: page reader (OCR / text layer), reading order and noise repair,
  Monster/Spell/MagicItem parsers, self-checks, recognition report, provenance, the canonical output it produces, and the
  development scorer.
- `entity-verification-review`: how check results set disposition, the overrides file and its merge, the `IsAdmin`
  guard, and the Blazor admin review page (list, browse, page image, override, approve).

### Modified Capabilities

(none. The qwen3 pipeline, 5etools and MinerU stay in place untouched until Phase 4.)

## Impact

- New: `Tools/BookExtractor/` (Python package, Dockerfile, tests), new `book-extractor` service in `docker-compose.yml`
  (and the prod compose) with a read-only `books/` mount and a CPU limit.
- App: an HTTP client for the extractor, a queue lane for long runs (`IngestionQueueWorker`), writing the canonical file
  plus the overrides merge, `EntityEnvelope` (`checks`, `provenance`), `CanonicalJsonSchema.CurrentVersion` "2",
  regenerated `Schemas/canonical/`, `User.IsAdmin` + EF migration, an authorization policy, and a new Blazor admin page.
- `DndMcpAICsharpFun.http` and `dnd-mcp-api.insomnia.json` updated for the new endpoint. CLAUDE.md "Adding a new
  book" gains the new route.
- Data: `books/canonical/{phb14,mm14,dmg14}*.json` replaced. The `books/canonical` files are root-owned in the dev
  container; fix ownership.
- Eval: class cases and the character coach lose their data until Phase 2 (accepted).
