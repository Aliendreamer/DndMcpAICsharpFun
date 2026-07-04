## Context

`OllamaEntityExtractionClient` builds an Ollama chat request from `ExtractionRequest` but never sets the `think` field, so qwen3 defaults to thinking on. `EntityExtractionOptions.MaxOutputTokensPerEntity = 8192`. The extraction is a per-candidate tool-call constrained by a JSON schema (union for type selection, per-type for field completion). This change adds a knob + the measurement scaffolding to evaluate turning thinking off — it does not itself decide the default.

## Goals / Non-Goals

**Goals:**
- Make qwen3 think mode configurable for extraction, default-unchanged.
- Guarantee reasoning never leaks into extracted content (strip `<think>`), independent of the decision.
- Produce comparable per-candidate metrics so think-on vs think-off is a data call, not a guess.

**Non-Goals:**
- Permanently adopting `think:false` (decided from the A/B results).
- The no_think-on-retry fallback (follow-up).
- Changing `MaxOutputTokensPerEntity` (a separate lever).

## Decisions

- **Nullable `Think` option, default `null` = model default (thinking on).** `EntityExtractionOptions.Think` (bool?) flows into `ExtractionRequest.Think`, which the client maps to the Ollama request `think` field only when non-null. Default null keeps the exact current behaviour, so merging this change is a no-op until someone flips the switch. *Alternative:* `bool` defaulting true — rejected; nullable lets "unset" mean "let the model/Ollama decide" and avoids sending `think:true` we haven't verified.
- **Strip `<think>…</think>` defensively at the client, always.** Even with thinking off, a soft directive can be ignored; and with thinking on, some Ollama/response paths surface the block inline. A single strip pass before parsing makes reasoning-leak impossible regardless of mode — complementary to the structural decline-not-leak fix already shipped.
- **Per-candidate metric line: `{thinkMode, candidate, type, wallMs, outcome}`** where outcome ∈ {ok, empty, declined}. Emitted at info level so a run's log is directly parseable into an A/B table. *Alternative:* a bespoke metrics store — rejected as overkill for an experiment; structured logs suffice.

## A/B Test Procedure (the deliverable "for us to test")

Run the **same fixed sample** twice — once think-on, once think-off — and compare. Sample = a book with a known mix incl. object stat blocks (DMG book 3 is ideal: siege weapons + monsters + magic items), or a smaller curated subset to keep each run short.

1. Set `Think = null` (on). Re-extract the sample. Capture from the metric lines: total wall time, count of `empty` outcomes, and the set of `(name → type)` classifications.
2. Set `Think = false` (off). Re-extract the same sample. Capture the same three.
3. Compare:
   - **Speed:** think-off wall time vs think-on (expect a large drop).
   - **Robustness:** think-off `empty` count vs think-on (expect think-off ≤ think-on; the empty failures are the hypothesis).
   - **Accuracy:** diff the `(name → type)` maps. Key checks: do siege weapons (ballista/cannon/ram) still classify as `Object`? Do known monsters/spells/magic items still classify correctly? Count regressions.

**Acceptance for adopting `think:false`:** think-off must show materially lower wall time AND a lower-or-equal empty-failure count AND **zero classification regressions on the object/gated-type checks** (siege → Object, known entities → correct type). Any classification regression → keep thinking on (and consider the no_think-on-retry fallback instead).

## Risks / Trade-offs

- **Think-off degrades classification** (esp. Object-vs-Monster) → the A/B's accuracy gate catches it; default stays on until proven safe.
- **`<think>` strip false-positives** (stripping legitimate content containing the literal tag) → match only well-formed `<think>…</think>` spans; entity prose won't contain them.
- **Sample not representative** → prefer the full DMG sample (or a curated subset that deliberately includes object stat blocks) so the object case is actually exercised.

## Open Questions

- Sample size for the A/B: full DMG (~3 h/run, most representative) vs a curated ~30-candidate subset (fast, must include siege weapons). Resolve when running the experiment; the spec supports either.
