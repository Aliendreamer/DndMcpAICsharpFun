# Chat Tool Binding Fixes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every optional chat-tool parameter schema-optional (via a C# default value) so qwen3:8b can omit it without a `Microsoft.Extensions.AI` binding failure, and lock it in with a schema-level regression test.

**Architecture:** All in-process chat tools are declared in `Features/Chat/DndChatService.cs` inside `SendAsync` via `AIFunctionFactory.Create(lambda, name, description)`. `AIFunctionFactory` marks a parameter *required* unless it has a C# default value — nullability is irrelevant. The fix adds `= null` defaults (and a trailing `CancellationToken toolCt = default`) to the optional params on 9 tools, reordering the 3 lambdas where an optional param currently precedes a required one (C# requires trailing defaults). Binding is by parameter *name*, so reordering is invisible to the model and every existing (named-argument) test keeps working.

**Tech Stack:** .NET 10, C#, `Microsoft.Extensions.AI` (`AIFunctionFactory`, `AIFunction`, `AIFunctionArguments`), xUnit + FluentAssertions + NSubstitute.

## Global Constraints

- Warnings-as-errors for every project (`Directory.Build.props`) — the build fails on any warning.
- `dotnet` commands require `dangerouslyDisableSandbox: true` in this environment (git-crypt smudge).
- Persistence tests need Docker; the chat-wiring tests in scope here are DB-free (NoOpDbFactory).
- No HTTP endpoints change → no `DndMcpAICsharpFun.http` / `.insomnia.json` update needed.
- Edits go through Serena symbolic/`replace_content` tools, not the built-in Read/Edit, for code files.
- Behavior-preserving: delegate bodies, service calls, tool names, and caller-supplied parameter names stay identical; only parameter *order* and *default values* change.

---

### Task 1: Fix the 6 no-reorder tools (defaults only)

The six tools whose optional params already trail the required ones — add `= null` defaults and `CancellationToken toolCt = default`. No reordering needed.

**Files:**
- Test: `DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs` (add one theory)
- Modify: `Features/Chat/DndChatService.cs` (six lambda signatures)

**Interfaces:**
- Consumes: `FakeChatClient` (captures `LastOptions`), `AuthenticatedAs(long)`, `CreateService(...)` — existing test helpers. `client.LastOptions!.Tools!.OfType<AIFunction>()` yields the registered tools; `AIFunction.JsonSchema` is a `System.Text.Json.JsonElement`.
- Produces: a reusable `OptionalParamIsNotRequired(string toolName, string paramName)` assertion pattern that Task 2 extends.

- [ ] **Step 1: Write the failing schema test**

Add to `DndChatServiceTests.cs` (inside the existing test class):

```csharp
public static IEnumerable<object[]> NoReorderOptionalParams() =>
[
    ["plan_level_up", "targetClass"],
    ["plan_level_up", "considerDip"],
    ["ask_setting_lore", "edition"],
    ["ask_rules", "ruleTopics"],
    ["ask_rules", "edition"],
    ["plan_downtime", "edition"],
    ["generate_npc", "maxCr"],
    ["recommend_build", "targetLevel"],
];

[Theory]
[MemberData(nameof(NoReorderOptionalParams))]
public async Task Optional_chat_tool_param_is_not_in_the_schema_required_set(
    string toolName, string paramName)
{
    // AIFunctionFactory marks a parameter required unless it has a C# default value; a nullable
    // type is NOT enough. These params are documented-optional, so the model must be able to omit
    // them — i.e. they must be absent from the tool schema's `required` array.
    var client = new FakeChatClient();
    var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(42));

    await svc.SendAsync("hello", false, CancellationToken.None);

    var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == toolName);
    var required = tool.JsonSchema.TryGetProperty("required", out var req)
        ? req.EnumerateArray().Select(e => e.GetString()).ToArray()
        : Array.Empty<string?>();
    required.Should().NotContain(paramName,
        $"{toolName}.{paramName} is optional and the model must be able to omit it");
}
```

- [ ] **Step 2: Run the test — expect FAIL**

Run: `dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj --filter "FullyQualifiedName~Optional_chat_tool_param_is_not_in_the_schema_required_set"` (with `dangerouslyDisableSandbox: true`)
Expected: FAIL — every case fails because the params are currently in `required`.

- [ ] **Step 3: Add defaults to the six lambdas in `DndChatService.cs`**

Change only the parameter lists (bodies unchanged):

