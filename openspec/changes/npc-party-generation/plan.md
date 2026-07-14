# generate_npc_party (NPC-gen v2) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Add a single-string-param `generate_npc_party(theme)` chat tool that returns a themed ensemble of grounded NPCs (leader + supporting), each anchored to a real Monster stat block — the qwen3-reliable way to produce a "cast."

**Architecture:** A deterministic `NpcPartyTemplates` table maps the theme (keyword match) to an ordered roster of `(Role, Archetype)` drawn from `NpcArchetypes.Common`; `NpcGenerationService.GeneratePartyAsync` grounds each roster member by reusing the existing anti-fuzzy `GenerateAsync`; a 1-param ownership-free chat tool exposes it. Theme selects the template + is handed to the LLM for flavor — never a monster-search filter.

**Tech Stack:** .NET 10, C#, xunit + FluentAssertions + NSubstitute. `Microsoft.Extensions.AI` `AIFunctionFactory`.

## Global Constraints

- `net10.0`, nullable, implicit usings, **warnings-as-errors**.
- **qwen3 constraint:** the chat tool takes EXACTLY ONE required string param (`theme`) — no arrays, no extra params, no `userId`/`campaignId`.
- **Theme is a template key + LLM flavor, NEVER a monster/keyword search filter** (the session-prep theme-over-filter lesson).
- Every archetype named in every roster (incl. Default) MUST be a member of `NpcArchetypes.Common` (guaranteed-grounded MM roster) — enforced by a unit test.
- Reuse `NpcGenerationService.GenerateAsync(concept, archetype, maxCr, ct)` and `IEntityRetrievalService`; no new retrieval/migration/HTTP route.
- Build/test with `dangerouslyDisableSandbox: true` (git-crypt); Docker up. **LSP throws FALSE CS0246/CS1061 on test files — trust `dotnet build`/`dotnet test`, not the LSP.** Full-suite baseline: run `dotnet test` once at start to capture the current count.
- **git worktree is unusable while git-crypt is active** (smudge fails) — run tasks sequentially, no worktree isolation.
- Use Serena for all code reads/edits.

---

### Task 1: NpcPartyTemplates + result records

**Files:**
- Modify: `Features/Npc/GeneratedNpc.cs` (add two records)
- Create: `Features/Npc/NpcPartyTemplates.cs`
- Test: `DndMcpAICsharpFun.Tests/Npc/NpcPartyTemplatesTests.cs`

**Interfaces — Produces:**
- `record NpcPartyMember(string Role, GeneratedNpc Npc)`
- `record GeneratedNpcParty(string Theme, string Template, IReadOnlyList<NpcPartyMember> Members)`
- `static class NpcPartyTemplates` with `(string Name, IReadOnlyList<(string Role, string Archetype)> Roster) Resolve(string theme)`

- [ ] **Step 1: add the records** to `Features/Npc/GeneratedNpc.cs`:

```csharp
/// <summary>One member of a generated NPC ensemble: its role plus the grounded NPC.</summary>
public sealed record NpcPartyMember(string Role, GeneratedNpc Npc);

/// <summary>A themed ensemble of grounded NPCs (leader first) resolved from a party template.</summary>
public sealed record GeneratedNpcParty(string Theme, string Template, IReadOnlyList<NpcPartyMember> Members);
```

- [ ] **Step 2: write the failing test** `NpcPartyTemplatesTests` (namespace `DndMcpAICsharpFun.Tests.Npc`):

