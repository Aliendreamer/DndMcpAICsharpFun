## Why

Chat answers can be wrong in ways nothing catches until someone tries them by hand: a wrong class fact
(hit die, saving throws), a wrong spell fact (level, school, range), or a wrong crafting calculation.
The existing `Tools/ModelEval` harness (2026-07-14) only measures the *model* against **stubbed**
tools. It never exercises real retrieval or `dnd_entities` data, never asks fact questions, bypasses
the production `QueryRouter`, keeps no history, and isn't part of the dev flow. Quality decisions,
especially the upcoming local-model upgrade, are still made by live smoke and intuition.
This change adds **evaluation-driven development**: a measured, repeatable, full-chain eval with a
saved baseline and a regression rule. It is also the prerequisite for the follow-up
context-engineering change, whose prompt and context edits will be judged by this eval.

## What Changes

- **Full-chain mode for `Tools/ModelEval`** (`--mode full`). Each case goes through the real production
  chat turn on a running host: the persona, the `QueryRouter` tool narrowing, the real MCP and per-user
  tools, real retrieval and `dnd_entities` data, and the configured chat model. The existing stub mode
  stays as the default, unchanged.
- **A trace of every turn.** A new admin-key-guarded endpoint `POST /admin/eval/turn` runs one isolated
  chat turn as a given user and returns the final text **plus a trace**: the tools offered by the
  router, each tool call (name and arguments), each tool result or error, and latency. It is not
  persisted, does not touch chat history, and is not rate-limited.
- **Failure attribution.** Each failed run is labelled as one of `wrong-tool`, `bind-error`,
  `wrong-tool-data`, or `wrong-answer`, so a failure points at the router/model, the binder, the data,
  or the composition step.
- **Generated cases from entity data plus a manual case file.** A seeded, deterministic generator
  builds class, spell, and crafting questions with known-correct answers:
  - class and spell answers come from 5etools-confirmed canonical fields;
  - crafting answers come from `CraftingMath` itself.
  A hand-written `Tools/ModelEval/cases/manual.json` is merged in. Every case is tagged
  `generated` or `manual`.
- **N runs, saved report, baseline comparison.** Each case runs N times (default 3) and the result is
  reported as k/N. A JSON report is written per run, keyed by date and model. The run is compared with
  the previous report for the same mode, and the comparison shows per-category pass rates, newly
  failing cases, and newly passing cases.
- **Regression rule.** A category whose passing-case count drops by **more than one** versus the
  baseline is a **regression**: it gets a non-zero exit code, and `dev-flow` treats the change as not
  done. A drop of one or less is reported but does not block.
- **Dev-flow integration.** The `dev-flow` skill gains a step: run the full-chain eval **on every
  chat-model change** (compare old vs new model) and **on demand**.

## Capabilities

### New Capabilities

- `full-chain-eval`: the full-chain eval mode, the eval-turn trace endpoint, generated and manual case
  sets, failure attribution, saved reports and baseline comparison, the regression rule, and the
  dev-flow trigger.

### Modified Capabilities

<!-- None. The existing model-eval-harness requirements (stub mode) are unchanged; full-chain is additive. -->

## Impact

- **Code:**
  - `Tools/ModelEval/`: mode flag, an HTTP client for the eval endpoint, the case generator, the
    manual case loader, the report and baseline writer, and the regression exit code.
  - `Features/Chat/DndChatService.cs`: extract the turn core so it can run for an explicit user id and
    return a trace, without changing the behaviour of the UI path.
  - A new admin endpoint, `POST /admin/eval/turn`.
- **API:** new admin route. `DndMcpAICsharpFun.http` and
  `dnd-mcp-api.insomnia.json` must be updated in the same commit.
- **Runtime dependency:** full-chain mode needs the live stack (host, Ollama, Qdrant, Postgres). It is a
  deliberate dev check, not part of `dotnet test` or CI. Roughly 15 cases × 3 runs ≈ 10–15 minutes.
- **Skills:** `.claude/skills/dev-flow` gets the eval trigger (a sandbox-protected path, edited with
  explicit permission).
