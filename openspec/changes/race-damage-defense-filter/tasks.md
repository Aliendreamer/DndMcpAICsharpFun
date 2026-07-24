# Tasks — race-damage-defense-filter (resist/immune filters on entity retrieval, query-time; mirrors race-ability-filter)

## 1. `RaceDamageDefenseParser`
- [ ] 1.1 Create `Features/Retrieval/Entities/RaceDamageDefenseParser.cs`: `public static IReadOnlySet<string> DamageTypes(JsonElement fields, string key)` (`key` = `resist`|`immune`). Case-tolerant lookup of the `key` array on `fields` (enumerate props, match `key` OrdinalIgnoreCase — `TryGetProperty` is case-sensitive). For each array element: a String → add lowercased value; a `choose` object with a `from` array → add each string entry. Ignore other shapes. `fields` not Object / no key / empty → empty set. Never throws. (Mirror `RaceAbilityParser`'s structure — read it first via Serena.)
- [ ] 1.2 Unit tests: `resist=["necrotic","radiant"]` → {necrotic,radiant}; choose `[{"choose":{"from":["acid","fire"]}}]` → includes acid,fire; mixed string+choose; `immune=["poison"]` read via key="immune" while key="resist" (absent) → empty; malformed/non-object → empty, no throw; case-tolerant key (`"Resist"`). RED-first.

## 2. Query params + service filter
- [ ] 2.1 Add `string? ResistDamage = null` and `string? ImmuneDamage = null` (trailing, after `AbilityBonus`) to `EntitySearchQuery`.
- [ ] 2.2 In `EntityRetrievalService`'s list method, after the `AbilityBonus` branch: `if (!string.IsNullOrWhiteSpace(q.ResistDamage) || !string.IsNullOrWhiteSpace(q.ImmuneDamage)) return await ListRacesByDamageDefenseAsync(q, clamped, ct);`. Implement `ListRacesByDamageDefenseAsync` mirroring `ListRacesByAbilityAsync`: `BuildFilters(q) with { Type = EntityType.Race }`, `ListByFilterAsync(..., RaceDamageDefenseMaxScan)`, then `matched = hits.Where(h => ResistOk(h.Envelope.Fields) && ImmuneOk(h.Envelope.Fields))` where `ResistOk(f)` = `string.IsNullOrWhiteSpace(q.ResistDamage) || RaceDamageDefenseParser.DamageTypes(f,"resist").Contains(q.ResistDamage!.Trim().ToLowerInvariant())` and `ImmuneOk` likewise for `"immune"`/`q.ImmuneDamage`; `.Take(cap)`; `total = matched.Count`. Add `RaceDamageDefenseMaxScan` const (= 3000, mirroring `RaceAbilityMaxScan`). Do NOT add the params to `BuildFilters`.
- [ ] 2.3 Unit test (fake store): a fire-resist race (`{"resist":["fire"]}`), a choose-includes-fire race (`{"resist":[{"choose":{"from":["fire","cold"]}}]}`), a poison-immune race (`{"immune":["poison"]}`), and a plain race → `resist=fire` returns the two fire races (`total==2`); `immune=poison` returns the one immune race; `resist=fire&immune=poison` returns none (no race has both); `resist=FIRE` == `resist=fire`; neither set → generic path unchanged. RED-first.

## 3. Endpoint wiring
- [ ] 3.1 In `EntityRetrievalEndpoints.ListPublic`, add `string? resist, string? immune` (alongside `abilityBonus`) and extend `with { … AbilityBonus = abilityBonus }` to also set `ResistDamage = resist, ImmuneDamage = immune`. Public `/retrieval/entities/list` only.
- [ ] 3.2 Update `DndMcpAICsharpFun.http` (add `?resist=fire&limit=50` and `?immune=poison&limit=50` examples near the abilityBonus one, ~line 278) AND `dnd-mcp-api.insomnia.json` (mirror both) — same commit.

## 4. Real-infra grounding
- [ ] 4.1 A real-Qdrant integration test (reuse `QdrantFixture`; mirror `RaceAbilityFilterIntegrationTests`): seed Race entities with the REAL 5etools shapes — a fixed `{"resist":["fire"]}`, a choose `{"resist":[{"choose":{"from":["fire","cold"]}}]}`, a `{"immune":["poison"]}`, and a plain race. Assert `resist=fire` → the two fire races (`total==2`); `immune=poison` → the poison race; the plain race is absent from both. Proves `Fields` round-trip + parse/filter on real infra.

## 5. Gates
- [ ] 5.1 `dotnet build` 0/0; FULL `dotnet test` green (Docker for the Qdrant test); `dotnet format` clean on touched files; `git diff --stat` confined to `Features/Retrieval/Entities/*` + `.http` + insomnia + tests; `.http`/insomnia updated for the two new params; no re-index/migration.
