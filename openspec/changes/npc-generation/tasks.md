## 1. NpcArchetypes + NpcStatBlock/GeneratedNpc + NpcGenerationService

- [ ] 1.1 (TDD) Tests (substitute `IEntityRetrievalService`): a valid archetype (exact-name hit) → `GeneratedNpc` with `ArchetypeInCorpus=true` + a grounded `NpcStatBlock` (name/cr/ac/hp/abilities/canonicalText from the entity, sourceBook cited); an unknown archetype (no hit or non-exact-name hit) → `ArchetypeInCorpus=false` + `AvailableArchetypes = NpcArchetypes.Common`, NO unrelated block; a resolved archetype whose CR exceeds `maxCr` → `ArchetypeInCorpus=false` + roster
- [ ] 1.2 `Features/Npc/`: `NpcArchetypes` (curated `Common` name list); `NpcStatBlock` + `GeneratedNpc` records; `NpcGenerationService.GenerateAsync(concept, archetype, double? maxCr, ct)` — `SearchDiagnosticAsync(QueryText=archetype, Type=Monster, TopK=1)`, exact normalized-name match on the top hit (reuse `EntityNameIndex.Normalize`), project `MonsterFields`/`CanonicalText` → `NpcStatBlock`, read CR via the existing monster CR reader for the `maxCr` gate; miss/over-CR → not-in-corpus + roster. No LLM call

## 2. DI wiring

- [ ] 2.1 Register `NpcGenerationService`; `AddDndChat` pulls in its `Add*` (mirror `AddLore`/`AddRules`); confirm it's in the `FullContainerScopeValidationTests` graph (transitive via `AddDndChat`); run the scope-validation filter green

## 3. generate_npc chat tool

- [ ] 3.1 Register `generate_npc(concept, archetype, maxCr?)` in `DndChatService` (authenticated block; NOT ownership-gated; no userId/campaignId arg); description binds the contract (pick the fitting archetype; stats come from the returned block, cited; invent name/personality/appearance/hook; never invent stat numbers; re-pick from availableArchetypes on a miss)
- [ ] 3.2 (TDD) Guard tests: `generate_npc` present-when-authenticated / absent-when-not; schema exposes NO `userId`/`campaignId`; a routing test that invoking it reaches the service and returns a `GeneratedNpc`

## 4. Real-Qdrant grounding test

- [ ] 4.1 Real-Qdrant (or entity-store) integration test: seed a "Spy" Monster entity with real fields; `GenerateAsync(concept, "Spy", null, ct)` → `ArchetypeInCorpus=true` + the grounded stats; a bogus archetype → not-in-corpus + roster (no unrelated block). Reuse the entity-store test fixtures if a lighter store-level test suffices

## 5. Verification

- [ ] 5.1 Build 0/0 + FULL `dotnet test` green (real Postgres + Qdrant) — the FULL suite, not a filter
- [ ] 5.2 Rebuild app container; live smoke (Ollama): "generate a shifty Sharn dockworker" → the LLM picks Spy/Commoner, the tool returns grounded stats, the persona presents an NPC with real AC/HP/CR (cited) + invented name/personality/hook; a nonsense archetype triggers the re-pick path. No new UI → no overflow gate
- [ ] 5.3 Final opus whole-branch review; on READY, refresh the `companion_roadmap` memory
