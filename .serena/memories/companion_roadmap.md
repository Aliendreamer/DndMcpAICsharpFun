# D&D Companion — Roadmap & Progress (living; refreshed 2026-07-08)

**North star:** a companion agent that REASONS (character build / encounter design / setting-aware
lore), not just retrieves. RAG + extraction are the *means*, and that foundation is now largely built —
the remaining north-star work is the REASONING layer (Item 3; Items 2 & 4 are DONE).

## Status legend: ✅ done · 🔄 in progress · ⬜ not started

## FOUNDATION — extraction + retrieval  ✅ (collapsed; see git history + archived changes)
Everything below shipped and is archived — do NOT re-plan it, just build on it:
- **WRITE-layer extraction quality:** recall fix + precision authoritative-allowlist + deterministic
  type resolution; generic 5etools backfill (`fivetools-entity-backfill`, archived 2026-07-04);
  **Object entity type + decline-not-leak** (archived 2026-07-05 — `mem:extraction/dmg_generic_backfill_status`).
- **Extraction perf — qwen3 /no_think:** SHIPPED (`803da7b`), ~8× faster, no classification regression.
- **Slice 1 character-fact-resolution:** shipped (`235699a`) — `CharacterResolutionService`,
  structured-fact store, `resolve_character_feature` MCP tool.

## FRONTIER — the REASONING layer (north star)
- **Item 2 — Slice 2: multiclass character** ✅ DONE (archived `multiclass-character`, 2026-07-05;
  18 commits `057e7e7..db16979`, 992/992 tests). GENERAL multiclass (any combo, caster or not).
  Plus **HeroDetail multiclass-editing UI** ✅ DONE (archived `hero-multiclass-editing`) + Playwright
  UI smoke passed (2026-07-06).
- **Item 3 — Auto-NeedsReview grounding cascade** ⬜ ← candidate NEXT (own brainstorm→spec). Tier 1 =
  embedding check (reuse mxbai `dnd_blocks` vectors → promote); Tier 2 = qwen3 judge on residual.
- **Item 4 — Corpus-wide dedup** ✅ DONE (archived `corpus-wide-entity-dedup`, 2026-07-08;
  9 code commits `bf0909c..d68e99b`, base 4af09a5; build 0/0, FULL suite 1017/1017 incl real Qdrant +
  Postgres Testcontainers). Dedup key = `(EntityNameIndex.Normalize(name), Type, Edition)` — editions
  never merge. Pure authority-first `DuplicateResolver` (BookType Core>Supplement>Adventure>Setting>
  Unknown → authoritative DataSource 5etools-backfill/hand-authored → not-NeedsReview → longer
  CanonicalText → smallest Id). Slice 1 = query-time collapse in `FusedRetrievalService` (group entity
  candidates by dedup key BEFORE fusion/rerank, emit winner carrying group MAX score; prose untouched;
  distinct editions survive) = the DURABLE correctness layer. Slice 2 = `GET /admin/retrieval/entities/
  duplicates` (read-only report) + `POST /admin/retrieval/entities/compact?apply=` (dry-run default;
  apply deletes ONLY loser points from Qdrant; canonical JSON NEVER rewritten). New store methods
  `ScrollAllAsync`/`DeleteByIdsAsync` (delete guards empty-set, no match-all). BookType resolved at
  dedup time via `BookTypeLookup` (SourceBook→BookType from ingestion records; keyed by
  FivetoolsSourceKey for official + raw DisplayName for non-official — final-review fix d68e99b caught
  that entities carry DisplayName not slug in SourceBook). Dedup kept OUT of extraction/ingestion write
  path; compact is transient (re-ingest re-adds losers) — query-time collapse is what guarantees
  continuous correctness. New infra: first real Qdrant Testcontainer (`Testcontainers.Qdrant` 4.12.0 +
  `QdrantFixture`). Files: `Features/Retrieval/Entities/Dedup/*`. DEFERRED (non-blocking): live-host
  endpoint smoke (stack was down; Testcontainers cover the real-infra paths); Minors — BuildAsync
  per-query rebuild + sequential await on hot path (small tables, could Task.WhenAll/cache);
  QdrantFixture has no per-test reset (safe w/ unique collection name).

## LOOSE ENDS / follow-ups
- **Published-container Blazor static assets** ✅ FIXED (commit `8139397` — `blazor.web.js` restored in
  image via `MapStaticAssets`). Container rebuild to confirm was the prior pending item; the fix landed.
- **Qdrant scalar int8 quantization:** shipped + archived; live-validated. Closed.
- **Spec housekeeping:** `extraction-think-mode` spec proposed, not applied (`/no_think` already shipped).
- **DMG Object residuals** (hand-correctable): tighten `StatBlockScanner` naming / `IsObjectStatBlock`.

## How we progress (discipline — never skip)
Each item: **superpowers:brainstorming** (full dialogue) → **opsx:propose** (spec in
`openspec/changes/<name>/`) → **superpowers:writing-plans** → **superpowers:subagent-driven-development**
(per-task TDD + reviewer subagents; final whole-branch review on opus). Work DIRECTLY on main — no
feature branches (`mem:workflow/work_on_main`); commit autonomy granted. FINISH on "commit"/"archive"
(or an explicit "finish X" goal): commit → `openspec archive` → `skill-optimizer` → refresh this roadmap
(`mem:workflow/finishing_a_spec`).
PLAN-VS-SPEC lesson: a writing-plans plan can silently deviate from the approved spec — the final
whole-branch review catches it; the SPEC governs. (Item 4 example: the plan sketched
`BookTypeLookup(IngestionTracker)` concrete; task-review corrected it to the `IIngestionTracker`
interface per codebase convention.)

## Current position (2026-07-08)
Extraction/retrieval FOUNDATION complete; **Items 2 (multiclass) + its UI + 4 (corpus-wide dedup) all
SHIPPED + archived.** Next frontier: **Item 3 — Auto-NeedsReview grounding cascade** (the last named
reasoning-layer item). One deferred operational task on Item 4: live-host endpoint smoke of the two
dedup admin endpoints (run when app+Qdrant+Postgres stack is up). Relates to
`mem:extraction/dmg_generic_backfill_status`, `mem:project_entity_extraction_rethink`,
`mem:reference_build_env_gotchas`.