```csharp
using DndMcpAICsharpFun.Features.Npc;
using FluentAssertions;
using Xunit;

public sealed class NpcPartyTemplatesTests
{
    [Fact]
    public void Criminal_keyword_selects_bandit_captain_led_roster()
    {
        var (name, roster) = NpcPartyTemplates.Resolve("a Sharn heist crew");
        name.Should().Be("criminal");
        roster[0].Archetype.Should().Be("Bandit Captain");
        roster.Select(r => r.Archetype).Should().Contain("Thug").And.Contain("Spy");
    }

    [Fact]
    public void Unmatched_theme_falls_back_to_default_never_empty()
    {
        var (name, roster) = NpcPartyTemplates.Resolve("a quiet afternoon in the meadow");
        name.Should().Be("default");
        roster.Should().NotBeEmpty();
        roster[0].Archetype.Should().Be("Veteran");
    }

    [Fact]
    public void Every_roster_archetype_is_a_grounded_common_member()
    {
        foreach (var t in NpcPartyTemplates.All)
            foreach (var (_, archetype) in t.Roster)
                NpcArchetypes.Common.Should().Contain(archetype, $"template '{t.Name}' references {archetype}");
        foreach (var (_, archetype) in NpcPartyTemplates.DefaultRoster)
            NpcArchetypes.Common.Should().Contain(archetype);
    }
}
```

- [ ] **Step 3: run → fail** (`dotnet test --filter FullyQualifiedName~NpcPartyTemplatesTests`, dangerouslyDisableSandbox). Expected: type/member not found.
- [ ] **Step 4: implement** `Features/Npc/NpcPartyTemplates.cs`:

```csharp
namespace DndMcpAICsharpFun.Features.Npc;

/// <summary>Deterministic theme→ensemble templates. The theme selects a template by keyword substring
/// (first match wins) and is otherwise handed to the LLM for flavor — it is NEVER used as a monster
/// search filter. Every archetype is a member of <see cref="NpcArchetypes.Common"/> (grounded roster).</summary>
public static class NpcPartyTemplates
{
    public sealed record Template(string Name, string[] Keywords, IReadOnlyList<(string Role, string Archetype)> Roster);

    public static readonly IReadOnlyList<Template> All =
    [
        new("criminal", ["criminal", "heist", "gang", "thie", "smuggl", "crime"],
            [("leader","Bandit Captain"), ("enforcer","Thug"), ("enforcer","Thug"), ("informant","Spy")]),
        new("military", ["military", "guard", "watch", "soldier", "mercenary", "garrison"],
            [("commander","Veteran"), ("soldier","Guard"), ("soldier","Guard"), ("scout","Scout")]),
        new("cult", ["cult", "temple", "zealot", "heretic", "sect"],
            [("high priest","Cult Fanatic"), ("cultist","Cultist"), ("cultist","Cultist"), ("acolyte","Acolyte")]),
        new("noble", ["noble", "court", "political", "intrigue", "house", "aristocra"],
            [("noble","Noble"), ("bodyguard","Guard"), ("bodyguard","Guard"), ("agent","Spy")]),
        new("arcane", ["arcane", "mage", "wizard", "arcanist", "sorcer"],
            [("archmage","Mage"), ("apprentice","Acolyte"), ("warden","Guard"), ("warden","Guard")]),
    ];

    public static readonly IReadOnlyList<(string Role, string Archetype)> DefaultRoster =
        [("captain","Veteran"), ("guard","Guard"), ("guard","Guard"), ("townsfolk","Commoner")];

    public static (string Name, IReadOnlyList<(string Role, string Archetype)> Roster) Resolve(string theme)
    {
        var t = (theme ?? string.Empty).ToLowerInvariant();
        foreach (var tpl in All)
            if (tpl.Keywords.Any(k => t.Contains(k, StringComparison.Ordinal)))
                return (tpl.Name, tpl.Roster);
        return ("default", DefaultRoster);
    }
}
```

- [ ] **Step 5: run → pass.** **Step 6: commit** `feat(npc): NpcPartyTemplates deterministic theme→ensemble table`.

---

### Task 2: GeneratePartyAsync on NpcGenerationService

**Files:**
- Modify: `Features/Npc/NpcGenerationService.cs`
- Test: `DndMcpAICsharpFun.Tests/Npc/NpcGenerationServicePartyTests.cs`

