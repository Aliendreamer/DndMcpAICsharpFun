## Why

The canonical name normalization logic was implemented as a Python script (`scripts/normalize_canonical_names.py`), but the project is pure C#. Python is an unnecessary runtime dependency, the heuristic logic duplicates `ExtractionNeedsReview.cs`, and the one-off script cannot be invoked from the running API without a shell.

## What Changes

- Delete `scripts/normalize_canonical_names.py` and `tests/test_normalize_names.py`
- Add `CanonicalNameNormalizerService` in C# implementing the D&D title-case algorithm and OCR-artifact heuristic, reusing `ExtractionNeedsReview.HasOcrArtifacts`
- Add `POST /admin/canonical/normalize` endpoint with optional `?dryRun=true` query parameter
- Add xUnit tests covering the normalizer service

## Capabilities

### New Capabilities

- `canonical-name-normalizer`: C# service that title-cases ALL-CAPS entity names and flags OCR-garbled names as `needsReview`, exposed via an admin HTTP endpoint with dry-run support

### Modified Capabilities

(none)

## Impact

- Removes Python/pytest from the project entirely
- New admin endpoint documented in `DndMcpAICsharpFun.http` and `dnd-mcp-api.insomnia.json`
- `CanonicalNameNormalizerService` reads/writes canonical JSON files using the existing `CanonicalJsonLoader`
- No change to `EntityEnvelope`, Qdrant payload, or extraction pipeline
