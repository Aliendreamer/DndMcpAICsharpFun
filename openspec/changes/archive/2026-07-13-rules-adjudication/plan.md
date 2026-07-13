# Rules Adjudication Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A grounded, cited `ask_rules` chat tool that answers rules questions from the actual rules (scoped to the core rulebooks, excluding monster/lore prose), framing a ruling that names the rules combined and flags RAW-vs-DM-call.

**Architecture:** Mirrors setting-aware-lore's retrieve→cited-passages→persona-synthesizes pattern, **minus ownership**: `RulesAdjudicationService.AskAsync` scopes prose retrieval to a fixed `RuleSources` set via the shipped `RetrievalQuery.SourceBooks` OR filter, returns cited passages, and the `ask_rules` chat tool exposes it under a ruling contract. No new LLM call, no migration, no UI.

**Tech Stack:** .NET 10, C#, xUnit + FluentAssertions + NSubstitute, Testcontainers (Postgres + Qdrant), Qdrant.Client, Microsoft.Extensions.AI (`AIFunctionFactory` chat tools).

## Global Constraints

- Target `net10.0`; nullable; implicit usings; **warnings-as-errors** — no new warnings. No new package.
- `ask_rules` is a per-session chat tool: it is **not ownership-gated** and takes **no `userId`/`campaignId`** argument (rules are universal). No HTTP route, no `.http`/`.insomnia`, no shared-key MCP surface, no EF migration.
- **DATA INVARIANT:** `RuleSources`'s strings MUST equal the real `dnd_blocks.source_book` values (display names, e.g. `"PlayerHandbook 2014"` — verified live 2026-07-13). The scoping *logic* is unit/integration-tested; the exact strings are confirmed against the live corpus in Task 5.2. A self-seeded test passes with the expected value even if prod differs.
- `dotnet` fails under the command sandbox (git-crypted `Config/`) — run every `dotnet` with `dangerouslyDisableSandbox: true`, timeout ~300000ms (~400000ms for container integration tests). LSP shows stale/false CS errors on new-namespace/test files — trust `dotnet build`/`dotnet test`.
- Serena symbolic tools for all code reads/edits. Work directly on `main`; commit each reviewed task.
- Reuse `Features/Lore`'s `public sealed record CitedPassage(string Text, string SourceBook, string? Section, double Score)` (add `using DndMcpAICsharpFun.Features.Lore;`).

---

### Task 1: RuleSources + RulesRulingResult + RulesAdjudicationService + DI

**Files:**
- Create: `Features/Rules/RuleSources.cs`, `Features/Rules/RulesRulingResult.cs`, `Features/Rules/RulesAdjudicationService.cs`, `Features/Rules/RulesServiceCollectionExtensions.cs`
- Modify: `Extensions/ChatExtensions.cs` (`AddDndChat` calls `AddRules()`)
- Test: `DndMcpAICsharpFun.Tests/Rules/RulesAdjudicationServiceTests.cs`