**Interfaces — Produces:** `Task<GeneratedNpcParty> NpcGenerationService.GeneratePartyAsync(string theme, CancellationToken ct)`.
**Consumes:** existing `GenerateAsync(concept, archetype, maxCr, ct)`; `NpcPartyTemplates.Resolve`.

- [ ] **Step 1: write the failing test** `NpcGenerationServicePartyTests`. **Fake design (critical):** `GeneratePartyAsync` resolves each member by exact archetype NAME, so the fake `IEntityRetrievalService` must return a hit **named after the queried archetype**, not one fixed monster — else only the name-matching member resolves in-corpus. Use a callback keyed off the query's `QueryText`:

```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Npc;
using DndMcpAICsharpFun.Features.Retrieval.Entities;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Npc;

public sealed class NpcGenerationServicePartyTests
{
    // A fake that grounds ANY queried archetype: the returned hit is named after the query text.
    private static IEntityRetrievalService EchoSearch(ISet<string>? missing = null)
    {
        var s = Substitute.For<IEntityRetrievalService>();
        s.SearchDiagnosticAsync(Arg.Any<EntitySearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var name = ci.Arg<EntitySearchQuery>().QueryText;
                if (missing is not null && missing.Contains(name))
                    return new List<EntityDiagnosticResult>();
                var id = "mm.monster." + name.ToLowerInvariant().Replace(' ', '-');
                return new List<EntityDiagnosticResult> { new(id, EntityType.Monster, name, "MM",
                    "Edition2014", null, [], "pt",
                    JsonDocument.Parse("""{"cr":"1","hp":{"average":11},"dex":14}""").RootElement, 0.9f) };
            });
        s.GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var id = ci.Arg<string>();
                var name = id.Replace("mm.monster.", "").Replace('-', ' ');
                return new EntityFullResult(new EntityEnvelope(id, EntityType.Monster, name, "MM",
                    "Edition2014", null, new FirstAppearance("MM","Edition2014"), [], [],
                    $"{name}\nAC 12\nHP 27\nSTR 10 DEX 15",
                    JsonDocument.Parse("""{"cr":"1","hp":{"average":27},"str":10,"dex":15,"con":10,"int":12,"wis":14,"cha":16}""").RootElement));
            });
        return s;
    }

    [Fact]
    public async Task Themed_party_grounds_every_member_in_roster_order()
    {
        var svc = new NpcGenerationService(EchoSearch());
        var party = await svc.GeneratePartyAsync("a Sharn heist", CancellationToken.None);

        party.Template.Should().Be("criminal");
        party.Members.Should().HaveCount(4);
        party.Members[0].Role.Should().Be("leader");
        party.Members[0].Npc.ArchetypeInCorpus.Should().BeTrue();
        party.Members[0].Npc.StatBlock!.Name.Should().Be("Bandit Captain");
        party.Members.Should().OnlyContain(m => m.Npc.ArchetypeInCorpus);
    }

    [Fact]
    public async Task Missing_archetype_is_flagged_but_party_still_returns()
    {
        var svc = new NpcGenerationService(EchoSearch(missing: new HashSet<string> { "Spy" }));
        var party = await svc.GeneratePartyAsync("criminal gang", CancellationToken.None);

        party.Members.Should().HaveCount(4);
        party.Members.Single(m => m.Role == "informant").Npc.ArchetypeInCorpus.Should().BeFalse();
        party.Members.Count(m => m.Npc.ArchetypeInCorpus).Should().Be(3);
    }
}
```

- [ ] **Step 2: run → fail** (method missing).
- [ ] **Step 3: implement** `GeneratePartyAsync` in `NpcGenerationService` (inspect the class with Serena first to match style/usings):

```csharp
public async Task<GeneratedNpcParty> GeneratePartyAsync(string theme, CancellationToken ct)
{
    var (templateName, roster) = NpcPartyTemplates.Resolve(theme);
    var members = new List<NpcPartyMember>(roster.Count);
    foreach (var (role, archetype) in roster)
    {
        var npc = await GenerateAsync(concept: role, archetype: archetype, maxCr: null, ct);
        members.Add(new NpcPartyMember(role, npc));
    }
    return new GeneratedNpcParty(theme, templateName, members);
}
```

