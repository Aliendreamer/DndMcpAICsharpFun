## 1. EncounterMath core (pure, both editions, TDD)

- [ ] 1.1 Add `Features/Encounters/EncounterMath.cs`: CR→XP table (CR 0..30, standard values); 2014 per-level Easy/Medium/Hard/Deadly thresholds + 2024 per-level Low/Moderate/High budgets; the 2014 count→multiplier table (×1..×4) with the <3 / ≥6 PC band shift. Failing `EncounterMathTests` first: CR→XP spot-checks (CR 0,1/2,1,5,10,20,30), 2014 thresholds + 2024 budgets spot-checked per level vs known DMG values, multiplier per monster count, and the party-size band shift.
- [ ] 1.2 Add difficulty classification (pure): 2014 = classify `sum×multiplier` vs 4 thresholds; 2024 = classify raw `sum` vs 3 budgets. Failing tests at band boundaries (just-below/just-above each threshold) for both editions.
- [ ] 1.3 Implement; make 1.1–1.2 pass; build 0/0.

## 2. EncounterAssessor (TDD)

- [ ] 2.1 Add `Features/Encounters/EncounterAssessor.cs` + result DTO (`EncounterAssessment`: totalXp, adjustedXp?, band, neighbouring-boundary context). Input = party levels + monsters(CR/XP) + edition. Failing `EncounterAssessorTests`: correct band for a sample party+monsters in 2014 (with multiplier) and 2024 (without); context boundaries reported.
- [ ] 2.2 Implement over `EncounterMath`; make 2.1 pass; build 0/0.

## 3. EncounterGenerator (TDD, fake retrieval)

- [ ] 3.1 Define a monster-candidate retrieval seam (`IEncounterMonsterSource` or reuse an existing entity-search interface) returning `{id, name, cr, xp}` filtered by `type=Monster`, CR band, keyword, srd, edition. Add `Features/Encounters/EncounterGenerator.cs` + result DTO (proposed monsters + the Assessor's `EncounterAssessment` + a not-fully-matched flag).
- [ ] 3.2 Failing `EncounterGeneratorTests` with a FAKE candidate source: builds a set that assesses (via the real `EncounterAssessor`) to the requested band; build-and-rate agree (assert the returned difficulty == Assessor over the same monsters); theme/CR filters passed through to the source; graceful fallback (sparse source) returns the closest set flagged not-fully-matched; 2014 count/multiplier loop lands in-band (bounded iteration).
- [ ] 3.3 Implement the generator (greedy fit; 2014 targets the assessed band, bounded search); make 3.2 pass; build 0/0.

## 4. EncounterDesignService + real monster retrieval

- [ ] 4.1 Add `Features/Encounters/EncounterDesignService.cs` composing assessor + generator + the real monster source (implement `IEncounterMonsterSource` over the existing entity search / `get_entity` — reuse `EntityFilters` type=Monster + crNumeric band + keyword + srd + edition). Add `RateForUserAsync` / `BuildForUserAsync` that resolve the party from the caller's campaign heroes after an ownership check (mirror `CharacterResolutionService.*ForUserAsync` / `GetSnapshotForUserAsync`), or from explicit `partyLevels`; throw Unauthorized on a foreign campaign; clear error when neither party source is given. DI-register.
- [ ] 4.2 Tests: monster-source resolves CR→XP from real entity fields; ownership check (negative: caller B's campaign → throws) — reuse the character-tool test harness pattern; unit-level with fakes where possible.

## 5. Per-user MCP tools

- [ ] 5.1 In `Features/Chat/DndChatService.cs`, inside the authenticated `long.TryParse(idClaim, out userId)` block, add `rate_encounter(campaignId?, partyLevels?, monsters[], edition)` and `build_encounter(campaignId?, difficulty, edition, theme?, constraints?)` via `AIFunctionFactory.Create`, closing over `userId`, routing through the service's `*ForUserAsync`. Descriptions state the tools are edition-parameterized and party comes from the caller's campaign or explicit levels.
- [ ] 5.2 Confirm the tools are NOT on the shared-key `Features/Mcp` surface (grep); no new HTTP route → `.http`/`.insomnia` unchanged. Tool-wiring test (per-user, SEC-08 closure) mirroring the multiclass tool test.

## 6. Verify + review

- [ ] 6.1 Full build 0/0 + full suite green (incl. Testcontainers).
- [ ] 6.2 Drive the flow (per `verify`): via the chat/MCP client, `build_encounter` a Hard undead fight for a sample party, then `rate_encounter` the same monsters — confirm identical band; confirm a foreign-campaign call is rejected. (Defer to a live run if the stack is down; unit/ownership tests cover the logic.)
- [ ] 6.3 Whole-branch opus review; cross-check every ADDED requirement (both editions' math, assessor context, build==rate, ownership, fallback). Address findings; stop for the user's commit/archive directive.
