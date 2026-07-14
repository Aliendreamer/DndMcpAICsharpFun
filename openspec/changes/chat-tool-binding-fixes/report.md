# Chat Tool Binding Fixes — report

## Outcome

Generalized the `calculate_crafting` `= null` fix to every affected chat tool. `AIFunctionFactory`
marks a parameter required unless it has a C# default value (nullability is not enough), so 9 tools
declared optional-but-required params that qwen3 could not omit without a MEAI binding failure.

## What shipped

Fixed 9 tools in `Features/Chat/DndChatService.cs` (parameter defaults; behavior-preserving —
bodies, service calls, tool names, and descriptions unchanged):

- **Defaults only** (Task 1, commit 86c3396): `plan_level_up`, `ask_setting_lore`, `ask_rules`,
  `plan_downtime`, `generate_npc`, `recommend_build`.
- **Reorder + defaults** (Task 2, commit ac582fc) — optional param preceded a required one, so C#
  forced required-first ordering + `CancellationToken toolCt = default`: `build_encounter`,
  `prep_session`, `rate_encounter`. Binding is by name, so the reorder is invisible to the model and
  the existing named-argument tests are unaffected.

## Tests

- **Schema regression (all 9 tools):** two data-driven theories assert every optional param is
  excluded from its tool's JSON-schema `required` array — the precise guard the original null-passing
  tests lacked. RED before each fix, GREEN after.
- **Omit-key binding:** `BuildEncounterTool_binds_when_the_model_omits_all_optional_params` invokes
  `build_encounter` with only `difficulty`/`edition` (optional keys omitted, not null) and asserts it
  reaches `EncounterDesignService`'s `"supply campaignId or partyLevels"` guard — proving binding
  succeeded rather than throwing a missing-required-parameter error.
- **Full suite:** 1360/1360 green, build clean (warnings-as-errors). (Baseline was 1344; +16 from the
  two new theories' cases and the omit-key fact.)

## Live verification

- App image rebuilt (`docker compose up -d --build app`), healthy.
- **Ollama probe (reliable path; browser chat is qwen3-flaky on complex multi-tool prompts):** the
  shared Ollama (`qwen3:8b`, `think:false`) given the fixed `build_encounter` tool emitted a clean
  call `build_encounter(difficulty:"Hard", edition:"2014")` — supplying only the two required params
  and **omitting all four optionals** (`campaignId`/`theme`/`maxCr`/`minCr`). This is exactly the call
  shape that bound 0/5 pre-fix; 16 s, eval_count 30, no fabrication.
- The browser chat driven at the same prompt entered a long function-invocation loop with **no**
  `ArgumentException` / "missing required parameter" in the app logs — consistent with binding
  working (a pre-fix build_encounter call would have thrown quickly). The slowness is the documented
  qwen3 flakiness on complex prompts, not a binding regression.

## Finding #2 (out of scope) — resolved as not-a-bug

The model-eval-harness `craft-magic` adherence 0/5 was a **mis-specified harness check**, not a
production bug: the scenario prompt asks *"how long"* (time) but the adherence check asserts the text
contains `"2000"` (the gold cost). The model correctly answers the time question and omits the gold.
The production live smoke during the harness landing returned the correct 2000/20000 for magic-item
crafting. No code change; the archived harness is left untouched.
