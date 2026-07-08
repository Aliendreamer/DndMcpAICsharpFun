## 1. Dedup key + winner resolver (pure core, TDD)

- [ ] 1.1 Add `EntityDedupKey` (a value type / helper) computing `(EntityNameIndex.Normalize(name), Type, Edition)` from an `EntityEnvelope`; failing tests first for equality, edition separation, and homebrew (name absent from 5etools index still keys).
- [ ] 1.2 Write failing `DuplicateResolverTests` covering every precedence tier and tiebreaks: Core>Supplement>Adventure>Setting>Unknown; authoritative DataSource > raw parsed; not-NeedsReview > NeedsReview; longer CanonicalText; lexicographically smallest Id; and order-independence (same group in any order → same winner).
- [ ] 1.3 Implement `DuplicateResolver` (pure) taking a group of entities plus a `SourceBook → BookType` lookup; authority-first precedence per design. Make 1.1–1.2 pass. Build green (warnings-as-errors).

## 2. Slice 1 — query-time collapse in fused retrieval

- [ ] 2.1 Add a `SourceBook → BookType` lookup source (from ingestion records / book registry) usable by `FusedRetrievalService`; unmapped books → `BookType.Unknown`.
- [ ] 2.2 Write failing `FusedRetrievalService` tests: two same-key entity hits collapse to one representative = resolver winner carrying the group's MAX similarity score; distinct editions both survive; prose candidates never collapsed.
- [ ] 2.3 Implement the collapse in `FusedRetrievalService` — group entity candidates by dedup key before fusion/rerank, emit one max-score representative per group. Make 2.2 pass. Build + full non-persistence tests green.

## 3. Slice 2 — full-corpus enumeration + report + compact

- [ ] 3.1 Extend `IEntityVectorStore` (+ `QdrantEntityVectorStore`) with a paginated scroll of ALL entity points (envelope + point/entity id) and a delete-by-entity-ids path; reuse the existing scroll pagination pattern.
- [ ] 3.2 Add a `EntityDuplicateService` that scans the full corpus, groups by dedup key, runs `DuplicateResolver`, and returns groups with >1 member as `{ key, winnerId, loserIds }`; `apply` flag deletes only loser points. Failing tests against the real-Qdrant Testcontainer first (dry-run groups correctly; apply deletes only losers, keeps winner; canonical files untouched).
- [ ] 3.3 Implement `EntityDuplicateService`; make 3.2 pass.
- [ ] 3.4 Add `GET /admin/retrieval/entities/duplicates` (read-only report) and `POST /admin/retrieval/entities/compact` (dry-run default, `?apply=true` deletes losers), both under `/admin` (admin-key guarded); register in composition root. Endpoint tests green.

## 4. Contracts, docs, wiring

- [ ] 4.1 Register `DuplicateResolver` / `EntityDuplicateService` / book-type lookup in `ServiceCollectionExtensions`; confirm app builds and starts.
- [ ] 4.2 Update `DndMcpAICsharpFun.http` with example requests for the two new endpoints (with `X-Admin-Api-Key`).
- [ ] 4.3 Update `dnd-mcp-api.insomnia.json` to match (http-contracts sync rule); validate JSON.

## 5. Verify

- [ ] 5.1 Run full non-persistence suite + the new persistence tests (Docker up) — all green; build clean under warnings-as-errors.
- [ ] 5.2 Drive the two endpoints against a running host (duplicates report, then compact dry-run, then `apply=true`) and confirm losers deleted / winner kept / canonical files unchanged.
- [ ] 5.3 Whole-change code review (opus reviewer, Serena-based) — address findings; cross-check plan against the spec's ADDED/MODIFIED requirements.
