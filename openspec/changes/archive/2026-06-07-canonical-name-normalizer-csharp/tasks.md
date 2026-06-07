## 1. Report record

- [x] 1.1 Create `Features/Admin/CanonicalNameNormalizerReport.cs` with `CanonicalNameNormalizerFileResult(string File, int TitleCased, int Flagged, int Unchanged)` and `CanonicalNameNormalizerReport(int FilesScanned, int TotalEntities, bool DryRun, IReadOnlyList<CanonicalNameNormalizerFileResult> Changes)` records

## 2. Normalizer service

- [x] 2.1 Create `Features/Admin/CanonicalNameNormalizerService.cs` with a private static `DndTitleCase(string name)` method: split on spaces, capitalize each word, lowercase small words (`of the a an in on at to and or but for nor`) except at position 0, capitalize after hyphens, fix `'[A-Z]` → lowercase via regex
- [x] 2.2 Add `NormalizeAsync(bool dryRun, CancellationToken ct)` method: enumerate canonical JSON files (skip `.errors.json` / `.warnings.json` / `.progress*.json` sidecars), load each with `CanonicalJsonLoader`, iterate entities, apply `DndTitleCase` for all-caps names with no other OCR artifacts (reuse `ExtractionNeedsReview.HasOcrArtifacts`), set `needsReview = true` for names with artifacts, write back with `JsonSerializer` (WriteIndented, camelCase) unless `dryRun`, accumulate per-file counts
- [x] 2.3 Register `CanonicalNameNormalizerService` in `Program.cs` (scoped, alongside `CanonicalValidationService`)

## 3. Endpoint

- [x] 3.1 Create `Features/Admin/CanonicalNameNormalizerEndpoints.cs` mapping `POST /admin/canonical/normalize` with optional `?dryRun=true`; inject `CanonicalNameNormalizerService`; return 200 with `CanonicalNameNormalizerReport`
- [x] 3.2 Register the endpoint group in `Program.cs`
- [x] 3.3 Add example request to `DndMcpAICsharpFun.http` and `dnd-mcp-api.insomnia.json`

## 4. Tests

- [x] 4.1 Create `DndMcpAICsharpFun.Tests/Entities/Admin/CanonicalNameNormalizerServiceTests.cs` — unit tests for `DndTitleCase`: simple all-caps, small-word lowercasing, first-word override, apostrophe-S, hyphenated word
- [x] 4.2 Add integration tests: ALL-CAPS entity gets title-cased and `needsReview` stays false; OCR-garbled entity gets `needsReview = true` and name unchanged; clean entity is unchanged; dry-run does not write files; service is idempotent (run twice → same output)

## 5. Remove Python

- [x] 5.1 Delete `scripts/normalize_canonical_names.py`
- [x] 5.2 Delete `tests/test_normalize_names.py`
- [x] 5.3 Verify `dotnet build` and `dotnet test` pass with no Python files present
