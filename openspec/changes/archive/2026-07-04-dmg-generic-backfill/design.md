## Context

The 5etools recall+backfill pattern exists twice: `MonsterBackfillService` (323 lines) and `SpellBackfillService` (201 lines), each exposing type-specific admin routes. They share one algorithm — diff a book's canonical entities of one type against the 5etools roster for the book's source key, report recall (present/missing/extra), split extra into cross-printed-elsewhere vs unknown, append the gaps as `NeedsReview`-carrying envelopes, and (monster only) flag the unknowns. What differs per type is small: which 5etools files to read, the JSON array key, and how to build the `EntityEnvelope` fields — and the field build already exists as `FivetoolsXxxMapper`, which `MonsterBackfillService.BuildFields` currently duplicates.

DMG is the forcing function: 289 MagicItems, 69 Monsters, plus God/Spell, and a stale pre-gate `dmg14.json`. Adding a third and fourth copy of the service is the wrong response; the right one is to invert it into a generic engine + per-type providers.

Constraints: warnings-as-errors on every project; `.http` + `.insomnia.json` must track route changes in the same commit; canonical files are the source of truth and are hand-reviewed in PRs; `dnd_entities` re-ingest needs Ollama (deferred). Single-dev on `main`, so breaking the type-specific routes is acceptable (no external consumers).

## Goals / Non-Goals

**Goals:**
- One `EntityBackfillService` covering Monster, Spell, MagicItem, God — behavior-identical to today's Monster/Spell paths for those two types.
- Per-type provider seam so a future type is a mapper + a small provider, not a new service.
- Reuse existing `FivetoolsXxxMapper` for envelope field projection (delete the duplicated `BuildFields`).
- Type-parameterized admin endpoints; old routes removed.
- DMG re-extracted fresh and brought to recall parity; corpus validation clean for dmg14.

**Non-Goals:**
- `dnd_entities` re-ingest into Qdrant (deferred until the Ollama-backed stack is up).
- Backfill for Item/Trap/Plane (DMG-original prose, thin 5etools authority) — out of scope.
- Changing the extraction pipeline itself (allowlist gate, stat-line strip already shipped).
- Corpus-wide dedup (a separate roadmap item).

## Decisions

**D1 — Generic engine + provider seam (over per-type services or a hybrid).**
`EntityBackfillService` owns the algorithm; `IFivetoolsBackfillProvider` supplies `EntityType`, `EnumerateRoster(sourceKey)` (yielding `(name, source, JsonElement)` across the type's 5etools files + array key), and `BuildEntity(key, edition, name, element)`. The engine is constructed with the set of providers keyed by `EntityType`. Monster is included in the generic path (not left as a one-off) so there is exactly one algorithm; its provider reproduces today's bestiary enumeration. Alternatives rejected: four per-type services (heavy duplication, no generalization); hybrid keeping Monster separate (leaves two algorithms to maintain).

**D2 — Providers own a curated field projection (NOT the mapper).**
Investigation showed the existing `FivetoolsXxxMapper.Map` inherits the base `BuildFields = entry.Clone()` — it stores the whole 5etools entry as `fields`, which does NOT match the curated canonical `*Fields` shapes (Monster's 30 named stat fields; Spell's `Description`-block wrapper). The hand-rolled `BuildFields` in the two services are deliberately different from the mapper for exactly this reason. So each provider owns its curated projection: Monster and Spell lift the existing `BuildEntity`/`BuildFields`/`GetKeywords` verbatim (guaranteeing the ported tests pass unchanged), and MagicItem/God are newly written to `MagicItemFields{Rarity,ItemCategory,Attunement,Description}` and `GodFields{Alignment,Domains,Symbol,Pantheon,Plane,Description}`. The DRY win is the recall/diff/flag algorithm, which is genuinely identical across types; field projection is inherently type-specific and stays so. Alternative rejected: reusing `mapper.Map` — it would regress canonical field quality (bloated whole-entry fields, wrong Spell shape).

**D3 — MagicItem roster definition.**
The MagicItem provider reads `items.json` (magic items) filtered to the source key, treating an item as a magic item when it has a rarity other than `none`/absent (mirrors `FivetoolsMagicItemMapper`'s own inclusion rule). `items-base.json` (mundane base items) is excluded — those map to the `Item` type, which is out of scope for backfill.

**D4 — Type-parameterized endpoints.**
`GET /admin/books/{id}/entity-recall?type=`, `POST /admin/books/{id}/backfill-entities?type=`, `POST /admin/books/{id}/flag-unknown-entities?type=`. `type` parses to the supported set {Monster, Spell, MagicItem, God}; anything else → 400. The four old routes are removed. Recall response keeps the monster-recall shape (present/missing/extra, grounded/backfilled, extraOtherSource/extraUnknown) generalized across types.

**D5 — TDD swap order (de-risk the Monster path).**
Port the existing `MonsterBackfillService`/`SpellBackfillService` test suites onto the generic engine + providers FIRST (they define the behavioral contract), get them green against the new engine, then delete the old services. The engine's core logic is also unit-tested with a fake provider (recall diff, gap-only idempotency, otherSource/unknown split, flag-unknown gap-only write) so type providers stay thin.

## Risks / Trade-offs

- **Regressing the hard-won Monster backfill/recall path** → Mitigation: port its full test suite onto the generic engine before deleting the old service; the ported tests must pass unchanged (same inputs, same recall numbers).
- **Mapper signature mismatch when reused for single-element envelope build** → Mitigation: if a mapper can't be called cleanly per-element, extract the per-element projection into a shared method the mapper and provider both call, rather than duplicating.
- **Fresh DMG re-extraction is a multi-hour qwen3 run and may surface new field errors** → Mitigation: the run is checkpointed/resumable; canonical diff is hand-reviewed in the PR before backfill; `errorsOnly` retry handles transient Ollama failures.
- **MagicItem "all NeedsReview" today may be intended, not a bug** → Mitigation: the fresh extraction + backfill sets NeedsReview by the same rules as other types; we validate the count is explainable (grounded vs backfilled) rather than blindly clearing it.

## Migration Plan

1. Land engine + providers + endpoints (tests green, old services deleted, `.http`/`.insomnia.json` updated) on `main`.
2. Re-extract DMG (`extract-entities?force=true`) → review canonical diff in PR → hand-correct.
3. Recall + backfill + flag-unknown for Monster, Spell, MagicItem, God; commit corrected canonical.
4. `POST /admin/canonical/validate` → expect 0 failures for dmg14.
5. Deferred: start Ollama-backed stack → `ingest-entities` to project dmg14 into `dnd_entities`.

Rollback: the change is additive-then-deletive in code; reverting the commit restores the two services and old routes. Canonical regeneration is git-tracked and revertible.

## Open Questions

- None blocking. Whether God backfill yields meaningful recall for DMG (uneven 5etools deity coverage) is an empirical question resolved by the recall report during the run, not a design blocker.
