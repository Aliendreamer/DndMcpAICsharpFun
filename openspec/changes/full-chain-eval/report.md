# Full-chain eval — first live baseline (2026-11-03)

Report: `Tools/ModelEval/reports/20261103-194614-qwen3_8b-full.json`
Model `qwen3:8b` · seed 42 · books `phb14` · 16 cases × 3 runs · case-set hash `6c5dadb70067` · infra errors 0.

| Category | Passed | Failure labels (runs) |
| --- | --- | --- |
| class | 1/6 | wrong-tool:not-chosen 10, wrong-tool-data 4, wrong-answer 1 |
| spell | 1/5 | wrong-tool-data 11, wrong-tool:not-chosen 1 |
| crafting | 0/5 | wrong-tool:not-offered 15 |

## Findings (verified with direct `/admin/eval/turn` traces)

1. **Router misses crafting (all 15 runs).** For "How much do the materials cost and how many workweeks does it take to
   craft a nonmagical item with a market value of 300 gp?" the `QueryRouter` offered only the entity group
   (`search_entities`, `search_lore`, `get_entity`, `list_entities`) and never `calculate_crafting`. The Calculators
   exemplars ("how long does it take to craft plate armor", ...) don't match price-phrased questions. In that probe the
   model also returned **empty text** with no tool call.
2. **Spell questions retrieve the wrong chunks.** "What level is the spell Fireball ..." was routed to `search_lore`
   only. Retrieval returned the class spell-*list* chunks ("Sorcerer Spells"), not the spell's entry, so the tool data
   lacks the level. The model often answers correctly anyway (from parametric memory), which is why these runs are
   `wrong-tool-data`, not `wrong-answer`: the answer is not grounded.
3. **Class questions: the model skips the tool.** In most runs the right tool was offered but not called
   (`not-chosen`); the model answered from memory.

None of these is a harness bug. They are exactly the context-engineering targets (router exemplars, tool
descriptions, retrieval scoping) for the follow-up change, which must move these numbers against this baseline.

## Harness fix found during the live run

The first attempt hit Ollama during a cold model load. The HttpClient timeout (100 s) surfaced as an unhandled **500**
because the endpoint rethrew every `OperationCanceledException`. Fixed in `33270c6b`: it now rethrows only when the
request token is cancelled, and returns 502 otherwise (regression test `RunnerTimesOut_Returns502`).
