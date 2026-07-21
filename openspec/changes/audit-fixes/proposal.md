## Why

A six-dimension full backend audit (2026-07-22, reports in `.superpowers/sdd/audit/`) graded the codebase FEASIBLE (AMBER→GREEN) but found: two HIGH bugs in the newest extraction layers (resume silently skips decline-recovery; Object-rescue entities get `monster.*` ids), a correctness risk in the resolver (`EndsWith` cross-book collision returns wrong-book data with "ok" confidence), registry drift (3 extracted books unwired from `BookCatalog`; 2 tools missing from the SEC-08 guard list; `.http` contract drift), and a set of hardening/housekeeping items (non-atomic sidecar writes, force-overwrite without backup, cache-key without converter version, enqueue TOCTOU, forever-cached MCP client init failure, missing FK indexes, BM25 ingest N+1, unhonored rerank flags, orphaned ChatTurns, untested retry policy). The user directed: fix everything found.

## What Changes

All P0/P1/P2/P3 items from `FULL-AUDIT.md`, grouped:

- **P0 bugs:** resume-safe decline-recovery collection (derive recovery input from the decline audits, not only freshly-looped candidates); honest ids for ForceType entities (id derives from the forced type even without a 5etools canonical name); resolver multi-match on `EndsWith` → `needsReview` instead of arbitrary wrong-book pick.
- **P1 drift:** register MPMM/MTF/SCAG in `BookCatalog` (+ setting scope where applicable); extend the scope-health guard with an unknown-`source_key` drift warning; add `resolve_character_feature`/`check_multiclass` to the SEC-08 guard filter; sync `.http` + insomnia (add `list_entities`, `search_dnd`).
- **P2 hardening:** compose `stop_grace_period`; atomic tmp+rename sidecar writes; `force=true` auto-backup of the existing canonical; converter-version discriminator in the conversion cache key; ingestion-queue enqueue dedupe (409 on duplicate); MCP loopback client retry-with-backoff instead of caching a failed init; global exception handler; rate limiting on `/admin` + `/mcp`.
- **P3 housekeeping:** BM25 term-lookup batching; `FusedRetrievalService` honors rerank flags; canonical-loader mtime cache; migration adding FK indexes (`Campaigns.UserId`, `Heroes.CampaignId`, `HeroSnapshots.HeroId`); ChatTurns campaign-scoped cleanup on campaign delete; `HeroRepository.DeleteAsync` scoping; `CharacterSheet` JSON-converter corruption guard; `ExtractionRetryPolicy` retry-branch tests; orchestrator cancellation/crash-mid-loop test.

**Explicitly deferred with reasons** (documented, not silently dropped): orchestrator full/errors-only DRY consolidation (a behavior-preserving refactor that must not ride along with bug fixes); ONNX reranker batching (performance-neutral at pool=20); config-file key cleanup + persona injection note (Config/ is git-crypt/sandbox-blocked); test-directory reorg (churn); grounding judge tier enablement (by design); BM25 hash width (informational).

## Capabilities

### New Capabilities
- `backend-audit-hardening`: the correctness/safety invariants this batch adds — resume-complete decline-recovery, type-honest entity ids, ambiguity-safe table resolution, catalog-drift detection, duplicate-enqueue protection, and data-safety backups for destructive re-extraction.

### Modified Capabilities
<!-- Fixes restore intended behavior of existing capabilities; no existing spec's requirements change direction. -->

## Impact

- Extraction: `EntityExtractionOrchestrator`, `ExtractionEntityIds`, `SidecarJsonFileWriter`, `PdfConversionDiskCache`, extraction endpoint/queue (`BooksAdminEndpoints`, `IIngestionQueue`/worker).
- Resolution/Retrieval: `CharacterResolutionService`, `BookCatalog`, `ScopeHealthCheck`, `FusedRetrievalService`, `Bm25CorpusStatsStore`, `CanonicalJsonLoader`.
- Chat/host: `McpToolsProvider`, `Program`/`Extensions` (exception handler, rate limiting), `DndChatServiceTests` guard list.
- Persistence: one additive migration (3 indexes), `CampaignRepository` cascade, `HeroRepository`, `AppDbContext` converter guard.
- Docs/contracts: `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json`; compose files.
- No new HTTP endpoint. No behavior change to the main extraction gate.
