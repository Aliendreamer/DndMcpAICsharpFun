## Why

qwen3:8b (the shared `Ollama:ChatModel`) keeps surfacing weaknesses across every chat surface —
tool-selection misrouting, multi-param MEAI arg-binding failures, fabrication when a tool returns
empty, prose-rule disobedience, and latency (the flaky slow chat smokes this session). The roadmap's
assumed fix — "upgrade to a stronger local model" — was reframed during exploration: the **8 GB RTX
5070 Laptop GPU boxes it** (14B/32B/MoE models don't fit; only same-size 7-8B swaps are possible, with
marginal expected gains). Meanwhile exploration found a nearly-free lever the roadmap never named: the
**chat path runs qwen3 with thinking ON** (the extraction path already uses `think:false` for a ~4-5×
speedup). We are choosing between levers with no data. This builds a small, repeatable eval harness so
the choice is measured, and lands the cheapest proven win.

## What Changes

- **`Tools/ModelEval` console** (mirrors `Tools/SchemaGenerator`): runs a fixed set of tool-use
  scenarios through the **real MEAI `FunctionInvocation` stack** against a real `OllamaChatClient`,
  with **stubbed tool delegates** returning canned/empty results — isolating the *model's* behavior
  and (critically) exercising the **MEAI binder** (the layer the raw `/api/chat` probe is blind to,
  where the `calculate_crafting` bug lived).
- **Scorecard** per (model, think-setting): each scenario run **N times** (LLMs are flaky, so we want
  pass-RATES not pass/fail) scoring four dimensions — **selection** (right tool, incl. negative "no
  tool" cases), **binding** (args bind through MEAI without throwing), **adherence** (final text
  reports the stub's exact value / no fabrication on empty / prose not numbered-list), and **latency**
  (p50/p95, time-to-first-tool-call).
- **Model + think-mode swappable** via CLI args, so `qwen3 think-on` vs `qwen3 think-off` vs a
  same-size alternative are one command apart.
- **Land lever A if it wins:** if the harness shows `think:false` on the chat path improves latency
  without regressing selection/binding/adherence, apply `think:false` to the production chat path
  (`ChatExtensions`), matching what extraction already does.

## Capabilities

### New Capabilities

- `model-eval-harness`: the `Tools/ModelEval` console, its scenario set + N-run scorecard, and the
  measured think-off decision for the chat path.

### Modified Capabilities

<!-- Chat inference config only (think-mode on the chat OllamaChatClient), gated on the harness result;
observable chat behavior unchanged except latency. -->

## Impact

- **Code:** new `Tools/ModelEval/` console project (re-declares the top-tool schemas with stubbed
  delegates to avoid a shared-toolset refactor for now); a possible one-line `think:false` change to
  `Extensions/ChatExtensions.cs` if the harness proves lever A.
- **No** change to production tool logic, retrieval, DB, or HTTP surface. The harness needs a running
  Ollama with the models pulled; it is a dev/bench tool, not part of the app runtime or CI.
- **Decision output:** a scorecard that makes levers A (think-off), C (same-size model swap), and E
  (go off-GPU) decidable with numbers instead of vibes — and a regression bench for future chat-tool
  changes.