- `plan_level_up`: `(long heroSnapshotId, string? targetClass = null, bool? considerDip = null, CancellationToken toolCt = default)`
- `ask_setting_lore`: `(long campaignId, string question, string? edition = null, CancellationToken toolCt = default)`
- `ask_rules`: `(string question, string[]? ruleTopics = null, string? edition = null, CancellationToken toolCt = default)`
- `plan_downtime`: `(string activity, string? edition = null, CancellationToken toolCt = default)`
- `generate_npc`: `(string concept, string archetype, double? maxCr = null, CancellationToken toolCt = default)`
- `recommend_build`: `(string className, string concept, int? targetLevel = null, CancellationToken toolCt = default)`

- [ ] **Step 4: Run the test — expect PASS**

Run: same filter as Step 2.
Expected: PASS (all 8 cases green).

- [ ] **Step 5: Full build + test**

Run: `dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj` (with `dangerouslyDisableSandbox: true`)
Expected: build clean (warnings-as-errors), all tests pass.

- [ ] **Step 6: Commit**

```bash
git add Features/Chat/DndChatService.cs DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs
git commit -m "fix(chat): default optional params on 6 no-reorder chat tools + schema regression test"
```

---

### Task 2: Fix the 3 reorder tools (reorder + defaults)

`build_encounter`, `prep_session`, `rate_encounter` each have an optional param that currently precedes a required one, so C# forces a reorder (required first, then defaulted optionals, then `CancellationToken toolCt = default`). Binding is by name; the existing routing tests (which pass named arguments) keep working.

**Files:**
- Test: `DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs` (extend the theory + one omit-key test)
- Modify: `Features/Chat/DndChatService.cs` (three lambda signatures)

**Interfaces:**
- Consumes: the `NoReorderOptionalParams`/schema-assertion pattern from Task 1, plus `ToArgs(object)` (builds `AIFunctionArguments` from an anonymous object; OMITTING a key = the model not supplying it) and `AuthenticatedAs`.
- Produces: nothing downstream (final code task).

- [ ] **Step 1: Add the failing reorder-tool schema cases + a build_encounter omit-key test**

Add a second member-data source and theory:

```csharp
public static IEnumerable<object[]> ReorderOptionalParams() =>
[
    ["build_encounter", "campaignId"],
    ["build_encounter", "theme"],
    ["build_encounter", "maxCr"],
    ["build_encounter", "minCr"],
    ["prep_session", "difficulty"],
    ["rate_encounter", "campaignId"],
    ["rate_encounter", "partyLevels"],
];

[Theory]
[MemberData(nameof(ReorderOptionalParams))]
public async Task Reordered_optional_chat_tool_param_is_not_in_the_schema_required_set(
    string toolName, string paramName)
{
    var client = new FakeChatClient();
    var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(42));

    await svc.SendAsync("hello", false, CancellationToken.None);

    var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == toolName);
    var required = tool.JsonSchema.TryGetProperty("required", out var req)
        ? req.EnumerateArray().Select(e => e.GetString()).ToArray()
        : Array.Empty<string?>();
    required.Should().NotContain(paramName);
}

[Fact]
public async Task BuildEncounterTool_binds_when_the_model_omits_all_optional_params()
{
    // Regression for the required-param binding bug: the model calls build_encounter with only the
    // required difficulty/edition and OMITS campaignId/theme/maxCr/minCr. Binding must succeed and
    // reach EncounterDesignService's party-resolution guard (proving the tool ran), rather than
    // throwing a MEAI "missing required parameter" binding error.
    var client = new FakeChatClient();
    var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(42));

    await svc.SendAsync("Build it", false, CancellationToken.None);
    var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == "build_encounter");

    var act = () => tool.InvokeAsync(
        ToArgs(new { difficulty = "Hard", edition = "2024" }), CancellationToken.None).AsTask();

    await act.Should().ThrowAsync<ArgumentException>()
        .WithMessage("*supply campaignId or partyLevels*");
}
```

- [ ] **Step 2: Run — expect FAIL**

Run: `dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj --filter "FullyQualifiedName~Reordered_optional_chat_tool_param_is_not_in_the_schema_required_set|FullyQualifiedName~BuildEncounterTool_binds_when_the_model_omits_all_optional_params"` (with `dangerouslyDisableSandbox: true`)
Expected: FAIL — schema cases fail (params required); the omit-key test fails because binding throws a missing-required-parameter error, not the `"supply campaignId or partyLevels"` guard.

- [ ] **Step 3: Reorder + default the three lambdas in `DndChatService.cs`**

