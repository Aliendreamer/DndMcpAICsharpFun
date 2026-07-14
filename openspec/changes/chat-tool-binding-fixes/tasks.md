## 1. Add the schema-required regression test (TDD — write it failing first)

- [ ] 1.1 In `DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs`, add a data-driven test that
  drives `SendAsync` once as an authenticated user, pulls each affected `AIFunction` from the
  captured `ChatOptions.Tools`, and asserts the tool's JSON-schema `required` array EXCLUDES its
  optional params. Cover all 9 tools + their optional params: `build_encounter`
  (campaignId/theme/maxCr/minCr), `prep_session` (difficulty), `rate_encounter`
  (campaignId/partyLevels), `plan_level_up` (targetClass/considerDip), `ask_setting_lore`
  (edition), `ask_rules` (ruleTopics/edition), `plan_downtime` (edition), `generate_npc` (maxCr),
  `recommend_build` (targetLevel). Treat "`required` property absent" as "not required" (pass).
  Run it and confirm it FAILS against current code (the params are currently required).

## 2. Fix the 6 no-reorder tools

- [ ] 2.1 In `Features/Chat/DndChatService.cs`, add defaults to the optional params (and
  `CancellationToken toolCt = default`) on: `plan_level_up` (`targetClass = null`,
  `considerDip = null`), `ask_setting_lore` (`edition = null`), `ask_rules` (`ruleTopics = null`,
  `edition = null`), `plan_downtime` (`edition = null`), `generate_npc` (`maxCr = null`),
  `recommend_build` (`targetLevel = null`). Delegate bodies unchanged.

## 3. Fix the 3 reorder tools

- [ ] 3.1 `build_encounter`: reorder the lambda params so required (`difficulty`, `edition`)
  precede optional (`campaignId = null`, `theme = null`, `maxCr = null`, `minCr = null`),
  `CancellationToken toolCt = default` last; keep the delegate body's `BuildForUserAsync` call
  and named args identical.
- [ ] 3.2 `prep_session`: reorder so required (`campaignId`, `theme`, `npcArchetype`) precede
  optional (`difficulty = null`), `toolCt = default` last; body unchanged.
- [ ] 3.3 `rate_encounter`: reorder so required (`monsters`, `edition`) precede optional
  (`campaignId = null`, `partyLevels = null`), `toolCt = default` last; body unchanged.

## 4. Omit-key invocation coverage

- [ ] 4.1 Add omit-key `InvokeAsync` cases on 1-2 representative tools (`build_encounter`,
  `ask_rules`): call with an args object that OMITS the optional keys entirely and assert it does
  NOT throw a missing-required-parameter binding error (a downstream domain error is acceptable,
  as in the existing routing tests — assert the failure is not the binding error).

## 5. Verify and finish

- [ ] 5.1 `dotnet build` clean; the Task 1 regression test now PASSES; full `dotnet test` green
  (including the existing SEC-08 no-userId and per-tool routing tests).
- [ ] 5.2 Rebuild the app image (`docker compose up -d --build app`) and run a live chat smoke on
  `build_encounter` (the former 0/5 tool): a prompt that omits theme/maxCr/minCr must now select
  and bind the tool and return a built encounter (not a fabricated answer).
- [ ] 5.3 Update the change report with results; commit.
