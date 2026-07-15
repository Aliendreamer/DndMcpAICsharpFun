## 1. Switch chat to think-on (TDD)

- [ ] 1.1 Add a guard test in `DndMcpAICsharpFun.Tests/Chat/` (follow the existing `DndChatServiceTests`
  pattern — it injects a fake `IChatClient` that captures the `ChatOptions` passed to
  `GetResponseAsync`). Assert that the captured per-request `ChatOptions.RawRepresentationFactory` does
  NOT produce an OllamaSharp `ChatRequest` with `Think == false` (either the factory is null, or invoking
  it yields `Think != false`). Confirm it FAILS against the current think-off code (RED).
- [ ] 1.2 In `Features/Chat/DndChatService.cs`, REMOVE the
  `RawRepresentationFactory = _ => new OllamaSharp.Models.Chat.ChatRequest { Think = false }` line from
  the `ChatOptions` built in `SendAsync`, so qwen3 reasons in its default think-on mode (mirroring
  `Tools/ModelEval/ModelClientFactory` think-on, which omits the factory). Rewrite the adjacent comment
  to justify think-ON (reasoning correctness; selection is a rig-measured 36/36 tie). Test goes GREEN.
- [ ] 1.3 `dotnet build` 0/0; run the chat tests (`--filter ~DndChatService`) then the FULL `dotnet test`
  suite (Docker up) — must stay green (no test encoded think-off). Commit.

## 2. Live validation + report

- [ ] 2.1 Rebuild the app image (`docker compose up -d --build app`), wait healthy. Live smoke a
  SINGLE-rule question whose single-shot retrieval returns the rule (e.g. "How does the Dodge action work
  in combat? Cite the rule.") and confirm: a correct, PHB-cited, prose answer; NO reasoning-trace markup
  (`<think>`) in the shown/persisted turn (inspect the rendered bubble and the `ChatTurns` row); tool
  selection still works. If the browser SignalR circuit drops on latency, validate the persisted
  assistant turn from the DB instead (per dev-flow flaky-smoke guidance).
- [ ] 2.2 Write the change report (`report.md`): the mode switch, the guard test, the live-smoke result
  (correct cited answer + no `<think>` leak), and the honest caveat that multi-rule questions still await
  the deferred multi-hop retrieval. Commit.
