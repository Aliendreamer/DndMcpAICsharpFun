## Context

The backfill engine is already generalized and proven (read before implementing):
- `EntityBackfillService.ComputeAsync(record, ct)` — for one `IFivetoolsBackfillProvider`, diffs the book's canonical entities of `provider.Type` (by normalized name) against the provider's 5etools roster filtered to the book's `FivetoolsSourceKey`, and returns `EntityBackfillResult(hasSourceKey, canonicalPath, toAppend[], alreadyPresent, missing[NAMED], extra[], grounded, backfilled, extraOtherSource[], extraUnknown[])`. It does NOT write — the apply step does. So the coverage report is just `ComputeAsync` run per provider, aggregated, not applied.
- `IFivetoolsBackfillProvider`: `EntityType Type`; `IEnumerable<JsonElement> EnumerateRoster(fivetoolsDir)` (every qualifying element of this type across all sources — name/source read by the engine); `EntityEnvelope BuildEntity(sourceKey, edition, name, element)` (curated `*Fields` projection, `dataSource:"5etools-backfill"`, Disposition Accepted, id/edition from the key).
- 4 providers exist: `MonsterBackfillProvider`, `SpellBackfillProvider`, `MagicItemBackfillProvider`, `GodBackfillProvider` (`Features/Ingestion/FivetoolsIngestion/Providers/`). Registered as `IFivetoolsBackfillProvider[]` in `ServiceCollectionExtensions.AddEntityExtraction` (~line 284) AND replicated in `EntityBackfillEndpointTests.BuildClientAsync`.
- 18 field-fill mappers exist (`Mappers/Fivetools*Mapper.cs`, registry `FivetoolsMapperRegistry`) — each already projects a 5etools element of its type into `*Fields`; a new provider's `BuildEntity` reuses the corresponding mapper's projection.
- `EntityType` enum (23): Class, Subclass, Race, Subrace, Background, Feat, Spell, Weapon, Armor, Item, MagicItem, Monster, Trap, DiseasePoison, VehicleMount, God, Plane, Faction, Location, Condition, Lore, Rule, Object. NO OptionalFeature.

## Goals / Non-Goals

**Goals:** additive gap-only backfill for every modeled catalog type 5etools cleanly supplies; a per-book/per-type coverage report with NAMED gaps, surfaced loud (endpoint + validate-warning + startup), never blocking. **Non-Goals:** a new `OptionalFeature` type (deferred — measured only); Class/Subclass/Race/Subrace providers (measured only — multi-level aggregates, extraction already covers them); rewriting/deleting extraction-owned entities; any extraction/field-fill/main-gate change; homebrew.

## Decisions

- **D1 — new providers wrap existing mappers.** Each of the 9 new providers (Feat, Background, Item, Weapon, Armor, Condition, Trap, DiseasePoison, VehicleMount) mirrors the 4 existing providers' shape: `EnumerateRoster` reads the type's 5etools file/array (implementer confirms the exact file per type by reading the corresponding mapper + how the 4 existing providers enumerate — e.g. Feat→`feats.json` `"feat"`, Background→`backgrounds.json` `"background"`, Condition→`conditionsdiseases.json`, Vehicle→`vehicles.json`); `BuildEntity` delegates to the existing `Fivetools*Mapper` projection. No new projection logic.
- **D2 — the Item/Weapon/Armor split is the one tricky provider set.** 5etools mundane gear lives in `items-base.json` (`"baseitem"`) with type codes; magic/named items in `items.json` (the existing `MagicItemBackfillProvider` reads `items.json` filtering rarity-present). The mundane Item/Weapon/Armor providers read `items-base.json` (+ mundane `items.json` entries) and split by the type code exactly as the existing `FivetoolsItemMapper`/`WeaponMapper`/`ArmorMapper` already classify. Implementer verifies the split against those mappers so the three providers partition the base-item roster without overlap or double-count.
- **D3 — coverage aggregator, no apply.** `FivetoolsCoverageService.ComputeAsync(record, ct)` loops ALL registered providers, calls `EntityBackfillService.ComputeAsync` per provider (never applies), and returns a `BookCoverage { sourceKey, perType: [{type, rosterCount, present(grounded+backfilled), missingCount, missingNames[]}], totalPresent, totalRoster, coveragePct }`. Types with NO provider but that 5etools has for the book (optionalfeatures, senses, etc.) are reported in a `unmodeled[]` bucket from a small known-5etools-file list so the ~82 optionalfeatures gap is VISIBLE. Non-official book (no source key) → empty/no-op.
- **D4 — three surfaces, all non-blocking.** (a) `GET /admin/books/{id}/coverage` → the full `BookCoverage` JSON (admin-gated); (b) `POST /admin/canonical/validate` response gains a `coverage` warnings summary (per book: pct + total missing) — stays 200/valid, never 422 on coverage; (c) startup: one `LogWarning` per official book below 100% (`"<KEY> coverage <pct>% (<n> missing across <t> types)"`), mirroring the existing scope-health guard.
- **D5 — apply path unchanged + safe.** Adding providers to the DI array means the EXISTING backfill endpoint/apply covers the new types automatically; the engine's gap-dedup (by normalized name, seeded with canonical names) already guarantees no duplicate ids and never touches non-backfill entities. Per the dev-flow canonical-rewrite gate, the backfill apply already writes via `CanonicalJsonWriter` and `CanonicalJsonLoader` throws on duplicate id — a test asserts unique ids + a load round-trip after a multi-type apply.

## Risks / Trade-offs

- Item/Weapon/Armor base-item split (D2) is the real correctness risk — a miscode double-counts or drops gear. Mitigate: test each of the three providers' roster against a known PHB base-item subset; assert the partition.
- Coverage `unmodeled[]` relies on a hand-listed set of 5etools files with no EntityType — a hand-maintained list (the audit's recurring drift class). Mitigate: keep it tiny + comment it as the known-unmodeled set, and it only feeds a WARNING, never logic.
- Live validation is required (dev-flow data gate): run backfill on PHB, review the canonical diff (feats 1→42 etc.), confirm gap-only (extraction-owned untouched), unique ids + load round-trip, coverage report reads ~100% for provided types + shows the optionalfeatures gap, then ingest.

## Migration Plan

Land per-task on `main`. Backfill apply is idempotent + additive; a re-run is a no-op once canonical is filled. No schema/data migration. `ingest-entities` (PHB) as the finish step to project the new entities into `dnd_entities`.

## Open Questions

- Exact 5etools file/array per new type — resolved by the implementer reading each mapper + the 4 existing providers (D1). No blocking unknowns.