**Interfaces:**
- Consumes: `IRagRetrievalService.SearchAsync(RetrievalQuery, ct)` → `IReadOnlyList<RetrievalResult>` where `RetrievalResult(string Text, ChunkMetadata Metadata, float Score)` and `ChunkMetadata` has `.SourceBook`, `.SectionTitle` (nullable), `.Chapter`; `RetrievalQuery` (trailing `SourceBooks` + `TopK` params); `Features.Lore.CitedPassage`.
- Produces:
  - `public static class RuleSources { public static readonly IReadOnlyCollection<string> Books = [...]; public const int TopK = 10; }`
  - `public sealed record RulesRulingResult(IReadOnlyList<CitedPassage> Passages, IReadOnlyCollection<string> ScopedBooks);`
  - `public sealed class RulesAdjudicationService(IRagRetrievalService rag)` with `Task<RulesRulingResult> AskAsync(string question, DndVersion? edition, CancellationToken ct)`.
  - `internal static IServiceCollection AddRules(this IServiceCollection)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.Rules;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Rules;

public sealed class RulesAdjudicationServiceTests
{
    private static IReadOnlyList<RetrievalResult> Results(params (string text, string book)[] rows) =>
        rows.Select(r => new RetrievalResult(
            r.text,
            new ChunkMetadata(SourceBook: r.book, Version: DndVersion.Edition2014, Category: default,
                Chapter: "Combat", SectionTitle: "Grappling", Page: 0, EntityName: null, BookType: default),
            0.9f)).ToList();
    // NOTE: match the REAL ChunkMetadata constructor (verify via Serena find_symbol on ChunkMetadata;
    // adjust arg names/order/values to whatever it actually declares — this is illustrative).

    [Fact]
    public async Task Scopes_retrieval_to_the_core_rulebooks_at_higher_topK()
    {
        var rag = Substitute.For<IRagRetrievalService>();
        rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
            .Returns(Results(("Grappling: ...", "PlayerHandbook 2014")));
        var svc = new RulesAdjudicationService(rag);

        await svc.AskAsync("grapple while prone", edition: null, CancellationToken.None);

        await rag.Received().SearchAsync(
            Arg.Is<RetrievalQuery>(q =>
                q.SourceBooks != null
                && q.SourceBooks.Contains("PlayerHandbook 2014")
                && q.SourceBooks.Contains("Dungeon Master's Guide 2014")
                && q.TopK == RuleSources.TopK),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Projects_results_to_cited_passages()
    {
        var rag = Substitute.For<IRagRetrievalService>();
        rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
            .Returns(Results(("Grappling rule text", "PlayerHandbook 2014")));
        var svc = new RulesAdjudicationService(rag);

        var result = await svc.AskAsync("grappling", edition: null, CancellationToken.None);

        result.Passages.Should().ContainSingle();
        result.Passages[0].SourceBook.Should().Be("PlayerHandbook 2014");
    }

    [Fact]
    public async Task Empty_retrieval_returns_explicit_empty()
    {
        var rag = Substitute.For<IRagRetrievalService>();
        rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<RetrievalResult>());
        var svc = new RulesAdjudicationService(rag);

        (await svc.AskAsync("nonsense", edition: null, CancellationToken.None)).Passages.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~RulesAdjudicationServiceTests"` (dangerouslyDisableSandbox: true)
Expected: FAIL — `RuleSources`/`RulesRulingResult`/`RulesAdjudicationService` don't exist. (First verify the real `RetrievalResult`/`ChunkMetadata` shapes via Serena `find_symbol` and fix the `Results` helper to match, so the failure is "types missing," not a helper mismatch.)

- [ ] **Step 3: Write the implementation**

```csharp
// Features/Rules/RuleSources.cs
namespace DndMcpAICsharpFun.Features.Rules;

/// <summary>
/// The core rulebooks rules-adjudication retrieval is scoped to. VALUES are the real
/// `dnd_blocks.source_book` display-name payloads (verified live 2026-07-13) — a mismatch scopes to
/// nothing. Scoping to these excludes monster/lore prose (e.g. Monster Manual) that a naive semantic
/// query surfaces for a rules question.
/// </summary>
public static class RuleSources
{
    public static readonly IReadOnlyCollection<string> Books =
        ["PlayerHandbook 2014", "Dungeon Master's Guide 2014"];

    /// <summary>Higher than the default so a multi-rule question (grapple + prone) can surface each rule.</summary>
    public const int TopK = 10;
}
```

```csharp
// Features/Rules/RulesRulingResult.cs
using DndMcpAICsharpFun.Features.Lore; // CitedPassage

namespace DndMcpAICsharpFun.Features.Rules;

public sealed record RulesRulingResult(
    IReadOnlyList<CitedPassage> Passages,
    IReadOnlyCollection<string> ScopedBooks);
```

```csharp
// Features/Rules/RulesAdjudicationService.cs
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Lore;      // CitedPassage
using DndMcpAICsharpFun.Features.Retrieval;

namespace DndMcpAICsharpFun.Features.Rules;

/// <summary>
/// Retrieves rule prose for a universal (non-campaign, non-user) rules question, scoped to the core
/// rulebooks so monster/lore prose is excluded, and returns cited passages for the chat persona to
/// frame into a ruling. Not ownership-gated. Does NOT call an LLM.
/// </summary>
public sealed class RulesAdjudicationService(IRagRetrievalService rag)
{
    public async Task<RulesRulingResult> AskAsync(string question, DndVersion? edition, CancellationToken ct)
    {
        var query = new RetrievalQuery(
            question, Version: edition, TopK: RuleSources.TopK, SourceBooks: RuleSources.Books);

        var results = await rag.SearchAsync(query, ct);

        var passages = results.Select(r => new CitedPassage(
            r.Text,
            r.Metadata.SourceBook,
            r.Metadata.SectionTitle ?? r.Metadata.Chapter,
            r.Score)).ToList();

        return new RulesRulingResult(passages, RuleSources.Books);
    }
}
```

