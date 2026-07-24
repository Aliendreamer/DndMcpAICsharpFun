## Why

`race-ability-filter` added an `abilityBonus` filter to entity retrieval (races by boosted ability). The sibling race-selection question — "which races resist fire?" / "which are immune to poison?" — is still unanswered, even though the data exists: `RaceFields.Resist` and `RaceFields.Immune` carry the 5etools damage-type structures (strings like `["necrotic","radiant"]` plus 6 `{"choose":{"from":[…]}}` resist blocks on Dragonborn/Tiefling), projected by fivetools-field-fill. This adds `resist` and `immune` filters as a query-time match reusing the exact `abilityBonus`/`castableByClass` pattern — no re-index, no GPU.

## What Changes

- Add a `RaceDamageDefenseParser.DamageTypes(fields, key)` (`key` = `resist`|`immune`) that returns the damage-type codes a race resists/is immune to — from string entries and `choose.from` blocks. A race with no such data → empty set (never matches; no fabrication). One method serves both fields.
- Add `ResistDamage` and `ImmuneDamage` query params (damage-type codes, case-insensitive) to `EntitySearchQuery`.
- In `EntityRetrievalService`, when either is set, run a query-time filter (like `abilityBonus`): force `Type=Race`, scroll (capped), keep hits matching **(resist unset OR resist-set contains the code) AND (immune unset OR immune-set contains the code)** — the two params AND when both given — and report the honest matched count.
- Thread `resist` and `immune` through `GET /retrieval/entities/list`. Update `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json`.

## Capabilities

### New Capabilities

- `race-damage-defense-filter`: entity retrieval accepts `resist` and `immune` filters returning the races that resist / are immune to a given damage type — counting BOTH fixed and choosable (choose-block) defenses — as a query-time in-memory match over Race entities (mirroring `abilityBonus`), ANDed when both are provided, with an honest matched-vs-returned total and no re-indexing.

### Modified Capabilities

<!-- Additive query params on the existing list endpoint; no existing REQUIREMENT changes. -->

## Impact

- `Features/Retrieval/Entities/RaceDamageDefenseParser.cs` (new) + `EntitySearchQuery.cs` (`ResistDamage`/`ImmuneDamage`) + `EntityRetrievalService.cs` (`ListRacesByDamageDefenseAsync` mirroring `ListRacesByAbilityAsync`) + `EntityRetrievalEndpoints.cs` (thread `resist`/`immune`).
- Reuses `ListByFilterAsync`, `BuildFilters(q) with { Type = Race }`, the capped-scan + matched-count pattern, and `EntityType.Race`.
- Tests: parser units (string / choose / mixed / both keys / empty) + a fake-store service test (resist-only / immune-only / both-AND / case-insensitive) + a real-Qdrant integration test (fire-resist, choose-includes-fire, poison-immune, plain races).
- **HTTP contract change** (two new query params) → `.http` + insomnia updated same commit. No re-index, no GPU, no migration. A combined single "damage defense" param and any chat/MCP tool are OUT of scope.
