## Context

`Tools/ModelEval` (shipped 2026-07-14, extended 2026-07-15 for persona tuning) is a console that drives
`OllamaChatClient(...).UseFunctionInvocation()` against **stubbed** `AIFunction`s. That isolates the
model. It scores four things: selection, binding, adherence (including the no-list and number-label
symptoms), and latency. It was used once to choose think-on and has not run since.

The production chat turn is `DndChatService.SendAsync` (`Features/Chat/DndChatService.cs`), which:

1. builds the tool list from the MCP tools plus ~15 per-user in-process tools that close over the
   signed-in user id read from `IHttpContextAccessor`;
2. sends `[System(persona), ...History.TakeLast(12)]`;
3. narrows the tools via `QueryRouter.RouteAsync`;
4. calls `chatClient.GetResponseAsync`;
5. returns only `bool` and appends the reply text to `History`.

The tool calls and results exist in `response.Messages` (`FunctionCallContent` /
`FunctionResultContent`) but are thrown away.

The owner wants three kinds of wrong answer caught end to end (wrong class fact, wrong spell fact,
wrong calculation), starting small and expanding later, and the eval run on every chat-model change
and on demand.

Canonical data notes, found during recon of `books/canonical/phb14.json`:

- Some class *features* are typed `Class` (e.g. "Action Surge", "Coinage"). Only real classes have a
  `fields.hd` hit die.
- Some non-spells are typed `Spell` (e.g. "Spell Slots", `authority: canon-unindexed`,
  `needsReview: true`).
- 5etools-confirmed fields are listed in `fields._fivetoolsFilledFields`.
- Spell `school` is a 5etools letter code (`V` = Evocation) and `range` is a structured object.

## Goals / Non-Goals

**Goals:**

- Score the real production chat turn (persona, router, real tools, real data, configured model)
  against questions with known-correct answers.
- Attribute each failure to a layer: router/model choice, binder, tool data, or composition.
- Make results comparable over time and across models: a seeded case set, k/N per case, saved reports,
  a baseline diff, and a regression exit code.
- Start with class, spell, and crafting cases, and make adding categories a matter of writing a new
  template or manual case.

**Non-Goals:**

- Replacing or changing stub mode. It stays the default and its scoring is unchanged.
- Running in `dotnet test` or CI. Full-chain needs the live stack.
- An LLM-as-judge. All starter checks are deterministic fact matches.
- Context-engineering changes (tool-description trimming, chunk caps, history trimming). That is the
  follow-up change, which will use this eval.
- Rules, lore, encounter, or NPC categories. These are possible later expansions, not v1.

## Decisions

### D1 — Run full-chain turns in-host via an admin endpoint; the console orchestrates

`POST /admin/eval/turn` (admin-key guarded, like every `/admin` route) runs one isolated turn and
returns `{ text, offeredTools[], toolCalls[{name, arguments, result|error}], latencyMs }`. The console
then handles case generation, the N runs, scoring, the report, and the baseline comparison, calling
the endpoint for each run.

- *Why:* the real tools need the host's DI graph: MCP client, Qdrant, Postgres, the services behind the
  per-user tools, the router, and the persona. Rebuilding that graph in the console would duplicate the
  composition root and drift from production, which is the very gap this change closes. The console
  keeps the eval logic out of the app runtime.
- *Alternatives:*
  - (a) Reference the host project from the console and build a `WebApplication` in-process. This is
    heavy, doubles startup, and the MCP loopback client expects a listening server.
  - (b) Drive the Blazor UI with Playwright. This is flaky and slow, and gives no trace.

### D2 — Extract a user-explicit, trace-returning turn core from `DndChatService`

Refactor `SendAsync` into a core such as `RunTurnAsync(IReadOnlyList<ChatMessage> messages, long? userId,
bool allowWebSearch, CancellationToken)`. The core returns a `ChatTurnResult` holding the text plus the
trace built from `response.Messages`, along with the router's offered tool names. `SendAsync` keeps its
current observable behaviour: rate limit, user from `HttpContext`, history append, and persistence. It
calls the core with the HttpContext user and discards the trace. The eval endpoint calls the core
directly with the requested user id, a fresh single-message history, no persistence, and no rate
limiting.

- *Why:* there is one code path for UI and eval, so the eval measures production. Per-user tool
  construction must take an explicit user id rather than read `IHttpContextAccessor`, which in the eval
  request is the admin caller with no `NameIdentifier`.
- The eval endpoint takes `userId` in the body, defaulting to the seeded `test` user, and optionally
  `campaignId`/`heroSnapshotId` context for future per-user categories. Admin-key access already
  implies full trust; the per-user ownership checks still apply inside each service.

### D3 — Failure attribution order

For each run, the first applicable label is chosen:

1. `wrong-tool`: the expected tool is not among the called tools. The trace also records whether the
   expected tool was *offered*, so the report can separate a **router** miss (not offered) from a
   **model** miss (offered but not chosen).
2. `bind-error`: the expected tool was called but its result is an exception.
3. `wrong-tool-data`: the tool result does not contain the expected fact. This is a data or retrieval
   problem, not the model's fault.
4. `wrong-answer`: the tool data was right but the final text does not contain the expected fact. The
   model garbled it or made it up.

A case can opt out of check 3 (`toolDataCheck: null`) when its tool output isn't reliably inspectable.

Fact matching normalises case, whitespace, and number formats ("8d6", "150 feet"/"150 ft",
"3rd-level"/"level 3"). Each case carries a list of accepted variants rather than relying on a fuzzy
matcher.

### D4 — Cases: a seeded generator over canonical JSON plus a manual file

