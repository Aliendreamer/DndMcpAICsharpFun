## 1. EntityEnvelope Schema

- [x] 1.1 Add `bool NeedsReview` property (default `false`) to `EntityEnvelope` record in `Domain/Entities/EntityEnvelope.cs`
- [x] 1.2 Update `CanonicalJsonLoader` to deserialize `"needsReview"` from canonical JSON into `NeedsReview`; missing field defaults to `false`
- [x] 1.3 Update `QdrantEntityVectorStore.ToPoint` to write `needs_review` bool payload field; update `FromPayload` to read it back
- [x] 1.4 Verify `EntityMerger.Merge` passes `NeedsReview` through from canonical (it already uses `canonical with { … }` so it's inherited automatically — confirm with a unit test)

## 2. Extraction Prompt + Confidence Field

- [x] 2.1 Add naming-case rule to `ExtractionPromptBuilder`: entity names MUST be title case; ALL-CAPS PDF headings must be converted; list the small words that stay lowercase
- [x] 2.2 Add `confidence` field (`"low" | "medium" | "high"`) to the JSON schema the LLM is asked to produce in `ExtractionPromptBuilder` or the schema builder
- [x] 2.3 Add unit test: rendered prompt contains the naming-case rule text

## 3. Post-Extraction needsReview Derivation

- [x] 3.1 In the entity extraction orchestrator/post-processor, after parsing each LLM entity, set `NeedsReview = true` if `confidence` is `"low"` or `"medium"` (consume and discard `confidence` — do not persist it)
- [x] 3.2 Apply OCR-artifact heuristic to the entity `name` after LLM extraction: flag if (a) `name.isupper() && name.length > 1`, (b) lowercased name matches `\b[a-z] [a-z]\b`, (c) name contains `\.{3,}`, or (d) any single word has >3 upper/lower alternations
- [x] 3.3 Add unit tests: low-confidence entity gets `NeedsReview = true`; high-confidence clean name stays `false`; each heuristic rule fires independently

## 4. Canonical Validation Warning

- [x] 4.1 In `CanonicalValidationService` (or wherever validation runs), after loading each file, count entities with `NeedsReview = true` and append a warning entry `{ file, type: "needs_review", count, message }` to the response
- [x] 4.2 Add unit test: file with 2 `needsReview` entities produces one warning with `count: 2`; file with none produces no warning; validation still returns 200

## 5. Normalization Script

- [x] 5.1 Create `scripts/normalize_canonical_names.py` implementing the D&D title-case algorithm: `str.title()` baseline, lowercase small words except at name start, fix `'S` → `'s`
- [x] 5.2 Add the OCR-artifact heuristic to the script (same four rules as §3.2); names matching heuristic get `"needsReview": true` set; all-caps names that pass heuristic get title-cased
- [x] 5.3 Add `--dry-run` flag that prints planned changes without writing; add `--file` flag to target a single JSON; default processes all files in `data/canonical/`
- [x] 5.4 Add idempotency: script running twice on the same file produces the same result
- [x] 5.5 Write pytest tests for the title-case function and heuristic function (no file I/O needed — test the pure functions)

## 6. Apply Script to Existing Data

- [x] 6.1 Run `python3 scripts/normalize_canonical_names.py --dry-run` and review output; confirm counts match expectations (~234 tce, ~506 dmg14 all-caps conversions)
- [x] 6.2 Run `python3 scripts/normalize_canonical_names.py` to apply changes to `data/canonical/tce.json` and `data/canonical/dmg14.json`
- [x] 6.3 Run `POST /admin/canonical/validate` — confirm 0 errors; warnings list `needs_review` counts for tce and dmg14
- [x] 6.4 Re-ingest both books: `POST /admin/books/1/ingest-entities` and `POST /admin/books/2/ingest-entities`
- [x] 6.5 Spot-check: `GET /retrieval/entities/tce.subclass.circle-of-spores` — name should be `"Circle of Spores"` not `"CIRCLE OF SPORES"`
