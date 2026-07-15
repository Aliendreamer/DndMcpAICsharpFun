## Context

`Tools/ModelEval` (from the archived `model-eval-harness` change; code still in the repo) runs each
scenario N times through the real MEAI function-invocation loop (tool call → tool result → final
prose), scoring selection, binding, and a per-scenario adherence check on the final text, plus
p50/p95 latency. Its persona is currently a hardcoded string in `Program.cs`.

The production chat persona lives in `Config/personas/companion.md` (2877 bytes, baked into the app
image). It is git-crypt-encrypted and host-read/write-protected by policy — readable only from inside
the running container, not editable from the host by the agent. It already covers prose-over-lists,
"call the calculator for numbers," and retrieval+citation. Three residual qwen3:8b symptoms remain:
number mis-labeling in prose, tool misrouting, and list-iness.

The MoE upgrade was benchmarked and rejected (8 GB VRAM ceiling), so the persona is the lever, and
qwen3:8b has a hard adherence ceiling — the goal is measurable reduction, not perfection.

## Goals / Non-Goals

**Goals:**

- Turn `ModelEval` into a repeatable persona-tuning rig: persona-from-file + symptom-specific
  adherence scoring (number-label, prose-not-list) on top of the existing selection/binding scoring.
- Iterate persona edits cheaply on a host copy (the rig reads persona *text*, so no encrypted-file
  writes during iteration), measuring before/after per symptom.
- Land a persona edit that measurably reduces the three symptoms without regressing the working
  tools' selection/binding.

**Non-Goals:**

- No change to app runtime code, endpoints, DB, or the extraction path.
- Not chasing 100% adherence — qwen3:8b has a ceiling; we stop at a measured plateau.
- Not re-architecting the persona; edits are surgical additions to the existing structure.
- Real retrieval quality is out of scope — the rig uses stub tools, which is correct for
  selection/prose symptoms.

## Decisions

1. **`--persona <path>` with default fallback.** `EvalArgs` gains a `PersonaPath`; `Program.cs` reads
   the file when set, else uses the existing constant. Backward compatible — no-flag runs are
   identical to today.
2. **Symptom checks operate on the final composed prose.** The existing `Scenario.AdherenceCheck`
   already inspects the final text. Add reusable predicates: `NoList` (no `1.`/`2.` numbered or `- `
   bulleted lines beyond a small threshold) and a per-scenario `NumberLabel` check (the canned tool
   result's value appears with its correct label, not a wrong one). Extend the scorecard to tally
   these as their own columns/rows so before/after is legible.
3. **Iterate on a host copy of the persona, not the encrypted file.** Read the current persona from
   the container once into `.moe-bench/persona-current.md`; produce edit variants as sibling files;
   run the rig against each. Only the winning text is handed to the user to apply to the real
   `companion.md`. This sidesteps the write-protection entirely during iteration.
4. **Persona edits are surgical additions** keyed to the three symptoms: a no-relabel numbering
   directive, a compute-vs-lookup routing either/or, and a concrete list→prose exemplar. Keep every
   existing directive.
5. **Success = measured symptom drop + no regression**, judged from the before/after scorecards.
   Final confirmation is one live chat smoke after the user applies the winner and rebuilds the image.

## Risks / Trade-offs

- **Stub-tool fidelity:** the rig measures selection + prose composition, not real retrieval. That is
  exactly the surface of the three symptoms, so acceptable — but a persona edit could still behave
  differently against real retrieval; the final live smoke covers that.
- **qwen3 variance:** small N can make a symptom tally noisy. Use the same N (5) and identical
  scenarios/seedless prompts for before and after; treat only clear deltas as real.
- **Prose-not-list heuristic:** a regex for lists can false-positive on a legitimately enumerated
  answer (e.g. "list all level-1 spells"). Keep the check on scenarios where prose is the correct
  form, and set a tolerant threshold; document what it flags.
- **Write-protection:** the agent cannot apply the final edit; the user must. The plan makes the
  hand-off explicit (drafted text + apply + rebuild + smoke) rather than assuming agent write access.
- **Low blast radius:** all code changes are confined to `Tools/ModelEval` (a dev console, not
  shipped in the app); the only production artifact is the persona text + an image rebuild.
