# Tasks — race-damage-defense-filter (resist/immune filters on entity retrieval, query-time; mirrors race-ability-filter)

All tasks complete. Commits 734a608, 77a27ba, 13a18f5, b76f334. Full suite 1716/1716 with Docker; whole-branch review APPROVE (zero findings).

## 1. `RaceDamageDefenseParser`
- [x] 1.1 `DamageTypes(JsonElement fields, string key)` — string entries + `choose.from`; case-tolerant key; ValueKind-guarded; never throws; empty when absent. (734a608)
- [x] 1.2 Unit tests: strings, choose, mixed, key-scoped read, case-tolerant-key + malformed-no-throw (5). (734a608)

## 2. Query params + service filter
- [x] 2.1 `EntitySearchQuery.ResistDamage` + `ImmuneDamage` trailing optionals. (77a27ba)
- [x] 2.2 `ListRacesByDamageDefenseAsync` mirroring `ListRacesByAbilityAsync`; AND semantics (unset param = no-op TRUE clause, both set = AND); Type=Race, RaceDamageDefenseMaxScan=3000, total=matched.Count; BuildFilters untouched. (77a27ba)
- [x] 2.3 Fake-store test: resist=fire→2, immune=poison→1, both→none, FIRE==fire, neither→generic unchanged. (77a27ba)

## 3. Endpoint wiring
- [x] 3.1 `resist`/`immune` threaded through `ListPublic` → `/retrieval/entities/list`. (13a18f5)
- [x] 3.2 `.http` (2 examples) + `dnd-mcp-api.insomnia.json` (2 param-object requests, JSON valid) — same commit. (13a18f5)

## 4. Real-infra grounding
- [x] 4.1 Real-Qdrant `RaceDamageDefenseFilterIntegrationTests`: resist=fire → 2 fire races (total==2), immune=poison → 1 (total==1), plain absent. Proves Fields round-trip + parse/filter end-to-end. (b76f334)

## 5. Gates
- [x] 5.1 build 0/0; FULL suite 1716/1716 with Docker; format clean; diff confined to Features/Retrieval/Entities/* + .http + insomnia + tests; .http/insomnia synced; no re-index/migration. Whole-branch review APPROVE.
