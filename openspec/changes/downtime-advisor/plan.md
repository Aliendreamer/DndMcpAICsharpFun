# Downtime Advisor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** `plan_downtime(activity, edition?)` — a grounded, cited downtime/crafting advisor that scopes retrieval to the XGE + DMG downtime rules and returns cited passages the persona composes into a plan (time cost, gold cost, outcome). Times/costs come from the rules, never invented.

**Architecture:** Mirrors the shipped `rules-adjudication` verbatim, minus the rulebook scope: `DowntimeService.PlanAsync` scopes prose retrieval to a fixed `DowntimeSources` set via `RetrievalQuery.SourceBooks` (the shipped OR filter), returns cited passages, and the `plan_downtime` chat tool exposes it under a plan contract. Ownership-free, no LLM call, no migration/UI.

**Tech Stack:** .NET 10, C#, xUnit + FluentAssertions + NSubstitute, Testcontainers (Qdrant), Microsoft.Extensions.AI chat tools.

## Global Constraints

- net10.0; nullable; **warnings-as-errors** — no new warnings; no new package.
- `plan_downtime` is ownership-free (no `userId`/`campaignId`). No HTTP route, no `.http`/`.insomnia`, no migration, no shared-key MCP surface.
- **DATA INVARIANT:** `DowntimeSources.Books` strings MUST equal the real `dnd_blocks.source_book` values (display names). XGE's is expected to be `"Xanathar's Guide to Everything"` and DMG's is the verified `"Dungeon Master's Guide 2014"` — confirmed live in Task 4.2 (XGE ingest completes async). A self-seeded test passes with the expected value even if prod differs.
- **Re-run the FULL suite at verification (dev-flow) — not a feature-filtered subset.**
- `dotnet` fails under the command sandbox (git-crypted `Config/`) — run every `dotnet` with `dangerouslyDisableSandbox: true`, timeout ~300000ms (~400000ms for container tests). LSP shows stale/false CS errors on new-namespace/changed test files — trust `dotnet build`/`dotnet test`.
- Serena symbolic tools for all code reads/edits (prefer literal/symbol edits — regex-mode can corrupt literal `\n`). Work on `main`; commit each reviewed task.
- Reuse `Features/Lore`'s `public sealed record CitedPassage(string Text, string SourceBook, string? Section, double Score)` (add `using DndMcpAICsharpFun.Features.Lore;`).

---

### Task 1: DowntimeSources + DowntimePlanResult + DowntimeService + DI

**Files:**
- Create: `Features/Downtime/DowntimeSources.cs`, `Features/Downtime/DowntimePlanResult.cs`, `Features/Downtime/DowntimeService.cs`, `Features/Downtime/DowntimeServiceCollectionExtensions.cs`
- Modify: `Extensions/ChatExtensions.cs` (`AddDndChat` calls `AddDowntime()`)
- Test: `DndMcpAICsharpFun.Tests/Downtime/DowntimeServiceTests.cs`

**Interfaces:** (mirror `Features/Rules/RulesAdjudicationService.cs` + `RuleSources` + `RulesRulingResult` — read them first)
- Consumes: `IRagRetrievalService.SearchAsync(RetrievalQuery, ct) → IList<RetrievalResult>` (`RetrievalResult(Text, ChunkMetadata Metadata, float Score)`, `Metadata.SourceBook`/`SectionTitle`/`Chapter`); `RetrievalQuery` (trailing `TopK`, `SourceBooks`); `Features.Lore.CitedPassage`.
- Produces:
  - `public static class DowntimeSources { public static readonly IReadOnlyCollection<string> Books = ["Xanathar's Guide to Everything", "Dungeon Master's Guide 2014"]; public const int TopK = 10; }`
  - `public sealed record DowntimePlanResult(IReadOnlyList<CitedPassage> Passages, IReadOnlyCollection<string> ScopedBooks);`
  - `public sealed class DowntimeService(IRagRetrievalService rag)` with `Task<DowntimePlanResult> PlanAsync(string activity, DndVersion? edition, CancellationToken ct)`.
  - `internal static IServiceCollection AddDowntime(this IServiceCollection)`.

