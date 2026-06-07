## Why

LLM extraction (qwen3:8b via Docling) captures entity names directly from PDF headings, which are typically rendered in ALL CAPS. This produces canonical JSON files where entity names like `"CIRCLE OF SPORES"` or `"BESTIAL SOUL"` require human correction before the data is usable for search and display. Some entries also carry OCR-related noise (split words like `"f eature"`, garbled casing like `"Gons OF YouR WoRLD"`), which cannot be fixed by case normalization alone.

## What Changes

- Extraction prompt gains a **naming rule**: entity names must be output in proper title case.
- Extraction schema gains a **`confidence` field** (`"low" | "medium" | "high"`) per entity — the LLM self-assesses extraction quality.
- `EntityEnvelope` and canonical JSON gain a **`needsReview` boolean** field (default `false`).
- A post-extraction step sets `needsReview = true` when confidence < `"high"` OR the heuristic detects OCR artifacts in the name.
- `POST /admin/canonical/validate` reports a warning for each file that contains `needsReview: true` entities.
- A one-time normalization script converts all-caps names in existing canonical JSONs to title case, and flags garbled names with `needsReview: true`.

## Capabilities

### New Capabilities

- `canonical-name-normalization`: One-time and on-demand normalization of entity names in canonical JSON files — mechanical title-case conversion for cleanly all-caps names, plus `needsReview` flagging for OCR-garbled entries.

### Modified Capabilities

- `entity-extraction-pipeline`: Extraction prompt adds naming-case rule and `confidence` output field; post-processing step derives `needsReview` from confidence + heuristic.
- `structured-entities`: `EntityEnvelope` and canonical JSON schema add `needsReview: bool`; validation reports entities still flagged.

## Impact

- `Features/Ingestion/EntityExtraction/ExtractionPromptBuilder.cs` — new naming rule + confidence field instruction
- `Features/Ingestion/EntityExtraction/` (extractor/orchestrator) — post-processing to set `needsReview`
- `Domain/Entities/EntityEnvelope.cs` — add `NeedsReview` property
- `Features/Entities/CanonicalJsonLoader.cs` — deserialize `needsReview`
- `Features/Admin/CanonicalValidationService.cs` — warn on `needsReview` count
- `data/canonical/tce.json`, `data/canonical/dmg14.json` — one-time normalization pass
- No API breaking changes; `needsReview` is additive to the canonical JSON schema