```csharp
// Features/Rules/RulesServiceCollectionExtensions.cs
using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Features.Rules;

internal static class RulesServiceCollectionExtensions
{
    internal static IServiceCollection AddRules(this IServiceCollection services)
    {
        services.AddScoped<RulesAdjudicationService>();
        return services;
    }
}
```

Verify the `r.Metadata.SectionTitle ?? r.Metadata.Chapter` and `r.Score` projection against the real `RetrievalResult`/`ChunkMetadata` (Serena `find_symbol`) — it mirrors `SettingLoreService.cs`.

- [ ] **Step 4: Wire AddRules into AddDndChat**

In `Extensions/ChatExtensions.cs`, alongside `services.AddLore();` add `services.AddRules();` (add the `using DndMcpAICsharpFun.Features.Rules;`). This makes the chat container self-contained; `FullContainerScopeValidationTests.BuildServiceCollection` calls `AddDndChat`, so the service is transitively in the validated graph (same as `SettingLoreService` — confirm by running the scope-validation filter in Step 5).

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~RulesAdjudicationServiceTests|FullyQualifiedName~FullContainerScopeValidation"`
Expected: PASS (3 service tests + the scope-validation tests green with the new service in the graph).

- [ ] **Step 6: Commit**

```bash
git add Features/Rules/ Extensions/ChatExtensions.cs DndMcpAICsharpFun.Tests/Rules/RulesAdjudicationServiceTests.cs
git commit -m "feat(rules): RulesAdjudicationService scopes cited rule retrieval to the core rulebooks"
```

---

### Task 2: ask_rules chat tool + guard tests

**Files:**
- Modify: `Features/Chat/DndChatService.cs` (inject `RulesAdjudicationService`; register `ask_rules`)
- Modify: `DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs` (DI wiring + guard/routing tests)

**Interfaces:**
- Consumes: `RulesAdjudicationService.AskAsync` (Task 1); `ParseEdition` (existing private helper in `DndChatService`).
- Produces: `ask_rules(question, edition?)` chat tool (returns `RulesRulingResult`); no `userId`/`campaignId` arg.

- [ ] **Step 1: Write the failing guard/routing tests**

