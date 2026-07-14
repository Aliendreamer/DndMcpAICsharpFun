# Model Eval Harness — report

## Task 1 — Think-mechanism spike (findings)

Probed the live shared Ollama (`personalcommandcenter-ollama-1`, reachable at
`http://172.18.0.10:11434`; port not published to host) with `qwen3:8b`.

### Result: the top-level `think:false` API field is the only effective lever

| Config | eval_count | thinking field | Notes |
|---|---:|---:|---|
| think ON (default) | 388 / 969* | present (~2.5k chars) | reasoning in a separate `message.thinking` field |
| **`think:false` (top-level API param)** | **12** | absent | ~32× fewer output tokens; clean answer |
| `/no_think` directive (in-repo extraction mechanism) | 369 / 901* | still ~2k chars | **near no-op** — model keeps reasoning |

*Second numbers are a longer prompt; the ratio holds.

- **`think:false` also works WITH tool-calling**: eval_count 41, model still emits a clean
  `calculate_crafting` tool_call. (It over-supplied both `marketValue` AND `rarity` — exactly the
  binding-quality issue the harness is built to catch.)
- **`/no_think` is ineffective on this qwen3:8b / Ollama version** — thinking field stays ~2k chars.
  The extraction path's assumed 4-5× `/no_think` speedup likely came from other factors
  (Temperature=0 + JSON-schema-constrained output length) or an older Ollama; not re-validated here
  but flagged. This does NOT change extraction (out of scope), only corrects this change's mechanism.

### Mechanism decision (corrects design D3 and the plan's default)

The design/plan defaulted to `ChatOptions.AdditionalProperties["think"]=false` or the `/no_think`
directive. Both are wrong for this stack:

- **MEAI.Ollama `OllamaChatClient` 9.7.0-preview cannot send `think`** — the DLL has no `think`
  string; `AdditionalProperties` map into the Ollama `options` sub-object, not the top-level `think`
  sibling. This is the client the app + the original harness plan use.
- **OllamaSharp 5.4.25 CAN** — `ChatRequest.Think` + `ThinkValueConverter`, and its `OllamaApiClient`
  implements MEAI `IChatClient` with an `AbstractionMapper` that honors
  `ChatOptions.RawRepresentationFactory`.

**Chosen mechanism (harness + production):** use OllamaSharp's `OllamaApiClient` as the `IChatClient`
and set think per request via:

```csharp
IChatClient client = new OllamaSharp.OllamaApiClient(new Uri(baseUrl), model);
client = client.AsBuilder().UseFunctionInvocation().Build();

var options = new ChatOptions
{
    Tools = [.. tools],
    // think OFF; omit the factory (or Think = true) for think ON
    RawRepresentationFactory = _ => new OllamaSharp.Models.Chat.ChatRequest { Think = false },
};
```

### Consequence for the plan

- **Task 2** `ModelClientFactory` builds an OllamaSharp `OllamaApiClient` (not MEAI.Ollama
  `OllamaChatClient`); the think toggle is a `RawRepresentationFactory` on the per-call `ChatOptions`,
  not a message mutation.
- **Task 5** (landing lever A) is a **chat-client swap** in `Extensions/ChatExtensions.cs`
  (`OllamaChatClient` → `OllamaApiClient`) plus a `RawRepresentationFactory` on the `ChatOptions`
  built in `DndChatService.SendAsync` — gated on the bench result AND on explicit user sign-off
  (bigger than the design's "one-line change"; needs full `dotnet test` + a live chat smoke).

## Task 4 — Bench results (N=5) & the think decision

Scorecards saved: `scorecard-qwen3-thinkon.txt`, `scorecard-qwen3-thinkoff.txt` (commit 34e4018).
Same binary for both (fair latency); reached the shared Ollama at `http://172.18.0.10:11434`.

| Dimension (/50) | think ON | think OFF | delta |
|---|---:|---:|---:|
| Selection | 40 | **45** | +5 |
| Binding | 28 | **38** | +10 |
| Adherence | 28 | **33** | +5 |
| p50 latency | 8–27 s | **1–5 s** | ~4–8× faster |

**Verdict: LAND lever A (think-off).** Think-off wins on *every* quality dimension AND is 4–8×
faster — think-on actively *derails* qwen3's tool use (e.g. `npc-single` selection 1/5 → 5/5,
`downtime-craft` 0/5 → 5/5 with think off; the reasoning block appears to crowd out clean tool-call
emission). No same-size alternative was needed to decide — think-off is the clear local ceiling win.

### Production landing (Task 5) — mechanism & gate

Landing requires the spike-confirmed client swap (MEAI.Ollama `OllamaChatClient` cannot send `think`):

- `Extensions/ChatExtensions.cs`: `new OllamaChatClient(...)` → `new OllamaApiClient(...)` (OllamaSharp,
  implements MEAI `IChatClient`).
- `Features/Chat/DndChatService.cs`: add `RawRepresentationFactory = _ => new ChatRequest { Think = false }`
  to the `ChatOptions` passed to `GetResponseAsync`.

Both implement `IChatClient` and the chat-wiring tests mock `IChatClient`, so registration/tests are
unaffected; needs a full `dotnet test` + a live chat smoke. **Gated on explicit user sign-off** (a
production chat-client swap, bigger than the design's assumed one-liner).

## Bonus findings (surfaced by the harness — OUT of this spec's scope, for a follow-up)

The harness did its job and exposed two real production issues, both independent of the think setting:

1. **Latent required-param binding bug on 6 chat tools.** `ask_rules`, `plan_downtime`,
   `build_encounter`, `ask_setting_lore`, `generate_npc`, `plan_level_up` declare optional params
   WITHOUT a C# `= null` default, so AIFunctionFactory marks them *required*; when qwen3 omits one,
   MEAI binding throws and the tool never runs. `build_encounter` binds **0/5 even with think-off**
   (it has three such params: theme/maxCr/minCr). This is the SAME bug class the `calculate_crafting`
   fix resolved — the fix generalizes: add `= null` defaults to those params in `DndChatService`.
2. **`craft-magic` adherence 0/5.** The model calls `calculate_crafting` but the final text never
   contains the stub's `2000` — likely calling with `marketValue` instead of `rarity`, or reporting a
   fabricated/re-derived number. Worth a closer look (persona hardening or the same binding issue).

Recommend a follow-up change (`chat-tool-binding-fixes` or similar) to apply the `= null`
generalization and investigate #2; this spec stays scoped to the harness + the think decision.
