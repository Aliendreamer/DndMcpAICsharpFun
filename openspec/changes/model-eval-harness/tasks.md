## 1. Spike: how MEAI OllamaChatClient toggles qwen3 `think`

- [ ] 1.1 Against the running Ollama, determine how to make MEAI's `OllamaChatClient` send `think:false` (try `ChatOptions.AdditionalProperties["think"]=false`; else the request-body path the extraction client uses). Confirm think-off actually suppresses the `<think>` block (compare eval_count / latency for a thinking prompt with think on vs off). Record the exact mechanism — the harness (D3) and the production chat path (Task 5) both need it.

## 2. Tools/ModelEval console scaffold

- [ ] 2.1 Create `Tools/ModelEval/ModelEval.csproj` + `Program.cs` mirroring `Tools/SchemaGenerator` (console, `Microsoft.Extensions.AI` + `Microsoft.Extensions.AI.Ollama` refs). Parse args: `--model` (default `qwen3:8b`), `--think on|off` (default on), `--runs N` (default 5), `--base-url` (default `http://localhost:11434`).
- [ ] 2.2 Build the client: `new OllamaChatClient(baseUrl, model).AsBuilder().UseFunctionInvocation().Build()`, with the think mechanism from Task 1 applied to the per-call `ChatOptions`.
- [ ] 2.3 Verify it builds (`dotnet build Tools/ModelEval/ModelEval.csproj`, dangerouslyDisableSandbox) and runs a trivial "hello" prompt end-to-end against Ollama. Commit.

## 3. Tool schemas (stubbed) + scenario set

- [ ] 3.1 Re-declare the top chat tools as `AIFunction`s with STUBBED delegates returning canned results: `calculate_crafting` (canned 750/2000), `generate_npc`/`generate_npc_party` (canned member(s)), `ask_rules`/`plan_downtime`/`ask_setting_lore` (canned cited passage; also an EMPTY variant), `build_encounter` (canned monster). Keep signatures identical to the real registrations (single/`= null`-defaulted params) so binding behavior matches production.
- [ ] 3.2 Define the scenario set (design.md D4): a record `Scenario(string Prompt, string? ExpectedTool, Func<string,bool> AdherenceCheck)` and the ~10 scenarios incl. the negative (no-tool) and the empty-result (fabrication) cases.
- [ ] 3.3 Commit.

## 4. Scorecard: N-run scoring + table

- [ ] 4.1 For each scenario, run the model `N` times; per run capture: invoked tool name (or none), bind ok (no MEAI exception), adherence-check result, wall-clock ms + ms-to-first-tool-call. A MEAI binding exception → bind-fail for that run (caught, not fatal to the harness).
- [ ] 4.2 Aggregate to per-scenario `sel N/N`, `bind N/N`, `adhere N/N`, `p50/p95 ms`; print a table + a totals row + the run config (model, think, runs) as a header.
- [ ] 4.3 Manual bench run (controller): capture three scorecards — `qwen3:8b think-on` (baseline), `qwen3:8b think-off` (lever A), and one same-size alternative if pulled (e.g. `qwen2.5:7b`, lever C). Save the scorecards to the change dir / report for the decision.

## 5. Land the measured think decision

- [ ] 5.1 Read the scorecards: if think-off improves latency with no selection/binding/adherence regression, set the chat path to `think:false` in `Extensions/ChatExtensions.cs` (apply the Task 1 mechanism to the chat client's default `ChatOptions`); else keep think-on and record why.
- [ ] 5.2 If the chat client changed, rebuild the app (`docker compose up -d --build app`) and re-run a quick live chat smoke (or the ollama probe) to confirm chat still selects+binds a tool correctly and is faster. Full `dotnet test` green (the chat-wiring tests still pass).
- [ ] 5.3 Record the decision + the scorecards in the finish notes (and a dev-flow lesson if the think-off delta is notable). Commit.
