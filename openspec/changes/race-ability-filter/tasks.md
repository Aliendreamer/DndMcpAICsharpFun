# Tasks — race-ability-filter (ability-bonus filter on entity retrieval, query-time like castableByClass)

## 1. `RaceAbilityParser`
- [ ] 1.1 Create `Features/Retrieval/Entities/RaceAbilityParser.cs`: `public static IReadOnlySet<string> BoostedAbilities(RaceFields? fields)`. For each `JsonElement` in `fields?.Ability`: add each top-level property whose name ∈ `{str,dex,con,int,wis,cha}` and whose value is a Number (fixed); if a `choose` object with a `from` array exists, add each string in `from` that is an ability code. Lowercase; robust (`TryGetProperty`, `ValueKind` checks; ignore unknown keys); null/empty → empty set. Never throws.
- [ ] 1.2 Unit tests: fixed `{"str":2,"con":1}` → {str,con}; choose `{"choose":{"from":["str","dex","con"]}}` → includes str,dex,con; mixed `{"cha":2,"choose":{"from":["str","wis"]}}` → {cha,str,wis}; null/empty Ability → empty; a malformed element → no throw, ignored. RED-first.

## 2. `AbilityBonus` query param + service filter
- [ ] 2.1 Add `string? AbilityBonus = null` (trailing) to `EntitySearchQuery`.
- [ ] 2.2 In `EntityRetrievalService.ListEntitiesAsync`, mirror the `CastableByClass` branch: before the generic `ListByFilterAsync`, add `if (!string.IsNullOrWhiteSpace(q.AbilityBonus)) return await ListRacesByAbilityAsync(q, clamped, ct);`. Implement `ListRacesByAbilityAsync` mirroring `ListSpellsByClassAsync`: `BuildFilters(q) with { Type = EntityType.Race }`, `ListByFilterAsync(spellFilters..., RaceAbilityMaxScan)`, `.Where(h => RaceAbilityParser.BoostedAbilities(<race fields from h.Envelope>).Contains(code))` where `code = q.AbilityBonus!.Trim().ToLowerInvariant()`, `.Take(cap)`, `total = matched.Count`. Add a `RaceAbilityMaxScan` const mirroring `SpellClassMaxScan`. FIRST read (via Serena) how the codebase gets `RaceFields` from a search hit's `Envelope` (there is an existing typed-field access path — reuse it; do NOT hand-roll a deserialize if a helper exists). Do NOT add `AbilityBonus` to `BuildFilters` (it is not a payload field).
- [ ] 2.3 Unit test (fake store): a fixed-STR race + a choose-includes-STR race + a DEX-only race seeded → `AbilityBonus="str"` returns the first two, `total==2`; `AbilityBonus` absent → generic path unchanged; `AbilityBonus="STR"` == `"str"`. RED-first.

## 3. Endpoint wiring
- [ ] 3.1 In `EntityRetrievalEndpoints`, add an `abilityBonus` parameter to `ListPublic` + `BuildQuery` (set it on the `EntitySearchQuery` exactly as `castableByClass` is). Public `/retrieval/entities/list` only. If the diagnostic/admin endpoints share `BuildQuery`, they inherit it harmlessly; confirm no other endpoint needs a separate change.
- [ ] 3.2 Update `DndMcpAICsharpFun.http` (add an `?abilityBonus=str` example to the entities/list request) AND `dnd-mcp-api.insomnia.json` (mirror) — same commit.

## 4. Real-infra grounding
- [ ] 4.1 A real-Qdrant integration test (reuse `QdrantFixture`): seed Race entities carrying the REAL 5etools ability shapes — a fixed `{"str":2}` race, a `{"choose":{"from":["str","dex"]}}` race, and a `{"dex":2}` race — then assert `list?abilityBonus=str` (or the service call with `AbilityBonus="str"`) returns exactly the fixed-STR + choose-STR races with `total==2`, and the DEX-only race is absent. Proves the parse+filter against real Qdrant + real ability JSON.

## 5. Gates
- [ ] 5.1 `dotnet build` 0/0; FULL `dotnet test` green (Docker for the Qdrant integration test); `dotnet format` clean on touched files; `git diff --stat` confined to `Features/Retrieval/Entities/*` + `.http` + insomnia + tests; `.http`/insomnia updated for the new param; no re-index/migration.
