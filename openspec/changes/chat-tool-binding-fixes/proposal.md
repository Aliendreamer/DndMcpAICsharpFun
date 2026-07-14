## Why

The model-eval-harness surfaced a latent production bug: several chat tools declare
optional parameters with nullable types (`string?`, `double?`) but **no C# default value**.
`AIFunctionFactory` decides "optional" from `ParameterInfo.HasDefaultValue`, not from
nullability — so those parameters land in the tool's JSON-schema `required` array. When the
local model (qwen3:8b) omits one, `Microsoft.Extensions.AI` function binding throws
*"missing required parameter"*, the tool silently fails, and the model tends to fabricate an
answer instead. The harness measured `build_encounter` binding **0/5** because of this. The
`calculate_crafting` fix already proved the remedy (add `= null`); this change generalizes it
to every affected tool.

## What Changes

- Add `= null` (and `CancellationToken toolCt = default`) defaults to the optional nullable
  parameters on **9 chat tools** so they are schema-optional: `build_encounter`,
  `plan_level_up`, `ask_setting_lore`, `ask_rules`, `plan_downtime`, `generate_npc`,
  `prep_session`, `rate_encounter`, `recommend_build`.
- Reorder the parameter lists of the 3 tools where an optional parameter currently precedes a
  required one (`build_encounter`, `prep_session`, `rate_encounter`) so required parameters
  come first — a C# language requirement for trailing defaults. Binding is by name, so the
  reorder is invisible to the model, and `null` defaults preserve existing call semantics.
- Add a data-driven regression test asserting every chat tool's JSON-schema `required` array
  excludes its optional parameters, plus omit-key invocation coverage on representative tools.
- **Non-goal (documented, no code change):** the harness's `craft-magic` adherence 0/5 is a
  mis-specified harness check (prompt asks "how long", check asserts the gold value), not a
  production bug — confirmed by a live smoke returning the correct numbers.

## Capabilities

### New Capabilities

- `chat-tool-optional-binding`: the cross-cutting invariant that every in-process chat
  `AIFunction`'s optional parameters are declared schema-optional (via a C# default value), so
  the model may omit them without a binding failure — enforced by a schema-level regression test.

### Modified Capabilities

<!-- None: no existing capability's behavioral requirements change; this adds a new invariant. -->

## Impact

- `Features/Chat/DndChatService.cs` — parameter defaults + minor reorders on 9 tool lambdas
  (behavior-preserving; the delegate bodies and service calls are unchanged).
- `DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs` — new schema-required regression test
  and omit-key invocation cases.
- No API/MCP endpoint contract changes, no persona changes, no extraction-path changes.
