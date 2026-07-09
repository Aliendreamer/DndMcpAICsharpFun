# D&D Companion — Roadmap & Progress (living; refreshed 2026-07-09b)

**North star:** a companion agent that REASONS (character build / encounter design / setting-aware
lore), not just retrieves. RAG + extraction are the *means*, and that foundation is built —
**all named reasoning-layer items (2, 3, 4) are now SHIPPED.** The frontier is now UN-named:
either resume the parked `prose-grounded-knowledge-model` re-architecture, or add net-new
companion reasoning surfaces (encounter design, setting-aware lore). Next item is a fresh brainstorm.

## Status legend: ✅ done · 🔄 in progress · ⬜ not started

## FOUNDATION — extraction + retrieval  ✅ (collapsed; see git history + archived changes)
Everything below shipped and is archived — do NOT re-plan it, just build on it:
- **WRITE-layer extraction quality:** recall fix + precision authoritative-allowlist + deterministic
  type resolution; generic 5etools backfill (`fivetools-entity-backfill`, archived 2026-07-04);
  **Object entity type + decline-not-leak** (archived 2026-07-05 — `mem:extraction/dmg_generic_backfill_status`).
- **Extraction perf — qwen3 /no_think:** SHIPPED (`803da7b`), ~8× faster, no classification regression.
- **Slice 1 character-fact-resolution:** shipped (`235699a`) — `CharacterResolutionService`,
  structured-fact store, `resolve_character_feature` MCP tool.

## FRONTIER — the REASONING layer (all named items DONE)
- **Item 2 — multiclass character** ✅ DONE (archived `multiclass-character`, 2026-07-05) + **HeroDetail
  multiclass-editing UI** ✅ (archived `hero-multiclass-editing`) + Playwright UI smoke passed.
- **Item 3 — Auto-NeedsReview grounding cascade** ✅ DONE (archived `entity-grounding-cascade`,
  2026-07-09; 11 code commits `e71d256..6721bc7` + 3 integration-test commits `0809074,8d4ef8e,e25d613`,
  base 42d56a9; build 0/0, FULL suite **1062/1062**).
  Built the missing Tier 1 + Tier 2 on top of existing Tier 0 as ONE shared `GroundingCascade`
  (`Features/Ingestion/EntityExtraction/`). `GroundingVerdict{Status:Grounded|Ungrounded|Uncertain,
  DecidedByTier,Score}` = pure `GroundingCombiner` of (Tier0 bool, Tier1 vs floor, Tier2 bool?).
  **Tier 0** = existing `Tier0FieldGrounding` OCR field-match (extracted `HasAnyFieldGrounded` helper,
  runner delegates). **Tier 1** (`Tier1EmbeddingGrounding` behind `ITier1Grounding`) = embed entity +
  `dnd_blocks` search scoped to the entity's OWN SourceBook + page window (`GroundingOptions`
  SimilarityFloor .5 / PageWindow 2); ESCALATION-GATE-ONLY — topical similarity ALONE never promotes
  (the Dragonborn-fabrication guard). **Tier 2** (`IGroundingJudge`/`QwenGroundingJudge`, shared
  `IChatClient` qwen3) = field-support judge ("not from general D&D knowledge"), **tri-state `bool?`**
  so I/O failure/unparseable → null → Uncertain (never a false fabrication). Verdict→action
  (`GroundingActionPolicy`, name-gated via `HasOcrArtifacts`): Grounded→promote (clear NeedsReview→
  Accepted) unless garbled name; Ungrounded→new `EntityDisposition.Ungrounded`; Uncertain→stay flagged.
  **`EntityDisposition.Ungrounded` is EXCLUDED from `dnd_entities` at ALL 3 write paths** (ingest skips,
  reground deletes, `ReindexEntityAsync` deletes-not-upserts — final-review caught the admin-accept
  leak). Extraction wires the cascade (judge OFF default = behavior-identical to before). Backlog:
  `POST /admin/books/{id}/reground-entities?judge=` (`RegroundService`) — Tier0/1 fast pass, `?judge=true`
  opts into Tier2; checkpointed/resumable `<slug>.reground.progress.json` (persists changedIds so a
  crash-resume still reindexes/deletes); writes canonical in place + targeted reindex/delete per changed
  entity; NEVER deletes canonical. Opt: cascade skips Tier1 when judge off (verdict-neutral).
  **REAL-QDRANT INTEGRATION TESTS ADDED (2026-07-09b):** `Tier1EmbeddingGroundingIntegrationTests`
  (4 facts — proves book+page scoping genuinely filters via a real Qdrant seed, non-vacuity verified by
  a deliberate filter break) + `RegroundServiceIntegrationTests` (real `QdrantEntityVectorStore` +
  `ReindexEntityAsync`, fake cascade/tracker — proves promote→indexed, Ungrounded→genuinely DELETED
  from real `dnd_entities`, i.e. the I-1 invariant end-to-end). Both GUID-suffix collections + clean up.
  STILL DEFERRED (non-blocking): fully-live manual smoke needing real Ollama (judge) + app; Minors —
  crash-window stale needs_review payload flag on promote; recount double-counts pre-existing Ungrounded;
  RegroundService bypasses NeedsReviewService per-path write lock (race, low); admin re-accept of an
  Ungrounded entity needs a disposition-clearing action (UX, out of scope).
