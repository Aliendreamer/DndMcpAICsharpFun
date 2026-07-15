## Context

`DndChatService.SendAsync` builds a per-request `ChatOptions` and calls a `FunctionInvokingChatClient`
(over an OllamaSharp `OllamaApiClient`). It currently sets
`RawRepresentationFactory = _ => new OllamaSharp.Models.Chat.ChatRequest { Think = false }`, which
OllamaSharp's `AbstractionMapper` copies into the outgoing request's top-level `think` field, forcing
qwen3:8b into non-reasoning mode for every internal turn (tool selection AND final composition).

That think-off default was chosen from an early `model-eval-harness` run (think-off marginally better
selection + 4–8× faster). A later diagnosis of a failed live rules smoke (grapple-vs-prone: qwen3
fabricated rules) isolated the cause: think-off cannot reason over multiple rules. A controlled
grounding probe (hand the model the real grapple + prone passages via a simulated `ask_rules` result)
showed qwen3:8b **think-off → wrong** ("No, you can't"), **think-on → correct** (grounded "Yes"). A
same-hardware reasoning-model bench found no 8 GB-fit model beats qwen3:8b (qwen3:14b reasons best but
offloads to 112–179 s/answer — unusable), so the lever is the *mode*, not the model. `Tools/ModelEval`'s
think-on path is simply the *absence* of the `RawRepresentationFactory` (“qwen3 thinks by default”).

## Goals / Non-Goals

**Goals:**

- The conversational chat reasons in qwen3's native think-on mode so multi-rule questions compose
  correct grounded answers instead of think-off fabrications.
- The model's internal reasoning trace is never shown to the user.
- Guard the mode in a test so it cannot silently revert to think-off.
- Keep think-on latency bounded regardless of conversation length by sending only a recent history
  window to the model.

**Non-Goals:**

- No split think-mode (think-off for selection, think-on for composition). The rig shows
  selection/binding is a **tie** (36/36) between modes, so the split's complexity buys nothing.
- No multi-hop `ask_rules` retrieval change (separate follow-up) — this change fixes reasoning, not
  which passages are retrieved.
- No persona change, no endpoint/DB/schema change, no SignalR circuit-timeout hardening.

## Decisions

1. **Remove the `Think = false` override rather than set `Think = true`.** ModelEval's think-on path
   omits the factory entirely and relies on qwen3's default reasoning; mirroring that keeps the two code
   paths identical and avoids depending on an explicit-`true` mapping. The chat's `ChatOptions` keeps
   `Tools` but no `RawRepresentationFactory`.
2. **Selection is not protected because it needs no protection.** Rig persona-v2 think-off vs think-on:
   selection 36/36, binding 36/36 (identical); per-scenario it even *helps* `rules-grapple` (1/5→4/5).
   The only measured cost is latency, which is accepted.
3. **The reasoning trace is separated by OllamaSharp, not stripped by us.** Under `think=true` Ollama
   returns the reasoning in `message.thinking`, distinct from `message.content`; OllamaSharp surfaces
   `content` as `response.Text`. Confirmed empirically: think-on rig runs kept `nolist` at 24–25/25
   (a `<think>` block leaking into `response.Text` would have collapsed that). The design relies on
   this separation; the guard/live check confirms no reasoning text reaches the user.
4. **Guard test asserts the mode.** A `DndChatService` test asserts the per-request `ChatOptions` does
   not force `Think=false` (the override is gone), so a future edit can't silently restore think-off.
5. **Bound the model's history window to the last N=12 messages.** Think-on cost grows with prompt size;
   the live smoke proved this is severe — a fresh chat answers in ~45 s but a 40-turn chat (the full
   `LoadHistoryAsync(maxTurns=40)` load, all sent via `messages.AddRange(History)`) exceeded 10 min and
   the SignalR circuit dropped. Fix: send only `History.TakeLast(12)` to the model (system persona still
   prepended); keep loading/displaying/persisting the full conversation. A message-count window (not a
   token budget) is chosen for simplicity and predictability; N=12 (≈6 exchanges) preserves follow-up
   coherence while keeping latency near the fresh-conversation cost regardless of chat length.

## Risks / Trade-offs

- **Latency.** Think-on answers run ~45 s on a fresh conversation (live-measured), vs ~4–5 s think-off.
  Without the history cap this grew unboundedly with chat length (40 turns → >10 min → dropped circuit);
  the N=12 window (Decision 5) bounds it back to roughly the fresh-conversation cost. ~45 s/answer is
  still slow and raises the chance the Blazor SignalR circuit drops mid-answer on the browser — real but
  a separate UX hardening (circuit-timeout / streaming), out of scope here.
- **Rig adherence dipped slightly** (32→29) under think-on, but that metric is stub-prose format
  checks, not reasoning; the reasoning win (the actual goal) is not something the current rig measures —
  it is validated by the grounding probe + the live smoke.
- **Multi-rule live answers stay partially gated** on the deferred multi-hop retrieval: if single-shot
  retrieval misses one rule set, think-on reasons over incomplete passages. So the provable success of
  THIS change is on questions whose retrieval already returns the needed passages (e.g. a single-rule
  "how does the Dodge action work?"), plus think-on active + no reasoning-trace leak + selection unchanged.
