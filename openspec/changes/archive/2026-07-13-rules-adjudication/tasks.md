## 1. RuleSources + RulesAdjudicationService

- [ ] 1.1 (TDD) Tests: service scopes retrieval to `RuleSources` (`RetrievalQuery.SourceBooks` contains the core rulebooks) at the higher `TopK`; projects results to cited passages (source book + section/title); empty retrieval → explicit empty `RulesRulingResult`
- [ ] 1.2 `Features/Rules/RuleSources` (fixed core-rulebook display-name set + `RuleTopK`) + `RulesRulingResult(Passages, ScopedBooks)` (reuse `Features/Lore`'s `CitedPassage`) + `RulesAdjudicationService.AskAsync(question, edition, ct)` — no ownership/userId

## 2. DI wiring

- [ ] 2.1 Register `RulesAdjudicationService`; make `AddDndChat` pull it in (mirror `AddLore`/`AddEncounters`); add the registration to `FullContainerScopeValidationTests.BuildServiceCollection` (or confirm it's transitively covered via `AddDndChat`); run the scope-validation filter green

## 3. ask_rules chat tool

- [ ] 3.1 Register `ask_rules(question, edition?)` in `DndChatService` (authenticated block; NOT ownership-gated; no userId/campaignId arg); description binds the ruling contract (compose only from returned cited passages; name rules combined + cite; flag RAW-vs-DM; honest "rules don't cover this" on empty); `edition` → null default = no filter
- [ ] 3.2 (TDD) Guard tests: `ask_rules` present-when-authenticated / absent-when-not; schema exposes NO `userId` and NO `campaignId`; a routing test that invoking it reaches the service and returns a `RulesRulingResult`

## 4. Real-Qdrant non-vacuity scoping test

- [ ] 4.1 Seed a rule block (`source_book` = a core rulebook, e.g. PHB "Grappling") AND an off-scope Monster Manual block; build a real `RagRetrievalService`; run `RulesAdjudicationService`; assert the passages include the rule block and EXCLUDE the MM block (identical embedding vectors so only the source-book filter discriminates — mutation-verify it goes RED without the scope). Reuse the setting-aware `SettingLoreScopingIntegrationTests` seeding pattern

## 5. Verification

- [ ] 5.1 Build 0/0 + full `dotnet test` green (real Postgres + Qdrant)
- [ ] 5.2 **DATA-INVARIANT check** (live): confirm the real rule blocks (Grappling, Prone, Cover) carry `source_book ∈ RuleSources` via `GET /retrieval/search` — if the strings differ from `RuleSources`, fix the constant and re-run Task 1's tests
- [ ] 5.3 Rebuild app container; live smoke (Ollama): ask "can I grapple a creature that's already prone?" → grounded ruling naming Grappling AND Prone, cited to the rulebooks, flagging any RAW-vs-DM interaction; a nonsense rules question → honest "the rules don't directly cover this". No new UI, so no overflow gate
- [ ] 5.4 Final opus whole-branch review; on READY, refresh the `companion_roadmap` memory