The generator reads `books/canonical/*.json` (the source of truth that `dnd_entities` is ingested from)
and applies a fixed seed (default `--seed 42`) and a per-category count (default 5).

- **Class** cases. Eligible entities have `type == "Class"` and a `fields.hd`, which drops misclassified
  features. Templates:
  - "What hit die does a {name} have?" → `d{faces}`.
  - "What saving throw proficiencies does a {name} get?" → each of `fields.proficiency`, mapped to
    ability names.
- **Spell** cases. Eligible entities have `type == "Spell"`, `authority == "canon"`, and
  `needsReview == false`, and the asked field must be listed in `_fivetoolsFilledFields`. Templates:
  - level ("3rd-level" / "level 3");
  - school (letter code mapped to the name);
  - range (rendered from the structured object; a template is skipped for range types it can't render).
- **Crafting** cases. The generator draws nonmagical market values and magic-item rarities and computes
  the expected numbers by calling `CraftingMath` directly. The console references the shared code, or
  links the file if a project reference is too heavy. The expected tool is `calculate_crafting`.

Expected tool per category: class and spell → `search_entities` (or the MCP entity lookup tool the
router offers for fact questions; the implementer confirms the exact name from `ToolGroups`). This is
stored per case so it is easy to change.

`Tools/ModelEval/cases/manual.json` uses the same case schema: id, category, prompt, expectedTool,
expectedFacts with variants, and an optional toolDataCheck. Manual cases merge in after the generated
ones. Case ids are stable: generated ids are derived from the entity id and the template, so baselines
line up across runs.

### D5 — Reports and baseline

Each run writes `Tools/ModelEval/reports/<UTC timestamp>-<model>-full.json`. The report holds the
seed, case set hash, model, per-case k/N, failure labels, and per-category totals.

- The **baseline** is the most recent previous report for mode `full`. `--baseline <path>` overrides it
  so an old-model run can be compared with a new-model run explicitly.
- A **regression** is any category whose passing-case count (a case passes when it is ≥2/3, i.e. a
  majority of N) drops by more than one versus the baseline. The console exits with code 2 on
  regression, 0 otherwise.
- If the baseline's case set hash differs from the current one (cases added or changed), the comparison
  covers only the shared case ids and says so.
- Reports are committed. They are small, and they are the history of quality over time.

### D6 — Dev-flow trigger

`.claude/skills/dev-flow` gains a rule: when `Ollama:ChatModel` changes (or the model or its quant is
swapped), run full-chain eval on the old model and the new one. A regression exit means the change is
not done until it is fixed or explicitly accepted by the owner. The eval is also available on demand.
The skill path is sandbox-protected, so the edit is proposed and made with explicit permission.

### D7 — Builder-check clarifications (owner-delegated calls)

- **Expected tool is a set.** `expectedTools` lists every acceptable tool. Class and spell cases accept
  `search_entities`, `get_entity`, and `search_lore`/`search_dnd`, because the router's embedding
  backstop often routes bare fact questions to the lore group. A run passes the tool check if *any*
  called tool is in the set. A correct answer reached through retrieval passes. `wrong-tool` with the
  router-vs-model reason still fires when none of the set was offered or called.
- **The tool-data check has its own variants.** `toolDataVariants` (raw forms such as `"faces":12`,
  `"school":"V"`, `"amount":150`) are matched against the concatenated results of *all* calls.
  `answerVariants` (human forms such as `d12`, `Evocation`, `150 feet`) are matched against the final
  text. There is no JSON parsing. A case may set `toolDataVariants` to null to skip the data check.
- **v1 books = `phb14` only.** That is the core, fully ingested source, and it keeps v1 small, in line
  with the owner's "start small". The prompt names the book ("in the 2014 Player's Handbook") so
  duplicates aren't ambiguous. `--books` widens the scope later. `.bak` and `.progress*` files and
  non-canonical names are never read. The book list is part of the case-set hash.
- **What counts as a `bind-error`.** Any of: an `Exception` on `FunctionResultContent`; an MCP
  `isError` result; result text starting `Error:`; or an object result with a top-level `error` field.
  The trace records a normalised `error` string per call.
- **Multi-fact cases require all facts.** Saving throws need both abilities; crafting needs both the
  materials/gold and the workweeks. Each fact may have its own variants.
- **The default eval user is resolved by username `test`** (the seeded dev user) at request time. If
  that user is missing, the endpoint returns 400 and does not run without a user, because per-user
  tools such as `calculate_crafting` would otherwise be absent.

## Risks / Trade-offs

- [qwen3 nondeterminism makes single runs noisy] → N runs with majority pass. The regression threshold
  is >1 case per category, so one flaky case cannot block.
- [The eval mutates nothing but still invokes real tools] → It uses read-only tools for v1 (lookups and
  pure math). Per-user write tools are not in the starter categories.
- [Canonical JSON and the ingested `dnd_entities` drift] → If they drift, a `wrong-tool-data` label
  shows up. That is a true finding (ingestion stale), not a harness bug.
- [Refactoring `DndChatService` regresses the UI path] → Existing chat tests must stay green. Add a test
  that `SendAsync` still appends and persists, and that the core builds the per-user tools for the
  explicit user id.
- [Slow: ~12–16 s per model call] → The default set is ~15 cases × 3 runs, about 10–15 minutes.
  `--category` and `--count` flags allow a quicker subset.
- [Admin endpoint as a new surface] → Admin-key guarded like the rest of `/admin`. It runs as an explicit
  user, so per-user ownership checks still apply.

## Open Questions

- None blocking. The acceptable-tool set per category is confirmed against `ToolGroups` during
  implementation (D7).
