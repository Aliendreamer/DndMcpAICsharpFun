# chat-think-on-reasoning — change report

## Summary

Switched the conversational chat from hardcoded think-off to qwen3's native **think-on** reasoning so
multi-rule questions compose correct grounded answers, and bounded the history sent to the model so
think-on latency stays bounded regardless of conversation length.

## Why

A failed live rules smoke (grapple-vs-prone → qwen3 fabricated `Heal`/`Grasping Hand` rules) was
diagnosed: the corpus HAS the rules, `ask_rules` retrieves them, but **think-off cannot reason over
multiple rules**. A controlled grounding probe (hand the model the real grapple+prone passages) showed
qwen3:8b **think-off → wrong** ("No, you can't"), **think-on → correct**. A same-hardware reasoning-model
bench (qwen3:4b / deepseek-r1:8b / qwen3:14b) found **no 8 GB-fit model beats qwen3:8b** (qwen3:14b
reasons best but offloads to 112–179 s — unusable), so the lever is the *mode*, not the model. And a rig
re-measurement showed think-off vs think-on tool **selection/binding is a 36/36 tie**, so the original
think-off justification no longer held.

## What shipped (both on `main`, each reviewed clean)

| Commit | Task | What |
| --- | --- | --- |
| `6c31fef` | 1 | Removed the `Think = false` `RawRepresentationFactory` from `DndChatService`'s `ChatOptions` → qwen3 reasons in default think-on mode. Guard test asserts the mode isn't forced off. |
| `7dec65b` | 2 | Bounded the model's history window: `const MaxModelHistoryMessages = 12`; send `History.TakeLast(12)` (system persona still prepended). Full history still loaded/displayed/persisted. Two guard tests (long-history truncation + short-history full-send). |

Per-task reviews: both **Spec ✅ / Quality Approved, no findings**. Each reviewer INDEPENDENTLY verified
the guard tests are non-vacuous — Task 1: `ChatRequest.Think` is `ThinkValue?` (not `bool?`), so a naive
`NotBe(false)` is a tautology; the committed test converts via `ThinkValue.ToBoolean()` for a genuine
RED→GREEN. Task 2: the truncation test reds 13-vs-16 against the old `AddRange(History)` and pins message
identity, not just count.

## Live validation

- **Reasoning fixed (single-rule):** "How does the Dodge action work?" → correct, **PHB-cited**
  ("PlayerHandbook 2014, Actions in Combat"), **no `<think>` leak** in the persisted turn (validates the
  reasoning-trace-separation requirement live). ~45 s.
- **Latency bounded (the history-cap regression check):** with an 18-turn history seeded in the DB, a new
  question ("What does the Dash action do?") returned a correct, cited answer in **108 s — bounded and
  completed**, versus the pre-cap 40-turn history that exceeded **10 min and dropped the SignalR circuit**.
  The cap prevents the unbounded blow-up.
- **Tool selection:** unchanged (rig 36/36 tie); questions still route and ground.

## Honest caveats

- **Absolute think-on latency is slow** (~45–108 s/answer, high variance) — inherent to think-on + the
  ~19-tool set. The cap bounds it against *conversation length*, but it is still much slower than
  think-off (~4–5 s). A follow-up UX hardening (streaming / circuit-timeout, or a token budget) is
  warranted; out of scope here.
- **List-iness / tangential padding persists** — think-on does not fix prose-vs-list adherence (a
  persona/model trait); the Dodge answer rendered as a list with a couple of tangential extras.
- **Multi-rule questions remain partially gated** on the deferred **multi-hop `ask_rules` retrieval**
  (single-shot can still miss one rule set); think-on reasons correctly over whatever IS retrieved.

## Gates

- `dotnet build` 0/0; `dotnet format` clean; **full `dotnet test` 1385/1385** (1383 prior + 2 new
  history-window tests; the think-on guard test replaced no existing assertion), 0 failures.
- No HTTP endpoint / DB / schema / persona change → no `.http`/insomnia update. Deployed via image rebuild.

## Follow-ups (separate changes)

- Multi-hop `ask_rules` retrieval (always fetch every rule set for a multi-rule question).
- Chat latency UX: response streaming and/or SignalR circuit-timeout hardening; optionally a token-based
  history budget instead of a fixed message count.
