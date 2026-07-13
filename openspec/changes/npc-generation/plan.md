# NPC / Statblock Generation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** `generate_npc(concept, archetype, maxCr?)` — the LLM picks an NPC stat-block archetype fitting a concept; the service validates it against `dnd_entities` and returns its real, cited stat block; the persona invents only the flavour (name/personality/hook) around the grounded numbers.

**Architecture:** Mirrors the shipped `BuildRecommenderService` (LLM picks → service validates by exact name → grounded result / suggest-on-miss → persona composes), minus ownership. `NpcGenerationService` resolves the archetype via entity search + exact-name match, gates by `maxCr`, and projects the entity to a grounded `NpcStatBlock` (CR/HP/abilities + the rendered `CanonicalText`). No LLM call, no migration, no UI.

**Tech Stack:** .NET 10, C#, xUnit + FluentAssertions + NSubstitute, Testcontainers (Qdrant), Microsoft.Extensions.AI chat tools.

## Global Constraints

- net10.0; nullable; **warnings-as-errors** — no new warnings; no new package.
- `generate_npc` is ownership-free (no `userId`/`campaignId`). No HTTP route, no `.http`/`.insomnia`, no migration, no shared-key MCP surface.
- `dnd_entities.sourceBook` is the 5etools KEY (`"MM"`/`"PHB"`) — cite it as-is (do NOT expect display names here; that's `dnd_blocks`).
- **Re-run the FULL suite at verification (dev-flow: a new service/DI + shape can ripple) — not a feature-filtered subset.**
- `dotnet` fails under the command sandbox (git-crypted `Config/`) — run every `dotnet` with `dangerouslyDisableSandbox: true`, timeout ~300000ms (~400000ms for container integration). LSP shows stale/false CS errors on new-namespace/changed test files — trust `dotnet build`/`dotnet test`.
- Serena symbolic tools for all code reads/edits (prefer literal/symbol edits — regex-mode can corrupt literal `\n`). Work on `main`; commit each reviewed task.

---

### Task 1: NpcArchetypes + records + NpcGenerationService + DI

**Files:**
- Create: `Features/Npc/NpcArchetypes.cs`, `Features/Npc/GeneratedNpc.cs` (holds `NpcStatBlock` + `GeneratedNpc`), `Features/Npc/NpcGenerationService.cs`, `Features/Npc/NpcServiceCollectionExtensions.cs`
- Modify: `Extensions/ChatExtensions.cs` (`AddDndChat` calls `AddNpc()`)
- Test: `DndMcpAICsharpFun.Tests/Npc/NpcGenerationServiceTests.cs`

**Interfaces:**
- Consumes: `IEntityRetrievalService.SearchDiagnosticAsync(EntitySearchQuery, ct) → IList<EntityDiagnosticResult>` (`.Id`, `.Name`, `.SourceBook`, `.Fields` JsonElement) and `.GetByIdAsync(id, ct) → EntityFullResult?` (`.Envelope.CanonicalText`, `.Envelope.Fields`); `EntitySearchQuery` (positional record); `MonsterCr.TryRead(JsonElement fields, out double cr)` (internal static in `Features/Encounters/EntitySearchMonsterSource.cs` — same assembly; add `using DndMcpAICsharpFun.Features.Encounters;`); `MonsterFields` (`Domain/Entities/Fields/MonsterFields.cs`) for HP/abilities via `JsonSerializer.Deserialize`; `EntityType.Monster`.
- Produces:
  - `public static class NpcArchetypes { public static readonly IReadOnlyList<string> Common = [...]; }`
  - `public sealed record NpcStatBlock(string Name, string SourceBook, double? Cr, int? Hp, int? Str, int? Dex, int? Con, int? Int, int? Wis, int? Cha, string CanonicalText);`
  - `public sealed record GeneratedNpc(string Concept, string Archetype, NpcStatBlock? StatBlock, bool ArchetypeInCorpus, IReadOnlyList<string> AvailableArchetypes);`
  - `public sealed class NpcGenerationService(IEntityRetrievalService retrieval)` with `Task<GeneratedNpc> GenerateAsync(string concept, string archetype, double? maxCr, CancellationToken ct)`.
  - `internal static IServiceCollection AddNpc(this IServiceCollection)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Npc;
using DndMcpAICsharpFun.Features.Retrieval.Entities;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Npc;

public sealed class NpcGenerationServiceTests
{
    private static EntityDiagnosticResult Diag(string id, string name, string cr) =>
        new(id, EntityType.Monster, name, "MM", "Edition2014", null, [], "pt",
            JsonDocument.Parse($$"""{"cr":"{{cr}}","hp":{"average":11},"dex":14}""").RootElement, 0.9f);

    private static EntityFullResult Full(string id, string name) =>
        new(new EntityEnvelope(id, EntityType.Monster, name, "MM", "Edition2014", null,
            new FirstAppearance("MM", "Edition2014"), [], [],
            "Spy\nMedium humanoid\nAC 12\nHP 27\nSTR 10 DEX 15 ...",   // rendered block
            JsonDocument.Parse("""{"cr":"1","hp":{"average":27},"str":10,"dex":15,"con":10,"int":12,"wis":14,"cha":16}""").RootElement));

    private static IEntityRetrievalService Search(EntityDiagnosticResult? hit, EntityFullResult? full)
    {
        var s = Substitute.For<IEntityRetrievalService>();
        s.SearchDiagnosticAsync(Arg.Any<EntitySearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(hit is null ? new List<EntityDiagnosticResult>() : new List<EntityDiagnosticResult> { hit });
        s.GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(full);
        return s;
    }

    [Fact]
    public async Task Valid_archetype_returns_grounded_stat_block()
    {
        var svc = new NpcGenerationService(Search(Diag("mm.monster.spy", "Spy", "1"), Full("mm.monster.spy", "Spy")));

        var npc = await svc.GenerateAsync("a shifty dockworker", "Spy", maxCr: null, CancellationToken.None);

        npc.ArchetypeInCorpus.Should().BeTrue();
        npc.StatBlock!.Name.Should().Be("Spy");
        npc.StatBlock.SourceBook.Should().Be("MM");
        npc.StatBlock.CanonicalText.Should().Contain("AC 12");
        npc.StatBlock.Cr.Should().Be(1);
    }

    [Fact]
    public async Task Unknown_archetype_returns_not_in_corpus_with_roster_and_no_block()
    {
        var svc = new NpcGenerationService(Search(hit: null, full: null));

        var npc = await svc.GenerateAsync("x", "Nonexistent Archetype", null, CancellationToken.None);

        npc.ArchetypeInCorpus.Should().BeFalse();
        npc.StatBlock.Should().BeNull();
        npc.AvailableArchetypes.Should().BeEquivalentTo(NpcArchetypes.Common);
    }

    [Fact]
    public async Task Non_exact_name_hit_is_treated_as_not_in_corpus()
    {
        // search returns a DIFFERENT monster than the requested archetype — must NOT be accepted
        var svc = new NpcGenerationService(Search(Diag("mm.monster.spider", "Giant Spider", "1"), Full("mm.monster.spider", "Giant Spider")));

        var npc = await svc.GenerateAsync("x", "Spy", null, CancellationToken.None);

        npc.ArchetypeInCorpus.Should().BeFalse();
        npc.StatBlock.Should().BeNull();
    }

    [Fact]
    public async Task Archetype_over_maxCr_is_rejected_with_roster()
    {
        var svc = new NpcGenerationService(Search(Diag("mm.monster.mage", "Mage", "6"), Full("mm.monster.mage", "Mage")));

        var npc = await svc.GenerateAsync("a wizard", "Mage", maxCr: 2, CancellationToken.None);

        npc.ArchetypeInCorpus.Should().BeFalse();
        npc.AvailableArchetypes.Should().NotBeEmpty();
    }
}
```

(Verify the real `EntityDiagnosticResult`/`EntityFullResult`/`EntityEnvelope`/`FirstAppearance` constructors via Serena `find_symbol` and adjust the test builders to match exactly — the shapes above are from the current records but confirm arg order.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~NpcGenerationServiceTests"`
Expected: FAIL — `NpcArchetypes`/`NpcStatBlock`/`GeneratedNpc`/`NpcGenerationService` don't exist.

- [ ] **Step 3: Write NpcArchetypes**

```csharp
namespace DndMcpAICsharpFun.Features.Npc;

/// <summary>Curated common NPC stat-block names (the MM NPC appendix roster) used as the suggestion
/// list when the caller's chosen archetype isn't in the corpus. The service can fetch ANY monster by
/// name; this is only the fallback menu.</summary>
public static class NpcArchetypes
{
    public static readonly IReadOnlyList<string> Common =
    [
        "Commoner", "Guard", "Acolyte", "Bandit", "Cultist", "Noble", "Spy", "Thug", "Scout",
        "Bandit Captain", "Priest", "Veteran", "Knight", "Mage", "Assassin", "Berserker",
        "Gladiator", "Cult Fanatic", "Archmage",
    ];
}
```

- [ ] **Step 4: Write the records**

```csharp
namespace DndMcpAICsharpFun.Features.Npc;

/// <summary>A grounded NPC stat block resolved from a real corpus Monster entity.</summary>
public sealed record NpcStatBlock(
    string Name, string SourceBook, double? Cr, int? Hp,
    int? Str, int? Dex, int? Con, int? Int, int? Wis, int? Cha, string CanonicalText);

/// <summary>The generation result: the grounded stat block when the archetype resolved, or the
/// not-in-corpus flag + the available-archetypes menu for the caller to re-pick.</summary>
public sealed record GeneratedNpc(
    string Concept, string Archetype, NpcStatBlock? StatBlock,
    bool ArchetypeInCorpus, IReadOnlyList<string> AvailableArchetypes);
```

- [ ] **Step 5: Write NpcGenerationService**

```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Encounters;      // MonsterCr
using DndMcpAICsharpFun.Features.Retrieval.Entities;

namespace DndMcpAICsharpFun.Features.Npc;

/// <summary>
/// Grounds an NPC to a real corpus stat block: the caller (chat LLM) picks the archetype fitting a
/// concept, this validates it exists as a Monster entity by exact name and returns its real stats;
/// a miss (or a CR over maxCr) yields the not-in-corpus flag + the archetype menu. Not ownership-gated;
/// does NOT call an LLM — the persona invents the flavour around the returned numbers.
/// </summary>
public sealed class NpcGenerationService(IEntityRetrievalService retrieval)
{
    public async Task<GeneratedNpc> GenerateAsync(
        string concept, string archetype, double? maxCr, CancellationToken ct)
    {
        var hits = await retrieval.SearchDiagnosticAsync(
            new EntitySearchQuery(
                QueryText: archetype, Type: EntityType.Monster, SourceBook: null, Edition: null,
                BookType: null, SettingTag: null, Keyword: null, CrNumericLte: null, CrNumericGte: null,
                SpellLevel: null, DamageType: null, TopK: 1),
            ct);

        var hit = hits.FirstOrDefault(
            r => string.Equals(r.Name, archetype, StringComparison.OrdinalIgnoreCase));

        // Not found, or the resolved CR exceeds the caller's cap → suggest the roster.
        if (hit is null
            || (maxCr is { } cap && MonsterCr.TryRead(hit.Fields, out var hitCr) && hitCr > cap))
        {
            return new GeneratedNpc(concept, archetype, StatBlock: null,
                ArchetypeInCorpus: false, AvailableArchetypes: NpcArchetypes.Common);
        }

        // Fetch the full envelope for the rendered stat-block text.
        var full = await retrieval.GetByIdAsync(hit.Id, ct);
        var canonicalText = full?.Envelope.CanonicalText ?? string.Empty;
        var fields = full?.Envelope.Fields ?? hit.Fields;

        double? cr = MonsterCr.TryRead(hit.Fields, out var crVal) ? crVal : null;
        MonsterFields? mf = TryDeserialize(fields);

        var block = new NpcStatBlock(
            hit.Name, hit.SourceBook, cr, mf?.Hp?.Average,
            mf?.Str, mf?.Dex, mf?.Con, mf?.Int, mf?.Wis, mf?.Cha, canonicalText);

        return new GeneratedNpc(concept, archetype, block,
            ArchetypeInCorpus: true, AvailableArchetypes: []);
    }

    private static MonsterFields? TryDeserialize(JsonElement fields)
    {
        try { return fields.Deserialize<MonsterFields>(); }
        catch (JsonException) { return null; } // structured fields are a convenience; CanonicalText is the base
    }
}
```

(Confirm `MonsterCr.TryRead`'s exact signature via Serena; if it's `out double`, the above matches. Confirm `MonsterFields`'s JSON options handle the corpus field casing — if `Deserialize` needs `JsonSerializerOptions`, use the same ones the ingestion/readers use, or read `Hp.Average`/abilities individually via `fields.TryGetProperty`.)

- [ ] **Step 6: Write AddNpc + wire into AddDndChat**

```csharp
// Features/Npc/NpcServiceCollectionExtensions.cs
using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Features.Npc;

internal static class NpcServiceCollectionExtensions
{
    internal static IServiceCollection AddNpc(this IServiceCollection services)
    {
        services.AddScoped<NpcGenerationService>();
        return services;
    }
}
```

In `Extensions/ChatExtensions.cs`, alongside `services.AddRules();` add `services.AddNpc();` (+ `using DndMcpAICsharpFun.Features.Npc;`).

- [ ] **Step 7: Run tests + scope validation**

Run: `dotnet test --filter "FullyQualifiedName~NpcGenerationServiceTests|FullyQualifiedName~FullContainerScopeValidation"`
Expected: PASS (4 service tests + scope-validation green with the new service in the graph).

- [ ] **Step 8: Commit**

```bash
git add Features/Npc/ Extensions/ChatExtensions.cs DndMcpAICsharpFun.Tests/Npc/NpcGenerationServiceTests.cs
git commit -m "feat(npc): NpcGenerationService grounds an NPC to a real corpus stat block (validate-or-suggest)"
```

---

### Task 2: generate_npc chat tool + guard tests

**Files:**
- Modify: `Features/Chat/DndChatService.cs` (inject `NpcGenerationService`; register `generate_npc`)
- Modify: `DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs` (DI wiring + guard/routing)

**Interfaces:**
- Consumes: `NpcGenerationService.GenerateAsync` (Task 1).
- Produces: `generate_npc(concept, archetype, maxCr?)` tool; no `userId`/`campaignId`.

- [ ] **Step 1: Write the failing guard/routing test**

In `DndChatServiceTests`: add `BuildNpcGenerationService(IEntityRetrievalService? search = null) => new(search ?? Substitute.For<IEntityRetrievalService>())`, thread a `NpcGenerationService?` param through `CreateService` into the `DndChatService` ctor. Add `"generate_npc"` to the authenticated-present + unauthenticated-absent lists and the `...do_not_expose_userId...` name filter. Add:

```csharp
[Fact]
public async Task GenerateNpcTool_exposes_no_user_or_campaign_id_and_reaches_the_service()
{
    var search = Substitute.For<IEntityRetrievalService>();
    search.SearchDiagnosticAsync(Arg.Any<EntitySearchQuery>(), Arg.Any<CancellationToken>())
        .Returns(new List<EntityDiagnosticResult>()); // no hit → not-in-corpus
    var client = new FakeChatClient();
    var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(9),
        npcGenerationService: BuildNpcGenerationService(search));

    await svc.SendAsync("npc", false, CancellationToken.None);
    var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == "generate_npc");

    tool.JsonSchema.TryGetProperty("properties", out var props);
    props.TryGetProperty("userId", out _).Should().BeFalse();
    props.TryGetProperty("campaignId", out _).Should().BeFalse();

    var result = await tool.InvokeAsync(
        ToArgs(new { concept = "a shifty dockworker", archetype = "Spy", maxCr = (double?)null }),
        CancellationToken.None);
    var npc = ((JsonElement)result!).Deserialize<GeneratedNpc>(tool.JsonSerializerOptions);
    npc!.ArchetypeInCorpus.Should().BeFalse();            // empty search → not-in-corpus (reached the service)
    npc.AvailableArchetypes.Should().NotBeEmpty();
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~DndChatServiceTests"`
Expected: FAIL — `generate_npc` not registered; `CreateService` has no npc param.

- [ ] **Step 3: Register the tool**

Inject `NpcGenerationService npcGenerationService` into `DndChatService`'s ctor (mirror `rulesAdjudicationService`). In the authenticated block:

```csharp
            toolList.Add(AIFunctionFactory.Create(
                (string concept, string archetype, double? maxCr, CancellationToken toolCt) =>
                    npcGenerationService.GenerateAsync(concept, archetype, maxCr, toolCt),
                name: "generate_npc",
                description: "Generate an NPC for a scene from a concept (e.g. 'a shifty Sharn dockworker'). " +
                    "YOU pick the stat-block archetype that best fits the concept and pass it as archetype " +
                    "(e.g. Spy, Commoner, Guard, Bandit Captain, Veteran); the tool returns that archetype's " +
                    "REAL stat block (CR/HP/ability scores + the rendered block). Compose the NPC's name, " +
                    "personality, appearance, and a hook to fit the concept, but take ALL mechanical stats " +
                    "from the returned block and CITE it (source book) — NEVER invent stat numbers. If the " +
                    "result says the archetype is not in the corpus (archetypeInCorpus false), pick a different " +
                    "one from availableArchetypes and call again. Optional maxCr caps the archetype's power. " +
                    "Not tied to any campaign or character."));
```

Update `CreateService`/DI in the tests to pass the new service.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~DndChatServiceTests"`
Expected: PASS — present/absent, no-userId/no-campaignId, routing reaches the service.

- [ ] **Step 5: Commit**

```bash
git add Features/Chat/DndChatService.cs DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs
git commit -m "feat(chat): generate_npc grounded NPC tool (ownership-free, real stat block + invented flavour)"
```

---

### Task 3: Real-store grounding test + full verification

**Files:**
- Create: `DndMcpAICsharpFun.Tests/Npc/NpcGenerationIntegrationTests.cs` (real entity store, or a store-level test if lighter)

- [ ] **Step 1: Study the entity-store test pattern**

Read an existing entity-store integration test (Serena — e.g. `DndMcpAICsharpFun.Tests/Entities/...` or the retrieval integration tests) to find the lightest way to exercise a real `IEntityRetrievalService`/`QdrantEntityVectorStore` with a seeded entity. If a real store is heavy, a store-level test that seeds a "Spy" Monster entity and drives `NpcGenerationService` through the real `EntityRetrievalService` is sufficient.

- [ ] **Step 2: Write the grounding test**

Seed a "Spy" Monster entity (real fields incl. cr/hp/abilities + a CanonicalText); `GenerateAsync("a shifty dockworker", "Spy", null, ct)` → `ArchetypeInCorpus=true` + the grounded stats (name "Spy", cr, canonicalText non-empty); a bogus archetype → `ArchetypeInCorpus=false` + roster, no unrelated block.

- [ ] **Step 3: Run it (Docker + Qdrant if store-backed)**

Run: `dotnet test --filter "FullyQualifiedName~NpcGenerationIntegrationTests"`
Expected: PASS.

- [ ] **Step 4: Build + FULL suite**

Run: `dotnet build` (0/0) then `dotnet test` (the FULL suite — a new service/DI can ripple; do not filter). Record the total (was 1289/1289).

- [ ] **Step 5: Commit**

```bash
git add DndMcpAICsharpFun.Tests/Npc/NpcGenerationIntegrationTests.cs
git commit -m "test(npc): real-store grounding — a valid archetype resolves to real stats, bogus suggests the roster"
```

- [ ] **Step 6: Rebuild container + live smoke + final review**

`docker compose build app && docker compose up -d app`; wait healthy. Sign in as `test`, ask "generate a shifty Sharn dockworker for my game" → the LLM picks Spy/Commoner, the tool returns grounded stats, the persona presents an NPC with real AC/HP/CR (cited to MM) + an invented name/personality/hook; a nonsense archetype triggers the re-pick path. Then request a final opus whole-branch review; on READY, refresh the `companion_roadmap` memory.
