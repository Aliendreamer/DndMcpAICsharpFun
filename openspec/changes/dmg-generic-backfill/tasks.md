# Tasks

## 1. Provider seam + engine skeleton

- [ ] 1.1 Define `IFivetoolsBackfillProvider` (`EntityType Type`, `IEnumerable<JsonElement> EnumerateRoster(string fivetoolsDir)` yielding qualifying elements across all sources, `EntityEnvelope BuildEntity(string sourceKey, string edition, string name, JsonElement element)`) in `Features/Ingestion/FivetoolsIngestion/`.
- [ ] 1.2 Write engine unit tests against a FAKE provider first (TDD): recall diff (present/missing/extra), gap-only idempotency, `extraOtherSource` vs `extraUnknown` split, flag-unknown gap-only write + never-delete + never-flag-otherSource.
- [ ] 1.3 Implement `EntityBackfillService` (generic recall/diff/flag core lifted verbatim from `MonsterBackfillService`, provider-driven: reads name/source from each element, edition from `BookSourceRegistry`) with a provider map keyed by `EntityType`; make 1.2 green.

## 2. Per-type providers (each owns its curated field projection — NOT the mapper)

- [ ] 2.1 `MonsterBackfillProvider` — bestiary-*.json enumeration + `BuildEntity`/`BuildFields`/`GetKeywords`/`MonsterFieldNames` lifted verbatim from `MonsterBackfillService`.
- [ ] 2.2 `SpellBackfillProvider` — spells/*.json enumeration + `BuildEntity`/`BuildFields` (Description-block wrapper, entriesHigherLevel, damageInflict, conditionInflict) lifted verbatim from `SpellBackfillService`.
- [ ] 2.3 `MagicItemBackfillProvider` — `items.json` "item" array filtered to magic items (rarity present & ≠ none); NEW `BuildFields` → `MagicItemFields{Rarity,ItemCategory,Attunement,Description}`; excludes `items-base.json`.
- [ ] 2.4 `GodBackfillProvider` — `deities.json` "deity" array; NEW `BuildFields` → `GodFields{Alignment,Domains,Symbol,Pantheon,Plane,Description}`.
- [ ] 2.5 Per-provider tests: backfilled entity `fields` round-trip as the correct typed `*Fields`; MagicItem base-item (rarity none) exclusion; God multi-field projection.

## 3. Port existing behavior + delete old services

- [ ] 3.1 Port `MonsterBackfillService` test suite onto the engine + `MonsterBackfillProvider`; must pass unchanged (same inputs, same recall numbers, grounded preserved).
- [ ] 3.2 Port `SpellBackfillService` test suite onto the engine + `SpellBackfillProvider`; must pass unchanged.
- [ ] 3.3 Delete `MonsterBackfillService.cs` and `SpellBackfillService.cs`; update DI registration to the engine + providers; build green (warnings-as-errors).

## 4. Endpoints (type-parameterized)

- [ ] 4.1 Replace the four routes in `BooksAdminEndpoints.cs` with `GET entity-recall?type=`, `POST backfill-entities?type=`, `POST flag-unknown-entities?type=`; parse `type` to {Monster,Spell,MagicItem,God}, 400 on unsupported; remove `monster-recall`/`backfill-monsters`/`flag-unknown-monsters`/`backfill-spells`.
- [ ] 4.2 Endpoint tests: each supported type routes to its provider; unsupported type → 400; no-source-key → no-op.
- [ ] 4.3 Update `DndMcpAICsharpFun.http` and `dnd-mcp-api.insomnia.json` with the new routes (remove old), same commit.

## 5. DMG coverage run (data)

- [ ] 5.1 Verify DMG is registered with `FivetoolsSourceKey=DMG`; if not, register/set it.
- [ ] 5.2 `POST /admin/books/{dmgId}/extract-entities?force=true`; let the run complete (checkpointed/resumable); review the `dmg14.json` diff (Class noise gone, names clean, errors recovered) and hand-correct.
- [ ] 5.3 Run `entity-recall` for Monster/Spell/MagicItem/God; record present/missing/extra + grounded:backfilled.
- [ ] 5.4 Run `backfill-entities` for each of the four types; then `flag-unknown-entities` for each; commit the corrected `dmg14.json`.
- [ ] 5.5 `POST /admin/canonical/validate` → confirm zero FAIL-class issues for `dmg14`.

## 6. Verify + finish

- [ ] 6.1 Full build (0/0) + `dotnet test` non-persistence suite green.
- [ ] 6.2 Whole-change review on opus (reviewer subagent, Serena-driven) before committing.
- [ ] 6.3 DEFERRED (stack-up, not in this change's commit): start Ollama-backed stack → `POST /admin/books/{dmgId}/ingest-entities` to project `dmg14` into `dnd_entities`.
