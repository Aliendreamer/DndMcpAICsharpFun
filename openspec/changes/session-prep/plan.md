# Session Prep Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** `prep_session(campaignId, theme, difficulty?, npcArchetype)` — one call that composes the shipped encounter, NPC, and setting-lore surfaces into a cohesive, campaign-scoped grounded packet the persona weaves into a session outline.

**Architecture:** `SessionPrepService` composes the three shipped services verbatim — `EncounterDesignService.BuildForUserAsync` (first, so its ownership check gates the whole prep), `NpcGenerationService.GenerateAsync`, `SettingLoreService.AskForUserAsync` (lore question derived from the theme) — into a `SessionPrepPacket` that reuses their result types. No new grounding, no new LLM call, no migration/UI.

**Tech Stack:** .NET 10, C#, xUnit + FluentAssertions + NSubstitute, Testcontainers (Postgres), Microsoft.Extensions.AI chat tools.

## Global Constraints

- net10.0; nullable; **warnings-as-errors** — no new warnings; no new package.
- `prep_session` is per-user (closes over the session `userId`; takes `campaignId`; exposes no `userId` arg). Ownership is enforced by the campaign-scoped sub-services (encounter build runs first). No HTTP route, no `.http`/`.insomnia`, no migration, no shared-key MCP surface.
- **Re-run the FULL suite at verification (dev-flow: a new service/DI can ripple) — not a feature-filtered subset.**
- `dotnet` fails under the command sandbox (git-crypted `Config/`) — run every `dotnet` with `dangerouslyDisableSandbox: true`, timeout ~300000ms (~400000ms for container tests). LSP shows stale/false CS errors on new-namespace/changed test files — trust `dotnet build`/`dotnet test`.
- Serena symbolic tools for all code reads/edits (prefer literal/symbol edits — regex-mode can corrupt literal `\n`). Work on `main`; commit each reviewed task.
- Sub-service signatures (verify via Serena before use): `EncounterDesignService.BuildForUserAsync(long userId, long? campaignId, IReadOnlyList<int>? partyLevels, Difficulty target, DndVersion ed, string? theme, double? crLte, double? crGte, CancellationToken ct) → BuiltEncounter`; `NpcGenerationService.GenerateAsync(string concept, string archetype, double? maxCr, CancellationToken ct) → GeneratedNpc`; `SettingLoreService.AskForUserAsync(long userId, long campaignId, string question, DndVersion? version, CancellationToken ct) → SettingLoreResult`.

---

### Task 1: SessionPrepService + SessionPrepPacket + DI

**Files:**
- Create: `Features/SessionPrep/SessionPrepPacket.cs`, `Features/SessionPrep/SessionPrepService.cs`, `Features/SessionPrep/SessionPrepServiceCollectionExtensions.cs`
- Modify: `Extensions/ChatExtensions.cs` (`AddDndChat` calls `AddSessionPrep()`)
- Test: `DndMcpAICsharpFun.Tests/SessionPrep/SessionPrepServiceTests.cs`

**Interfaces:**
- Consumes: the three shipped services (concrete). `Difficulty` enum (`Features/Encounters`), `DndVersion` (`Domain`).
- Produces:
  - `public sealed record SessionPrepPacket(string Theme, BuiltEncounter Encounter, GeneratedNpc Npc, SettingLoreResult LoreHooks);`
  - `public sealed class SessionPrepService(EncounterDesignService encounters, NpcGenerationService npcs, SettingLoreService lore)` with `Task<SessionPrepPacket> PrepForUserAsync(long userId, long campaignId, string theme, Difficulty difficulty, string npcArchetype, DndVersion edition, CancellationToken ct)`.
  - `internal static IServiceCollection AddSessionPrep(this IServiceCollection)`.

- [ ] **Step 1: Write the failing tests (real Postgres, mirror EncounterDesignServiceTests)**

Read `DndMcpAICsharpFun.Tests/Encounters/EncounterDesignServiceTests.cs` for the exact `PostgresFixture` + real `CampaignRepository`/`HeroRepository` + `PoolMonsterSource`/`CapturingMonsterSource` + `SeedCampaignWithHeroLevelAsync` helpers, and reuse them. Construct the three sub-services over the SAME real repos + substitutes:

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Encounters;
using DndMcpAICsharpFun.Features.Lore;
using DndMcpAICsharpFun.Features.Npc;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.Retrieval.Entities;
using DndMcpAICsharpFun.Features.SessionPrep;
using DndMcpAICsharpFun.Tests.Persistence;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace DndMcpAICsharpFun.Tests.SessionPrep;

