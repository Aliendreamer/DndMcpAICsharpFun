## 1. Switch chat to think-on (TDD)

- [x] 1.1 Add a guard test in `DndMcpAICsharpFun.Tests/Chat/` (follow the existing `DndChatServiceTests`
  pattern — it injects a fake `IChatClient` that captures the `ChatOptions` passed to
  `GetResponseAsync`). Assert that the captured per-request `ChatOptions.RawRepresentationFactory` does
  NOT produce an OllamaSharp `ChatRequest` with `Think == false` (either the factory is null, or invoking
  it yields `Think != false`). Confirm it FAILS against the current think-off code (RED).
- [x] 1.2 In `Features/Chat/DndChatService.cs`, REMOVE the
  `RawRepresentationFactory = _ => new OllamaSharp.Models.Chat.ChatRequest { Think = false }` line from
  the `ChatOptions` built in `SendAsync`, so qwen3 reasons in its default think-on mode (mirroring
  `Tools/ModelEval/ModelClientFactory` think-on, which omits the factory). Rewrite the adjacent comment
  to justify think-ON (reasoning correctness; selection is a rig-measured 36/36 tie). Test goes GREEN.
- [x] 1.3 `dotnet build` 0/0; run the chat tests (`--filter ~DndChatService`) then the FULL `dotnet test`
  suite (Docker up) — must stay green (no test encoded think-off). Commit.

## 2. Bound the history window sent to the model (TDD)

- [x] 2.1 Add a guard test in `DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs`: seed a
  `DndChatService` `History` with MORE than N (=12) messages (e.g. via `LoadHistoryAsync` against a
  fake repo, or by adding turns), send a request through a `FakeChatClient`, and assert
  `client.LastMessages` is exactly `[system persona] + History.TakeLast(12)` — one system message then
  the last 12 history messages, no older ones. Add a second test: with ≤12 history messages, the full
  history is sent. Confirm the first test FAILS against the current `messages.AddRange(History)` (RED).
- [x] 2.2 In `DndChatService.SendAsync`, replace `messages.AddRange(History)` with a bounded window:
  send the last `MaxModelHistoryMessages` (a `const int = 12`) history messages
  (`messages.AddRange(History.TakeLast(MaxModelHistoryMessages))`). The single system persona message is
  still prepended; full history is still loaded/displayed/persisted — only the model input is windowed.
  Add a brief comment (why: think-on cost grows with prompt size; bound latency regardless of chat
  length). Tests GREEN.
- [x] 2.3 `dotnet build` 0/0; `--filter ~DndChatService` green; FULL `dotnet test` green. Commit.

## 3. Live validation + report

- [x] 3.1 Rebuild the app image (`docker compose up -d --build app`), wait healthy. Live smoke:
  (a) a SINGLE-rule question whose single-shot retrieval returns the rule ("How does the Dodge action
  work in combat? Cite the rule.") → confirm a correct, PHB-cited answer with NO `<think>` markup in the
  persisted `ChatTurns` row; (b) THE KEY REGRESSION CHECK — after a LONG conversation (>12 turns), a new
  question still returns within a bounded time (~fresh-conversation latency, not minutes) because only the
  last 12 turns are sent. Validate via the persisted assistant turn + timing from the DB (per dev-flow
  flaky-smoke guidance; the browser circuit drops on long think-on waits).
- [x] 3.2 Write the change report (`report.md`): the mode switch, both guard tests, the live-smoke result
  (correct cited answer + no `<think>` leak + bounded latency after the history cap), and the honest
  caveats (list-iness unchanged; multi-rule questions still await the deferred multi-hop retrieval;
  think-on is ~45 s/answer even bounded). Commit.
