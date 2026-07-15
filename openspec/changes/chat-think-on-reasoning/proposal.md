## Why

The conversational chat hardcodes qwen3 into **think-off** mode (`DndChatService`), chosen when a
prior bench found think-off marginally better at tool *selection* and 4–8× faster. But a diagnosis of a
failed live rules smoke proved think-off is exactly what breaks multi-rule **reasoning**: handed the
correct grapple + prone passages, qwen3 think-off answers "No, you can't grapple a prone creature"
(wrong) while think-**on** answers correctly and grounds it. A fresh rig measurement (persona-v2, 5
runs) shows the original justification no longer holds: think-off vs think-on tool **selection/binding
is a tie (36/36)** — the only real cost of think-on is latency. So switching the chat to think-on fixes
the reasoning wall at no selection cost, and a same-hardware reasoning-model bench confirmed no
8 GB-fit model beats qwen3:8b, so the lever is the *mode*, not the model.

## What Changes

- Switch the conversational chat's model invocation from **think-off to think-on** by removing the
  `RawRepresentationFactory = _ => new ChatRequest { Think = false }` override from the `ChatOptions`
  built in `DndChatService` (mirroring `Tools/ModelEval`'s think-on path — omitting the factory lets
  qwen3 reason by default). The persona and tool set are unchanged.
- Rewrite the adjacent code comment to justify think-on (reasoning correctness; selection is a
  rig-measured tie) so the rationale doesn't silently rot back to think-off.
- Add a guard test that the chat's per-request `ChatOptions` does NOT force `Think=false`, preventing a
  silent revert.

## Capabilities

### New Capabilities

- `chat-reasoning-mode`: the conversational chat invokes the local model in its native reasoning
  ("think-on") mode so multi-rule questions compose correct grounded answers, and the model's internal
  reasoning trace is never surfaced to the user.

### Modified Capabilities

<!-- None: think-mode is not covered by an existing spec; this introduces one capability. -->

## Impact

- `Features/Chat/DndChatService.cs` — remove the think-off `RawRepresentationFactory`; update the comment.
- `DndMcpAICsharpFun.Tests/Chat/*` — new guard test on the chat's `ChatOptions` (no forced `Think=false`).
- No HTTP endpoint / DB / schema / persona change → no `.http`/insomnia update. The persona baked into
  the app image is unchanged, but the mode lives in code, so a normal image rebuild deploys it.
- **Accepted cost:** ~2.5–4× answer latency (p50 up to ~16 s on tool answers; real answers with
  retrieval likely 20–40 s; casual ~1.6 s→3.5 s). Slower answers raise the chance the Blazor SignalR
  circuit times out mid-answer — flagged as a separate UX hardening, out of scope here.
- **Out of scope (separate follow-ups):** multi-hop `ask_rules` retrieval (so a multi-rule question
  always fetches every rule set); split think-mode (unjustified — selection is a tie); circuit-timeout
  hardening.