[Collection("postgres")]
public sealed class SessionPrepServiceTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly CampaignRepository _campaigns = new(new TestDb(pg));
    private readonly HeroRepository _heroes = new(new TestDb(pg));
    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // A monster source returning a fixed pool so the encounter build has candidates.
    private sealed class PoolMonsterSource(IReadOnlyList<MonsterRef> pool) : IEncounterMonsterSource
    {
        public Task<IReadOnlyList<MonsterRef>> FindAsync(
            DndVersion ed, double crGte, double crLte, string? theme, bool srdOnly, int limit, CancellationToken ct) =>
            Task.FromResult(pool);
    }

    private (SessionPrepService svc, IRagRetrievalService rag) Build()
    {
        IReadOnlyList<MonsterRef> pool = Enumerable.Range(1, 6)
            .Select(i => new MonsterRef($"mm.monster.{i}", $"Monster {i}", 3, EncounterMath.CrToXp(3))).ToList();
        var assessor = new EncounterAssessor();
        var encounters = new EncounterDesignService(
            assessor, new EncounterGenerator(new PoolMonsterSource(pool), assessor),
            _heroes, _campaigns, Substitute.For<IEntityRetrievalService>());

        var rag = Substitute.For<IRagRetrievalService>();
        rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<RetrievalResult>()); // hooks empty is fine for these tests
        var lore = new SettingLoreService(_campaigns, rag);

        var npcs = new NpcGenerationService(Substitute.For<IEntityRetrievalService>()); // bogus archetype → not-in-corpus

        return (new SessionPrepService(encounters, npcs, lore), rag);
    }

    [Fact]
    public async Task Prep_composes_encounter_npc_and_hooks_for_an_owned_campaign()
    {
        var (svc, rag) = Build();
        var campaignId = await SeedCampaignWithHeroLevelAsync(userId: 1, level: 5); // reuse EncounterDesignServiceTests helper

        var packet = await svc.PrepForUserAsync(
            1, campaignId, "Sharn intrigue", Difficulty.Medium, "Spy", DndVersion.Edition2014, CancellationToken.None);

        packet.Theme.Should().Be("Sharn intrigue");
        packet.Encounter.Should().NotBeNull();
        packet.Encounter.Assessment.Monsters.Should().NotBeEmpty();     // built from the pool
        packet.Npc.Should().NotBeNull();                                // GeneratedNpc (archetype not in the substitute → not-in-corpus, still present)
        packet.LoreHooks.Should().NotBeNull();
        // lore question derived from the theme
        await rag.Received().SearchAsync(
            Arg.Is<RetrievalQuery>(q => q.QueryText.Contains("Sharn intrigue")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Foreign_campaign_throws_before_any_prep()
    {
        var (svc, _) = Build();
        var campaignId = await SeedCampaignWithHeroLevelAsync(userId: 2, level: 5); // owned by user 2

        var act = () => svc.PrepForUserAsync(
            1, campaignId, "x", Difficulty.Medium, "Spy", DndVersion.Edition2014, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    // Copy SeedCampaignWithHeroLevelAsync from EncounterDesignServiceTests (CreateAsync + hero + SaveSnapshotAsync).
    private async Task<long> SeedCampaignWithHeroLevelAsync(long userId, int level) { /* mirror the sibling helper */ }
}
```

(Confirm `SettingLoreService`'s constructor is `(CampaignRepository campaigns, IRagRetrievalService rag)` and `NpcGenerationService`'s is `(IEntityRetrievalService retrieval)` via Serena; copy `SeedCampaignWithHeroLevelAsync` verbatim from `EncounterDesignServiceTests`.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~SessionPrepServiceTests"` (Docker required — real Postgres)
Expected: FAIL — `SessionPrepService`/`SessionPrepPacket` don't exist.

- [ ] **Step 3: Write the records + service**

```csharp
// Features/SessionPrep/SessionPrepPacket.cs
using DndMcpAICsharpFun.Features.Encounters;
using DndMcpAICsharpFun.Features.Lore;
using DndMcpAICsharpFun.Features.Npc;

namespace DndMcpAICsharpFun.Features.SessionPrep;

/// <summary>A cohesive, campaign-scoped prep packet: an encounter for the party, a fitting NPC, and
/// setting lore hooks — each a grounded sub-result the persona weaves into a session outline.</summary>
public sealed record SessionPrepPacket(
    string Theme, BuiltEncounter Encounter, GeneratedNpc Npc, SettingLoreResult LoreHooks);
```

```csharp
// Features/SessionPrep/SessionPrepService.cs
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Encounters;
using DndMcpAICsharpFun.Features.Lore;
using DndMcpAICsharpFun.Features.Npc;

namespace DndMcpAICsharpFun.Features.SessionPrep;

/// <summary>
/// Composes the shipped encounter, NPC, and setting-lore surfaces into one campaign-scoped prep
/// packet. Adds no grounding of its own — each sub-service keeps its grounding and ownership check.
/// The encounter build runs FIRST so its ownership check gates the whole prep. No LLM call.
/// </summary>
public sealed class SessionPrepService(
    EncounterDesignService encounters, NpcGenerationService npcs, SettingLoreService lore)
{
    public async Task<SessionPrepPacket> PrepForUserAsync(
        long userId, long campaignId, string theme, Difficulty difficulty, string npcArchetype,
        DndVersion edition, CancellationToken ct)
    {
        // First — its ownership check (foreign/empty campaign → throws) gates the whole prep.
        var encounter = await encounters.BuildForUserAsync(
            userId, campaignId, partyLevels: null, difficulty, edition, theme,
            crLte: null, crGte: null, ct);

        var npc = await npcs.GenerateAsync(concept: theme, archetype: npcArchetype, maxCr: null, ct);

        var hooks = await lore.AskForUserAsync(userId, campaignId, LoreQuestion(theme), edition, ct);

        return new SessionPrepPacket(theme, encounter, npc, hooks);
    }

    private static string LoreQuestion(string theme) =>
        $"factions, locations, and plot hooks related to {theme}";
}
```

```csharp
// Features/SessionPrep/SessionPrepServiceCollectionExtensions.cs
using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Features.SessionPrep;

internal static class SessionPrepServiceCollectionExtensions
{
    internal static IServiceCollection AddSessionPrep(this IServiceCollection services)
    {
        services.AddScoped<SessionPrepService>();
        return services;
    }
}
```

- [ ] **Step 4: Wire AddSessionPrep into AddDndChat**

In `Extensions/ChatExtensions.cs`, alongside `services.AddNpc();` add `services.AddSessionPrep();` (+ `using DndMcpAICsharpFun.Features.SessionPrep;`). `AddDndChat` already pulls in `AddEncounters`/`AddLore`/`AddNpc`, so all three deps resolve.

- [ ] **Step 5: Run tests + scope validation**

Run: `dotnet test --filter "FullyQualifiedName~SessionPrepServiceTests|FullyQualifiedName~FullContainerScopeValidation"`
Expected: PASS (2 service tests + scope-validation green with the new service in the graph).

- [ ] **Step 6: Commit**

```bash
git add Features/SessionPrep/ Extensions/ChatExtensions.cs DndMcpAICsharpFun.Tests/SessionPrep/SessionPrepServiceTests.cs
git commit -m "feat(session-prep): SessionPrepService composes encounter + NPC + setting hooks (ownership-gated)"
```

---

### Task 2: prep_session chat tool + guard tests

**Files:**
- Modify: `Features/Chat/DndChatService.cs` (inject `SessionPrepService`; register `prep_session`)
- Modify: `DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs` (DI wiring + guard/routing)

**Interfaces:**
- Consumes: `SessionPrepService.PrepForUserAsync`; the existing private `ParseDifficulty` / `ParseEdition` helpers; the session `userId` closure.
- Produces: `prep_session(campaignId, theme, difficulty?, npcArchetype)` tool; no `userId` arg.

- [ ] **Step 1: Write the failing guard/routing test**

In `DndChatServiceTests`: add `BuildSessionPrepService(...)` — construct a `SessionPrepService` over the DB-free sub-service helpers (`BuildEncounterDesignService()`, `BuildNpcGenerationService()`, `BuildSettingLoreService()` — all over `NoOpDbFactory`/substitutes), thread a `SessionPrepService?` param through `CreateService` into the `DndChatService` ctor. Add `"prep_session"` to the authenticated-present + unauthenticated-absent lists and the `...do_not_expose_userId...` name filter. Add a routing test mirroring `BuildEncounterTool_forwards_...`:

```csharp
[Fact]
public async Task PrepSessionTool_reaches_the_service_and_exposes_no_userId()
{
    var client = new FakeChatClient();
    var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(42));
    await svc.SendAsync("prep", false, CancellationToken.None);
    var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == "prep_session");

    tool.JsonSchema.TryGetProperty("properties", out var props);
    props.TryGetProperty("userId", out _).Should().BeFalse();

    // With a NoOpDbFactory-backed campaign repo, the encounter sub-service's ownership fetch throws
    // (NotSupportedException) — proving prep_session routed campaignId/userId through to PrepForUserAsync.
    var act = () => tool.InvokeAsync(
        ToArgs(new { campaignId = 1L, theme = "Sharn intrigue", difficulty = "Medium", npcArchetype = "Spy" }),
        CancellationToken.None).AsTask();
    await act.Should().ThrowAsync<Exception>();
}
```

(The default `BuildSessionPrepService` uses `NoOpDbFactory` repos, so `PrepForUserAsync` throws when it hits the campaign fetch — this proves routing, like the `build_encounter` guard test. Match whatever exception `NoOpDbFactory` produces.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~DndChatServiceTests"`
Expected: FAIL — `prep_session` not registered; `CreateService` has no session-prep param.

- [ ] **Step 3: Register the tool**

Inject `SessionPrepService sessionPrepService` into `DndChatService`'s ctor (mirror `npcGenerationService`). In the authenticated block:

```csharp
            toolList.Add(AIFunctionFactory.Create(
                (long campaignId, string theme, string? difficulty, string npcArchetype, CancellationToken toolCt) =>
                    sessionPrepService.PrepForUserAsync(
                        userId, campaignId, theme, ParseDifficulty(difficulty), npcArchetype,
                        DndVersion.Edition2014, toolCt),
                name: "prep_session",
                description: "Prep a game session for the signed-in user's OWN campaign (campaignId). Pass a " +
                    "theme (e.g. 'Sharn intrigue'), an optional difficulty (Trivial/Easy/Medium/Hard/Deadly), " +
                    "and the NPC stat-block archetype you pick to fit the theme (e.g. Spy, Guard, Cult Fanatic). " +
                    "Returns a cohesive packet: an encounter built for the campaign's party, a grounded NPC, and " +
                    "setting lore hooks scoped to the campaign's world. Compose a session outline STRICTLY from " +
                    "the returned pieces — cite the encounter's monsters, the NPC's real stat block, and the " +
                    "lore hooks; if the NPC archetype isn't in the corpus (npc.archetypeInCorpus false), re-pick " +
                    "from npc.availableArchetypes. Never invent stat numbers or world lore."));
```

Update `CreateService`/DI in the tests to pass the new service.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~DndChatServiceTests"`
Expected: PASS — present/absent, no-userId schema, routing reaches the service.

- [ ] **Step 5: Commit**

```bash
git add Features/Chat/DndChatService.cs DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs
git commit -m "feat(chat): prep_session composes a campaign-scoped session packet (ownership-gated)"
```

---

### Task 3: Full verification + live smoke

**Files:** none (verification)

- [ ] **Step 1: Build + FULL suite**

Run: `dotnet build` (0/0) then `dotnet test` (the FULL suite — a new service/DI can ripple; do not filter). Record the total (was 1296/1296).

- [ ] **Step 2: Rebuild container + live smoke (Ollama)**

`docker compose build app && docker compose up -d app`; wait healthy. Sign in as `test`; ensure the Eberron campaign (id 3) has at least one hero (add one via the UI if empty, so the encounter can build); ask "prep a Sharn intrigue session for my Eberron campaign (campaign 3), use a Spy NPC" → a cohesive outline citing: an encounter built for the party (monsters/XP), a Spy NPC with real stats, and ERLW setting hooks. Confirm a nonexistent/foreign campaign is rejected. No new UI → no overflow gate.

- [ ] **Step 3: Final whole-branch review + roadmap refresh**

Request a final opus whole-branch review of the branch diff. On READY, refresh the `companion_roadmap` Serena memory with the shipped session-prep slice.
