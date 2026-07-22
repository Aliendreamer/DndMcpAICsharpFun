# Tasks — audit-fixes (every P0–P3 item from FULL-AUDIT.md; deferred list in proposal)

## 1. P0 bugs
- [ ] 1.1 Resume-complete decline-recovery: recovery input derived from `declined` + `extraction_declined` audits at phase start (dedup by recorded id); tests incl. a resumed-run scenario (checkpointed decline recovered).
- [ ] 1.2 Honest ForceType ids in `ExtractionEntityIds.RecordedEntityId` (forced type, canonical-name-or-display-name) + dedicated `ExtractionEntityIdsTests`.
- [ ] 1.3 Resolver ambiguity → `needsReview` on multi-match in `ResolveClassFeaturesAsync`/`ResolveSubclassSpellsAsync` + tests (2-book collision seeded).

## 2. P1 drift
- [ ] 2.1 `BookCatalog` + MPMM/MTF/SCAG (verbatim display names); SCAG→Forgotten Realms setting scope if present; `ScopeHealthCheck` unknown-`source_key` warning; tests.
- [ ] 2.2 SEC-08 guard list += `resolve_character_feature`, `check_multiclass`; `.http` + insomnia += `list_entities`, `search_dnd`.

## 3. P2 hardening
- [ ] 3.1 Data safety: atomic `SidecarJsonFileWriter`; `force=true` rolling `.bak` (+ gitignore); `ConverterVersion` in the conversion-cache key; tests.
- [ ] 3.2 Queue dedupe (`TryEnqueue` + 409) + tests.
- [ ] 3.3 `McpToolsProvider` retry-with-backoff on failed init + test.
- [ ] 3.4 Host: global exception handler (ProblemDetails), `/admin`+`/mcp` rate limits, compose `stop_grace_period: 90s`; endpoint-contract check.

## 4. P3 housekeeping
- [ ] 4.1 BM25 term batching + tests; canonical-loader mtime cache + test.
- [ ] 4.2 `FusedRetrievalService` honors rerank flags + tests.
- [ ] 4.3 Persistence: additive migration (3 FK indexes, dev-flow gates); ChatTurns campaign cascade + test; `HeroRepository.DeleteAsync` scoping + test; `CharacterSheet` converter corruption guard + test.
- [ ] 4.4 Tests-only: `ExtractionRetryPolicy` retry/backoff/cancellation coverage; orchestrator cancellation/crash-mid-loop test (Reground pattern).

## 5. Gates
- [ ] 5.1 Full suite green after EVERY task; final: build 0/0, full `dotnet test`, format clean on touched files, `.http`/insomnia synced, security pass on middleware/endpoint diffs.
