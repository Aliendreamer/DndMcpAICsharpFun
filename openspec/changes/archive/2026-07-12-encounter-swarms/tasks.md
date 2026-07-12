## 1. Representation & display helper

- [ ] 1.1 Add `MonsterQuantity(string Name, int Quantity)` input record in `Features/Encounters/`
- [ ] 1.2 Add `MonsterGrouping.Group(IReadOnlyList<MonsterRef>) → IReadOnlyList<MonsterCount>` (group by `Id`, first-appearance order) with `MonsterCount(MonsterRef Monster, int Count)` record
- [ ] 1.3 Unit-test `MonsterGrouping`: repeats collapse to counts; distinct monsters stay separate; order preserved; empty list → empty

## 2. Build side — anchor-then-fill (EncounterGenerator)

- [ ] 2.1 (TDD) Write generator tests first: anchor + strictly-cheaper minion multiples for an under-fillable band; solo boss when anchor reaches target; uniform-swarm fallback when no cheaper candidate; `MaxMonsters=15` still bounds the loop; overshoot/scarcity `Note` still fires
- [ ] 2.2 Change `BuildAsync` to allow re-selection: record `anchorXp` on the first pick; after the anchor, restrict the greedy candidate pool to `m.Xp < anchorXp` (falling back to the anchor tier when none is cheaper); stop removing selected candidates
- [ ] 2.3 Verify the build↔rate agreement and existing generator tests still pass (flat list with repeats rates identically)

## 3. Rate side — structured pairs (EncounterDesignService)

- [ ] 3.1 (TDD) Write `RateForUserAsync` tests: `{name, 8}` → 8 resolved copies (one lookup); quantity ≤ 0 → 1; clamp at `MaxCopiesPerType=100`; empty list unchanged; ownership/party resolution unchanged
- [ ] 3.2 Change `RateForUserAsync` signature to `IReadOnlyList<MonsterQuantity> monsters`; resolve each name once via `ResolveMonsterAsync`, repeat `Clamp(Quantity,1,100)` times into the flat list; update internal callers

## 4. Chat-tool surfaces (DndChatService)

- [ ] 4.1 Reshape `rate_encounter`'s `monsters` param from `string[]` to `{name, quantity}` pairs; update its description to note quantity support
- [ ] 4.2 Make `build_encounter` echo the grouped result via `MonsterGrouping` ("A hobgoblin leading 8 goblins"); note swarm support in its description
- [ ] 4.3 Update the existing `rate_encounter`/`build_encounter` presence + behavior tests to the new shapes; add a `rate_encounter` test that `{name, quantity}` pairs are accepted and expanded

## 5. UI surface (EncounterPanel)

- [ ] 5.1 Render the built-encounter summary grouped by `MonsterGrouping` ("8× Goblin") on `EncounterPanel` (CampaignTable + Scratch); confirm `OnBuilt` still feeds the flat `MonsterRef` list to the tracker (individual combatants, unchanged)

## 6. Integration & verification

- [ ] 6.1 Extend the real-Qdrant build↔rate agreement integration test to a **swarm** build: build a swarm, rate the exact repeated set, assert identical `Difficulty`
- [ ] 6.2 Build 0/0 + full `dotnet test` green
- [ ] 6.3 Live Playwright smoke (rebuild app container first): build a swarm on the Scratch page → grouped display + N individual combatants land in the tracker; no horizontal overflow desktop + mobile
