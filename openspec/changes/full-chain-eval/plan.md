# Full-Chain Eval Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended)
> or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`)
> syntax for tracking.

**Goal:** Add a full-chain mode to `Tools/ModelEval`. It scores generated and manual class, spell, and
crafting questions through the production chat turn, attributes each failure to a layer, saves
reports, and exits 2 on a regression against the baseline.

**Architecture:** The host gets a trace-returning turn core that is extracted from `DndChatService`, and
an admin endpoint `POST /admin/eval/turn` that runs that core for an explicit user. The console does
the rest: it generates cases from `books/canonical/phb14.json` plus `cases/manual.json`, calls the
endpoint N times per case, labels failures, writes a JSON report, diffs it against the previous one,
and sets the exit code.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, Microsoft.Extensions.AI (`ChatResponse`,
`FunctionCallContent`, `FunctionResultContent`), xunit + FluentAssertions + NSubstitute, System.Text.Json.

**Spec:** `openspec/changes/full-chain-eval/` — read `design.md` (D1–D7) and
`specs/full-chain-eval/spec.md` alongside this plan.

## Global Constraints

- Warnings-as-errors for every project (`Directory.Build.props`). Package versions live only in
  `Directory.Packages.props`.
- Use **Serena** symbolic tools for all code reads and edits. Call `initial_instructions` first, then
  activate project `DndMcpAICsharpFun`.
- `dotnet build` / `dotnet test` need `dangerouslyDisableSandbox: true` (git-crypt config).
- Every endpoint change updates `DndMcpAICsharpFun.http` **and** `dnd-mcp-api.insomnia.json` in the same
  commit.
- Hand-written markdown (README, CLAUDE.md, docs/) must pass `pnpm lint:md` with 0 errors.
- Work and commit on `main`. Each task ends in its own commit.
- Stub mode (no `--mode`, or `--mode stub`) MUST behave exactly as today.
- Defaults: `--runs 3`, `--seed 42`, `--count 5` per category, `--books phb14`. A case passes on a
  majority of runs (≥2/3). A regression is a category drop of **>1** passing case → exit 2.
- Failure labels, in this exact order and spelling: `wrong-tool`, `bind-error`, `wrong-tool-data`,
  `wrong-answer`. `wrong-tool` carries the reason `not-offered` or `not-chosen`.
- The eval turn never persists `ChatTurn` rows, never touches `History`, and bypasses `ChatRateLimiter`.

## Review Focus

1. **The host is down, or the admin key is wrong.** The runner must abort with exit 1 and a clear
   message, not record every case as `wrong-tool`. Test it in Task 6.
2. **A single run times out or returns 5xx mid-set** (an Ollama hiccup). That run is labelled
   `infra-error`, counts as a failed run, and is listed separately in the report, so the operator can
   tell infra trouble from model trouble. Test it in Task 6.
3. **Model names with `:` or `/`** (`qwen3:8b`, `hf.co/x/y`) used in the report filename must be
   sanitised to `[a-z0-9._-]`. Test it in Task 7.
4. **No previous report exists** (the first ever run). There is no baseline, the comparison prints
   "no baseline", and the exit code is 0. Test it in Task 7.
5. **Fewer eligible entities than `--count`.** The generator returns what exists, without throwing or
   repeating cases. Test it in Task 5.

---

### Task 1: Chat turn trace model and builder

**Files:**

- Create: `Features/Chat/ChatTurnTrace.cs`
- Test: `DndMcpAICsharpFun.Tests/Chat/ChatTurnTraceTests.cs`

**Interfaces:**

- Produces:
  - `public sealed record ToolCallTrace(string Name, string ArgumentsJson, string? Result, string? Error);`
  - `public sealed record ChatTurnResult(string Text, IReadOnlyList<string> OfferedTools, IReadOnlyList<ToolCallTrace> ToolCalls, long LatencyMs);`
  - `public static class ChatTurnTraceBuilder { public static IReadOnlyList<ToolCallTrace> Build(ChatResponse response); }`

- [ ] **Step 1: Write the failing tests** in `ChatTurnTraceTests`. Each test builds a `ChatResponse` by
  hand from `ChatMessage`s carrying `FunctionCallContent` / `FunctionResultContent`.
  - `Build_PairsCallWithResultByCallId`: a call `get_entity` with args `{id:"phb14.spell.fireball"}` and a
    string result → one trace, `Name == "get_entity"`, `ArgumentsJson` contains `"phb14.spell.fireball"`,
    `Result` equals the string, `Error` is null.
  - `Build_SerializesObjectResultsAsJson`: the result is `new { materialsGp = 750 }` → `Result` contains
    `"materialsGp":750`.
  - `Build_ExceptionResult_SetsError`: the result has `Exception = new InvalidOperationException("missing required parameter")`
    → `Error` contains `"missing required parameter"`.
  - `Build_ErrorObjectOrErrorText_SetsError`: an `{ error = "Unknown rarity" }` result, and a string result
    `"Error: boom"` → both set `Error` (D7 bind-error forms). An MCP `isError` result also counts: when
    the MCP client surfaces it as a result whose text starts `Error:` it is covered by this case. The
    implementer checks the MCP client's representation and adds a case if it differs.
  - `Build_MultipleCalls_PreservesOrder`.
- [ ] **Step 2: Run** `dotnet test --filter FullyQualifiedName~ChatTurnTraceTests`. Expected: FAIL to
  compile.
- [ ] **Step 3: Implement** the records and `Build`. Serialize non-string results with
  `JsonSerializerDefaults.Web`. Detect an error object by checking for a top-level `error` property
  after serialization.
- [ ] **Step 4: Run** the same filter. Expected: PASS.
- [ ] **Step 5: Commit** with message `feat(chat): add chat turn trace model and builder`.

### Task 2: Extract the trace-returning turn core from `DndChatService`

**Files:**

- Modify: `Features/Chat/DndChatService.cs` (`SendAsync`, plus the new members below)
- Test: `DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs`

**Interfaces:**

- Consumes: `ChatTurnResult`, `ChatTurnTraceBuilder.Build` (Task 1).
- Produces:
  - `public Task<ChatTurnResult> RunTurnAsync(IReadOnlyList<ChatMessage> conversation, long? userId, bool allowWebSearch, CancellationToken ct)`
    - prepends `System(persona)`, sends `conversation.TakeLast(MaxModelHistoryMessages)`, routes the
      tools, calls the model, and returns text + offered tool names + trace + latency;
    - throws on model failure (the caller decides what to do);
    - performs **no** persistence, history mutation, or rate limiting.
  - `private async Task<List<AITool>> BuildToolsAsync(long? userId, bool allowWebSearch, CancellationToken ct)`
    - the current tool-list construction, moved verbatim;
    - per-user tools are added only when `userId` has a value and close over that value.

- [ ] **Step 1: Write the failing tests:**
  - `RunTurnAsync_WithUserId_OffersPerUserTools`: a fake `IChatClient` captures `ChatOptions.Tools`.
    Call with `userId: 7` → the captured tool names include `calculate_crafting` and
    `resolve_character_feature`. Use a pass-through router setup consistent with the existing tests.
  - `RunTurnAsync_NullUser_OffersNoPerUserTools` → none of the per-user names is present.
  - `RunTurnAsync_DoesNotTouchHistory` → `History` is empty after the call.
  - `RunTurnAsync_ReturnsTraceAndOfferedTools`: the fake client returns a response containing one
    call/result pair → `ToolCalls.Count == 1`, and `OfferedTools` equals the names the router passed on.
  - The existing `SendAsync` tests stay unchanged and green (append, failure → `false`).
- [ ] **Step 2: Run** `dotnet test --filter FullyQualifiedName~DndChatServiceTests`. Expected: the new
  tests FAIL.
- [ ] **Step 3: Implement.** Move the tool construction into `BuildToolsAsync`, add `RunTurnAsync`, and
  make `SendAsync` do the following:
  1. rate limit;
  2. resolve the user from `HttpContext`;
  3. append and persist the user message;
  4. call `RunTurnAsync(History, userId, allowWebSearch, ct)`;
  5. append and persist the reply;
  6. keep the same `catch` semantics.

  Measure latency with a `Stopwatch` around `GetResponseAsync`.
- [ ] **Step 4: Run** the chat tests. Expected: PASS (all old and new tests).
- [ ] **Step 5: Commit** with message `refactor(chat): extract trace-returning turn core`.

### Task 3: `POST /admin/eval/turn` endpoint

**Files:**

- Create: `Features/Admin/EvalAdminEndpoints.cs`
- Modify: `Program.cs` (add `admin.MapEvalAdmin();` next to `admin.MapRetrievalAdmin();`),
  `DndMcpAICsharpFun.http`, `dnd-mcp-api.insomnia.json`
- Test: `DndMcpAICsharpFun.Tests/Admin/EvalAdminEndpointsTests.cs`

**Interfaces:**

- Consumes: `DndChatService.RunTurnAsync` (Task 2), `UserRepository.FindByUsernameAsync(string)`.
- Produces:
  - `public sealed record EvalTurnRequest(string Prompt, long? UserId = null, bool AllowWebSearch = false);`
  - `public sealed record EvalTurnResponse(string Model, string Text, IReadOnlyList<string> OfferedTools, IReadOnlyList<ToolCallTrace> ToolCalls, long LatencyMs);`
    The JSON is camelCase (web defaults). `Model` comes from config `Ollama:ChatModel`.
  - `public static RouteGroupBuilder MapEvalAdmin(this RouteGroupBuilder group)`
  - The endpoint calls the turn core through a thin seam, `public interface IEvalTurnRunner { Task<ChatTurnResult> RunAsync(string prompt, long userId, bool allowWebSearch, CancellationToken ct); }`.
    Its default implementation wraps `DndChatService.RunTurnAsync` with `[new(ChatRole.User, prompt)]`.
    Register it scoped in `Extensions/ChatExtensions.cs`. The seam exists so the endpoint test doesn't
    have to construct `DndChatService`.

- [ ] **Step 1: Write the failing tests.** Use the `NeedsReviewEndpointsTests` minimal-`WebApplication`
  pattern, with `AdminApiKeyMiddleware`, a fake `IEvalTurnRunner`, and a fake user lookup.
  - `NoAdminKey_Returns401`.
  - `ValidRequest_ReturnsTraceJson` → 200, and the body has `model`, `text`, `toolCalls[0].name`.
  - `NoUserId_ResolvesTestUser`: the user `test` has id 42 → the fake runner receives `userId == 42`.
  - `NoUserId_TestUserMissing_Returns400`.
  - `EmptyPrompt_Returns400`.
  - Persistence: this is guaranteed because the endpoint only calls `RunTurnAsync`, which never
    persists (Task 2 test `RunTurnAsync_DoesNotTouchHistory`). No DB is needed here.

  For the user lookup: `UserRepository` is concrete. Wrap the lookup in
  `Func<string, Task<long?>>`-style DI or a small `IEvalUserResolver`, whichever matches how other
  admin tests fake concrete repos. The implementer chooses one and keeps it internal to this feature.
- [ ] **Step 2: Run** `dotnet test --filter FullyQualifiedName~EvalAdminEndpointsTests`. Expected: FAIL.
- [ ] **Step 3: Implement** the endpoint, the runner, the registration, and the `Program.cs` line.
- [ ] **Step 4: Run** the tests. Expected: PASS.
- [ ] **Step 5: Update** `DndMcpAICsharpFun.http` with a `POST /admin/eval/turn` example that has the
  `X-Admin-Api-Key` header and the body `{"prompt":"What hit die does a Wizard have in the 2014 Player's Handbook?"}`,
  and mirror it in `dnd-mcp-api.insomnia.json`.
- [ ] **Step 6: Commit** with message `feat(admin): add eval-turn endpoint returning chat trace`.

### Task 4: Full-chain case model, manual loader, fact matcher

**Files:**

- Create: `Tools/ModelEval/Full/EvalCase.cs`, `Tools/ModelEval/Full/ManualCases.cs`,
  `Tools/ModelEval/Full/FactMatcher.cs`, `Tools/ModelEval/cases/manual.json`
- Modify: `Tools/ModelEval/ModelEval.csproj` (copy `cases/**` to the output)
- Test: `DndMcpAICsharpFun.Tests/ModelEval/FactMatcherTests.cs`, `.../ManualCasesTests.cs`

**Interfaces:**

- Produces:
  - `internal sealed record Fact(IReadOnlyList<string> Variants);` (a fact matches if any variant matches)
  - `internal sealed record EvalCase(string Id, string Category, string Tag, string Prompt, IReadOnlyList<string> ExpectedTools, IReadOnlyList<Fact>? ToolDataFacts, IReadOnlyList<Fact> AnswerFacts);`
    - `Category` is one of `class`, `spell`, `crafting`, or any string for manual cases.
    - `Tag` is `generated` or `manual`.
  - `internal static class FactMatcher`:
    - `public static string Normalize(string s)`: lowercases and removes all whitespace and commas.
    - `public static bool AllMatch(IReadOnlyList<Fact> facts, string haystack)`: every fact has a
      variant contained in `Normalize(haystack)`, comparing against `Normalize(variant)`.
  - `internal static class ManualCases { public static IReadOnlyList<EvalCase> Load(string path); }`
    - forces `Tag = "manual"`;
    - returns empty if the file is missing;
    - throws with the case id on a missing `id`, `prompt`, `expectedTools`, or `answerFacts`.

- [ ] **Step 1: Write the failing tests:**
  - `Normalize_StripsCaseWhitespaceCommas`: `"2,000 GP"` → `"2000gp"`.
  - `AllMatch_RequiresEveryFact`: facts `[["d6"],["intelligence"]]` against `"Wizards use a d6"` → false;
    against `"...d6... Intelligence and Wisdom"` → true.
  - `AllMatch_AnyVariantSuffices`: `[["3rd-level","level 3","3rd level"]]` against `"It's a level 3 spell"` → true.
  - `Load_MissingFile_ReturnsEmpty`; `Load_ForcesManualTag`; `Load_MissingPrompt_ThrowsWithId`.
- [ ] **Step 2: Run** `dotnet test --filter "FullyQualifiedName~FactMatcherTests|FullyQualifiedName~ManualCasesTests"`.
  Expected: FAIL.
- [ ] **Step 3: Implement.** Use `System.Text.Json` with camelCase. Seed `cases/manual.json` with one
  case:
  - id `manual.multiclass-wizard-hitdie`;
  - prompt: "My character is a level 1 Wizard. What hit die do they roll, per the 2014 Player's Handbook?";
  - expectedTools: `get_entity`, `search_entities`, `search_lore`, `search_dnd`;
  - answerFacts: `[["d6"]]`;
  - toolDataFacts: `[["\"faces\":6","d6"]]`.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** with message `feat(model-eval): add full-chain case model, matcher, manual cases`.

### Task 5: Seeded case generators

**Files:**

- Create: `Tools/ModelEval/Full/CaseGenerator.cs`
- Test: `DndMcpAICsharpFun.Tests/ModelEval/CaseGeneratorTests.cs`. Fixtures are small inline canonical
  JSON written to a temp dir; the real `phb14.json` is not used.

**Interfaces:**

- Consumes: `EvalCase`, `Fact` (Task 4); `CraftingMath.CraftNonmagical(int, int)`,
  `CraftingMath.CraftMagicItem(Rarity)` (`Features/Crafting`).
- Produces:
  - `internal sealed record GenOptions(int Seed, int CountPerCategory, IReadOnlyList<string> Books, IReadOnlyList<string>? Categories);`
  - `internal static class CaseGenerator`:
    - `public static IReadOnlyList<EvalCase> Generate(string canonicalDir, GenOptions o);`
    - `public static string CaseSetHash(IEnumerable<EvalCase> cases, IReadOnlyList<string> books);`
      This is SHA-256 hex over the sorted `id|prompt` lines plus the sorted books.

**Values pinned by the spec and design (D4/D7):**

- **Files read:** `<canonicalDir>/<book>.json` for each book, and nothing else. Entities come from the
  top-level `entities` array.
- **Class eligibility:** `type == "Class"` and `fields.hd` exists.
  - Hit die: id `gen.class.<entityId>.hitdie`, prompt `What hit die does a {name} have in the 2014 Player's Handbook?`,
    answer `[["d{faces}"]]`, toolData `[["\"faces\":{faces}","d{faces}"]]`.
  - Saves: id `gen.class.<entityId>.saves`, prompt `What saving throw proficiencies does a {name} get in the 2014 Player's Handbook?`,
    with one answer fact per `fields.proficiency` code mapped
    str/dex/con/int/wis/cha → Strength/Dexterity/Constitution/Intelligence/Wisdom/Charisma.
    toolData: one fact per code, `["\"{code}\"", "{AbilityName}"]`.
- **Spell eligibility:** `type == "Spell"`, `authority == "canon"`, `needsReview == false`, and the asked
  field is in `fields._fivetoolsFilledFields`.
  - Level is asked only when level ≥ 1. Answer variants are `{n}{ordinal}-level`, `{n}{ordinal} level`,
    `level {n}`; toolData is `"level":{n}`.
  - School codes map A/C/D/E/V/I/N/T → Abjuration/Conjuration/Divination/Enchantment/Evocation/
    Illusion/Necromancy/Transmutation. Answer is the name; toolData is `["\"school\":\"{code}\"", name]`.
  - Range is asked only for `range.type == "point"` with `distance.type == "feet"`. Answer variants are
    `{n} feet`, `{n} ft`, `{n}-foot`; toolData is `"amount":{n}`.
  - Ids are `gen.spell.<entityId>.<level|school|range>`. Prompts follow the pattern
    `What level is the spell {name} in the 2014 Player's Handbook?`, and likewise for school and range.
- **Crafting:**
  - Nonmagical values are drawn from `[50, 150, 500, 1500]`. Id `gen.crafting.nonmagical.{v}`, prompt
    `How much do the materials cost and how many workweeks does it take to craft a nonmagical item with a market value of {v} gp?`.
    Answer: `MaterialsGp` and `TotalWorkweeks` formatted `G` (invariant culture). toolData:
    `"materialsGp":{m}`.
  - Rarities are drawn from all `Rarity` values. Id `gen.crafting.magic.{rarity}`, prompt
    `How much gold and how many workweeks does it take to craft a {rarity words} magic item?`. Answer:
    `GoldCostGp` and `Workweeks`.
  - ExpectedTools are `["calculate_crafting"]`.
- **Class/spell ExpectedTools:** `["get_entity","search_entities","search_lore","search_dnd"]`. Confirm
  these against `ToolGroups.Map`; all four are present there today.
- **Sampling:** per category, build the full candidate list ordered by id, shuffle it with
  `new Random(seed)`, and take `CountPerCategory`. Within a category, pick at most one template per
  entity, choosing the template by a second draw from the same `Random`.

- [ ] **Step 1: Write the failing tests:**
  - `SameSeed_SameCases`: two `Generate` calls give equal id/prompt/facts sequences and an equal
    `CaseSetHash`.
  - `DifferentSeed_DifferentSelection`: the fixture must have more than `count` candidates.
  - `ClassWithoutHitDie_Excluded`: a fixture entity typed `Class` named `"Action Surge"` with no `hd`
    never appears.
  - `SpellNeedingReview_Excluded`; `SpellFieldNotFivetoolsFilled_NotAsked`.
  - `CraftingExpectation_EqualsCraftingMath`: for each generated nonmagical case, the answer contains
    `CraftingMath.CraftNonmagical(v).MaterialsGp.ToString()`.
  - `FewerCandidatesThanCount_ReturnsAllWithoutRepeats` (Review Focus 5).
  - `BakAndProgressFilesIgnored`: `phb14.json.bak` and `phb14.progress.json` sit in the fixture dir with
    `Books=["phb14"]` → only `phb14.json` is read.
- [ ] **Step 2: Run** `dotnet test --filter FullyQualifiedName~CaseGeneratorTests`. Expected: FAIL.
- [ ] **Step 3: Implement** with `JsonDocument` traversal.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** with message `feat(model-eval): add seeded class/spell/crafting case generator`.

### Task 6: Full-chain runner and failure attribution

**Files:**

- Create: `Tools/ModelEval/Full/EvalTurnClient.cs`, `Tools/ModelEval/Full/Attribution.cs`,
  `Tools/ModelEval/Full/FullChainRunner.cs`
- Modify: `Tools/ModelEval/EvalArgs.cs`. Add `Mode` (`stub` default | `full`); `Host` (default
  `http://localhost:5101`); `AdminKey` (`--admin-key`, falling back to env `ADMIN_API_KEY`); `Seed`;
  `Count`; `Books`; `Categories`; `Baseline`; `CanonicalDir` (default `books/canonical`); `ReportsDir`
  (default `Tools/ModelEval/reports`). For `--mode full` the default `Runs` is 3, while stub mode keeps
  5.
- Test: `DndMcpAICsharpFun.Tests/ModelEval/AttributionTests.cs`, `.../FullChainRunnerTests.cs`

**Interfaces:**

- Consumes: `EvalCase`, `FactMatcher` (Task 4); the endpoint JSON shape `EvalTurnResponse` (Task 3).
  The console defines its own DTO mirror `TurnDto(string Model, string Text, List<string> OfferedTools, List<ToolCallDto> ToolCalls, long LatencyMs)`
  and `ToolCallDto(string Name, string ArgumentsJson, string? Result, string? Error)`.
- Produces:
  - `internal interface IEvalTurnClient { Task<TurnDto> RunAsync(string prompt, CancellationToken ct); }`
    The HTTP implementation `EvalTurnClient(HttpClient, adminKey)` uses a 180 s timeout.
  - `internal enum RunOutcome { Pass, WrongTool, BindError, WrongToolData, WrongAnswer, InfraError }`
  - `internal sealed record RunScore(RunOutcome Outcome, string? Reason, long LatencyMs);`
    `Reason` is `not-offered` / `not-chosen` for `WrongTool`, or the error text.
  - `internal static class Attribution { public static RunScore Score(EvalCase c, TurnDto t); }`. The
    order is pinned:
    1. no called tool ∈ `ExpectedTools` → `WrongTool`, with `not-offered` when no `ExpectedTools`
       member ∈ `OfferedTools`, else `not-chosen`;
    2. any called expected tool has `Error` and no called expected tool has a non-error result →
       `BindError`;
    3. `ToolDataFacts` is non-null and not `AllMatch` over the concatenated `Result`s →
       `WrongToolData`;
    4. not `AllMatch(AnswerFacts, Text)` → `WrongAnswer`;
    5. otherwise `Pass`.
  - `internal sealed record CaseResult(EvalCase Case, IReadOnlyList<RunScore> Runs) { public bool Passed => Runs.Count(r => r.Outcome == RunOutcome.Pass) * 2 > Runs.Count; }`
  - `internal static class FullChainRunner { public static Task<(string Model, IReadOnlyList<CaseResult> Results)> RunAsync(IReadOnlyList<EvalCase> cases, int runs, IEvalTurnClient client, CancellationToken ct); }`
    - It **pre-flights** with the first call. An `HttpRequestException`, a 401/403, or a 400 on that call
      throws `EvalInfraException` (Review Focus 1).
    - After a successful pre-flight, a per-run timeout or 5xx → `RunScore(InfraError, message, ms)`
      (Review Focus 2).

- [ ] **Step 1: Write the failing tests.** Use synthetic `TurnDto`s and a fake `IEvalTurnClient`.
  - Attribution:
    - `NotOffered_WrongToolNotOffered`;
    - `OfferedNotCalled_WrongToolNotChosen`;
    - `CalledWithError_BindError`;
    - `DataMissingFact_WrongToolData`;
    - `DataOkAnswerWrong_WrongAnswer` (Fireball data `"level":3`, text "2nd-level");
    - `AnyAcceptableToolPasses` (only `search_lore` called, correct text → Pass);
    - `NullToolDataFacts_SkipsDataCheck`.
  - Runner:
    - `FirstCallUnreachable_ThrowsInfra`;
    - `FirstCall401_ThrowsInfra`;
    - `MidRunTimeout_InfraErrorRun`: the fake throws `TaskCanceledException` on call 3 →
      `InfraError`, and the set continues;
    - `Majority_Passed` (2/3 → `Passed`, 1/3 → not).
- [ ] **Step 2: Run** `dotnet test --filter "FullyQualifiedName~AttributionTests|FullyQualifiedName~FullChainRunnerTests"`.
  Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS. Run the existing `StubToolBindingTests` and
  `SymptomChecksTests` too. Expected: PASS (stub mode untouched).
- [ ] **Step 5: Commit** with message `feat(model-eval): add full-chain runner with failure attribution`.

### Task 7: Reports, baseline diff, regression exit, `Program` wiring

**Files:**

- Create: `Tools/ModelEval/Full/EvalReport.cs`, `Tools/ModelEval/Full/BaselineComparer.cs`,
  `Tools/ModelEval/reports/.gitkeep`
- Modify: `Tools/ModelEval/Program.cs`. Branch on `Mode`. The stub path stays byte-for-byte the current
  logic. The full path runs: generate + manual → `FullChainRunner` → write report → compare → print →
  return the exit code. `EvalInfraException` → print to stderr, return 1.
- Test: `DndMcpAICsharpFun.Tests/ModelEval/EvalReportTests.cs`, `.../BaselineComparerTests.cs`

**Interfaces:**

- Consumes: `CaseResult`, `RunOutcome` (Task 6); `CaseGenerator.CaseSetHash` (Task 5).
- Produces:
  - `internal sealed record CaseReport(string Id, string Category, string Tag, int Passes, int Runs, bool Passed, IReadOnlyList<string> Outcomes);`
    `Outcomes` holds per-run labels: `pass`, `wrong-tool:not-offered`, `wrong-tool:not-chosen`,
    `bind-error`, `wrong-tool-data`, `wrong-answer`, `infra-error`.
  - `internal sealed record EvalReport(string Mode, string Model, DateTime UtcTimestamp, int Seed, IReadOnlyList<string> Books, string CaseSetHash, IReadOnlyList<CaseReport> Cases, IReadOnlyDictionary<string, int> PassedByCategory, IReadOnlyDictionary<string, int> TotalByCategory);`
  - `internal static class EvalReportStore`:
    - `public static string Write(EvalReport r, string dir)` returns the path. The filename is
      `{yyyyMMdd-HHmmss}-{SanitizeModel(model)}-full.json`.
    - `public static string SanitizeModel(string m)` lowercases and maps every char outside
      `[a-z0-9._-]` to `_`.
    - `public static EvalReport? LatestBefore(string dir, string excludePath)`: the newest
      `*-full.json` other than the just-written report, or null.
  - `internal sealed record Comparison(bool HasBaseline, bool SameCaseSet, IReadOnlyDictionary<string,(int Baseline,int Current)> ByCategory, IReadOnlyList<string> NewlyFailing, IReadOnlyList<string> NewlyPassing, IReadOnlyList<string> RegressedCategories);`
  - `internal static class BaselineComparer`:
    - `public static Comparison Compare(EvalReport? baseline, EvalReport current)` compares on shared
      case ids only. A category is regressed when `baseline - current > 1`.
    - `public static int ExitCode(Comparison c) => c.RegressedCategories.Count > 0 ? 2 : 0;`

- [ ] **Step 1: Write the failing tests:**
  - `SanitizeModel`: `"qwen3:8b"` → `"qwen3_8b"`, `"hf.co/A/b"` → `"hf.co_a_b"` (Review Focus 3).
  - `Write_ThenLatestBefore_FindsPrevious`: write two reports, then `LatestBefore(dir, second)` returns
    the first.
  - `NoBaseline_ExitZero_HasBaselineFalse` (Review Focus 4).
  - `TwoCaseDrop_Regressed_Exit2`: spell 5 → 3.
  - `OneCaseDrop_NotRegressed_Exit0`.
  - `NewlyFailingListed`.
  - `DifferentCaseSets_CompareSharedIdsOnly`: the baseline has ids `{a,b,c}` and the current run has
    `{b,c,d}`. Only b and c are compared, and `SameCaseSet == false`.
- [ ] **Step 2: Run** `dotnet test --filter "FullyQualifiedName~EvalReportTests|FullyQualifiedName~BaselineComparerTests"`.
  Expected: FAIL.
- [ ] **Step 3: Implement**, plus the `Program.cs` full-mode branch. It prints a per-category table:
  category, passed/total, Δ vs baseline, and a `REGRESSION` marker. Then it prints the newly failing
  and newly passing cases, and the failure-label counts per category.
- [ ] **Step 4: Run** the full test suite: `dotnet test`. Expected: all green, and
  `dotnet build` reports 0 warnings.
- [ ] **Step 5: Commit** with message `feat(model-eval): add reports, baseline comparison and regression exit`.

### Task 8: Dev-flow trigger, docs, live baseline

**Files:**

- Modify: `.claude/skills/dev-flow/SKILL.md` (sandbox-protected: propose the diff and get the owner's
  permission before editing), `CLAUDE.md` (Commands section)
- Create: `Tools/ModelEval/reports/<first run>.json` (committed baseline)

- [ ] **Step 1: Add to dev-flow** a rule: "Chat-model change → before claiming done, run
  `dotnet run --project Tools/ModelEval -- --mode full` on the old model and on the new one, then compare
  with `--baseline <old report>`. Exit 2 = regression: fix it, or get explicit owner acceptance.
  On-demand: same command, any time chat quality is in doubt." Include the flags and that the live stack
  must be up.
- [ ] **Step 2: Add the command** to `CLAUDE.md` Commands. Then run `pnpm lint:md:fix && pnpm lint:md`.
  Expected: `0 error(s)`.
- [ ] **Step 3: Live run.** With the stack up (see Serena memory `operations/running_the_stack`), run
  `dotnet run --project Tools/ModelEval -- --mode full --admin-key $ADMIN_API_KEY`. Expected: exit 0,
  "no baseline", and a report written. Record the per-category pass rates and any `wrong-tool-data` or
  `wrong-answer` findings in `openspec/changes/full-chain-eval/report.md`.
- [ ] **Step 4: Commit** the report, the docs, and the skill change with message
  `chore(model-eval): add dev-flow eval trigger and first full-chain baseline`.
