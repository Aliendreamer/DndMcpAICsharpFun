# Setting-aware Lore Synthesis Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A campaign declares a setting (Eberron, …); a grounded, cited per-campaign chat tool answers lore questions using only that setting's source books (plus core rules), never free-loring.

**Architecture:** An in-code `SettingCatalog` maps a setting → a set of source-book keys (its book ∪ core rulebooks). `Campaign` gains a nullable `Setting`. The prose RAG path gains a multi-source-book OR filter. `SettingLoreService` (ownership-gated) resolves the campaign's setting → book set → scoped prose retrieval → cited passages; the `ask_setting_lore` chat tool exposes it and the persona synthesizes under a grounding contract. No new LLM call.

**Tech Stack:** .NET 10, C#, EF Core (PostgreSQL/Npgsql), Qdrant.Client, xUnit + FluentAssertions + NSubstitute, Testcontainers (Postgres + Qdrant), Blazor Server, Microsoft.Extensions.AI (`AIFunctionFactory` chat tools).

## Global Constraints

- Target `net10.0`; nullable enabled; implicit usings; **warnings-as-errors** (every project) — no new warnings.
- Central Package Management: never add a version to a `PackageReference`; this slice adds **no** new package.
- `ask_setting_lore` is a per-user **chat** tool (SEC-08): `userId` comes from the session-claim closure, never a tool argument. No HTTP route, no `.http`/`.insomnia` change (campaign CRUD is Blazor server-side; the tool is chat-only). The `Campaign.Setting` column is the only schema change (additive EF migration, applied at startup by `MigrateDatabaseAsync`).
- **CRITICAL DATA INVARIANT:** the catalog's source-book key strings MUST equal the `source_book` payload values stored in `dnd_blocks` (Qdrant keyword match is exact). The mapping *logic* is unit-tested; the actual key *strings* are validated against the real corpus at Task 6 (integration) and Task 7 (Phase-1 ingest + live smoke). If the corpus stores e.g. `"phb14"` not `"PHB"`, the catalog keys must be those values.
- `dotnet` commands fail under the command sandbox (git-crypted `Config/`) — run every `dotnet` command with `dangerouslyDisableSandbox: true`, generous timeout (~300000ms; ~400000ms for container integration tests). The LSP shows stale/false CS errors on test files after signature changes — trust `dotnet build`/`dotnet test`.
- Serena symbolic tools for all code reads/edits. Work directly on `main`; commit each reviewed task.

---

### Task 1: SettingCatalog (setting → source-book set)

**Files:**
- Create: `Features/Lore/SettingCatalog.cs`
- Test: `DndMcpAICsharpFun.Tests/Lore/SettingCatalogTests.cs`