- **Item 4 — Corpus-wide dedup** ✅ DONE (archived `corpus-wide-entity-dedup`, 2026-07-08;
  9 commits `bf0909c..d68e99b`; build 0/0, 1017/1017). Dedup key `(EntityNameIndex.Normalize(name),
  Type, Edition)` — editions never merge. Authority-first `DuplicateResolver`. Slice 1 = query-time
  collapse in `FusedRetrievalService` (winner carries group MAX score; prose untouched) = DURABLE
  correctness. Slice 2 = `GET /admin/retrieval/entities/duplicates` + `POST .../compact?apply=` (deletes
  ONLY loser Qdrant points; canonical never rewritten). `ScrollAllAsync`/`DeleteByIdsAsync`. First real
  Qdrant Testcontainer (`Testcontainers.Qdrant` 4.12.0 + `QdrantFixture`). `Features/Retrieval/Entities/
  Dedup/*`. DEFERRED: live-host smoke of the two dedup admin endpoints.

## TEST INFRA (confirmed present)
Real Testcontainers in-repo: `Testcontainers.PostgreSql` 4.12.0 (`Persistence/PostgresFixture.cs`,
postgres:18-alpine) + Respawn 7.0.0 per-test isolation; `Testcontainers.Qdrant` 4.12.0
(`VectorStore/Entities/QdrantFixture.cs`). Full `dotnet test` needs Docker. Grounding now HAS real-Qdrant
integration tests (Tier1 scoping + reground round-trip, 2026-07-09b) — GUID-unique collections per test
(the older `QdrantEntityVectorStoreScrollTests` still uses a fixed collection name; safe with 1 fact but
GUID-suffix it if a sibling test is added). Only the real-Ollama JUDGE path stays smoke-only.

## LOOSE ENDS / follow-ups
- **Published-container Blazor static assets** ✅ FIXED (`8139397`).
- **Qdrant scalar int8 quantization:** shipped + archived. Closed.
- **`extraction-think-mode` spec** ✅ CLOSED — deleted 2026-07-09b (superseded by shipped `/no_think` `803da7b`; the A/B toggle it proposed is moot now the decision is made).
- **DMG Object residuals** (hand-correctable): tighten `StatBlockScanner` / `IsObjectStatBlock`.
- **`dotnet format`** — a comprehensive `.editorconfig` ALREADY EXISTS (18KB, `dotnet_separate_import_directive_groups`, `dotnet_sort_system_directives_first`); the repo had just drifted from it. One-time normalization was applied 2026-07-09b (whitespace/finalnewline/imports; behavior-neutral, build 0/0). `dotnet format --verify-no-changes` clean going forward.
- **Operational live-host smokes** (deferred, need app+Qdrant+Postgres+Ollama up): Item 3 reground
  endpoint (fast pass + `?judge=true`); Item 4 dedup endpoints (duplicates report + compact apply).

## How we progress (discipline — never skip)
Each item: **superpowers:brainstorming** (full dialogue) → **opsx:propose** → **superpowers:writing-plans**
→ **superpowers:subagent-driven-development** (per-task TDD + reviewer subagents; final whole-branch
review on opus). Work DIRECTLY on main (`mem:workflow/work_on_main`); commit autonomy granted. FINISH on
"commit"/"archive"/"finish X": commit → `openspec archive` → `skill-optimizer` → refresh this roadmap
(`mem:workflow/finishing_a_spec`). `ingest-entities` in the finish step is EXTRACTION/CANONICAL-only —
retrieval/refactor changes skip it (dev-flow SKILL updated Item 4).
PLAN-VS-SPEC lesson: the final whole-branch review catches plan/spec drift; the SPEC governs. It also
catches spec-requirement-not-implemented that per-task reviews miss (Item 3: `Ungrounded` was set but
NOT excluded from `dnd_entities` — spanned 3 write paths — until final review; and judge I/O failure
mislabeled real entities). Cross-path invariants must be traced across ALL paths at final review; inject
INTERFACES not concrete types — both now in dev-flow SKILL.

## Current position (2026-07-09b)
Extraction/retrieval FOUNDATION + **ALL named reasoning items (2,3,4) SHIPPED + archived**, Item 3 now
with real-Qdrant integration coverage. **No named NEXT item** — the frontier is a fresh brainstorm:
(a) resume the parked `prose-grounded-knowledge-model` re-architecture (`mem:project_entity_extraction_rethink`
— prose-as-source-of-truth, nested entities + typed relationships + first-class tables/choice-sets); or
(b) net-new companion REASONING surfaces (encounter design/balancing, setting-aware lore synthesis,
deeper character-build advice) that consume the now-solid retrieval+extraction+grounding foundation.
Deferred operational: live-host smokes for Item 3 (reground, esp. the Ollama judge path) + Item 4 (dedup
endpoints). Relates to `mem:extraction/dmg_generic_backfill_status`, `mem:project_entity_extraction_rethink`,
`mem:reference_build_env_gotchas`.