In `DndChatServiceTests`: add a `BuildRulesAdjudicationService(IRagRetrievalService? rag = null) => new(rag ?? Substitute.For<IRagRetrievalService>())` helper (no repos — it's ownership-free), thread a `RulesAdjudicationService?` param through `CreateService` and construct `DndChatService` with it. Then:
- Add `"ask_rules"` to the authenticated-present assertions (`SendAsync_adds_..._tools_when_authenticated`) and the unauthenticated-absent assertions.
- Add `"ask_rules"` to the `Encounter_tool_schemas_do_not_expose_userId...` name filter, AND assert it also exposes no `campaignId` (rules are universal):

```csharp
[Fact]
public async Task AskRulesTool_exposes_no_user_or_campaign_id_and_reaches_the_service()
{
    var rag = Substitute.For<IRagRetrievalService>();
    rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
        .Returns(new List<RetrievalResult>());
    var client = new FakeChatClient();
    var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(42),
        rulesAdjudicationService: BuildRulesAdjudicationService(rag));

    await svc.SendAsync("rules?", false, CancellationToken.None);
    var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == "ask_rules");

    tool.JsonSchema.TryGetProperty("properties", out var props);
    props.TryGetProperty("userId", out _).Should().BeFalse();
    props.TryGetProperty("campaignId", out _).Should().BeFalse();

    var result = await tool.InvokeAsync(
        ToArgs(new { question = "grapple while prone", edition = (string?)null }), CancellationToken.None);
    var ruling = ((JsonElement)result!).Deserialize<RulesRulingResult>(tool.JsonSerializerOptions);
    ruling!.Passages.Should().BeEmpty(); // reached the service (empty rag → empty passages)
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~DndChatServiceTests"`
Expected: FAIL — `ask_rules` not registered; `CreateService` has no rules param.

- [ ] **Step 3: Register the tool**

Inject `RulesAdjudicationService rulesAdjudicationService` into `DndChatService`'s primary constructor (mirror `settingLoreService`). In the authenticated block (near `ask_setting_lore`):

```csharp
            toolList.Add(AIFunctionFactory.Create(
                (string question, string? edition, CancellationToken toolCt) =>
                    rulesAdjudicationService.AskAsync(
                        question,
                        string.IsNullOrWhiteSpace(edition) ? (DndVersion?)null : ParseEdition(edition),
                        toolCt),
                name: "ask_rules",
                description: "Answer a D&D RULES question (including multi-rule interactions like " +
                    "'can I grapple a creature that's already prone?'). Returns cited rule passages " +
                    "retrieved ONLY from the core rulebooks. Compose your ruling STRICTLY from the " +
                    "returned passages: NAME each rule you combine and CITE it (source book + section); " +
                    "where the rules don't explicitly resolve an interaction, say so and distinguish " +
                    "rules-as-written from a DM ruling; if no passages are returned, say the rules don't " +
                    "directly cover it — never invent a rule. Not tied to any campaign or character. " +
                    "edition is optional (\"2014\"/\"2024\"); omit it to search all editions."));
```

Update `CreateService`/DI construction in the tests to pass the new service.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~DndChatServiceTests"`
Expected: PASS — present/absent, no-userId/no-campaignId schema check, routing-reaches-service, plus the unchanged existing tests.

- [ ] **Step 5: Commit**

```bash
git add Features/Chat/DndChatService.cs DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs
git commit -m "feat(chat): ask_rules grounded rules-adjudication tool (ownership-free, cited rulebook passages)"
```

---

### Task 3: Real-Qdrant non-vacuity scoping test

**Files:**
- Create: `DndMcpAICsharpFun.Tests/Rules/RulesScopingIntegrationTests.cs`

**Interfaces:**
- Consumes: `QdrantFixture` + a real `RagRetrievalService` over a seeded collection; `RulesAdjudicationService`.
- Produces: a test proving scoping returns rulebook passages and EXCLUDES an off-scope Monster Manual block.

- [ ] **Step 1: Study the setting-aware seeding pattern**

Read `DndMcpAICsharpFun.Tests/Lore/SettingLoreScopingIntegrationTests.cs` (Serena) — reuse its exact real-Qdrant block-seeding + real-`RagRetrievalService` construction (GUID-unique collection, identical embedding vectors so only the filter discriminates, cleanup).

- [ ] **Step 2: Write the non-vacuity test**

Seed a rule block (`source_book = "PlayerHandbook 2014"`, text a Grappling rule) AND an off-scope block (`source_book = "Monster Manual 2014"`), both with the SAME embedding vector; run `RulesAdjudicationService.AskAsync("grappling", null, ct)`; assert:

```csharp
result.Passages.Select(p => p.SourceBook).Should().Contain("PlayerHandbook 2014");
result.Passages.Select(p => p.SourceBook).Should().NotContain("Monster Manual 2014"); // scoping is real
```

Because both blocks share the embedding, only the `SourceBooks` OR filter can exclude the MM block — remove the scope and the test goes red (mutation-verify once).

- [ ] **Step 3: Run the test (Docker + Qdrant required)**

Run: `dotnet test --filter "FullyQualifiedName~RulesScopingIntegrationTests"` (dangerouslyDisableSandbox: true, ~400000ms)
Expected: PASS — rulebook block included, MM block excluded.

- [ ] **Step 4: Commit**

```bash
git add DndMcpAICsharpFun.Tests/Rules/RulesScopingIntegrationTests.cs
git commit -m "test(rules): real-Qdrant non-vacuity — rulebook-scoped retrieval excludes off-scope blocks"
```

---

### Task 4: Verification + DATA-INVARIANT check + live smoke

**Files:** none (verification)

- [ ] **Step 1: Build clean + full suite**

Run: `dotnet build` (0/0) then `dotnet test` (Docker up). Record the new total (was 1280/1280).

- [ ] **Step 2: DATA-INVARIANT check (live)**

With the running app: confirm the actual rule blocks carry `source_book ∈ RuleSources`:
`GET /retrieval/search?q=grappling` / `?q=prone%20condition` / `?q=cover` → inspect `metadata.sourceBook`. Confirm the Grappling/Prone/Cover rule blocks are `"PlayerHandbook 2014"` (or `"Dungeon Master's Guide 2014"`). If any key rule lives under a different `source_book` string, add it to `RuleSources.Books` and re-run Task 1's tests.

- [ ] **Step 3: Rebuild the app container + live smoke (Ollama)**

`docker compose build app && docker compose up -d app`; wait healthy. Sign in as `test`, open chat, ask "can I grapple a creature that's already prone?" → the answer is a grounded ruling that NAMES Grappling and Prone, CITES the rulebook(s), and flags any RAW-vs-DM interaction. Ask a nonsense rules question → honest "the rules don't directly cover this." No UI change → no overflow gate.

- [ ] **Step 4: Final whole-branch review + roadmap refresh**

Request a final opus whole-branch review of the branch diff. On READY, refresh the `companion_roadmap` Serena memory with the shipped rules-adjudication slice.
