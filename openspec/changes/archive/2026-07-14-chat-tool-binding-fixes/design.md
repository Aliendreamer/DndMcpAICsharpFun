## Context

`DndChatService.SendAsync` registers the per-user in-process chat tools as `AIFunction`s via
`AIFunctionFactory.Create(lambda, name, description)`. `AIFunctionFactory` derives each
parameter's JSON schema from reflection and marks a parameter **required** unless
`ParameterInfo.HasDefaultValue` is true. Nullability alone does not make a parameter optional.

Several tools were written with nullable-but-defaultless optional parameters, so those
parameters are required in the emitted schema. When qwen3:8b (now running think-off after the
model-eval-harness change) omits one, MEAI binding throws *"missing required parameter"* and the
tool never executes — the model then fabricates. The harness measured `build_encounter` binding
0/5 for exactly this reason, while `calculate_crafting` — already fixed with `= null` defaults —
binds 5/5.

Affected tools and their optional-but-required parameters:

| Tool | Optional params to fix | Reorder? |
|---|---|---|
| `build_encounter` | campaignId, theme, maxCr, minCr | yes (optional precedes required difficulty/edition) |
| `prep_session` | difficulty | yes (difficulty precedes required npcArchetype) |
| `rate_encounter` | campaignId, partyLevels | yes (optionals precede required monsters/edition) |
| `plan_level_up` | targetClass, considerDip | no |
| `ask_setting_lore` | edition | no |
| `ask_rules` | ruleTopics, edition | no |
| `plan_downtime` | edition | no |
| `generate_npc` | maxCr | no |
| `recommend_build` | targetLevel | no |

`calculate_crafting` is the reference implementation (optional params trailing with `= null`,
no `CancellationToken`). Its live behavior was re-confirmed by a production smoke during the
model-eval-harness landing.

## Goals / Non-Goals

**Goals:**

- Make every optional chat-tool parameter schema-optional via a C# default value, so the model
  can omit it without a binding failure.
- Keep the change behavior-preserving: same delegate bodies, same service calls, same tool
  names and caller-supplied parameter names; `null`/`default` were already the "omitted" value
  each service expected.
- Lock the fix in with a schema-level regression test that would have caught the original bug
  (the existing invocation tests passed every key as null and masked it).

**Non-Goals:**

- The harness `craft-magic` adherence 0/5 finding — it is a mis-specified harness check (prompt
  asks "how long", check asserts the gold `2000`), not a production bug; documented, no code
  change, archived harness left untouched.
- No changes to the extraction-path `IChatClient`, the chat persona, MCP/API contracts, or the
  `search_web` / character-scoped tools that already have correct signatures.

## Decisions

1. **Mechanism = C# default values, not attributes.** `AIFunctionFactory` keys optionality off
   `HasDefaultValue`; there is no "optional" attribute. Add `= null` to nullable optional
   params, `= default` to the trailing `CancellationToken` once any earlier param has a default.
   This matches the proven `calculate_crafting` fix.
2. **Reorder only where C# forces it.** For `build_encounter`, `prep_session`, `rate_encounter`,
   move required params ahead of optional (defaulted) ones. Binding is by name, so the model
   sees no difference; the SEC-08 test already asserts `userId` never appears as a caller
   parameter, and that stays true.
3. **Preserve null semantics.** Each service already treats a null optional argument as "not
   supplied" (e.g. `considerDip ?? false`, null `campaignId` → derive party from partyLevels,
   null `edition` → search all editions). Defaulting to null is identical to the current
   all-keys-null test calls, so no service logic changes.
4. **Regression test = schema `required` assertion.** A single data-driven test drives
   `SendAsync` once (authenticated), pulls each affected `AIFunction` from the captured
   `ChatOptions`, and asserts each tool's `JsonSchema.required` excludes its optional param
   names. Add omit-key `InvokeAsync` cases on 1-2 representative tools (e.g. `build_encounter`,
   `ask_rules`) to prove binding succeeds with the keys absent.

## Risks / Trade-offs

- **Reordering risk:** a mismatch between the reordered signature and the delegate body would
  compile-fail or misroute arguments. Mitigated by name-based, minimal edits and the existing
  per-tool routing tests (`BuildEncounterTool_forwards_...`, `RateEncounterTool_routes_...`)
  plus the SEC-08 no-userId test.
- **Schema-shape assumption:** the test reads `JsonSchema` as a `JsonElement` with a `required`
  array; if a tool has *no* optional params it may omit `required` entirely — the test must
  treat "property absent" as "not required" (pass), not as an error.
- **Low blast radius:** the change is confined to `DndChatService` tool declarations and one
  test file; no runtime wiring, DI, or contract changes, so the full suite plus a single live
  `build_encounter` smoke is sufficient verification.
