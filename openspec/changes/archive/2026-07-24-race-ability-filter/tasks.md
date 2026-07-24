# Tasks — race-ability-filter (ability-bonus filter on entity retrieval, query-time like castableByClass)

All tasks complete. Commits 233dc0c, ca6549a, ad801c1, a5d0d2a. Full suite 1704/1704 with Docker; whole-branch review APPROVE.

## 1. `RaceAbilityParser`
- [x] 1.1 `RaceAbilityParser.BoostedAbilities(JsonElement fields)` — fixed keys + `choose.from` entries; case-tolerant `ability` key; ValueKind-guarded; never throws; empty when no data. (Refined from `RaceFields?` to the raw `JsonElement` to avoid the throwing `DeserialiseFields`.) (233dc0c)
- [x] 1.2 Unit tests: fixed, choose, mixed, no-data, case-tolerant-key + malformed-no-throw (5). (233dc0c)

## 2. `AbilityBonus` query param + service filter
- [x] 2.1 `EntitySearchQuery.AbilityBonus` trailing optional. (ca6549a)
- [x] 2.2 `ListRacesByAbilityAsync` mirroring `ListSpellsByClassAsync` (Type=Race, RaceAbilityMaxScan=3000, `.Where(BoostedAbilities(h.Envelope.Fields).Contains(code))`, `.Take(cap)`, total=matched.Count); branch before the generic path; BuildFilters untouched. RISK RESOLVED: `h.Envelope.Fields` populated on list hits (payloadSelector:true → ToEnvelope parses FieldsJson). (ca6549a)
- [x] 2.3 Fake-store test: fixed-STR + choose-STR + dex-only → str→2 matches, total==2, STR==str, null→generic path. (ca6549a)

## 3. Endpoint wiring
- [x] 3.1 `abilityBonus` threaded through `EntityRetrievalEndpoints.ListPublic` → `/retrieval/entities/list`. (ad801c1)
- [x] 3.2 `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json` both updated (JSON valid). (ad801c1)

## 4. Real-infra grounding
- [x] 4.1 Real-Qdrant `RaceAbilityFilterIntegrationTests`: fixed-STR / choose-STR / dex-only seeded → `abilityBonus=str` returns first two, total==2, dex-only absent. Proves Fields round-trip + parse/filter end-to-end. (a5d0d2a)

## 5. Gates
- [x] 5.1 build 0/0; FULL `dotnet test` 1704/1704 with Docker; `dotnet format` clean; diff confined to Features/Retrieval/Entities/* + .http + insomnia + tests; .http/insomnia synced; no re-index/migration. Whole-branch review APPROVE.
