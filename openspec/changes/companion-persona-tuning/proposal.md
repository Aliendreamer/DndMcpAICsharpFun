## Why

With the local-model upgrade benchmarked and rejected (qwen3:8b is the ceiling on 8 GB VRAM),
**persona/prompt tuning is the remaining software lever** for chat quality. Three residual qwen3:8b
symptoms persist despite the current persona: (1) it reports correct calculator numbers but
**mislabels** them in prose (calls the 750 gp materials cost the "market value"); (2) it
**misroutes** — answering a computable question from retrieval/memory instead of the matching
tool; (3) it still leans on **numbered/bulleted lists** despite the prose rule. Tuning the persona
blindly risks regressing chat quality, so this change first makes the effect **measurable**.

## What Changes

- Extend `Tools/ModelEval` into a persona-tuning rig: (a) load the persona from a `--persona <file>`
  argument (default = the current hardcoded string, so existing runs are unchanged); (b) add
  symptom-targeted adherence checks to the relevant scenarios — number-label correctness (the prose
  names each tool-result number by its true field, never relabeling it) and no-list (the answer is
  prose, not a numbered/bulleted list).
- Capture a **baseline scorecard** for the current `companion.md`, then **iterate persona-text
  variants in the rig** (the rig reads persona *text*, so iteration touches no encrypted file) until
  the three symptom checks improve without regressing selection/binding on the working tools.
- Apply the **winning** persona edit to `Config/personas/companion.md` (user-applied — the file is
  git-crypt/permission-protected), rebuild the app image, and run one live smoke to confirm.

## Capabilities

### New Capabilities

<!-- None: this extends two existing capabilities. -->

### Modified Capabilities

- `model-eval-harness`: gains persona-from-file loading and symptom-specific adherence scoring, so it
  can measure a persona change's effect on number-labeling and list-iness (not just tool selection).
- `conversational-chat-persona`: the companion persona gains explicit directives for labeling
  tool-result numbers by their true field, tighter tool-routing cues, and stronger prose-over-lists
  enforcement (with an exemplar).

## Impact

- `Tools/ModelEval/*` — new `--persona` arg + persona-file loader; symptom adherence checks
  (`Scenarios.cs`, `Program.cs`, `EvalArgs.cs`, `ScenarioRunner.cs`/`Scorecard.cs` as needed).
- `Config/personas/companion.md` — the persona edit (user-applied; git-crypt-protected). Baked into
  the app image, so production needs an image rebuild.
- No app runtime code, no HTTP endpoints, no DB/schema changes. Measurement via the Ollama-probe/rig,
  not the flaky browser; one live chat smoke at the end to confirm.
