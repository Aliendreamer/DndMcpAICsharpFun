## Why

Goal: for every official book, the structured entity layer verifiably contains ALL of 5etools' content. Today only 4 entity types get *backfilled* as new entities (Monster, Spell, MagicItem, God) — the catalog long-tail 5etools has cleanly but extraction misses is absent (PHB feats 1-of-42, mundane items 2-of-~37, backgrounds 16-of-20, plus every condition/trap/vehicle). And nothing MEASURES the gap — coverage is guesswork. The gap-only backfill ENGINE already exists and is generalized (`EntityBackfillService` + `IFivetoolsBackfillProvider`, and `EntityBackfillResult` already computes counts + named-missing), and all 18 field-fill mappers already hold the projection logic — so closing this is mostly wiring providers for types we already model, plus a coverage report.

## What Changes

- **Catalog backfill providers** for the modeled long-tail types 5etools cleanly supplies and extraction misses: Feat, Background, Item (mundane), Weapon, Armor, Condition, Trap, DiseasePoison, VehicleMount. Each mirrors the 4 existing providers (`EnumerateRoster` over the type's 5etools file/array + `BuildEntity` wrapping the existing `Fivetools*Mapper`). Registered in the DI provider array so the existing backfill endpoint covers them (gap-only, `dataSource:"5etools-backfill"`, `Accepted`).
- **Coverage reconciliation gate** — a `FivetoolsCoverageService` runs every provider's diff (`ComputeAsync`) for a book WITHOUT applying, aggregating per-type: 5etools roster count, present (grounded + backfilled), missing (NAMED), and a `noProvider`/unmodeled bucket (incl. the ~82 optionalfeatures with no EntityType yet). Surfaced via a new `GET /admin/books/{id}/coverage`, a summary block in `POST /admin/canonical/validate` warnings, and a startup coverage log. **Report + loud warning — never blocks** (some 5etools content is legitimately unmodeled; you drive coverage up by running backfill, not by being locked out).

## Capabilities

### New Capabilities
- `fivetools-coverage`: the structured layer's completeness against 5etools is (a) closed additively for every modeled catalog type and (b) MEASURED per book/type with named gaps, failing loud (warning) rather than silently.

### Modified Capabilities
<!-- Generalizes the existing backfill from 4 types to the modeled catalog set; no existing capability changes direction. -->

## Impact

- New: `Providers/{Feat,Background,Item,Weapon,Armor,Condition,Trap,DiseasePoison,Vehicle}BackfillProvider.cs` (wrap existing mappers); `FivetoolsCoverageService`; a coverage endpoint + validate-warning + startup log.
- Modified: the `IFivetoolsBackfillProvider[]` DI array (+ the endpoint-test replica); `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json` (new `/coverage` endpoint).
- Data: running backfill APPENDS new `5etools-backfill` entities to `books/canonical/<slug>.json` (reviewed in PR, ingested via `ingest-entities`); gap-only, never rewrites or deletes extraction-owned entities.
- **Explicitly deferred (documented):** a new `OptionalFeature` EntityType (+ schema + mapper + renderer + extraction-union wiring) to close the ~82 invocations/fighting-styles/metamagic — its own follow-up change (the coverage gate MEASURES this gap now so it's visible). Class/Subclass/Race/Subrace get MEASURED but no new providers (multi-level aggregates; extraction already covers them 12/12).
- No change to extraction, the main gate, or field-fill.