Change ONLY the parameter lists; keep each delegate body (the service call and its named arguments) exactly as-is.

`build_encounter`:
```csharp
async (string difficulty, string edition, long? campaignId = null, string? theme = null,
    double? maxCr = null, double? minCr = null, CancellationToken toolCt = default) =>
```

`prep_session`:
```csharp
(long campaignId, string theme, string npcArchetype, string? difficulty = null,
    CancellationToken toolCt = default) =>
```

`rate_encounter`:
```csharp
(MonsterQuantity[] monsters, string edition, long? campaignId = null, int[]? partyLevels = null,
    CancellationToken toolCt = default) =>
```

- [ ] **Step 4: Run — expect PASS (incl. existing routing + SEC-08 tests)**

Run: `dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj` (with `dangerouslyDisableSandbox: true`)
Expected: build clean; all tests pass — the new theories, the new omit-key test, AND the pre-existing `BuildEncounterTool_forwards_difficulty_edition_and_theme_to_EncounterDesignService`, `RateEncounterTool_routes_partyLevels_and_monsters_to_EncounterDesignService`, `RateEncounterTool_expands_quantity_pairs_into_repeated_monsters`, and `Encounter_tool_schemas_do_not_expose_userId_as_a_caller_supplied_argument` (SEC-08) tests, which use named arguments and are unaffected by the reorder.

- [ ] **Step 5: Commit**

```bash
git add Features/Chat/DndChatService.cs DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs
git commit -m "fix(chat): reorder + default optional params on build_encounter/prep_session/rate_encounter"
```

---

### Task 3: Verify end-to-end + live smoke + report

**Files:**
- Modify: create `openspec/changes/chat-tool-binding-fixes/report.md`

**Interfaces:**
- Consumes: the running PCC stack (Postgres/Qdrant/Ollama) + the rebuilt app container.

- [ ] **Step 1: Full suite green**

Run: `dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj` (with `dangerouslyDisableSandbox: true`)
Expected: all tests pass (the prior baseline was 1344; this change adds tests only).

- [ ] **Step 2: Rebuild the app image**

Run: `docker compose up -d --build app` (with `dangerouslyDisableSandbox: true`)
Expected: image builds, `dndmcpaicsharpfun-app-1` reports `Up ... (healthy)`.

- [ ] **Step 3: Live build_encounter smoke (the former 0/5 tool)**

Drive the Blazor chat at `http://localhost:5101/` (login `test`/`test`) with a prompt that supplies NO theme/CR and belongs to a campaign, e.g. *"Build me a hard encounter for my party."* (pick or note a campaign so the party resolves), OR probe Ollama directly to confirm qwen3 emits a `build_encounter` call binding only difficulty/edition. Confirm the tool now selects + binds (a built encounter comes back, or the app returns the party-resolution guard — NOT a fabricated answer and NOT a silent tool failure). Capture the result.

- [ ] **Step 4: Write the report**

Create `openspec/changes/chat-tool-binding-fixes/report.md` recording: the 9 tools fixed, the schema regression test, the omit-key test, full-suite result, and the live smoke outcome. Note finding #2 (craft-magic) was confirmed a harness-check mismatch (no code change).

- [ ] **Step 5: Commit**

```bash
git add openspec/changes/chat-tool-binding-fixes/report.md openspec/changes/chat-tool-binding-fixes/tasks.md
git commit -m "docs(chat-tool-binding-fixes): verification report + live build_encounter smoke"
```

---

## Self-Review

- **Spec coverage:** The invariant spec's four scenarios map to tasks — "optional param absent from required" → Task 1/2 schema theories; "model omits param and tool still binds" → Task 2 omit-key test; "reorder doesn't change tool identity" → Task 2 (existing SEC-08 no-userId + routing tests kept green); "regression test guards all chat tools" → the combined `NoReorderOptionalParams` + `ReorderOptionalParams` theories (all 9 tools). ✓
- **Placeholder scan:** every code/test step shows complete code; exact filter commands given. Task 3 Step 3 leaves the exact smoke prompt to the operator because the campaign/party is environment-specific, but states the precise pass/fail condition. ✓
- **Type consistency:** signatures match `DndChatService.cs` verbatim (verified against lines 97–313); `ToArgs`, `AuthenticatedAs`, `CreateService`, `FakeChatClient.LastOptions`, `AIFunction.JsonSchema` all match existing test helpers; the guard message `"supply campaignId or partyLevels"` matches `EncounterDesignService.cs:102`. ✓
