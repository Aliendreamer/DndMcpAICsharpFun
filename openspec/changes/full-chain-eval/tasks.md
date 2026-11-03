## 1. Turn core and trace (host)

- [x] 1.1 Add `ChatTurnResult` (text, offered tool names, tool calls with name/arguments/result-or-error,
  latency) and a trace builder that reads `FunctionCallContent` / `FunctionResultContent` from a
  `ChatResponse`. Unit-test the builder.
- [x] 1.2 Extract `DndChatService` turn core `RunTurnAsync(messages, userId, allowWebSearch, ct)` that
  builds the per-user tools for the explicit user id and routes, calls the model, and returns
  `ChatTurnResult`. `SendAsync` delegates to it with the HttpContext user, keeping its rate limit,
  history, and persistence.
- [x] 1.3 Tests: `SendAsync` still appends and persists; the core builds per-user tools for the passed
  user id; the core with a null user offers no per-user tools.

## 2. Eval-turn endpoint

- [x] 2.1 Add `POST /admin/eval/turn` (body: `prompt`, optional `userId` defaulting to the seeded `test`
  user, optional `allowWebSearch`). It calls the turn core with a single-message history, with no
  persistence and no rate limiter, and returns `ChatTurnResult` as JSON.
- [x] 2.2 Tests: requires the admin key; returns the trace; writes no `ChatTurn` rows.
- [x] 2.3 Update `DndMcpAICsharpFun.http` and `dnd-mcp-api.insomnia.json` with the new route, in the same
  commit.

## 3. Case model and generation (console)

- [x] 3.1 Add a full-chain case schema: id, category, tag, prompt, expectedTools (set), toolDataVariants, answerVariants (all required),
  nullable data check. Add a JSON loader for `Tools/ModelEval/cases/manual.json`, with a
  starter file that holds one manual example.
- [x] 3.2 Confirm the router tool groups (from `ToolGroups`) and set the acceptable-tool set for
  class/spell cases (entity + lore tools; D7). Default `--books phb14`.
- [x] 3.3 Class generator: entities typed `Class` that have `fields.hd`; hit-die and saving-throw
  templates; ability-code → name mapping.
- [x] 3.4 Spell generator: `Spell` + `canon` + not `needsReview` + asked field in
  `_fivetoolsFilledFields`; level, school (letter code → name), and range templates (skip ranges it
  can't render).
- [x] 3.5 Crafting generator: seeded market values and rarities with expectations from `CraftingMath`.
- [x] 3.6 Deterministic seeded sampling (`--seed`, `--count`, `--category`); stable generated ids
  (entity id + template); a case-set hash.
- [x] 3.7 Tests: same seed gives identical cases; misclassified entities are excluded; crafting
  expectation equals `CraftingMath`; the manual case is merged and tagged.

## 4. Full-chain runner, scoring and attribution

- [x] 4.1 Add `--mode full` (default stays stub) with `--host` and `--admin-key` (or an env var), plus an
  HTTP client for `/admin/eval/turn`.
- [x] 4.2 Run each case N times (default 3) and score each run with the ordered labels `wrong-tool`
  (router-not-offered vs model-not-chosen), `bind-error`, `wrong-tool-data`, `wrong-answer`. Fact
  matching is normalised (case, whitespace, accepted variants).
- [x] 4.3 Tests: attribution order on synthetic traces (each label, plus the router-vs-model
  distinction); fact normalisation.

## 5. Reports, baseline and regression

- [x] 5.1 Write a JSON report to `Tools/ModelEval/reports/<UTC>-<model>-full.json` (seed, case-set hash,
  model, per-case k/N and labels, per-category totals), and print a per-category summary.
- [x] 5.2 Baseline: the latest previous full report, or `--baseline <path>`. Compare shared case ids and
  note any case-set difference. Print per-category deltas and newly failing / newly passing cases.
- [x] 5.3 Regression rule: majority pass per case; a category drop of more than one case → exit code 2,
  otherwise 0.
- [x] 5.4 Tests: two-case drop → exit 2; one-case drop → exit 0; newly failing case listed; different
  case sets compare on shared ids.

## 6. Dev-flow, docs, and live validation

- [x] 6.1 With explicit permission, add the eval trigger to `.claude/skills/dev-flow`: a chat-model change
  requires an old-vs-new full-chain comparison; a regression must be fixed or accepted; document the
  on-demand run.
- [x] 6.2 Document how to run full-chain eval (in the console README or the CLAUDE.md commands section)
  and lint the markdown clean.
- [x] 6.3 Live run against the stack on qwen3:8b: commit the first report as the baseline and record the
  per-category results and any real wrong-answer findings in the change.
- [x] 6.4 Full build (warnings-as-errors) and the full test suite green.
