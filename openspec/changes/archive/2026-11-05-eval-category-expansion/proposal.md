## Why

The first full-chain baseline (2/16, `Tools/ModelEval/reports/20261103-194614-qwen3_8b-full.json`) covers only class,
spell and crafting questions. The upcoming context-engineering work (router, persona, retrieval scoping) also changes
how the chat answers rules, setting-lore, monster and NPC questions, and how it behaves when the data has no answer.
The owner's rule is **answers always come from our data; if the data doesn't have it, say "couldn't find it"**. Without
cases for those areas, a context change could break them silently. The report also lacks the reply text, so a
correct-from-memory answer can't be told apart from a wrong one when reading results.

## What Changes

- Add a **monster** case generator over canonical `mm14` (armor class, hit points, challenge rating), and widen the
  default `--books` to `phb14,mm14`.
- Add hand-written cases to `Tools/ModelEval/cases/manual.json` for **rules** (`ask_rules`), **lore** (Eberron, `erlw`),
  **npc** (`generate_npc` / `generate_npc_party`, grounded on a real stat block) and **not-found** (a made-up spell; the
  answer must say it couldn't find it).
- Store, per run, the called tool names and a truncated reply text in the report, so results can be read without
  re-running.
- Run the expanded set live on `qwen3:8b` and commit the report as the new "before" baseline for the context work.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `full-chain-eval` (in flight in `openspec/changes/full-chain-eval`): new categories and report run detail.

## Impact

- `Tools/ModelEval/Full/*` (generator, manual-case validation, report), `Tools/ModelEval/EvalArgs.cs` (default books),
  `Tools/ModelEval/cases/manual.json`, tests under `DndMcpAICsharpFun.Tests/ModelEval`.
- No production code changes, no endpoint changes. The router and persona are deliberately untouched, so this run
  measures the current behaviour.
