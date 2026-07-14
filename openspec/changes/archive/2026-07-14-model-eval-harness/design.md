## Context

The chat LLM is `OllamaChatClient(Ollama:ChatModel)` wrapped with MEAI `.UseFunctionInvocation()`
(`ChatExtensions`). Failures cluster in two layers the current one-shot ollama probe can't both see:
the model's **emit** (selection + arg JSON + latency — visible to a raw `/api/chat` probe) and MEAI's
**bind** of that emit to the .NET delegate (where the `calculate_crafting` missing-required-arg bug
lived — invisible to the raw probe). Adherence (fabrication, prose vs list) lives in the final text.
Hardware caps the model to ~8 GB VRAM, so the point of this harness is to compare *cheap* configs
(think on/off) and *same-size* models, and to prove the near-free think-off win.

## Goals / Non-Goals

**Goals:** a repeatable, model/think-swappable bench that scores selection + binding + adherence +
latency as N-run pass-rates through the REAL MEAI stack; a data-backed decision on lever A (think-off)
and a baseline to judge any same-size swap.

**Non-Goals:** a general LLM-eval framework; scoring retrieval quality (delegates are stubbed);
CI integration (needs Ollama + is slow/nondeterministic); running larger-than-8GB or MoE models (they
don't fit); the shared chat-toolset refactor (re-declare schemas for now); auto-selecting a model.

## Decisions

### D1 — Console tool, not an xunit test

`Tools/ModelEval` (console, like `Tools/SchemaGenerator`/`Tools/CanonicalNameCleanup`). A benchmark
that runs N times and prints a scorecard table is not a pass/fail test; a console keeps it re-runnable
per config and out of CI. Args: `--model <name>` (default `qwen3:8b`), `--think <on|off>`,
`--runs <N>` (default 5), `--base-url` (default `http://localhost:11434`).

### D2 — Real MEAI stack, stubbed delegates

Build the same `OllamaChatClient(baseUrl, model).AsBuilder().UseFunctionInvocation().Build()` the app
uses, so the **MEAI binder is exercised** (the raw-probe blind spot). Register the top tools' real
`AIFunctionFactory` schemas but with **stubbed delegates** returning canned results (or empty, to test
fabrication). Stubbing isolates model behavior from retrieval and makes the "empty result" case
deterministic. A binding failure surfaces as MEAI throwing / an error result for that run → scored as
bind-fail.

### D3 — think on/off toggle

Determine how MEAI's `OllamaChatClient` passes `think` to Ollama (likely `ChatOptions` +
`AdditionalProperties["think"]=false`, or the request body). Task 1 spikes this against the running
Ollama and confirms `think:false` actually suppresses the `<think>` block (compare token counts /
latency). This is the mechanism lever A needs both in the harness and, if it wins, in production.

### D4 — Scenario set (tight, high-value)

~8-10 scenarios covering the real tools + their known failure prompts + negatives, each with an
`ExpectedTool` (or `None`) and cheap adherence checks:

| Scenario prompt (paraphrased) | ExpectedTool | Adherence check |
|---|---|---|
| craft plate armor, market value 1500 gp | calculate_crafting | final text has "750" (stub value), not a fabricated number |
| a rare magic item's craft time | calculate_crafting | has stub's "2000" |
| give me a criminal gang for a heist | generate_npc_party | mentions ≥3 members |
| a single shifty dockworker NPC | generate_npc | one stat block |
| can I grapple a prone creature | ask_rules | cites the stub passage's book |
| how long to craft during downtime | plan_downtime | cites stub passage |
| build a hard encounter for my party | build_encounter | reports stub monster |
| who rules Sharn (eberron) | ask_setting_lore | cites stub lore |
| a rules Q where the stub returns EMPTY | (the tool) | says "not covered" — does NOT fabricate |
| "hi, how's it going" | None | no tool call (over-trigger check) |

### D5 — Scoring: N-run pass-rates + latency

Per scenario, run N times; record per run: tool invoked (name or none), bind ok (no MEAI throw),
adherence checks pass, wall-clock ms + ms-to-first-tool-call. Aggregate to `sel N/N`, `bind N/N`,
`adhere N/N`, `p50/p95 ms`. Print a table; also a one-line totals row. Compare configs by re-running
with different `--model`/`--think` and eyeballing the deltas (no automated diff needed v1).

### D6 — Land lever A if measured

If `qwen3 think-off` shows a large latency win with no selection/binding/adherence regression vs
think-on, apply `think:false` to the chat path in `ChatExtensions` (mirroring extraction) as the
final task. If think-off *regresses* quality, keep it on and record the finding (the harness earned
its keep either way).

## Risks / Trade-offs

- **[Re-declared schemas drift from DndChatService]** → accepted for v1 (avoids the shared-toolset
  refactor); the schemas are simple (name + params) and the harness is a dev tool, not a correctness
  gate. If it proves valuable, a later change extracts a shared `ChatToolset` both consume.
- **[Nondeterminism]** → the whole point: N-run pass-rates, not single verdicts. N=5 default; bump for
  a tighter read.
- **[think toggle mechanism unknown in MEAI]** → de-risked by the Task 1 spike before building the
  scorecard on top of it.
- **[Harness needs Ollama + pulled models]** → it's a bench, run on demand with `dangerouslyDisableSandbox`
  + Docker up; never in CI.
- **[Same-size swaps may show no winner]** → that IS a result: it says the local ceiling is
  qwen3+think-off. **Local-only is a HARD constraint (user decision): off-GPU / remote models (lever E)
  are OUT of scope** — the offline/on-this-laptop stance stands. So if no local same-size model beats
  qwen3+think-off, the fallback is to accept that ceiling and invest in harness-level reliability
  (retry-on-binding-fail, constrained tool-calls — lever B), NOT to reach for a bigger remote model.
  The harness therefore only ever compares models that fit the 8 GB budget.
