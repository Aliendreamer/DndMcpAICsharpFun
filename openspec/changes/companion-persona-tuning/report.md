# companion-persona-tuning — change report

## Summary

Turned `Tools/ModelEval` into a persona-tuning rig (persona-from-file + symptom-specific adherence
scoring), used it to baseline the current companion persona and iterate variants, and landed a surgical
persona edit that measurably improves rules-tool **routing** — while honestly documenting the qwen3:8b
ceiling the live smoke exposed.

## Code delivered (all on `main`, each reviewed clean)

| Commit | Task | What |
| --- | --- | --- |
| `287b918` | 1 | `--persona <file>` loading in `EvalArgs`/`Program.cs` (default persona unchanged when no flag). |
| `dc4f363` | 2 | Pure `SymptomChecks.NoList`/`NumberLabel` + wiring into scenarios + new scorecard columns (`nolist`, `numlbl`); 11 unit tests. |
| `1607c9c` | (stub-fidelity) | Stub tools given the same `= null` optional-param binding fix the real chat tools have (+ `build_encounter` required-first reorder); 11 guard tests. |

Per-task reviews: all **Spec ✅ / Quality Approved, no findings**. Whole-branch opus review of tasks 1–2:
**READY** (3 Minor measurement-cleanliness notes, no code changes required — see below).

## Measurement (rig, qwen3:8b, think off, runs 5, honest stubs)

Baseline = the shipped persona; v1/v2 = surgical additions (keeping every existing directive).

| metric | baseline | persona-v1 |
| --- | --- | --- |
| tool selection | 35/50 | **40/50** (+5) |
| binding | 35/50 | **40/50** (+5) |
| adherence | 30/50 | **36/50** (+6) |
| no-list | 25/25 | 24/25 (−1, small-N noise) |
| number-label | 10/10 | 10/10 |

Flagship: `rules-grapple` went **0/0/0 → 5/5/5** (routed + bound + PHB-cited in the stub rig). The
proposal's other two hypothesized symptoms (number-mislabeling, list-iness) were **already near-ceiling**
on the shipped persona, so the measurable gap was rules/downtime **routing** — which the "Which tool to
call" addition directly targets.

## Applied persona edit (v2 — three surgical additions, nothing removed)

1. A list→prose **exemplar** in Response style.
2. A **"report every number / never re-label"** paragraph in Calculations.
3. A new **"Which tool to call"** routing block (rules → `ask_rules`, downtime → `plan_downtime`, lore →
   `ask_setting_lore`), with the rules cue **broadened to "how does X work"** phrasings (v2 over v1).

## Live validation (real app, container-baked persona) — honest result

The live smoke and a follow-up **Ollama-probe** (browser-free, dev-flow's reliable method, using the REAL
competitor tools incl. `search_dnd`/`search_lore`) established:

- **Routing works and is persona-driven.** Old persona: grapple → **`<none>` 5/5** (answered from memory
  — the routing gap). Applied persona: grapple → **`ask_rules` 5/5**; Dodge → **`ask_rules` 3/3** (v2's
  broadened cue). No regression: crafting → `calculate_crafting`, Sharn → `ask_setting_lore`.
- **Routing is brittle at the qwen3:8b level.** Even v2 still routes structurally-identical "how does the
  **Help** action work" and "does cover apply to a prone creature" to `<none>` — adding examples is
  whack-a-mole, not a general fix.
- **Grounding is a separate qwen3 ceiling.** In the live UI the grapple answer, *despite correctly
  routing to `ask_rules`*, came back **uncited and hallucinated** (invented `Heal`/`Grasping Hand`
  rulings). Persona wording cannot reliably fix qwen3's grounding-disobedience.

**Verdict:** the persona edit is a real, attributable, low-risk **routing** improvement with no regression;
qwen3:8b caps both routing generality and grounding quality. This confirms the roadmap thesis that these
weaknesses are "only partly persona-fixable" and the strongest remaining lever is the **local-model
upgrade**.

## Whole-branch review Minors (triaged — no code change)

1. Symptom denominators count bind-throws as symptom-fails (consistent with the existing `adhere`
   column; contextualized by the adjacent `bind` column). Accepted.
2. "Applicable" is runtime-derived (a scenario whose runs all throw drops from the tally). Defensible for
   a dev tool. Accepted.
3. Tests→ModelEval `ProjectReference` to an Exe project is unusual but correct (no entry-point conflict,
   no test discovery in ModelEval). Accepted.

## Follow-ups (not in this change)

- **Local-model upgrade** remains the strongest lever for both routing generality and grounding.
- **`ask_rules` retrieval quality** for interaction rules (grapple-vs-prone) — worth checking whether the
  rule is cleanly indexed vs. qwen3 ignoring returned passages.

## Gates

- `dotnet build` 0/0; `dotnet format` clean; **full `dotnet test` suite 1382/1382** (1360 prior + 22 new
  ModelEval tests: SymptomChecks 11 + StubToolBinding 11), 0 failures.
- No HTTP endpoint / DB / schema change → no `.http`/insomnia update required.