- [ ] **Step 1: Write the failing tests** (mirror `DndMcpAICsharpFun.Tests/Rules/RulesAdjudicationServiceTests.cs`'s `Results` helper / shapes exactly)

```csharp
[Fact]
public async Task Scopes_retrieval_to_the_downtime_books_at_higher_topK()
{
    var rag = Substitute.For<IRagRetrievalService>();
    rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
        .Returns(Results(("Crafting: ...", "Xanathar's Guide to Everything")));
    var svc = new DowntimeService(rag);

    await svc.PlanAsync("craft plate armor", edition: null, CancellationToken.None);

    await rag.Received().SearchAsync(
        Arg.Is<RetrievalQuery>(q => q.SourceBooks != null
            && q.SourceBooks.Contains("Xanathar's Guide to Everything")
            && q.SourceBooks.Contains("Dungeon Master's Guide 2014")
            && q.TopK == DowntimeSources.TopK),
        Arg.Any<CancellationToken>());
}

[Fact]
public async Task Projects_results_to_cited_passages()
{
    var rag = Substitute.For<IRagRetrievalService>();
    rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
        .Returns(Results(("Crafting rule text", "Xanathar's Guide to Everything")));
    var svc = new DowntimeService(rag);

    var result = await svc.PlanAsync("crafting", null, CancellationToken.None);

    result.Passages.Should().ContainSingle();
    result.Passages[0].SourceBook.Should().Be("Xanathar's Guide to Everything");
}

[Fact]
public async Task Empty_retrieval_returns_explicit_empty()
{
    var rag = Substitute.For<IRagRetrievalService>();
    rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>()).Returns(new List<RetrievalResult>());
    (await new DowntimeService(rag).PlanAsync("nonsense", null, CancellationToken.None)).Passages.Should().BeEmpty();
}
```

(Copy the `Results` NSubstitute helper + `RetrievalResult`/`ChunkMetadata` construction verbatim from `RulesAdjudicationServiceTests`.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~DowntimeServiceTests"`
Expected: FAIL — `DowntimeSources`/`DowntimePlanResult`/`DowntimeService` don't exist.

- [ ] **Step 3: Write the source files** (mirror `Features/Rules/*` exactly, swapping names + scope)

```csharp
// Features/Downtime/DowntimeSources.cs
namespace DndMcpAICsharpFun.Features.Downtime;

/// <summary>
/// The source books downtime/crafting retrieval is scoped to. VALUES are the real
/// `dnd_blocks.source_book` display-name payloads — a mismatch scopes to nothing. XGE holds the
/// detailed downtime/crafting rules; the DMG holds the basics. Scoping to these excludes unrelated
/// prose a naive semantic query surfaces for a downtime question. (XGE value confirmed live at Task 4.2.)
/// </summary>
public static class DowntimeSources
{
    public static readonly IReadOnlyCollection<string> Books =
        ["Xanathar's Guide to Everything", "Dungeon Master's Guide 2014"];

    /// <summary>Higher than the default so an activity spanning cost + time surfaces each passage.</summary>
    public const int TopK = 10;
}
```

```csharp
// Features/Downtime/DowntimePlanResult.cs
using DndMcpAICsharpFun.Features.Lore; // CitedPassage
namespace DndMcpAICsharpFun.Features.Downtime;

public sealed record DowntimePlanResult(
    IReadOnlyList<CitedPassage> Passages, IReadOnlyCollection<string> ScopedBooks);
```

```csharp
// Features/Downtime/DowntimeService.cs
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Lore;      // CitedPassage
using DndMcpAICsharpFun.Features.Retrieval;
namespace DndMcpAICsharpFun.Features.Downtime;

/// <summary>
/// Retrieves downtime/crafting rule prose for a universal (non-campaign, non-user) activity, scoped to
/// the downtime source books so unrelated prose is excluded, and returns cited passages for the chat
/// persona to compose into a plan. Not ownership-gated. Does NOT call an LLM.
/// </summary>
public sealed class DowntimeService(IRagRetrievalService rag)
{
    public async Task<DowntimePlanResult> PlanAsync(string activity, DndVersion? edition, CancellationToken ct)
    {
        var query = new RetrievalQuery(
            activity, Version: edition, TopK: DowntimeSources.TopK, SourceBooks: DowntimeSources.Books);
        var results = await rag.SearchAsync(query, ct);
        var passages = results.Select(r => new CitedPassage(
            r.Text, r.Metadata.SourceBook, r.Metadata.SectionTitle ?? r.Metadata.Chapter, r.Score)).ToList();
        return new DowntimePlanResult(passages, DowntimeSources.Books);
    }
}
```

```csharp
// Features/Downtime/DowntimeServiceCollectionExtensions.cs
using Microsoft.Extensions.DependencyInjection;
namespace DndMcpAICsharpFun.Features.Downtime;

internal static class DowntimeServiceCollectionExtensions
{
    internal static IServiceCollection AddDowntime(this IServiceCollection services)
    {
        services.AddScoped<DowntimeService>();
        return services;
    }
}
```

(Verify the projection matches the real `RetrievalResult`/`ChunkMetadata` via Serena — identical to `RulesAdjudicationService`.)

- [ ] **Step 4: Wire AddDowntime into AddDndChat**

In `Extensions/ChatExtensions.cs`, alongside `services.AddRules();` add `services.AddDowntime();` (+ `using DndMcpAICsharpFun.Features.Downtime;`).

- [ ] **Step 5: Run tests + scope validation**

Run: `dotnet test --filter "FullyQualifiedName~DowntimeServiceTests|FullyQualifiedName~FullContainerScopeValidation"`
Expected: PASS (3 service tests + scope-validation green).

- [ ] **Step 6: Commit**

```bash
git add Features/Downtime/ Extensions/ChatExtensions.cs DndMcpAICsharpFun.Tests/Downtime/DowntimeServiceTests.cs
git commit -m "feat(downtime): DowntimeService scopes cited downtime retrieval to XGE + DMG"
```

---

### Task 2: plan_downtime chat tool + guard tests

**Files:**
- Modify: `Features/Chat/DndChatService.cs` (inject `DowntimeService`; register `plan_downtime`)
- Modify: `DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs` (DI wiring + guard/routing)

**Interfaces:**
- Consumes: `DowntimeService.PlanAsync`; the session-independent closure (no userId); `ParseEdition`.
- Produces: `plan_downtime(activity, edition?)` tool; no `userId`/`campaignId`.

- [ ] **Step 1: Write the failing guard/routing test** (mirror the shipped `AskRulesTool_exposes_no_user_or_campaign_id_and_reaches_the_service` test)

Add `BuildDowntimeService(IRagRetrievalService? rag = null) => new(rag ?? Substitute.For<IRagRetrievalService>())`, thread a `DowntimeService?` param through `CreateService` into the `DndChatService` ctor. Add `"plan_downtime"` to the authenticated-present + unauthenticated-absent lists and the `...do_not_expose_userId...` name filter. Add:

```csharp
[Fact]
public async Task PlanDowntimeTool_exposes_no_user_or_campaign_id_and_reaches_the_service()
{
    var rag = Substitute.For<IRagRetrievalService>();
    rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>()).Returns(new List<RetrievalResult>());
    var client = new FakeChatClient();
    var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(11), downtimeService: BuildDowntimeService(rag));

    await svc.SendAsync("downtime", false, CancellationToken.None);
    var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == "plan_downtime");

    tool.JsonSchema.TryGetProperty("properties", out var props);
    props.TryGetProperty("userId", out _).Should().BeFalse();
    props.TryGetProperty("campaignId", out _).Should().BeFalse();

    var result = await tool.InvokeAsync(ToArgs(new { activity = "craft plate armor", edition = (string?)null }), CancellationToken.None);
    var plan = ((JsonElement)result!).Deserialize<DowntimePlanResult>(tool.JsonSerializerOptions);
    plan!.Passages.Should().BeEmpty(); // reached the service (empty rag → empty passages)
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~DndChatServiceTests"`
Expected: FAIL — `plan_downtime` not registered; `CreateService` has no downtime param.

- [ ] **Step 3: Register the tool** (mirror the shipped `ask_rules` registration)

Inject `DowntimeService downtimeService` into `DndChatService`'s ctor. In the authenticated block:

```csharp
            toolList.Add(AIFunctionFactory.Create(
                (string activity, string? edition, CancellationToken toolCt) =>
                    downtimeService.PlanAsync(
                        activity, string.IsNullOrWhiteSpace(edition) ? (DndVersion?)null : ParseEdition(edition), toolCt),
                name: "plan_downtime",
                description: "Plan a D&D DOWNTIME activity (crafting an item, training, carousing, running a " +
                    "business, scribing a spell scroll, research, etc.). Pass the activity as free text (e.g. " +
                    "'craft plate armor', 'run a tavern for a month'). Returns cited rule passages retrieved ONLY " +
                    "from the downtime rulebooks (Xanathar's + DMG). Compose a downtime plan STRICTLY from the " +
                    "returned passages — the activity's TIME cost, GOLD cost, and outcome — and CITE each (source " +
                    "book + section); if no passages are returned, say the rules don't detail this activity — never " +
                    "invent times or costs. Not tied to any campaign or character. edition is optional (\"2014\"/\"2024\")."));
```

Update `CreateService`/DI in the tests to pass the new service.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~DndChatServiceTests"`
Expected: PASS — present/absent, no-userId/no-campaignId schema, routing reaches the service.

- [ ] **Step 5: Commit**

```bash
git add Features/Chat/DndChatService.cs DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs
git commit -m "feat(chat): plan_downtime grounded downtime advisor (ownership-free, cited XGE/DMG passages)"
```

---

### Task 3: Real-Qdrant non-vacuity scoping test

**Files:**
- Create: `DndMcpAICsharpFun.Tests/Downtime/DowntimeScopingIntegrationTests.cs`

- [ ] **Step 1: Study + mirror the shipped `RulesScopingIntegrationTests`**

Read `DndMcpAICsharpFun.Tests/Rules/RulesScopingIntegrationTests.cs` (Serena) — reuse its exact real-Qdrant seeding (GUID-unique collection, identical embedding vectors so only the filter discriminates, cleanup).

- [ ] **Step 2: Write the non-vacuity test**

Seed an XGE downtime block (`source_book="Xanathar's Guide to Everything"`) AND an off-scope block (`source_book="Monster Manual 2014"`), same embedding; `PlanAsync("crafting", null, ct)`; assert:

```csharp
result.Passages.Select(p => p.SourceBook).Should().Contain("Xanathar's Guide to Everything");
result.Passages.Select(p => p.SourceBook).Should().NotContain("Monster Manual 2014"); // scoping is real
```

Mutation-verify once (drop the scope → MM leaks → red), revert.

- [ ] **Step 3: Run it (Docker + Qdrant)**

Run: `dotnet test --filter "FullyQualifiedName~DowntimeScopingIntegrationTests"` (~400000ms)
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add DndMcpAICsharpFun.Tests/Downtime/DowntimeScopingIntegrationTests.cs
git commit -m "test(downtime): real-Qdrant non-vacuity — downtime-scoped retrieval excludes off-scope blocks"
```

---

### Task 4: Verification + DATA-INVARIANT + live smoke (after XGE ingest completes)

**Files:** none (verification)

- [ ] **Step 1: Build + FULL suite**

Run: `dotnet build` (0/0) then `dotnet test` (the FULL suite — do not filter). Record the total (was 1299/1299).

- [ ] **Step 2: DATA-INVARIANT check (live, after XGE blocks land)**

With the running app: `GET /retrieval/search?q=crafting armor` / `?q=downtime activities` → inspect `metadata.sourceBook`. Confirm the XGE downtime blocks are `"Xanathar's Guide to Everything"` (and a filter-probe `?sourceBook=Xanathar's Guide to Everything` returns >0). If XGE's `source_book` differs, fix `DowntimeSources.Books` to the real value and re-run Task 1's tests.

- [ ] **Step 3: Rebuild container + live smoke (Ollama)**

`docker compose build app && docker compose up -d app`; wait healthy. Sign in as `test`, ask "my ranger wants to craft plate armor — how long and how much?" → a grounded crafting plan (time + gold cost) cited to XGE; a nonsense activity → honest "the rules don't detail this." No UI → no overflow gate.

- [ ] **Step 4: Final whole-branch review + roadmap refresh**

Request a final opus whole-branch review. On READY, refresh the `companion_roadmap` Serena memory with the shipped downtime-advisor slice.
