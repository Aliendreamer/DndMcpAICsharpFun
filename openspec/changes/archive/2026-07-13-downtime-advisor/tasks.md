## 1. DowntimeSources + DowntimePlanResult + DowntimeService + DI

- [ ] 1.1 (TDD) Tests (substitute `IRagRetrievalService`): `PlanAsync` scopes retrieval to `DowntimeSources.Books` (`RetrievalQuery.SourceBooks` contains XGE + DMG) at `DowntimeSources.TopK`; projects results to cited passages (source book + section/title); empty retrieval → explicit empty `DowntimePlanResult`
- [ ] 1.2 `Features/Downtime/`: `DowntimeSources` (fixed downtime source-book display-name set `{"Xanathar's Guide to Everything","Dungeon Master's Guide 2014"}` + `TopK`); `DowntimePlanResult(Passages, ScopedBooks)` (reuse `Features/Lore`'s `CitedPassage`); `DowntimeService.PlanAsync(activity, edition, ct)` — scoped `RetrievalQuery(activity, Version:edition, TopK:DowntimeSources.TopK, SourceBooks:DowntimeSources.Books)` → project → result. No LLM call. Mirror `Features/Rules/RulesAdjudicationService`. `AddDowntime` (AddScoped) pulled into `AddDndChat`; confirm the `FullContainerScopeValidationTests` graph (transitive)

## 2. plan_downtime chat tool

- [ ] 2.1 Register `plan_downtime(activity, edition?)` in `DndChatService` (authenticated block; NOT ownership-gated; no userId/campaignId; `edition` → null default = no filter); description binds the contract (compose the plan — time/gold cost + outcome — only from the returned cited passages, cite each; honest "the rules don't detail this" on empty; never invent times/costs)
- [ ] 2.2 (TDD) Guard tests: `plan_downtime` present-when-authenticated / absent-when-not; schema exposes NO `userId`/`campaignId`; a routing test that invoking it reaches the service and returns a `DowntimePlanResult`

## 3. Real-Qdrant non-vacuity scoping test

- [ ] 3.1 Real-Qdrant test (reuse the `RulesScopingIntegrationTests` seeding pattern): seed an XGE downtime block (`source_book`="Xanathar's Guide to Everything") + an off-scope block (e.g. `source_book`="Monster Manual 2014"), identical embedding vectors; `PlanAsync("craft armor", null, ct)` → passages include the XGE block and EXCLUDE the off-scope block (mutation-verify RED without the scope)

## 4. Verification (after XGE ingest completes)

- [ ] 4.1 Build 0/0 + FULL `dotnet test` green (real Postgres + Qdrant) — the FULL suite, not a filter
- [ ] 4.2 **DATA-INVARIANT check** (live): confirm the real XGE downtime blocks carry `source_book ∈ DowntimeSources.Books` via `GET /retrieval/search?q=crafting` / `?q=downtime` — if XGE's `source_book` differs from `"Xanathar's Guide to Everything"`, fix the constant and re-run Task 1's tests
- [ ] 4.3 Rebuild app container; live smoke (Ollama, after XGE blocks land): "my ranger wants to craft plate armor — how long and how much?" → a grounded crafting plan (time + gold cost) cited to XGE; a nonsense activity → honest "the rules don't detail this". No new UI → no overflow gate
- [ ] 4.4 Final opus whole-branch review; on READY, refresh the `companion_roadmap` memory
