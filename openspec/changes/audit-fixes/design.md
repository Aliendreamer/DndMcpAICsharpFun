## Context

Findings, evidence, and per-dimension detail live in `.superpowers/sdd/audit/` (`FULL-AUDIT.md` + six dimension reports). This change implements every fix; the audit reports are the requirement source for each item, and the decisions below settle the fix shapes.

## Goals / Non-Goals

**Goals:** close every P0–P3 audit item with small, independently-reviewable, behavior-targeted fixes; keep the full suite green throughout; no change to the main extraction gate's semantics. **Non-Goals (deferred, per proposal):** orchestrator DRY consolidation; ONNX reranker batching; edits to git-crypt-blocked `Config/` files; test-dir reorg; judge-tier enablement.

## Decisions

- **D1 (P0 recovery-resume):** the decline-recovery phase derives its input from the AUDITS, not only the freshly-looped `declinedCandidates`: at phase start, add every candidate whose recorded id matches a `declined` entry or an `extraction_declined` error entry and that isn't already collected (map candidates→`RecordedEntityId` once). This makes fresh and resumed runs uniform; keep the loop-time collection (cheap) and dedupe by id.
- **D2 (P0 forced-type ids):** `ExtractionEntityIds.RecordedEntityId` derives the id from the FORCED type whenever resolution is `ForceType` — using the 5etools canonical name when present, else the candidate display name. Ids regenerate on the next re-extract (accepted; ids are per-extraction artifacts). Add dedicated `ExtractionEntityIds` unit tests (also closes audit-F's gap).
- **D3 (P0 resolver ambiguity):** in `ResolveClassFeaturesAsync`/`ResolveSubclassSpellsAsync`, fetch up to 2 `EndsWith` matches; >1 → that component resolves `needsReview` ("ambiguous across books"), never an arbitrary pick. Perf of the LIKE scan stays as-is (fine at scale).
- **D4 (P1 catalog):** add MPMM/MTF/SCAG `BookInfo` rows (display names VERBATIM from the live `IngestionRecords`: "Mordenkainen Presents: Monsters of the Multiverse", "Mordenkainen's Tome of Foes", "Sword Coast Adventurer's Guide"). Add SCAG to the Forgotten Realms setting scope if `SettingCatalog` has an FR entry (implementer verifies; if none, catalog-only). Rules/downtime scopes intentionally unchanged (monster books don't belong there).
- **D5 (P1 drift guard):** `ScopeHealthCheck` additionally counts `dnd_blocks` where `source_key` does NOT match any catalog key (a `must_not` match-any filter) and warns with the count — catalog drift now fails loud at startup.
- **D6 (P2 queue dedupe):** `IIngestionQueue` gains `TryEnqueue`-style dedupe on an in-flight/en-queued book-id set (cleared by the worker when a job finishes); the endpoints return 409 on duplicate. No new `IngestionStatus` enum member (avoids the exhaustive-consumer sweep).
- **D7 (P2 data safety):** `force=true` copies the existing canonical to `<slug>.json.bak` (rolling, git-ignored) before overwrite; sidecar writer goes tmp+rename; conversion-cache filename gains a `ConverterVersion` const (bump invalidates old cache by construction).
- **D8 (P2 host):** `app.UseExceptionHandler` + ProblemDetails (no detail leakage); fixed-window rate limits on `/admin` and `/mcp` (generous, single-user-sane); `stop_grace_period: 90s` in both compose files.
- **D9 (P3):** BM25 batch = chunked `WHERE Term IN (...)` load + in-memory merge + one save; canonical-loader cache keyed by (path, LastWriteTimeUtc); converter guard = `try/catch(JsonException)` → empty `CharacterSheet` (visible-but-contained corruption, logged via a static hook); ChatTurns with `CampaignId == id` deleted inside the existing campaign-delete transaction; `HeroRepository.DeleteAsync` gains userId scoping; migration adds the 3 FK indexes (additive-only per dev-flow migration gates).

## Risks / Trade-offs

- Highest-risk edits are again in the orchestrator (D1) — same discipline as the recovery change: adversarial review + both decline paths tested, cancellation/crash coverage added this time.
- D2 changes ids produced by future extractions (not committed data); a re-extract regenerates consistently. Old canonicals keep old ids until re-extracted — acceptable, documented.
- D7 cache-key bump forces one-time re-conversion per book on next extraction (MinerU minutes, not hours).
- Rate limiting/exception handler touch global middleware — full-suite + endpoint-contract checks gate regressions.

## Migration Plan

Land per-task on `main` (each reviewed, suite green). The EF migration follows the additive-only gates (open Up/Down, snapshot pure insertion). No data migration; no re-extraction required by this change.

## Open Questions

None — fix shapes are settled above; implementers verify exact code-level details against the audit reports.
