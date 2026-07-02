## Why

The audit surfaced a set of real behaviour and .NET-correctness issues that are not security-critical
but tax reliability: multi-step deletes that aren't transactional, an N+1 snapshot load, a
sync-over-async block on a potentially multi-hour HTTP call, ignored cancellation tokens, undisposed
per-key semaphores, exception swallowing, and several data-path logic bugs (slug-collision file
deletion, orphaned re-projection rows, blank ability lines, opaque parse failures, mis-attributed
provenance). Alongside them are test-hygiene gaps on these same paths. Grouping them keeps the
"make the data and async paths correct and well-tested" work in one change.

Closes audit findings: **NET-01, NET-02, NET-03, NET-04, NET-05, NET-06, NET-07, NET-11, NET-12,
COR-13, COR-18, COR-19, COR-20, COR-21, COR-01, COR-03, COR-04, COR-07, COR-08, COR-09**.

## What Changes

- **Transactional multi-step writes (NET-01, NET-03):** wrap campaign and hero deletions in a
  transaction (or rely on DB-level cascade).
- **Kill the N+1 (NET-02):** load each hero's latest snapshot in one grouped query.
- **Async I/O correctness (NET-04, NET-06, NET-07):** propagate `CancellationToken` to block
  deletion, make the PDF block extractor async instead of blocking, and batch the projector's
  `SaveChangesAsync`.
- **Resource + error hygiene (NET-05, NET-11):** manage/dispose per-key semaphores; stop swallowing
  transport/deserialization failures in the web-search client.
- **Storage mapping (NET-12):** evaluate `jsonb` (with GIN) vs `text` for the JSON columns and apply
  the chosen mapping deliberately.
- **Data-path logic bugs (COR-13, COR-18, COR-19, COR-20, COR-21):** guard slug-collision canonical
  deletion, propagate choice-set/table removals on re-projection, render ability lines only when
  present, throw descriptive errors on malformed MinerU responses, and attribute computed-value
  provenance correctly.
- **Test hygiene (COR-01, COR-03, COR-04, COR-07, COR-08, COR-09):** remove the dead `Build()` helper,
  use the shared path helper, isolate temp dirs with cleanup, signal async completion instead of
  fixed delays, and tighten weak substring assertions.

## Capabilities

### New Capabilities

- `data-and-async-correctness`: the reliability contract of the persistence and async I/O paths —
  transactional multi-row writes, single-query reads, cancellation propagation, resource disposal,
  descriptive error handling, and the tests that guard them.

### Modified Capabilities

<!-- None; first spec to formalize these reliability requirements. -->

## Impact

- Modified: `Features/Campaigns/CampaignRepository.cs`, `HeroRepository.cs`,
  `Features/Ingestion/BlockIngestionOrchestrator.cs`, `Pdf/StructureBlockExtractor.cs`
  (async signature — affects callers), `EntityExtraction/CanonicalJsonWriter.cs`,
  `Features/Resolution/StructuredFactProjector.cs`, `CharacterResolutionService.cs`,
  `Features/Ingestion/BookDeletionService.cs`, `Pdf/MinerUPdfConverter.cs`,
  `CanonicalText/MonsterCanonicalTextRenderer.cs`, `Features/Search/SearXNGClient.cs`,
  `Infrastructure/Persistence/AppDbContext.cs`, and the named test files.
- Possible migration if JSON columns move to `jsonb`.

## Non-goals

- Rearchitecting the ingestion/extraction orchestration (tracked in the simplification/structure
  change).
- Retrieval/BM25 correctness (tracked in the retrieval change).
