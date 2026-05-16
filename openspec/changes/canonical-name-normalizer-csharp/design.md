## Context

The canonical name normalization logic was delivered as a Python script (`scripts/normalize_canonical_names.py`). The project is pure C# (.NET 10). The script duplicates heuristic logic already in `ExtractionNeedsReview.cs` and requires Python/pytest as an external dependency. The existing admin feature surface (`Features/Admin/`) already has the validation and type-fixer pattern to follow.

## Goals / Non-Goals

**Goals:**

- Implement `CanonicalNameNormalizerService` in C# that replicates the Python script's behaviour exactly
- Expose `POST /admin/canonical/normalize` with `?dryRun=true` support
- Reuse `ExtractionNeedsReview.HasOcrArtifacts` for the heuristic — no duplication
- Delete `scripts/normalize_canonical_names.py` and `tests/test_normalize_names.py`
- xUnit test coverage for the normalizer service

**Non-Goals:**

- Changing `EntityEnvelope`, Qdrant payload, or the extraction pipeline
- Streaming progress (a synchronous response with counts is sufficient)
- Any UI or scheduled execution

## Decisions

**D1 — Reuse `ExtractionNeedsReview.HasOcrArtifacts` directly**
The heuristic (all-caps, split-word, noise, case-alternations) already exists in C#. The normalizer calls it instead of reimplementing it. Keeps the two in sync automatically.

**D2 — D&D title-case as a static helper on `CanonicalNameNormalizerService`**
`DndTitleCase(string name)` is a pure function: small-words list, apostrophe-S fix, capitalize after hyphen. No interface needed — tested directly on the service's static method or via the service itself.

**D3 — Read/write via `CanonicalJsonLoader` + raw `JsonDocument` mutation**
`CanonicalJsonLoader` returns typed `EntityEnvelope` records. The normalizer needs to mutate the underlying JSON (name field + needsReview field) and write it back. Pattern: load with `CanonicalJsonLoader`, mutate each entity's fields, serialize back with the same `JsonSerializerOptions` used elsewhere (`JsonSerializerDefaults.Web`).

**D4 — Response mirrors Python script output**
```json
{ "filesScanned": 2, "totalEntities": 1031,
  "changes": [
    { "file": "tce.json", "titleCased": 226, "flagged": 156, "unchanged": 22 }
  ],
  "dryRun": true }
```

**D5 — Follow `CanonicalTypeFixerEndpoints` / `CanonicalValidationEndpoints` pattern**
New files: `CanonicalNameNormalizerService.cs`, `CanonicalNameNormalizerEndpoints.cs`, `CanonicalNameNormalizerReport.cs`. Registered in `Program.cs` alongside the other canonical admin endpoints.

## Risks / Trade-offs

- **In-place JSON rewrite changes formatting** — `JsonSerializer` with `WriteIndented: true` will reformat any hand-authored JSON. Acceptable: the Python script had the same behaviour and the data files are already committed post-normalization.
- **Idempotency** — Running twice must produce the same output. Guaranteed because `HasOcrArtifacts` is deterministic and `DndTitleCase` on an already-title-cased name returns it unchanged.
