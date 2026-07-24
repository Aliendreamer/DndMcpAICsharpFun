## Why

The typed-entity retrieval layer lets callers filter entities by type, source, keyword, damage type, SRD flags, and `castableByClass`, but not by a race's **ability-score bonus** — a top race-selection question ("which races give a Strength bonus?"). The data already exists: `RaceFields.Ability` carries the 5etools ability structure (fixed `{"str":2}` and choose-blocks `{"choose":{"from":[…]}}`), projected onto Race entities by fivetools-field-fill. This adds a query-time ability-bonus filter that reuses the exact `castableByClass` join pattern — no re-index, no GPU.

## What Changes

- Add a `RaceAbilityParser` that, given a race's `RaceFields`, returns the set of boosted ability codes (`str/dex/con/int/wis/cha`) — unioning **fixed** bonus keys and **choosable** (`choose.from`) entries. A race with no structured ability data → empty set (never matches; no fabrication).
- Add an `AbilityBonus` query param (a 3-letter ability code, case-insensitive) to `EntitySearchQuery`.
- In `EntityRetrievalService`, when `AbilityBonus` is set, run a query-time filter exactly like `CastableByClass`: force `Type=Race`, scroll Race entities (capped scan), keep those whose `RaceAbilityParser` set contains the requested code, and report the honest matched count as `total` (Qdrant can't filter a computed set).
- Thread `abilityBonus` through `GET /retrieval/entities/list` (public list endpoint). Update `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json`.

## Capabilities

### New Capabilities

- `race-ability-filter`: entity retrieval accepts an `abilityBonus` filter that returns the races granting a bonus to that ability — counting BOTH fixed bonuses and choosable (choose-block) bonuses — as a query-time in-memory match over Race entities (mirroring `castableByClass`), with an honest matched-vs-returned total and no re-indexing.

### Modified Capabilities

<!-- Additive query param on the existing list endpoint; no existing REQUIREMENT changes. -->

## Impact

- `Features/Retrieval/Entities/RaceAbilityParser.cs` (new) + `EntitySearchQuery.cs` (new `AbilityBonus` param) + `EntityRetrievalService.cs` (a `ListRacesByAbilityAsync` mirroring `ListSpellsByClassAsync`) + `EntityRetrievalEndpoints.cs` (thread `abilityBonus` through `ListPublic`/`BuildQuery`).
- Reuses `ListByFilterAsync`, `BuildFilters(q) with { Type = Race }`, the capped-scan + matched-count pattern, and `EntityType.Race`.
- Tests: `RaceAbilityParser` units (fixed / choose / mixed / empty) + a real-Qdrant integration test (reuse `QdrantFixture`) seeding fixed-STR, choose-includes-STR, and no-STR races → `abilityBonus=str` returns exactly the first two.
- **HTTP contract change** (new query param) → `.http` + insomnia updated in the same commit. No re-index, no GPU, no migration. Resistance/immunity filtering and any chat/MCP tool are OUT of scope.
