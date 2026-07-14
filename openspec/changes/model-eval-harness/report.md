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

### Decision inputs still to gather (Task 4 bench)

Three scorecards: `qwen3:8b think-on` (baseline), `qwen3:8b think-off` (lever A), and one same-size
alternative if pulled. The `think:false` latency win is already visually enormous (12 vs 388 tokens);
the bench confirms it does not regress selection/binding/adherence before we touch production.