**Interfaces:**
- Produces:
  - `public static IReadOnlyList<string> SettingCatalog.KnownSettings { get; }` — catalog keys (e.g. `["Eberron"]`).
  - `public static IReadOnlySet<string> SettingCatalog.Resolve(string? setting)` — the source-book set (setting's book(s) ∪ core rulebooks); empty set for null/blank/unknown/generic (= unscoped).

- [ ] **Step 1: Write the failing test**

```csharp
using DndMcpAICsharpFun.Features.Lore;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Lore;

public sealed class SettingCatalogTests
{
    [Fact]
    public void Eberron_resolves_to_its_book_plus_core()
    {
        var books = SettingCatalog.Resolve("Eberron");

        books.Should().Contain("ERLW");
        books.Should().Contain(new[] { "PHB", "DMG", "MM" }); // core always included
    }

    [Fact]
    public void Unknown_or_generic_or_null_resolves_to_empty_unscoped()
    {
        SettingCatalog.Resolve(null).Should().BeEmpty();
        SettingCatalog.Resolve("").Should().BeEmpty();
        SettingCatalog.Resolve("Generic").Should().BeEmpty();
        SettingCatalog.Resolve("NotARealSetting").Should().BeEmpty();
    }

    [Fact]
    public void Resolve_is_case_insensitive_on_the_setting_key()
    {
        SettingCatalog.Resolve("eberron").Should().Contain("ERLW");
    }

    [Fact]
    public void KnownSettings_lists_the_catalog_keys()
    {
        SettingCatalog.KnownSettings.Should().Contain("Eberron");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~SettingCatalogTests"` (dangerouslyDisableSandbox: true)
Expected: FAIL — `SettingCatalog` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace DndMcpAICsharpFun.Features.Lore;

/// <summary>
/// Maps a named campaign setting to the set of source books its lore lives in, always unioned with
/// the core rulebooks (generic rules apply in every world). The KEYS are the exact `source_book`
/// payload values used in `dnd_blocks` — a mismatch silently scopes to nothing. A null/blank/unknown
/// setting resolves to the EMPTY set, meaning "no source-book restriction" (unscoped, today's behavior).
/// In-code registry (like FivetoolsSourceRegistry); grows as setting books are ingested.
/// </summary>
public static class SettingCatalog
{
    // NOTE: verify these strings equal the real dnd_blocks `source_book` values (see plan Task 6/7).
    private static readonly string[] Core = ["PHB", "DMG", "MM"];

    private static readonly IReadOnlyDictionary<string, string[]> SettingBooks =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Eberron"] = ["ERLW"],
        };

    public static IReadOnlyList<string> KnownSettings => SettingBooks.Keys.ToList();

    public static IReadOnlySet<string> Resolve(string? setting)
    {
        if (string.IsNullOrWhiteSpace(setting) || !SettingBooks.TryGetValue(setting, out var books))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase); // unscoped
        }

        return new HashSet<string>(books.Concat(Core), StringComparer.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~SettingCatalogTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add Features/Lore/SettingCatalog.cs DndMcpAICsharpFun.Tests/Lore/SettingCatalogTests.cs
git commit -m "feat(lore): SettingCatalog maps a setting to its source-book set plus core"
```

---

### Task 2: Multi-source-book OR filter on the prose RAG path

**Files:**
- Modify: `Features/Retrieval/RetrievalQuery.cs` (add `SourceBooks`)
- Modify: `Features/Retrieval/RagRetrievalService.cs:99-125` (`BuildFilter` — add OR condition)
- Test: `DndMcpAICsharpFun.Tests/Retrieval/RagRetrievalServiceSourceBooksFilterTests.cs`

**Interfaces:**
- Consumes: `RetrievalQuery`, Qdrant `Filter`/`Condition` (existing).
- Produces: `RetrievalQuery.SourceBooks` (`IReadOnlyCollection<string>? = null`); when non-empty, `RagRetrievalService` emits a Qdrant `should`/OR over those books as one `Must` condition. Single-element behaves like the single `SourceBook`; empty/null → no restriction.

- [ ] **Step 1: Write the failing test**

Follow the existing `RagRetrievalServiceTests` pattern (fake `IQdrantSearchClient` capturing the `Filter`). Study `DndMcpAICsharpFun.Tests/Retrieval/RagRetrievalServiceTests.cs` for how the fake captures the filter, then:

```csharp
// In RagRetrievalServiceSourceBooksFilterTests — assert the built filter contains an OR (should) over the set.
[Fact]
public async Task SourceBooks_set_builds_an_or_condition_over_the_books()
{
    // Arrange a service with a fake qdrant client that captures the Filter passed to SearchAsync
    // (mirror RagRetrievalServiceTests' capture setup).
    var captured = /* capture Filter */;
    await service.SearchAsync(new RetrievalQuery("who rules Sharn", SourceBooks: new[] { "ERLW", "PHB" }), CancellationToken.None);

    // The set produces ONE Must condition that is a nested Filter whose Should has both books.
    var orCondition = captured!.Must.Single(c => c.Filter is not null);
    var shouldKeywords = orCondition.Filter.Should
        .Select(s => s.Field.Match.Keyword).ToList();
    shouldKeywords.Should().BeEquivalentTo(new[] { "ERLW", "PHB" });
}

[Fact]
public async Task Empty_source_books_adds_no_source_book_restriction()
{
    var captured = /* capture Filter */;
    await service.SearchAsync(new RetrievalQuery("q", SourceBooks: Array.Empty<string>()), CancellationToken.None);
    // No nested-Filter (OR) condition added for source books.
    (captured?.Must.Any(c => c.Filter is not null) ?? false).Should().BeFalse();
}
```

(Match the real capture mechanism used by the sibling `RagRetrievalServiceTests`; if it uses NSubstitute `Received`/`Arg.Do`, reuse that.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~RagRetrievalServiceSourceBooksFilterTests"`
Expected: FAIL — `RetrievalQuery` has no `SourceBooks`; no OR condition built.

- [ ] **Step 3: Add `SourceBooks` to RetrievalQuery**

Edit `Features/Retrieval/RetrievalQuery.cs` — add the parameter (keep the existing single `SourceBook` for endpoint back-compat):

```csharp
public sealed record RetrievalQuery(
    string QueryText,
    DndVersion? Version = null,
    ContentCategory? Category = null,
    string? SourceBook = null,
    string? EntityName = null,
    BookType? BookType = null,
    int TopK = 5,
    IReadOnlyCollection<string>? SourceBooks = null);
```

- [ ] **Step 4: Emit the OR condition in BuildFilter**

In `RagRetrievalService.BuildFilter` (after the single `SourceBook` block, before `EntityName`), add:

```csharp
        if (query.SourceBooks is { Count: > 0 })
        {
            // A setting maps to several books — match ANY of them (OR) as a single Must condition,
            // so the set composes with the other filters via AND.
            var anyBook = new Filter();
            foreach (var book in query.SourceBooks)
            {
                anyBook.Should.Add(KeywordCondition(QdrantPayloadFields.SourceBook, book));
            }

            conditions.Add(new Condition { Filter = anyBook });
        }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~RagRetrievalServiceSourceBooksFilterTests"` — plus the existing `RagRetrievalServiceTests` (unchanged, still green).
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Features/Retrieval/RetrievalQuery.cs Features/Retrieval/RagRetrievalService.cs DndMcpAICsharpFun.Tests/Retrieval/RagRetrievalServiceSourceBooksFilterTests.cs
git commit -m "feat(retrieval): prose RAG accepts a source-book set (OR filter) for setting scoping"
```

---

### Task 3: Campaign.Setting field + repository + EF migration

**Files:**
- Modify: `Domain/Campaign.cs` (add `Setting`)
- Modify: `Features/Campaigns/CampaignRepository.cs` (`CreateAsync` gains `setting`; add `SetSettingAsync`)
- Modify: any `new Campaign(...)` call sites (the record ctor gains a param)
- Create: an EF migration (`AddCampaignSetting`)
- Test: `DndMcpAICsharpFun.Tests/Persistence/CampaignSettingTests.cs` (real Postgres)

**Interfaces:**
- Produces:
  - `Campaign` record gains `string? Setting = null` (last positional param, defaulted → existing `new Campaign(...)` calls still compile).
  - `CampaignRepository.CreateAsync(long userId, string name, string description, string? setting = null)`.
  - `CampaignRepository.SetSettingAsync(long id, long userId, string? setting)` — ownership-scoped update (0 rows on foreign/missing).

- [ ] **Step 1: Write the failing test (real Postgres)**

```csharp
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Tests.Persistence;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Persistence;

[Collection("postgres")]
public sealed class CampaignSettingTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly CampaignRepository _repo = new(new TestDb(pg));
    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_with_setting_round_trips()
    {
        var id = await _repo.CreateAsync(1, "Eberron Game", "desc", setting: "Eberron");
        (await _repo.GetByIdAsync(id, 1))!.Setting.Should().Be("Eberron");
    }

    [Fact]
    public async Task Create_without_setting_defaults_null()
    {
        var id = await _repo.CreateAsync(1, "Generic", "desc");
        (await _repo.GetByIdAsync(id, 1))!.Setting.Should().BeNull();
    }

    [Fact]
    public async Task SetSettingAsync_updates_owned_campaign_and_ignores_foreign()
    {
        var id = await _repo.CreateAsync(1, "C", "d");
        await _repo.SetSettingAsync(id, 1, "Eberron");
        (await _repo.GetByIdAsync(id, 1))!.Setting.Should().Be("Eberron");

        await _repo.SetSettingAsync(id, userId: 2, "Ravenloft"); // foreign — no-op
        (await _repo.GetByIdAsync(id, 1))!.Setting.Should().Be("Eberron");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~CampaignSettingTests"` (Docker required)
Expected: FAIL — `Campaign` has no `Setting`; `CreateAsync`/`SetSettingAsync` signatures don't match.

- [ ] **Step 3: Add `Setting` to the Campaign record**

Edit `Domain/Campaign.cs`:

```csharp
public sealed record Campaign(long Id, long UserId, string Name, string Description, DateTime CreatedAt, string? Setting = null);
```

- [ ] **Step 4: Thread through CampaignRepository**

In `CampaignRepository.CreateAsync`, add the param and pass it:

```csharp
    public async Task<long> CreateAsync(long userId, string name, string description, string? setting = null)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var campaign = new Campaign(0, userId, name, description, DateTime.UtcNow, setting);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();
        return campaign.Id;
    }

    public async Task SetSettingAsync(long id, long userId, string? setting)
    {
        await using var db = await dbf.CreateDbContextAsync();
        await db.Campaigns.Where(c => c.Id == id && c.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Setting, setting));
    }
```

- [ ] **Step 5: Generate the EF migration**

Run (dangerouslyDisableSandbox: true), from repo root:
`dotnet ef migrations add AddCampaignSetting --project DndMcpAICsharpFun.csproj`
Expected: a new migration under the Migrations folder adding a nullable `Setting` text column to `Campaigns`. Inspect it — it must be a single `AddColumn<string>(nullable: true)`, no data loss. (If the project scaffolds migrations differently, mirror the most recent existing migration's location/namespace.)

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~CampaignSettingTests"`
Expected: PASS (3 tests). The migration is applied by `PostgresFixture`/`MigrateDatabaseAsync` on the test container.

- [ ] **Step 7: Run the full persistence suite (guard against migration breakage)**

Run: `dotnet test --filter "FullyQualifiedName~DndMcpAICsharpFun.Tests.Persistence"`
Expected: PASS — the additive migration doesn't disturb existing campaign/hero tests.

- [ ] **Step 8: Commit**

```bash
git add Domain/Campaign.cs Features/Campaigns/CampaignRepository.cs Migrations/ DndMcpAICsharpFun.Tests/Persistence/CampaignSettingTests.cs
git commit -m "feat(campaigns): persist a nullable Setting on Campaign (additive migration)"
```

---

### Task 4: Campaign setting selector (create form + detail page)

**Files:**
- Modify: `CompanionUI/Pages/Campaigns/Campaigns.razor` (create form `<select>`)
- Modify: `CompanionUI/Pages/Campaigns/CampaignDetail.razor` (setting selector + save, for existing campaigns)

**Interfaces:**
- Consumes: `SettingCatalog.KnownSettings` (Task 1); `CampaignRepository.CreateAsync(..., setting)` / `SetSettingAsync` (Task 3).
- Produces: no code interface — presentational. Falls under the UI-validation dev-flow gate (build + full suite green + live Playwright + overflow check; rebuild the app container first).

- [ ] **Step 1: Add a setting select to the create form**

In `Campaigns.razor`, add `@using DndMcpAICsharpFun.Features.Lore`, a `_newSetting` field, and a `<select>` in the create form (after Description):

```razor
            <label>Setting
                <select @bind="_newSetting">
                    <option value="">Generic / none</option>
                    @foreach (var s in SettingCatalog.KnownSettings)
                    {
                        <option value="@s">@s</option>
                    }
                </select>
            </label>
```

Add `private string _newSetting = "";` and pass it to create (empty → null):

```csharp
        await CampaignRepo.CreateAsync(_userId, _newName.Trim(), _newDescription.Trim(),
            string.IsNullOrWhiteSpace(_newSetting) ? null : _newSetting);
```

Reset `_newSetting = "";` alongside the other resets after create.

- [ ] **Step 2: Add a setting selector to CampaignDetail**

Read `CampaignDetail.razor` first (Serena). Add a small setting `<select>` bound to the loaded campaign's `Setting` with an on-change that calls `CampaignRepository.SetSettingAsync(campaignId, _userId, value)` and reloads. Populate options from `SettingCatalog.KnownSettings` plus a "Generic / none" (empty → null). Keep it consistent with the page's existing controls/styling.

- [ ] **Step 3: Build to verify the Razor compiles**

Run: `dotnet build`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add CompanionUI/Pages/Campaigns/Campaigns.razor CompanionUI/Pages/Campaigns/CampaignDetail.razor
git commit -m "feat(ui): choose a campaign setting on create and on the campaign page"
```

---

### Task 5: SettingLoreService (ownership-gated, scoped, cited)

**Files:**
- Create: `Features/Lore/SettingLoreService.cs`
- Create: `Features/Lore/SettingLoreResult.cs` (result + cited passage records)
- Register in DI: the feature's `Add*` (see Step 5) so `DndChatService` can depend on it
- Test: `DndMcpAICsharpFun.Tests/Lore/SettingLoreServiceTests.cs`

**Interfaces:**
- Consumes: `CampaignRepository.GetByIdAsync(id, userId)` (ownership); `SettingCatalog.Resolve`; `IRagRetrievalService.SearchAsync(RetrievalQuery, ct)` returning results with `.Text`, `.Metadata.SourceBook`, `.Metadata.SectionTitle`/`.Metadata.Chapter`, `.Score` (mirror `DndMcpTools.search_lore`'s projection).
- Produces:
  - `public sealed record CitedPassage(string Text, string SourceBook, string? Section, double Score);`
  - `public sealed record SettingLoreResult(string? Setting, IReadOnlyList<string> ScopedBooks, IReadOnlyList<CitedPassage> Passages);`
  - `public sealed class SettingLoreService(CampaignRepository campaigns, IRagRetrievalService rag)` with `Task<SettingLoreResult> AskForUserAsync(long userId, long campaignId, string question, DndVersion? version, CancellationToken ct)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Lore;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Tests.Persistence;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Lore;

[Collection("postgres")]
public sealed class SettingLoreServiceTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly CampaignRepository _campaigns = new(new TestDb(pg));
    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // Use a fake IRagRetrievalService that captures the RetrievalQuery and returns canned results.
    // (Mirror the RetrievalResult shape the service consumes; construct minimal results.)

    [Fact]
    public async Task Scopes_retrieval_to_the_campaigns_setting_books()
    {
        var rag = Substitute.For<IRagRetrievalService>();
        // capture the query; return one canned cited result
        var id = await _campaigns.CreateAsync(1, "Eberron", "d", setting: "Eberron");
        var svc = new SettingLoreService(_campaigns, rag);

        await svc.AskForUserAsync(1, id, "Dragonmarked Houses", version: null, CancellationToken.None);

        await rag.Received().SearchAsync(
            Arg.Is<RetrievalQuery>(q => q.SourceBooks != null
                && q.SourceBooks.Contains("ERLW") && q.SourceBooks.Contains("PHB")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Generic_campaign_scopes_to_nothing_unscoped()
    {
        var rag = Substitute.For<IRagRetrievalService>();
        var id = await _campaigns.CreateAsync(1, "Generic", "d"); // no setting
        var svc = new SettingLoreService(_campaigns, rag);

        await svc.AskForUserAsync(1, id, "fireball", version: null, CancellationToken.None);

        await rag.Received().SearchAsync(
            Arg.Is<RetrievalQuery>(q => q.SourceBooks == null || q.SourceBooks.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Foreign_campaign_throws_and_does_not_query()
    {
        var rag = Substitute.For<IRagRetrievalService>();
        var id = await _campaigns.CreateAsync(2, "Other", "d", setting: "Eberron"); // owned by user 2
        var svc = new SettingLoreService(_campaigns, rag);

        var act = () => svc.AskForUserAsync(1, id, "q", version: null, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        await rag.DidNotReceive().SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Empty_retrieval_returns_an_explicit_empty_result()
    {
        var rag = Substitute.For<IRagRetrievalService>();
        rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
            .Returns(/* empty result list */);
        var id = await _campaigns.CreateAsync(1, "E", "d", setting: "Eberron");
        var svc = new SettingLoreService(_campaigns, rag);

        var result = await svc.AskForUserAsync(1, id, "nonsense", version: null, CancellationToken.None);

        result.Passages.Should().BeEmpty();
    }
}
```

(Fill the RAG result construction to match the real `IRagRetrievalService.SearchAsync` return type — read `RagRetrievalService`/`search_lore` for the result record shape, e.g. `RetrievalResult(Text, Metadata, Score)`.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~SettingLoreServiceTests"`
Expected: FAIL — `SettingLoreService`/result records don't exist.

- [ ] **Step 3: Write the result records and service**

```csharp
// Features/Lore/SettingLoreResult.cs
namespace DndMcpAICsharpFun.Features.Lore;

public sealed record CitedPassage(string Text, string SourceBook, string? Section, double Score);

public sealed record SettingLoreResult(
    string? Setting,
    IReadOnlyList<string> ScopedBooks,
    IReadOnlyList<CitedPassage> Passages);
```

```csharp
// Features/Lore/SettingLoreService.cs
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Retrieval;

namespace DndMcpAICsharpFun.Features.Lore;

/// <summary>
/// Answers a lore question for one of the caller's OWN campaigns, scoped to that campaign's setting
/// source books. Ownership is enforced here (SEC-08, mirrors EncounterDesignService): a campaign that
/// is not the caller's throws and never reaches retrieval. Returns cited passages for the chat persona
/// to synthesize from — it does NOT itself call an LLM.
/// </summary>
public sealed class SettingLoreService(CampaignRepository campaigns, IRagRetrievalService rag)
{
    public async Task<SettingLoreResult> AskForUserAsync(
        long userId, long campaignId, string question, DndVersion? version, CancellationToken ct)
    {
        var campaign = await campaigns.GetByIdAsync(campaignId, userId)
            ?? throw new UnauthorizedAccessException("Campaign not found or not owned by the caller.");

        var books = SettingCatalog.Resolve(campaign.Setting);
        var query = new RetrievalQuery(
            question, Version: version,
            SourceBooks: books.Count > 0 ? books.ToArray() : null);

        var results = await rag.SearchAsync(query, ct);

        var passages = results.Select(r => new CitedPassage(
            r.Text,
            r.Metadata.SourceBook,
            r.Metadata.SectionTitle ?? r.Metadata.Chapter,
            r.Score)).ToList();

        return new SettingLoreResult(campaign.Setting, books.ToList(), passages);
    }
}
```

(Adjust the projection to the real `RetrievalResult` property names — verify against `DndMcpTools.search_lore` at `Features/Mcp/DndMcpTools.cs:45-52`.)

- [ ] **Step 4: Register the service in DI**

Add `SettingLoreService` to the DI group that `AddDndChat` pulls in (or register it and make `AddDndChat` call the new `AddLore()` — mirror how `AddDndChat` pulls in `AddEncounters` for `EncounterDesignService`). The service must be in the container that `FullContainerScopeValidationTests.BuildServiceCollection` validates — add the registration there too (the test hand-replicates Program's `Add*` list). Verify by running `dotnet test --filter "FullyQualifiedName~FullContainerScopeValidation"` after wiring.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~SettingLoreServiceTests"`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add Features/Lore/SettingLoreService.cs Features/Lore/SettingLoreResult.cs DndMcpAICsharpFun.Tests/Lore/SettingLoreServiceTests.cs <DI files>
git commit -m "feat(lore): SettingLoreService scopes cited lore retrieval to the campaign's setting (ownership-gated)"
```

---

### Task 6: ask_setting_lore chat tool + guard tests + real-Qdrant scoping proof

**Files:**
- Modify: `Features/Chat/DndChatService.cs` (register `ask_setting_lore`; inject `SettingLoreService`)
- Modify: `DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs` (guard tests + routing)
- Create: `DndMcpAICsharpFun.Tests/Lore/SettingLoreScopingIntegrationTests.cs` (real Qdrant, non-vacuity)

**Interfaces:**
- Consumes: `SettingLoreService.AskForUserAsync` (Task 5); the session `userId` closure in `DndChatService` (existing pattern for `rate_encounter`).
- Produces: `ask_setting_lore(campaignId, question)` per-user chat tool (returns `SettingLoreResult`).

- [ ] **Step 1: Write the failing chat guard/routing tests**

In `DndChatServiceTests`, extend the existing lists so `ask_setting_lore` is covered by BOTH guards (add it to `CreateService`'s wiring — inject a `SettingLoreService` built over `NoOpDbFactory` repos + a substitute `IRagRetrievalService`, mirroring `BuildEncounterDesignService`):
- Add `"ask_setting_lore"` to the authenticated-present assertions (`SendAsync_adds_..._tools_when_authenticated`) and the unauthenticated-absent assertions.
- Add `"ask_setting_lore"` to the `Encounter_tool_schemas_do_not_expose_userId...` name filter (`t.Name is ... or "ask_setting_lore"`).
- Add a routing test: invoking `ask_setting_lore` with a foreign/absent `campaignId` throws (ownership reached the service) — mirror `BuildEncounterTool_forwards_...`.

- [ ] **Step 2: Write the failing real-Qdrant scoping integration test (non-vacuity)**

```csharp
// SettingLoreScopingIntegrationTests — real Qdrant (Testcontainers.Qdrant / QdrantFixture),
// seed blocks into dnd_blocks with source_book "ERLW" (an in-setting block) AND "VGM" (off-setting).
// Build a real RagRetrievalService over the seeded collection; run SettingLoreService for an Eberron
// campaign; assert the returned passages include the ERLW block and EXCLUDE the VGM block.
[Fact]
public async Task Scoping_returns_in_setting_blocks_and_excludes_off_setting_blocks()
{
    // seed: {text:"The Dragonmarked Houses rule commerce.", source_book:"ERLW"}  (in scope via Eberron→ERLW)
    //       {text:"Volo describes the beholder.",          source_book:"VGM"}   (off-setting, must be excluded)
    // ... build real RagRetrievalService + real embedding over a GUID-suffixed collection ...
    var result = await svc.AskForUserAsync(userId, eberronCampaignId, "who holds power", version: null, ct);

    result.Passages.Select(p => p.SourceBook).Should().Contain("ERLW");
    result.Passages.Select(p => p.SourceBook).Should().NotContain("VGM"); // non-vacuity: scoping is real
    // Break-the-filter check (comment): removing SourceBooks scoping would let VGM through — this asserts it doesn't.
}
```

Reuse `QdrantFixture` and the existing real-Qdrant block-seeding pattern (study `RegroundServiceIntegrationTests` / `Tier1EmbeddingGroundingIntegrationTests` for how blocks are embedded + upserted into a GUID-unique collection and cleaned up). Note: this test validates the `RagRetrievalService` OR-filter end to end against real Qdrant — it is the discrimination gate for the whole scoping mechanism.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~DndChatServiceTests|FullyQualifiedName~SettingLoreScopingIntegrationTests"`
Expected: FAIL — `ask_setting_lore` not registered; scoping test can't resolve the tool/service.

- [ ] **Step 4: Register the tool in DndChatService**

Inject `SettingLoreService settingLoreService` into the `DndChatService` primary constructor (mirror `encounterService`), and register inside the authenticated block (near `rate_encounter`):

```csharp
            toolList.Add(AIFunctionFactory.Create(
                (long campaignId, string question, string? edition, CancellationToken toolCt) =>
                    settingLoreService.AskForUserAsync(
                        userId, campaignId, question, ParseEdition(edition), toolCt),
                name: "ask_setting_lore",
                description: "Answer a lore/worldbuilding question for one of the signed-in user's own " +
                    "campaigns, scoped to that campaign's SETTING sources. Pass the campaignId and the " +
                    "question. Returns cited passages retrieved ONLY from the campaign's setting books " +
                    "(plus core rules). Compose your answer STRICTLY from the returned passages and CITE " +
                    "each (source book + section); if no passages are returned, say the campaign's setting " +
                    "sources don't cover it — never invent world lore. edition is \"2014\" or \"2024\"."));
```

(Update `CreateService`/DI construction in the tests accordingly.)

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~DndChatServiceTests|FullyQualifiedName~SettingLoreScopingIntegrationTests"` (Docker required for the integration test)
Expected: PASS — guard/routing tests + the non-vacuity scoping proof.

- [ ] **Step 6: Commit**

```bash
git add Features/Chat/DndChatService.cs DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs DndMcpAICsharpFun.Tests/Lore/SettingLoreScopingIntegrationTests.cs
git commit -m "feat(chat): ask_setting_lore per-campaign grounded lore tool + real-Qdrant scoping proof"
```

---

### Task 7: Phase-1 ingest + full verification + live smoke

**Files:** none (ops + verification)

- [ ] **Step 1: Build clean + full suite**

Run: `dotnet build` (0/0) then `dotnet test` (Docker up).
Expected: all green; record the new total (was 1265/1265).

- [ ] **Step 2: Phase-1 — ingest ERLW blocks (ops, existing endpoints)**

With the app running: confirm ERLW is registered (or `POST /admin/books/register` with `fivetoolsSourceKey=ERLW` and the ERLW PDF — ask the user for it if not on disk), then `POST /admin/books/{id}/ingest-blocks`. Then **capture the real `source_book` payload values** the corpus uses: `GET /retrieval/search?q=Sharn` and inspect `metadata.sourceBook` for ERLW blocks, and `GET /retrieval/search?q=fireball` for the core-book value. **If they differ from `SettingCatalog`'s keys (`ERLW`/`PHB`/`DMG`/`MM`), fix the catalog keys to match the real values** and re-run Task 1's tests — the DATA INVARIANT from Global Constraints.

- [ ] **Step 3: Rebuild the app container (so the smoke tests current code + the new migration runs)**

Run: `docker compose build app && docker compose up -d app`; wait for healthy.

- [ ] **Step 4: Live smoke**

Sign in as `test`. (a) Create (or open) a campaign, set its Setting to **Eberron** via the form — reload, confirm it persists. (b) In chat, ask "what are the Dragonmarked Houses?" for that campaign → the answer is grounded and cited to **ERLW** (setting-scoped). (c) Ask the same on a **Generic** campaign → unscoped answer (or no ERLW-specific citation). (d) Confirm the setting `<select>` renders with no horizontal overflow at desktop (1280) and mobile (390). Capture screenshots.

- [ ] **Step 5: Final whole-branch review + roadmap refresh**

Request a final opus whole-branch review of the branch diff. On READY, refresh the `companion_roadmap` Serena memory with the shipped setting-aware-lore slice.