- [ ] **Step 4: run → pass.** Run FULL `dotnet test` (green). **Step 5: commit** `feat(npc): GeneratePartyAsync grounds a themed NPC ensemble`.

---

### Task 3: generate_npc_party chat tool

**Files:**
- Modify: `Features/Chat/DndChatService.cs` (register the tool next to `generate_npc`)
- Modify: `DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs` (guard surfaces + a delegate test)

**Interfaces — Consumes:** `NpcGenerationService.GeneratePartyAsync` (Task 2).

- [ ] **Step 1: register the tool** in `DndChatService.cs` right after the `generate_npc` registration (authenticated block, ownership-free):

```csharp
toolList.Add(AIFunctionFactory.Create(
    (string theme, CancellationToken toolCt) =>
        npcGenerationService.GeneratePartyAsync(theme, toolCt),
    name: "generate_npc_party",
    description: "Generate a themed CAST of NPCs for a scene from a single theme string (e.g. " +
        "'a Sharn heist crew', 'a temple cult', 'the city watch'). Returns an ENSEMBLE — a leader plus " +
        "supporting members — each anchored to a REAL Monster stat block (CR/HP/abilities + rendered " +
        "block). Compose each member's name, personality, and hook to fit the theme, but take ALL " +
        "mechanical stats from the returned blocks and CITE them (source book); NEVER invent stat " +
        "numbers. If a member says archetypeInCorpus false, drop or replace it. Not tied to any " +
        "campaign or character."));
```

- [ ] **Step 2: guard surfaces (avoid a vacuous guard).** Add `generate_npc_party` to BOTH: (a) the `DndChatServiceTests` "no tool schema exposes a `userId` argument" name-filter list, AND (b) the authenticated-present / unauthenticated-absent presence assertions — mirror exactly how `generate_npc` appears in each (find with `grep -n "generate_npc" DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs`).
- [ ] **Step 3: delegate test.** Following the existing chat-tool test pattern (`client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == "generate_npc_party")` → `InvokeAsync(ToArgs(new { theme = "temple cult" }))`), with the service built over a fake retrieval that grounds each archetype (reuse the `EchoSearch` idea from Task 2, or the service's real construction with a substituted `IEntityRetrievalService`): assert the tool exposes exactly one `theme` string param and no `userId`; assert invoking it returns the cult ensemble (Template "cult", Cult Fanatic leader) with grounded members. If the service isn't directly constructible in that test's harness, assert presence + the single-param/no-userId schema here and cover the ensemble result in Task 2's service test (note the choice).
- [ ] **Step 4: run the new tests → pass. Run FULL `dotnet test` (green, incl. `FullContainerScopeValidationTests`).** **Step 5: commit** `feat(chat): generate_npc_party single-param ownership-free tool`.

---

### Task 4: Live smoke + finish (controller-run)

- [ ] **Step 1:** `docker compose up -d --build app`; wait `/health` 200. Live smoke (Playwright chat, `test`/`test`): ask for a themed cast (e.g. "give me a criminal gang for a Sharn heist"). Confirm qwen3 INVOKES `generate_npc_party` (1 param → should be reliable where 4-param `prep_session` wasn't) and the reply is a leader + supporting cast, each with a real cited stat block, flavored to the theme. **If the chat smoke is flaky** (qwen3 latency / Playwright circuit drop — check `ChatTurns` for a trailing `user` row with no assistant reply), fall back to validating the service/tool layer directly (the tool returns the grounded ensemble).
- [ ] **Step 2:** Capture any durable lesson in `.claude/skills/dev-flow/SKILL.md` (e.g. whether a 1-param party tool invokes reliably where multi-param didn't — evidence for/against the parked model upgrade). Then the finish ceremony: archive the change, refresh the roadmap memory, skill-optimizer pass.
