## ADDED Requirements

### Requirement: The eval harness MUST support a full-chain mode through the production chat turn

`Tools/ModelEval` SHALL accept `--mode full`, in which every case run is executed by the running host's
production chat turn. That turn includes the persona, the `QueryRouter` tool narrowing, the real MCP and
per-user tools, real retrieval and entity data, and the configured chat model. When `--mode` is omitted
the harness SHALL run the existing stub mode unchanged.

#### Scenario: Full-chain run uses the production turn

- **WHEN** the console is run with `--mode full` against a running host
- **THEN** each case run MUST be executed by the same turn core the chat UI uses, with the router
  narrowing the offered tools and real tools returning real data

#### Scenario: Stub mode is unchanged by default

- **WHEN** the console is run without `--mode`
- **THEN** it MUST run the existing stubbed scenario set and scorecard exactly as before

### Requirement: An admin eval-turn endpoint MUST return the turn's text and trace

The host SHALL expose `POST /admin/eval/turn`, guarded by the admin API key. It runs one isolated chat
turn for an explicit user id and returns the result below. When no user id is given, it resolves the
seeded user by username `test`; if that user does not exist, it MUST return 400 rather than run
without a user. The result contains:

- the final text;
- the names of the tools the router offered;
- each tool call with its arguments and its result or error;
- the latency.

The turn MUST NOT be persisted as chat history, MUST NOT read or modify any user's conversation, and
MUST NOT be subject to the chat rate limiter.

#### Scenario: Trace includes tool calls and results

- **WHEN** an eval turn asks a question that causes the model to call a tool
- **THEN** the response MUST include that tool's name, its arguments, and its result (or the binder
  error)

#### Scenario: Eval turns leave no chat history

- **WHEN** an eval turn completes for user id U
- **THEN** no `ChatTurn` row MUST be written for U by that turn

#### Scenario: Endpoint requires the admin key

- **WHEN** the endpoint is called without a valid `X-Admin-Api-Key`
- **THEN** it MUST be rejected, like every other `/admin` route

### Requirement: The chat UI turn behaviour MUST be preserved by the turn-core refactor

`DndChatService.SendAsync` SHALL keep its observable behaviour after the turn core is extracted:

- the rate limit;
- the signed-in user resolved from the HTTP context;
- history appended;
- turns persisted;
- `true`/`false` success semantics.

The per-user tools MUST be built for the explicit user id passed to the core.

#### Scenario: UI turn still appends and persists

- **WHEN** a signed-in user sends a chat message through `SendAsync`
- **THEN** the user and assistant messages MUST be appended to `History` and persisted, as before

### Requirement: Full-chain cases MUST be generated from canonical entity data and MAY be extended manually

The harness SHALL generate class, spell, and crafting cases deterministically for a given `--seed` and
per-category count, with these expected answers:

- **Class** answers come only from entities of type `Class` that have a hit die field.
- **Spell** answers come only from entities of type `Spell` with authority `canon`, not needing review,
  and whose asked field is 5etools-confirmed.
- **Crafting** answers come from `CraftingMath`.

The harness SHALL merge cases from `Tools/ModelEval/cases/manual.json`, which uses the same schema. Every
case MUST carry a stable id and a `generated` or `manual` tag. By default the generator SHALL read only
the `phb14` canonical file; `--books` MAY widen it, and backup or progress files MUST never be read.

#### Scenario: Same seed gives the same cases

- **WHEN** the harness generates cases twice with the same seed, count, and canonical data
- **THEN** both runs MUST produce identical case ids, prompts, and expected facts

#### Scenario: Misclassified entities are excluded

- **WHEN** a canonical entity is typed `Class` but has no hit die (e.g. a class feature), or typed
  `Spell` with `needsReview` true
- **THEN** the generator MUST NOT produce a case from it

#### Scenario: Crafting expectation comes from the calculator

- **WHEN** a crafting case is generated for a nonmagical item of market value V
- **THEN** its expected facts MUST equal `CraftingMath`'s computed materials cost and workweeks for V

#### Scenario: Manual cases are merged and tagged

- **WHEN** `cases/manual.json` contains a case
- **THEN** that case MUST run alongside generated cases and be reported with the `manual` tag

### Requirement: Each failed run MUST be attributed to a failing layer

The harness SHALL label every failed full-chain run with the first matching label, in this order:

1. `wrong-tool`: no tool from the case's acceptable-tool set was called. The report MUST distinguish
   not-offered-by-router from offered-but-not-chosen.
2. `bind-error`: an acceptable tool was called but errored. An error is an exception, an MCP `isError`
   result, result text starting `Error:`, or an object result with an `error` field.
3. `wrong-tool-data`: the combined tool results do not contain every expected tool-data variant.
4. `wrong-answer`: the tool data was correct but the final text does not contain every expected answer
   fact.

#### Scenario: Correct data but wrong final answer

- **WHEN** the tool result contains "3rd-level" for Fireball but the final text says "2nd-level"
- **THEN** the run MUST be labelled `wrong-answer`

#### Scenario: Correct answer via any acceptable tool passes

- **WHEN** a spell case accepts `search_entities`, `get_entity`, and `search_lore`, and the model
  answers correctly after calling only `search_lore`
- **THEN** the run MUST pass the tool check

#### Scenario: Router did not offer the expected tool

- **WHEN** no tool from the acceptable set was among the router's offered tools
- **THEN** the run MUST be labelled `wrong-tool` with the not-offered-by-router reason

### Requirement: Full-chain runs MUST be repeated, saved, and compared with a baseline

Each case SHALL run N times (default 3) and be reported as k/N. A case passes when a majority of its runs
pass. Each full-chain run MUST write a JSON report under `Tools/ModelEval/reports/`, named by UTC
timestamp, model, and mode, containing:

- the seed and case-set hash;
- per-case k/N and failure labels;
- per-category totals.

The harness MUST compare the run with a baseline, which is the previous full-mode report or the one
named by `--baseline`. The comparison MUST print per-category deltas and the cases that newly fail and
newly pass. When the case sets differ, it MUST compare only shared case ids and say so.

#### Scenario: Baseline comparison lists newly failing cases

- **WHEN** a case passed in the baseline and fails in the current run
- **THEN** the comparison MUST list it as newly failing

### Requirement: A category drop of more than one case MUST be reported as a regression

The harness SHALL treat any category whose passing-case count is more than one lower than the baseline
as a regression and exit with code 2. A drop of one case or fewer SHALL be reported but MUST exit with
code 0.

#### Scenario: Two-case drop blocks

- **WHEN** the spell category passes 5 cases in the baseline and 3 in the current run
- **THEN** the harness MUST report a regression for spell and exit with code 2

#### Scenario: One-case drop does not block

- **WHEN** a category drops by exactly one passing case
- **THEN** the harness MUST report the drop and exit with code 0

### Requirement: The dev flow MUST run the full-chain eval on every chat-model change

The `dev-flow` skill SHALL require that a change to the chat model (model name, tag, or quantisation)
is not done until full-chain eval has been run on both the previous and the new model and compared. A
regression MUST either be fixed or be explicitly accepted by the owner. The skill SHALL also document
running the eval on demand.

#### Scenario: Model swap requires an eval comparison

- **WHEN** `Ollama:ChatModel` is changed
- **THEN** the dev flow MUST call for a full-chain eval of the old and new model, with a `--baseline`
  comparison, before the change is claimed done
