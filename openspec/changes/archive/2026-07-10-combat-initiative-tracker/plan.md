# Combat Initiative Tracker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a persisted per-campaign combat/initiative tracker and a dedicated table-play page that hosts the dice roller, encounter builder, campaign log, and the tracker together.

**Architecture:** A new `Features/Combat/` vertical slice: two relational tables (`Combat` parent + `Combatant` children) behind a `CombatRepository` (ownership-scoped, `IDbContextFactory`, mirrors `CampaignLogRepository`) and a `CombatService` that composes it with `HeroRepository`, `DiceRoller`, and `CampaignLogRepository` for cross-aggregate work (party/monster drafting, DM-approval-gated HP write-back to hero snapshots, end-of-combat breadcrumb). A new Blazor page `/campaigns/{id}/table` hosts the existing panels (moved off `CampaignDetail`) plus a new `InitiativeTracker` component.

**Tech Stack:** .NET 10, ASP.NET Core, Blazor Server (InteractiveServer), EF Core + Npgsql (PostgreSQL), xunit + FluentAssertions + Testcontainers.PostgreSql + Respawn.

## Global Constraints

- Target framework `net10.0`; nullable enabled; implicit usings; **warnings-as-errors** on every project (a warning fails the build — includes EF collection-comparer warnings, so avoid value-converted mutable collections).
- All package versions are centrally managed (`Directory.Packages.props`); `PackageReference`s are version-less. No new packages are needed.
- `dotnet build` / `dotnet test` MUST be run with `dangerouslyDisableSandbox: true` (Config is git-crypt'd + sandbox-read-denied). Docker must be running for the Postgres Testcontainer.
- Repositories use `IDbContextFactory<AppDbContext>` and create a short-lived context per operation (Blazor-safe). Never inject `AppDbContext` directly.
- Every repository read and command is scoped by `UserId` (and `CampaignId` for campaign-scoped rows); a row not owned by the caller is never returned or mutated (0 rows changed).
- Migrations live in `Migrations/` (namespace `DndMcpAICsharpFun.Migrations`), applied at startup by `WebApplicationExtensions` (`db.Database.MigrateAsync()`). A new-table migration MUST be additive-only.
- `dnd_blocks`/`dnd_entities` Qdrant is NOT a dependency of this feature. The tracker must work with only Postgres + the app up.
- No new HTTP route and no MCP tool — server-side Blazor calls the repository/services directly. **Do NOT touch `DndMcpAICsharpFun.http` or `dnd-mcp-api.insomnia.json`.**
- `DndVersion` lives in `DndMcpAICsharpFun.Domain` with values `Edition2014` / `Edition2024`.

## File Structure

- Create `Features/Combat/Condition.cs` — the fixed 15-value `Condition` enum.
- Create `Features/Combat/Combat.cs` — `Combat` entity + `CombatStatus` enum.
- Create `Features/Combat/Combatant.cs` — `Combatant` entity + `CombatantConditions` JSON helper.
- Create `Features/Combat/CombatantOrder.cs` — pure initiative-ordering comparer.
- Create `Features/Combat/CombatRepository.cs` — ownership-scoped data gateway.
- Create `Features/Combat/CombatService.cs` — cross-aggregate orchestration (drafting + end-combat).
- Modify `Domain/CampaignLogEntry.cs` — add `Combat` to `CampaignLogKind`.
- Modify `Features/Campaigns/CampaignLogPayloads.cs` — add `CombatLogPayload` + `CombatCombatantLog`.
- Modify `Infrastructure/Persistence/AppDbContext.cs` — two `DbSet`s + entity config.
- Create `Migrations/<timestamp>_AddCombats.cs` (+ `.Designer.cs`, snapshot update) — additive migration.
- Modify `Features/Campaigns/CampaignRepository.cs` — add `Combatants`/`Combats` cascade to `DeleteAsync`.
- Modify `Extensions/DatabaseExtensions.cs` — register `CombatRepository`.
- Modify `Extensions/ServiceCollectionExtensions.cs` — add `AddCombat` (registers `CombatService`, pulls `AddDice`).
- Modify `Program.cs` — call `builder.Services.AddCombat()`.
- Modify `CompanionUI/Components/EncounterPanel.razor` — add an `OnBuilt` callback exposing the built encounter.
- Create `CompanionUI/Components/InitiativeTracker.razor` — the tracker UI.
- Create `CompanionUI/Pages/Campaigns/CampaignTable.razor` — the `/campaigns/{id}/table` play page.
- Modify `CompanionUI/Pages/Campaigns/CampaignDetail.razor` — remove the three panels, add the "▶ Run session" link.
- Create tests under `DndMcpAICsharpFun.Tests/Combat/`.

---

### Task 1: Domain entities + Condition enum + conditions JSON helper

**Files:**
- Create: `Features/Combat/Condition.cs`
- Create: `Features/Combat/Combat.cs`
- Create: `Features/Combat/Combatant.cs`
- Test: `DndMcpAICsharpFun.Tests/Combat/CombatantConditionsTests.cs`

**Interfaces:**
- Produces: `enum Condition` (15 values); `enum CombatStatus { Active, Ended }`; `sealed class Combat` (Id, CampaignId, UserId, string Name, DndVersion Edition, CombatStatus Status, int Round=1, int CurrentTurnIndex, DateTime CreatedAt, DateTime? EndedAt); `sealed class Combatant` (Id, CombatId, long? HeroId, string Name, bool IsPlayer, int? InitiativeRoll, int InitiativeModifier, int MaxHp, int CurrentHp, int? Ac, string ConditionsJson, int AddedOrder, `[NotMapped] IReadOnlyList<Condition> Conditions`); `static class CombatantConditions` with `string Serialize(IReadOnlyList<Condition>)` and `IReadOnlyList<Condition> Deserialize(string)`.

- [ ] **Step 1: Write the failing test**

```csharp
using DndMcpAICsharpFun.Features.Combat;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Combat;

public sealed class CombatantConditionsTests
{
    [Fact]
    public void Conditions_round_trip_through_json_as_names()
    {
        var combatant = new Combatant { Name = "Goblin 1", MaxHp = 7, CurrentHp = 7 };
        combatant.Conditions = new[] { Condition.Poisoned, Condition.Prone };

        combatant.ConditionsJson.Should().Contain("Poisoned").And.Contain("Prone");
        combatant.Conditions.Should().Equal(Condition.Poisoned, Condition.Prone);
    }

    [Fact]
    public void Default_combatant_has_no_conditions()
    {
        new Combatant().Conditions.Should().BeEmpty();
    }

    [Fact]
    public void Enum_has_the_fifteen_standard_conditions()
    {
        Enum.GetValues<Condition>().Should().HaveCount(15);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~CombatantConditionsTests` (with `dangerouslyDisableSandbox: true`)
Expected: FAIL — `Condition`/`Combatant` do not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

`Features/Combat/Condition.cs`:

```csharp
namespace DndMcpAICsharpFun.Features.Combat;

/// <summary>
/// The 15 standard D&D conditions. Edition-independent: the 2024 revision changed Exhaustion's
/// mechanics but not the set of conditions, so one enum serves both editions. Tracked as a status
/// label on a combatant; the mechanical effect is not simulated.
/// </summary>
public enum Condition
{
    Blinded,
    Charmed,
    Deafened,
    Frightened,
    Grappled,
    Incapacitated,
    Invisible,
    Paralyzed,
    Petrified,
    Poisoned,
    Prone,
    Restrained,
    Stunned,
    Unconscious,
    Exhaustion,
}
```

`Features/Combat/Combat.cs`:

```csharp
using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Combat;

public enum CombatStatus
{
    Active,
    Ended,
}

/// <summary>
/// A tracked fight for a campaign: the parent aggregate of a set of <see cref="Combatant"/> rows.
/// Campaign- and user-scoped; at most one <see cref="CombatStatus.Active"/> combat per campaign.
/// </summary>
public sealed class Combat
{
    public long Id { get; set; }
    public long CampaignId { get; set; }
    public long UserId { get; set; }
    public string Name { get; set; } = "";
    public DndVersion Edition { get; set; }
    public CombatStatus Status { get; set; }
    public int Round { get; set; } = 1;
    public int CurrentTurnIndex { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? EndedAt { get; set; }
}
```

`Features/Combat/Combatant.cs`:

```csharp
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DndMcpAICsharpFun.Features.Combat;

/// <summary>
/// One participant in a <see cref="Combat"/>. <see cref="HeroId"/> is set only when the combatant
/// was drafted from a campaign hero (enabling DM-approved HP write-back). Conditions persist as a
/// JSON array of enum names in <see cref="ConditionsJson"/>; the <see cref="Conditions"/> helper is
/// not mapped. Storing a scalar JSON string (rather than a value-converted collection) avoids the
/// EF collection-comparer warning that warnings-as-errors would turn into a build failure.
/// </summary>
public sealed class Combatant
{
    public long Id { get; set; }
    public long CombatId { get; set; }
    public long? HeroId { get; set; }
    public string Name { get; set; } = "";
    public bool IsPlayer { get; set; }
    public int? InitiativeRoll { get; set; }
    public int InitiativeModifier { get; set; }
    public int MaxHp { get; set; }
    public int CurrentHp { get; set; }
    public int? Ac { get; set; }
    public string ConditionsJson { get; set; } = "[]";
    public int AddedOrder { get; set; }

    [NotMapped]
    public IReadOnlyList<Condition> Conditions
    {
        get => CombatantConditions.Deserialize(ConditionsJson);
        set => ConditionsJson = CombatantConditions.Serialize(value);
    }
}

/// <summary>Serializes a combatant's condition set as a JSON array of enum names.</summary>
public static class CombatantConditions
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(IReadOnlyList<Condition> conditions) =>
        JsonSerializer.Serialize(conditions, Options);

    public static IReadOnlyList<Condition> Deserialize(string json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<Condition>>(json, Options) ?? [];
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~CombatantConditionsTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add Features/Combat/Condition.cs Features/Combat/Combat.cs Features/Combat/Combatant.cs DndMcpAICsharpFun.Tests/Combat/CombatantConditionsTests.cs
git commit -m "feat(combat): Combat/Combatant entities + Condition enum + conditions JSON helper"
```

---

### Task 2: Pure initiative-ordering comparer

**Files:**
- Create: `Features/Combat/CombatantOrder.cs`
- Test: `DndMcpAICsharpFun.Tests/Combat/CombatantOrderTests.cs`

**Interfaces:**
- Consumes: `Combatant` (Task 1).
- Produces: `static class CombatantOrder` with `IReadOnlyList<Combatant> Sort(IEnumerable<Combatant> combatants)` — orders by `InitiativeRoll` desc (nulls last), then `InitiativeModifier` desc, then player-before-monster, then `AddedOrder` asc.

- [ ] **Step 1: Write the failing test**

```csharp
using DndMcpAICsharpFun.Features.Combat;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Combat;

public sealed class CombatantOrderTests
{
    private static Combatant C(string name, int? init, int mod = 0, bool player = false, int added = 0) =>
        new() { Name = name, InitiativeRoll = init, InitiativeModifier = mod, IsPlayer = player, AddedOrder = added };

    [Fact]
    public void Sorts_by_initiative_descending()
    {
        var ordered = CombatantOrder.Sort(new[] { C("low", 8), C("high", 20), C("mid", 14) });
        ordered.Select(c => c.Name).Should().Equal("high", "mid", "low");
    }

    [Fact]
    public void Breaks_ties_by_modifier_then_player_then_added_order()
    {
        var monsterHiMod = C("monsterHiMod", 15, mod: 4);
        var playerLoMod = C("playerLoMod", 15, mod: 2, player: true);
        var monsterLoModFirst = C("monsterLoModFirst", 15, mod: 2, added: 1);
        var monsterLoModSecond = C("monsterLoModSecond", 15, mod: 2, added: 2);

        var ordered = CombatantOrder.Sort(new[] { monsterLoModSecond, playerLoMod, monsterHiMod, monsterLoModFirst });

        ordered.Select(c => c.Name).Should().Equal(
            "monsterHiMod",        // highest modifier wins the tie
            "playerLoMod",         // same modifier: player before monster
            "monsterLoModFirst",   // same modifier, both monsters: lower AddedOrder first
            "monsterLoModSecond");
    }

    [Fact]
    public void Combatants_without_initiative_sort_last()
    {
        var ordered = CombatantOrder.Sort(new[] { C("noInit", null), C("hasInit", 3) });
        ordered.Select(c => c.Name).Should().Equal("hasInit", "noInit");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~CombatantOrderTests`
Expected: FAIL — `CombatantOrder` does not exist.

- [ ] **Step 3: Write minimal implementation**

`Features/Combat/CombatantOrder.cs`:

```csharp
namespace DndMcpAICsharpFun.Features.Combat;

/// <summary>
/// Deterministic initiative ordering shared by the repository (turn advancement) and the UI, so the
/// displayed order and the turn marker never disagree. Order: highest <see cref="Combatant.InitiativeRoll"/>
/// first (an unset roll sorts last), then highest <see cref="Combatant.InitiativeModifier"/>, then
/// players before monsters, then lowest <see cref="Combatant.AddedOrder"/> (stable insertion).
/// </summary>
public static class CombatantOrder
{
    public static IReadOnlyList<Combatant> Sort(IEnumerable<Combatant> combatants) =>
        combatants
            .OrderByDescending(c => c.InitiativeRoll.HasValue)
            .ThenByDescending(c => c.InitiativeRoll ?? 0)
            .ThenByDescending(c => c.InitiativeModifier)
            .ThenByDescending(c => c.IsPlayer)
            .ThenBy(c => c.AddedOrder)
            .ThenBy(c => c.Id)
            .ToList();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~CombatantOrderTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add Features/Combat/CombatantOrder.cs DndMcpAICsharpFun.Tests/Combat/CombatantOrderTests.cs
git commit -m "feat(combat): deterministic initiative-ordering comparer"
```

---

### Task 3: Combat log kind + payload

**Files:**
- Modify: `Domain/CampaignLogEntry.cs`
- Modify: `Features/Campaigns/CampaignLogPayloads.cs`
- Test: `DndMcpAICsharpFun.Tests/Combat/CombatLogPayloadTests.cs`

**Interfaces:**
- Produces: `CampaignLogKind.Combat`; `sealed record CombatLogPayload(string CombatName, string Edition, int Rounds, IReadOnlyList<CombatCombatantLog> Combatants)`; `sealed record CombatCombatantLog(string Name, bool IsPlayer, int? InitiativeRoll, int CurrentHp, int MaxHp)`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Features.Campaigns;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Combat;

public sealed class CombatLogPayloadTests
{
    [Fact]
    public void Combat_payload_round_trips_through_json()
    {
        var payload = new CombatLogPayload(
            "Goblin Ambush",
            "Edition2014",
            3,
            new[] { new CombatCombatantLog("Aria", true, 17, 5, 12), new CombatCombatantLog("Goblin 1", false, 14, 0, 7) });

        var json = JsonSerializer.Serialize(payload);
        var back = JsonSerializer.Deserialize<CombatLogPayload>(json);

        back.Should().NotBeNull();
        back!.CombatName.Should().Be("Goblin Ambush");
        back.Rounds.Should().Be(3);
        back.Combatants.Should().HaveCount(2);
        back.Combatants[0].Name.Should().Be("Aria");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~CombatLogPayloadTests`
Expected: FAIL — `CombatLogPayload` does not exist.

- [ ] **Step 3: Write minimal implementation**

In `Domain/CampaignLogEntry.cs`, extend the enum:

```csharp
public enum CampaignLogKind
{
    Roll,
    Encounter,
    Combat,
}
```

Append to `Features/Campaigns/CampaignLogPayloads.cs`:

```csharp
public sealed record CombatCombatantLog(string Name, bool IsPlayer, int? InitiativeRoll, int CurrentHp, int MaxHp);

public sealed record CombatLogPayload(
    string CombatName,
    string Edition,
    int Rounds,
    IReadOnlyList<CombatCombatantLog> Combatants);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~CombatLogPayloadTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Domain/CampaignLogEntry.cs Features/Campaigns/CampaignLogPayloads.cs DndMcpAICsharpFun.Tests/Combat/CombatLogPayloadTests.cs
git commit -m "feat(combat): Combat campaign-log kind + payload record"
```

---

### Task 4: EF wiring + additive migration

**Files:**
- Modify: `Infrastructure/Persistence/AppDbContext.cs`
- Create: `Migrations/<timestamp>_AddCombats.cs` (+ `.Designer.cs`, snapshot update — generated)

**Interfaces:**
- Consumes: `Combat`, `Combatant` (Task 1).
- Produces: `DbSet<Combat> Combats`, `DbSet<Combatant> Combatants` on `AppDbContext`.

- [ ] **Step 1: Add the DbSets and entity configuration**

In `Infrastructure/Persistence/AppDbContext.cs`, add after line 28 (`CampaignLogEntries`):

```csharp
    public DbSet<Combat> Combats => Set<Combat>();
    public DbSet<Combatant> Combatants => Set<Combatant>();
```

Add the `using` at the top: `using DndMcpAICsharpFun.Features.Combat;`

In `OnModelCreating`, after the `CampaignLogEntry` block, add:

```csharp
        modelBuilder.Entity<Combat>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Edition).HasConversion<string>();
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.Name).IsRequired();
            e.HasIndex(x => new { x.CampaignId, x.UserId, x.Status });
        });

        modelBuilder.Entity<Combatant>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.ConditionsJson).HasColumnType("text");
            e.Ignore(x => x.Conditions);
            e.HasIndex(x => x.CombatId);
            e.HasOne<Combat>()
                .WithMany()
                .HasForeignKey(x => x.CombatId)
                .OnDelete(DeleteBehavior.Cascade);
        });
```

- [ ] **Step 2: Generate the migration**

Run (with `dangerouslyDisableSandbox: true`):

```bash
dotnet ef migrations add AddCombats --project DndMcpAICsharpFun.csproj --output-dir Migrations
```

Expected: creates `Migrations/<timestamp>_AddCombats.cs`, `.Designer.cs`, and updates `Migrations/AppDbContextModelSnapshot.cs`.

- [ ] **Step 3: Verify the migration is additive-only**

Open the generated `Migrations/<timestamp>_AddCombats.cs`. Verify:
- `Up()` contains ONLY `CreateTable("Combats", …)`, `CreateTable("Combatants", …)`, `CreateIndex("IX_Combats_CampaignId_UserId_Status", …)`, `CreateIndex("IX_Combatants_CombatId", …)`, and the FK `Combatants → Combats` with `onDelete: ReferentialAction.Cascade`.
- `Down()` drops ONLY `Combatants` then `Combats`.
- No `AlterColumn`/`DropColumn`/`AddColumn`/`RenameTable` touching any other table (if any appear, the model snapshot had drifted — STOP and investigate before continuing).

Run: `git diff --stat` and confirm the only changed migration files are the two new ones + `AppDbContextModelSnapshot.cs`.

- [ ] **Step 4: Build to confirm it compiles**

Run: `dotnet build` (with `dangerouslyDisableSandbox: true`)
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add Infrastructure/Persistence/AppDbContext.cs Migrations/
git commit -m "feat(combat): AppDbContext DbSets + additive AddCombats migration"
```

---

### Task 5: CombatRepository — combat lifecycle

**Files:**
- Create: `Features/Combat/CombatRepository.cs`
- Test: `DndMcpAICsharpFun.Tests/Combat/CombatRepositoryLifecycleTests.cs`

**Interfaces:**
- Consumes: `IDbContextFactory<AppDbContext>`; `Combat`, `Combatant`, `CombatStatus` (Task 1); `DndVersion`.
- Produces: `sealed class CombatRepository(IDbContextFactory<AppDbContext> dbf)` with:
  - `Task<long?> StartAsync(long userId, long campaignId, string name, DndVersion edition)` — returns new combat id, or `null` if an active combat already exists for the campaign.
  - `Task<Combat?> GetActiveAsync(long campaignId, long userId)`
  - `Task<IReadOnlyList<Combatant>> GetCombatantsAsync(long combatId, long campaignId, long userId)`
  - `Task<Combat?> GetByIdAsync(long combatId, long campaignId, long userId)`
  - `Task<IReadOnlyList<Combat>> GetHistoryAsync(long campaignId, long userId)`
  - `Task EndAsync(long combatId, long campaignId, long userId)`

- [ ] **Step 1: Write the failing test**

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Auth;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Combat;
using DndMcpAICsharpFun.Tests.Persistence;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Combat;

[Collection("postgres")]
public sealed class CombatRepositoryLifecycleTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly CombatRepository _repo = new(new TestDb(pg));
    private readonly CampaignRepository _campaigns = new(new TestDb(pg));
    private readonly UserRepository _users = new(new TestDb(pg));

    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(long userId, long campaignId)> SeedAsync()
    {
        var userId = await _users.CreateAsync("dm", "hash");
        var campaignId = await _campaigns.CreateAsync(userId, "Camp", "");
        return (userId, campaignId);
    }

    [Fact]
    public async Task Start_creates_active_combat_at_round_one()
    {
        var (userId, campaignId) = await SeedAsync();

        var id = await _repo.StartAsync(userId, campaignId, "Goblins", DndVersion.Edition2014);

        id.Should().NotBeNull();
        var active = await _repo.GetActiveAsync(campaignId, userId);
        active.Should().NotBeNull();
        active!.Id.Should().Be(id!.Value);
        active.Status.Should().Be(CombatStatus.Active);
        active.Round.Should().Be(1);
        active.Name.Should().Be("Goblins");
    }

    [Fact]
    public async Task Start_rejects_a_second_active_combat()
    {
        var (userId, campaignId) = await SeedAsync();
        await _repo.StartAsync(userId, campaignId, "First", DndVersion.Edition2014);

        var second = await _repo.StartAsync(userId, campaignId, "Second", DndVersion.Edition2014);

        second.Should().BeNull();
        (await _repo.GetHistoryAsync(campaignId, userId)).Should().BeEmpty(); // none ended
    }

    [Fact]
    public async Task End_moves_combat_to_history_and_clears_active()
    {
        var (userId, campaignId) = await SeedAsync();
        var id = await _repo.StartAsync(userId, campaignId, "Fight", DndVersion.Edition2014);

        await _repo.EndAsync(id!.Value, campaignId, userId);

        (await _repo.GetActiveAsync(campaignId, userId)).Should().BeNull();
        var history = await _repo.GetHistoryAsync(campaignId, userId);
        history.Should().ContainSingle(c => c.Id == id.Value && c.Status == CombatStatus.Ended);
    }

    [Fact]
    public async Task Start_allows_a_new_combat_after_the_previous_one_ended()
    {
        var (userId, campaignId) = await SeedAsync();
        var first = await _repo.StartAsync(userId, campaignId, "First", DndVersion.Edition2014);
        await _repo.EndAsync(first!.Value, campaignId, userId);

        var second = await _repo.StartAsync(userId, campaignId, "Second", DndVersion.Edition2014);

        second.Should().NotBeNull();
        (await _repo.GetActiveAsync(campaignId, userId))!.Name.Should().Be("Second");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~CombatRepositoryLifecycleTests` (Docker up, `dangerouslyDisableSandbox: true`)
Expected: FAIL — `CombatRepository` does not exist.

- [ ] **Step 3: Write minimal implementation**

`Features/Combat/CombatRepository.cs`:

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace DndMcpAICsharpFun.Features.Combat;

/// <summary>
/// Ownership-scoped persistence for combats and their combatants. Every read and command is scoped
/// by <c>CampaignId</c> and <c>UserId</c> (combat-targeted commands also by the combat <c>Id</c>),
/// so one user can never see or mutate another's combat, even within a shared campaign. Uses
/// short-lived contexts from <see cref="IDbContextFactory{AppDbContext}"/> (Blazor-safe).
/// </summary>
public sealed class CombatRepository(IDbContextFactory<AppDbContext> dbf)
{
    public async Task<long?> StartAsync(long userId, long campaignId, string name, DndVersion edition)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var hasActive = await db.Combats
            .AnyAsync(c => c.CampaignId == campaignId && c.UserId == userId && c.Status == CombatStatus.Active);
        if (hasActive) return null;

        var combat = new Combat
        {
            CampaignId = campaignId,
            UserId = userId,
            Name = name,
            Edition = edition,
            Status = CombatStatus.Active,
            Round = 1,
            CurrentTurnIndex = 0,
            CreatedAt = DateTime.UtcNow,
        };
        db.Combats.Add(combat);
        await db.SaveChangesAsync();
        return combat.Id;
    }

    public async Task<Combat?> GetActiveAsync(long campaignId, long userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        return await db.Combats.AsNoTracking()
            .Where(c => c.CampaignId == campaignId && c.UserId == userId && c.Status == CombatStatus.Active)
            .OrderByDescending(c => c.Id)
            .FirstOrDefaultAsync();
    }

    public async Task<Combat?> GetByIdAsync(long combatId, long campaignId, long userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        return await db.Combats.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == combatId && c.CampaignId == campaignId && c.UserId == userId);
    }

    public async Task<IReadOnlyList<Combatant>> GetCombatantsAsync(long combatId, long campaignId, long userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var owns = await db.Combats
            .AnyAsync(c => c.Id == combatId && c.CampaignId == campaignId && c.UserId == userId);
        if (!owns) return [];
        return await db.Combatants.AsNoTracking()
            .Where(x => x.CombatId == combatId)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Combat>> GetHistoryAsync(long campaignId, long userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        return await db.Combats.AsNoTracking()
            .Where(c => c.CampaignId == campaignId && c.UserId == userId && c.Status == CombatStatus.Ended)
            .OrderByDescending(c => c.EndedAt)
            .ThenByDescending(c => c.Id)
            .ToListAsync();
    }

    public async Task EndAsync(long combatId, long campaignId, long userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        await db.Combats
            .Where(c => c.Id == combatId && c.CampaignId == campaignId && c.UserId == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, CombatStatus.Ended)
                .SetProperty(c => c.EndedAt, DateTime.UtcNow));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~CombatRepositoryLifecycleTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add Features/Combat/CombatRepository.cs DndMcpAICsharpFun.Tests/Combat/CombatRepositoryLifecycleTests.cs
git commit -m "feat(combat): CombatRepository lifecycle (start/active/history/end, one-active guard)"
```

---

### Task 6: CombatRepository — combatant commands, turn advancement, ownership + cascade

**Files:**
- Modify: `Features/Combat/CombatRepository.cs`
- Test: `DndMcpAICsharpFun.Tests/Combat/CombatRepositoryCombatantTests.cs`

**Interfaces:**
- Consumes: `CombatantOrder` (Task 2).
- Produces (added to `CombatRepository`):
  - `Task<long> AddCombatantAsync(long combatId, long campaignId, long userId, Combatant combatant)` — sets `AddedOrder` to the current combatant count; returns new id (0 and no insert if not owned).
  - `Task UpdateCombatantAsync(long combatantId, long combatId, long campaignId, long userId, int currentHp, int? initiativeRoll, int initiativeModifier, IReadOnlyList<Condition> conditions)`
  - `Task RemoveCombatantAsync(long combatantId, long combatId, long campaignId, long userId)`
  - `Task AdvanceTurnAsync(long combatId, long campaignId, long userId)` — orders via `CombatantOrder`, advances index, wraps + increments `Round`.
  - `Task DeleteAsync(long combatId, long campaignId, long userId)`

- [ ] **Step 1: Write the failing test**

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Auth;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Combat;
using DndMcpAICsharpFun.Tests.Persistence;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Combat;

[Collection("postgres")]
public sealed class CombatRepositoryCombatantTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly CombatRepository _repo = new(new TestDb(pg));
    private readonly CampaignRepository _campaigns = new(new TestDb(pg));
    private readonly UserRepository _users = new(new TestDb(pg));

    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(long userId, long campaignId, long combatId)> SeedCombatAsync()
    {
        var userId = await _users.CreateAsync("dm", "hash");
        var campaignId = await _campaigns.CreateAsync(userId, "Camp", "");
        var combatId = (await _repo.StartAsync(userId, campaignId, "Fight", DndVersion.Edition2014))!.Value;
        return (userId, campaignId, combatId);
    }

    private static Combatant Monster(string name, int init) =>
        new() { Name = name, IsPlayer = false, InitiativeRoll = init, MaxHp = 7, CurrentHp = 7 };

    [Fact]
    public async Task Add_update_and_read_combatant_persists_hp_and_conditions()
    {
        var (userId, campaignId, combatId) = await SeedCombatAsync();
        var id = await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("Goblin", 12));

        await _repo.UpdateCombatantAsync(id, combatId, campaignId, userId,
            currentHp: 2, initiativeRoll: 15, initiativeModifier: 2,
            conditions: new[] { Condition.Poisoned, Condition.Prone });

        var combatants = await _repo.GetCombatantsAsync(combatId, campaignId, userId);
        var goblin = combatants.Single();
        goblin.CurrentHp.Should().Be(2);
        goblin.InitiativeRoll.Should().Be(15);
        goblin.Conditions.Should().Equal(Condition.Poisoned, Condition.Prone);
    }

    [Fact]
    public async Task Advance_turn_wraps_and_increments_round()
    {
        var (userId, campaignId, combatId) = await SeedCombatAsync();
        await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("A", 20));
        await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("B", 10));

        await _repo.AdvanceTurnAsync(combatId, campaignId, userId); // 0 -> 1
        var mid = await _repo.GetByIdAsync(combatId, campaignId, userId);
        mid!.CurrentTurnIndex.Should().Be(1);
        mid.Round.Should().Be(1);

        await _repo.AdvanceTurnAsync(combatId, campaignId, userId); // 1 -> wrap to 0, round 2
        var wrapped = await _repo.GetByIdAsync(combatId, campaignId, userId);
        wrapped!.CurrentTurnIndex.Should().Be(0);
        wrapped.Round.Should().Be(2);
    }

    [Fact]
    public async Task Remove_combatant_deletes_only_that_row()
    {
        var (userId, campaignId, combatId) = await SeedCombatAsync();
        var a = await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("A", 20));
        await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("B", 10));

        await _repo.RemoveCombatantAsync(a, combatId, campaignId, userId);

        (await _repo.GetCombatantsAsync(combatId, campaignId, userId)).Should().ContainSingle(c => c.Name == "B");
    }

    [Fact]
    public async Task Deleting_a_combat_removes_its_combatants()
    {
        var (userId, campaignId, combatId) = await SeedCombatAsync();
        await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("A", 20));

        await _repo.DeleteAsync(combatId, campaignId, userId);

        (await _repo.GetByIdAsync(combatId, campaignId, userId)).Should().BeNull();
        (await _repo.GetCombatantsAsync(combatId, campaignId, userId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Another_users_combat_cannot_be_mutated()
    {
        var (userId, campaignId, combatId) = await SeedCombatAsync();
        var combatantId = await _repo.AddCombatantAsync(combatId, campaignId, userId, Monster("A", 20));
        var intruder = await _users.CreateAsync("intruder", "hash");

        // Wrong user on every command → no effect.
        (await _repo.AddCombatantAsync(combatId, campaignId, intruder, Monster("X", 1))).Should().Be(0);
        await _repo.UpdateCombatantAsync(combatantId, combatId, campaignId, intruder, 0, 1, 0, Array.Empty<Condition>());
        await _repo.AdvanceTurnAsync(combatId, campaignId, intruder);
        await _repo.EndAsync(combatId, campaignId, intruder);
        await _repo.RemoveCombatantAsync(combatantId, combatId, campaignId, intruder);
        await _repo.DeleteAsync(combatId, campaignId, intruder);

        var combat = await _repo.GetByIdAsync(combatId, campaignId, userId);
        combat.Should().NotBeNull();
        combat!.Status.Should().Be(CombatStatus.Active);
        combat.CurrentTurnIndex.Should().Be(0);
        var combatants = await _repo.GetCombatantsAsync(combatId, campaignId, userId);
        combatants.Should().ContainSingle(c => c.Id == combatantId && c.CurrentHp == 7);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~CombatRepositoryCombatantTests`
Expected: FAIL — the new methods do not exist.

- [ ] **Step 3: Write minimal implementation**

Add to `Features/Combat/CombatRepository.cs` (inside the class). Note every command re-checks ownership through the parent `Combat` so an intruder changes 0 rows:

```csharp
    public async Task<long> AddCombatantAsync(long combatId, long campaignId, long userId, Combatant combatant)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var owns = await db.Combats
            .AnyAsync(c => c.Id == combatId && c.CampaignId == campaignId && c.UserId == userId);
        if (!owns) return 0;

        combatant.CombatId = combatId;
        combatant.AddedOrder = await db.Combatants.CountAsync(x => x.CombatId == combatId);
        db.Combatants.Add(combatant);
        await db.SaveChangesAsync();
        return combatant.Id;
    }

    public async Task UpdateCombatantAsync(
        long combatantId, long combatId, long campaignId, long userId,
        int currentHp, int? initiativeRoll, int initiativeModifier, IReadOnlyList<Condition> conditions)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var owns = await db.Combats
            .AnyAsync(c => c.Id == combatId && c.CampaignId == campaignId && c.UserId == userId);
        if (!owns) return;

        var conditionsJson = CombatantConditions.Serialize(conditions);
        await db.Combatants
            .Where(x => x.Id == combatantId && x.CombatId == combatId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CurrentHp, currentHp)
                .SetProperty(x => x.InitiativeRoll, initiativeRoll)
                .SetProperty(x => x.InitiativeModifier, initiativeModifier)
                .SetProperty(x => x.ConditionsJson, conditionsJson));
    }

    public async Task RemoveCombatantAsync(long combatantId, long combatId, long campaignId, long userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var owns = await db.Combats
            .AnyAsync(c => c.Id == combatId && c.CampaignId == campaignId && c.UserId == userId);
        if (!owns) return;

        await db.Combatants
            .Where(x => x.Id == combatantId && x.CombatId == combatId)
            .ExecuteDeleteAsync();
    }

    public async Task AdvanceTurnAsync(long combatId, long campaignId, long userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var combat = await db.Combats
            .FirstOrDefaultAsync(c => c.Id == combatId && c.CampaignId == campaignId && c.UserId == userId);
        if (combat is null) return;

        var count = await db.Combatants.CountAsync(x => x.CombatId == combatId);
        if (count == 0) return;

        var next = combat.CurrentTurnIndex + 1;
        if (next >= count)
        {
            combat.CurrentTurnIndex = 0;
            combat.Round += 1;
        }
        else
        {
            combat.CurrentTurnIndex = next;
        }
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(long combatId, long campaignId, long userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        // Combatants cascade at the DB level via the FK, but the parent delete is ownership-scoped,
        // so an intruder's DeleteAsync removes nothing.
        await db.Combats
            .Where(c => c.Id == combatId && c.CampaignId == campaignId && c.UserId == userId)
            .ExecuteDeleteAsync();
    }
```

Add `using DndMcpAICsharpFun.Features.Combat;` is unnecessary (same namespace). Ensure `CombatantOrder` is not needed here — turn advancement uses the stored `AddedOrder`-based count and index; the display order is applied in the UI via `CombatantOrder`. (The index is over the count; the UI sorts with `CombatantOrder` and highlights index `CurrentTurnIndex` of that sorted list — see Task 12.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~CombatRepositoryCombatantTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add Features/Combat/CombatRepository.cs DndMcpAICsharpFun.Tests/Combat/CombatRepositoryCombatantTests.cs
git commit -m "feat(combat): combatant commands + turn advancement + ownership-scoped cascade delete"
```

---

### Task 7: Campaign delete cascades combats + combatants

**Files:**
- Modify: `Features/Campaigns/CampaignRepository.cs`
- Test: `DndMcpAICsharpFun.Tests/Combat/CampaignDeleteCascadesCombatsTests.cs`

**Interfaces:**
- Consumes: `CombatRepository` (Tasks 5–6), `CampaignRepository.DeleteAsync`.

- [ ] **Step 1: Write the failing test**

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Auth;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Combat;
using DndMcpAICsharpFun.Tests.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace DndMcpAICsharpFun.Tests.Combat;

[Collection("postgres")]
public sealed class CampaignDeleteCascadesCombatsTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly CombatRepository _combats = new(new TestDb(pg));
    private readonly CampaignRepository _campaigns = new(new TestDb(pg));
    private readonly UserRepository _users = new(new TestDb(pg));
    private readonly TestDb _db = new(pg);

    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Deleting_a_campaign_removes_its_combats_and_combatants()
    {
        var userId = await _users.CreateAsync("dm", "hash");
        var campaignId = await _campaigns.CreateAsync(userId, "Camp", "");
        var combatId = (await _combats.StartAsync(userId, campaignId, "Fight", DndVersion.Edition2014))!.Value;
        await _combats.AddCombatantAsync(combatId, campaignId, userId,
            new Combatant { Name = "Goblin", MaxHp = 7, CurrentHp = 7 });

        await _campaigns.DeleteAsync(campaignId, userId);

        await using var db = _db.CreateDbContext();
        (await db.Combats.AnyAsync(c => c.CampaignId == campaignId)).Should().BeFalse();
        (await db.Combatants.AnyAsync(x => x.CombatId == combatId)).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~CampaignDeleteCascadesCombatsTests`
Expected: FAIL — combats/combatants remain (or an FK error), because `DeleteAsync` does not yet remove them.

- [ ] **Step 3: Write minimal implementation**

In `Features/Campaigns/CampaignRepository.cs` `DeleteAsync`, inside the transaction, add the two lines BEFORE the `Campaigns` delete (after the `CampaignLogEntries` line):

```csharp
            var combatIds = await db.Combats.Where(c => c.CampaignId == id).Select(c => c.Id).ToListAsync();
            await db.Combatants.Where(x => combatIds.Contains(x.CombatId)).ExecuteDeleteAsync();
            await db.Combats.Where(c => c.CampaignId == id).ExecuteDeleteAsync();
```

(The explicit `Combatants` delete mirrors the `HeroSnapshots`-before-`Heroes` ordering and is safe even though the FK would also cascade.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~CampaignDeleteCascadesCombatsTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Features/Campaigns/CampaignRepository.cs DndMcpAICsharpFun.Tests/Combat/CampaignDeleteCascadesCombatsTests.cs
git commit -m "feat(combat): cascade-delete combats+combatants when a campaign is deleted"
```

---

### Task 8: CombatService — party/monster/manual drafting

**Files:**
- Create: `Features/Combat/CombatService.cs`
- Test: `DndMcpAICsharpFun.Tests/Combat/CombatServiceDraftingTests.cs`

**Interfaces:**
- Consumes: `CombatRepository` (Tasks 5–6); `HeroRepository.GetByCampaignAsync`; `DiceRoller.Roll`; `IRandomSource`; `Features.Encounters.MonsterRef`.
- Produces: `sealed class CombatService(CombatRepository combats, HeroRepository heroes, CampaignLogRepository log, DiceRoller roller)` with:
  - `Task DraftPartyAsync(long combatId, long campaignId, long userId)` — adds each campaign hero as a player combatant (name/HP/AC from latest sheet, HeroId set, InitiativeRoll null).
  - `Task DraftMonstersAsync(long combatId, long campaignId, long userId, IReadOnlyList<MonsterRef> monsters)` — adds each monster as an auto-rolled combatant.
  - `Task AddManualAsync(long combatId, long campaignId, long userId, string name, int maxHp, int? ac, bool isPlayer, int initiativeModifier)` — player-style (init null) or monster-style (auto-rolled).
  - `int RollInitiative(int modifier)` — `d20 + modifier` via the injected roller (exposed for the UI's re-roll button; deterministic under a seeded `IRandomSource`).

- [ ] **Step 1: Write the failing test**

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Auth;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Combat;
using DndMcpAICsharpFun.Features.Dice;
using DndMcpAICsharpFun.Features.Encounters;
using DndMcpAICsharpFun.Tests.Persistence;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Combat;

/// <summary>A deterministic RNG that always returns the low end of the range (d20 → 1).</summary>
file sealed class MinRandomSource : IRandomSource
{
    public int Next(int minInclusive, int maxExclusive) => minInclusive;
}

[Collection("postgres")]
public sealed class CombatServiceDraftingTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly CombatRepository _combats = new(new TestDb(pg));
    private readonly HeroRepository _heroes = new(new TestDb(pg));
    private readonly CampaignLogRepository _log = new(new TestDb(pg));
    private readonly CampaignRepository _campaigns = new(new TestDb(pg));
    private readonly UserRepository _users = new(new TestDb(pg));

    private CombatService NewService() =>
        new(_combats, _heroes, _log, new DiceRoller(new MinRandomSource()));

    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Draft_party_creates_player_combatants_from_hero_sheets()
    {
        var userId = await _users.CreateAsync("dm", "hash");
        var campaignId = await _campaigns.CreateAsync(userId, "Camp", "");
        var heroId = await _heroes.CreateAsync(campaignId, "Aria");
        await _heroes.SaveSnapshotAsync(heroId, 1, "S1",
            new CharacterSheet { MaxHitPoints = 12, CurrentHitPoints = 9, ArmorClass = 15 });
        var combatId = (await _combats.StartAsync(userId, campaignId, "Fight", DndVersion.Edition2014))!.Value;

        await NewService().DraftPartyAsync(combatId, campaignId, userId);

        var combatants = await _combats.GetCombatantsAsync(combatId, campaignId, userId);
        var aria = combatants.Single();
        aria.IsPlayer.Should().BeTrue();
        aria.HeroId.Should().Be(heroId);
        aria.Name.Should().Be("Aria");
        aria.MaxHp.Should().Be(12);
        aria.CurrentHp.Should().Be(9);
        aria.Ac.Should().Be(15);
        aria.InitiativeRoll.Should().BeNull();
    }

    [Fact]
    public async Task Draft_monsters_auto_rolls_initiative_over_the_injected_rng()
    {
        var userId = await _users.CreateAsync("dm", "hash");
        var campaignId = await _campaigns.CreateAsync(userId, "Camp", "");
        var combatId = (await _combats.StartAsync(userId, campaignId, "Fight", DndVersion.Edition2014))!.Value;

        await NewService().DraftMonstersAsync(combatId, campaignId, userId,
            new[] { new MonsterRef("mm.monster.goblin", "Goblin", 0.25, 50) });

        var goblin = (await _combats.GetCombatantsAsync(combatId, campaignId, userId)).Single();
        goblin.IsPlayer.Should().BeFalse();
        goblin.InitiativeRoll.Should().Be(1); // MinRandomSource → d20 = 1, + modifier 0
        goblin.Name.Should().Be("Goblin");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~CombatServiceDraftingTests`
Expected: FAIL — `CombatService` does not exist.

- [ ] **Step 3: Write minimal implementation**

`Features/Combat/CombatService.cs`:

```csharp
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Dice;
using DndMcpAICsharpFun.Features.Encounters;

namespace DndMcpAICsharpFun.Features.Combat;

/// <summary>
/// Cross-aggregate orchestration for combats: drafting combatants from the party, a built encounter,
/// or a manual entry, and ending a combat (DM-approved HP write-back + campaign-log breadcrumb).
/// The thin data gateway is <see cref="CombatRepository"/>; this composes it with the hero, log, and
/// dice services that a repository should not reach into.
/// </summary>
public sealed class CombatService(
    CombatRepository combats,
    HeroRepository heroes,
    CampaignLogRepository log,
    DiceRoller roller)
{
    public int RollInitiative(int modifier) =>
        roller.Roll(new DiceExpression(1, 20, modifier, RollMode.Normal)).Total;

    public async Task DraftPartyAsync(long combatId, long campaignId, long userId)
    {
        var party = await heroes.GetByCampaignAsync(campaignId);
        foreach (var hero in party)
        {
            var sheet = hero.LatestSnapshot?.Sheet;
            await combats.AddCombatantAsync(combatId, campaignId, userId, new Combatant
            {
                HeroId = hero.Id,
                Name = hero.Name,
                IsPlayer = true,
                InitiativeRoll = null,
                MaxHp = sheet?.MaxHitPoints ?? 0,
                CurrentHp = sheet?.CurrentHitPoints ?? 0,
                Ac = sheet?.ArmorClass,
            });
        }
    }

    public async Task DraftMonstersAsync(
        long combatId, long campaignId, long userId, IReadOnlyList<MonsterRef> monsters)
    {
        foreach (var monster in monsters)
        {
            await combats.AddCombatantAsync(combatId, campaignId, userId, new Combatant
            {
                Name = monster.Name,
                IsPlayer = false,
                InitiativeModifier = 0,
                InitiativeRoll = RollInitiative(0),
                MaxHp = 0,
                CurrentHp = 0,
            });
        }
    }

    public async Task AddManualAsync(
        long combatId, long campaignId, long userId,
        string name, int maxHp, int? ac, bool isPlayer, int initiativeModifier)
    {
        await combats.AddCombatantAsync(combatId, campaignId, userId, new Combatant
        {
            Name = name,
            IsPlayer = isPlayer,
            InitiativeModifier = initiativeModifier,
            InitiativeRoll = isPlayer ? null : RollInitiative(initiativeModifier),
            MaxHp = maxHp,
            CurrentHp = maxHp,
            Ac = ac,
        });
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~CombatServiceDraftingTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add Features/Combat/CombatService.cs DndMcpAICsharpFun.Tests/Combat/CombatServiceDraftingTests.cs
git commit -m "feat(combat): CombatService party/monster/manual drafting with auto-rolled monster initiative"
```

---

### Task 9: CombatService.EndCombatAsync — approval write-back + breadcrumb

**Files:**
- Modify: `Features/Combat/CombatService.cs`
- Test: `DndMcpAICsharpFun.Tests/Combat/CombatServiceEndTests.cs`

**Interfaces:**
- Produces (added to `CombatService`): `Task EndCombatAsync(long combatId, long campaignId, long userId, IReadOnlyDictionary<long, int> approvedHpByCombatantId)` — for each player combatant whose `HeroId` is set, append a new `HeroSnapshot` cloning the latest sheet with `CurrentHitPoints` = approved value (from the map, falling back to the combatant's current `CurrentHp`); then `EndAsync`; then persist a `Combat`-kind `CampaignLogEntry` breadcrumb.
- Add to `CampaignLogRepository`: `Task<long> AddCombatAsync(long userId, long campaignId, CombatLogPayload payload, string? label)`.

- [ ] **Step 1: Write the failing test**

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Auth;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Combat;
using DndMcpAICsharpFun.Features.Dice;
using DndMcpAICsharpFun.Tests.Persistence;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Combat;

[Collection("postgres")]
public sealed class CombatServiceEndTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly CombatRepository _combats = new(new TestDb(pg));
    private readonly HeroRepository _heroes = new(new TestDb(pg));
    private readonly CampaignLogRepository _log = new(new TestDb(pg));
    private readonly CampaignRepository _campaigns = new(new TestDb(pg));
    private readonly UserRepository _users = new(new TestDb(pg));

    private CombatService NewService() =>
        new(_combats, _heroes, _log, new DiceRoller(new SystemRandomSource()));

    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task EndCombat_writes_back_approved_hp_and_drops_breadcrumb()
    {
        var userId = await _users.CreateAsync("dm", "hash");
        var campaignId = await _campaigns.CreateAsync(userId, "Camp", "");
        var heroId = await _heroes.CreateAsync(campaignId, "Aria");
        await _heroes.SaveSnapshotAsync(heroId, 1, "S1",
            new CharacterSheet { MaxHitPoints = 12, CurrentHitPoints = 12, ArmorClass = 15 });
        var combatId = (await _combats.StartAsync(userId, campaignId, "Goblin Ambush", DndVersion.Edition2014))!.Value;
        await NewService().DraftPartyAsync(combatId, campaignId, userId);
        var aria = (await _combats.GetCombatantsAsync(combatId, campaignId, userId)).Single();

        await NewService().EndCombatAsync(combatId, campaignId, userId,
            new Dictionary<long, int> { [aria.Id] = 4 });

        // A NEW snapshot is appended with the approved HP; the original snapshot is preserved.
        var snapshots = await _heroes.GetSnapshotsAsync(heroId);
        snapshots.Should().HaveCountGreaterThanOrEqualTo(2);
        var hero = await _heroes.GetByIdAsync(heroId);
        hero!.LatestSnapshot!.Sheet.CurrentHitPoints.Should().Be(4);

        // The combat is ended and a breadcrumb is in the log.
        (await _combats.GetActiveAsync(campaignId, userId)).Should().BeNull();
        var entries = await _log.GetByCampaignAsync(campaignId, userId);
        entries.Should().ContainSingle(e => e.Kind == CampaignLogKind.Combat);
    }

    [Fact]
    public async Task EndCombat_does_not_write_back_for_a_combatant_without_a_hero()
    {
        var userId = await _users.CreateAsync("dm", "hash");
        var campaignId = await _campaigns.CreateAsync(userId, "Camp", "");
        var combatId = (await _combats.StartAsync(userId, campaignId, "Brawl", DndVersion.Edition2014))!.Value;
        await NewService().AddManualAsync(combatId, campaignId, userId, "Thug", 11, 12, isPlayer: true, initiativeModifier: 0);
        var thug = (await _combats.GetCombatantsAsync(combatId, campaignId, userId)).Single();

        await NewService().EndCombatAsync(combatId, campaignId, userId,
            new Dictionary<long, int> { [thug.Id] = 0 });

        (await _combats.GetActiveAsync(campaignId, userId)).Should().BeNull(); // still ends
        var entries = await _log.GetByCampaignAsync(campaignId, userId);
        entries.Should().ContainSingle(e => e.Kind == CampaignLogKind.Combat);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~CombatServiceEndTests`
Expected: FAIL — `EndCombatAsync` / `AddCombatAsync` do not exist.

- [ ] **Step 3: Write minimal implementation**

Add to `CampaignLogRepository` (mirrors `AddEncounterAsync`):

```csharp
    public async Task<long> AddCombatAsync(long userId, long campaignId, CombatLogPayload payload, string? label)
    {
        return await AddAsync(userId, campaignId, CampaignLogKind.Combat, JsonSerializer.Serialize(payload), label, hidden: false);
    }
```

Add to `CombatService`:

```csharp
    /// <summary>
    /// Ends a combat: for each player combatant linked to a hero, appends a new <c>HeroSnapshot</c>
    /// that clones the hero's latest sheet with <c>CurrentHitPoints</c> set to the DM-approved value
    /// (falling back to the combatant's tracked HP if not in the map); then marks the combat ended and
    /// drops a Combat breadcrumb in the campaign log. Callers invoke this only after the DM approves
    /// the end-combat review — nothing here re-prompts; the approval gate lives in the UI.
    /// </summary>
    public async Task EndCombatAsync(
        long combatId, long campaignId, long userId, IReadOnlyDictionary<long, int> approvedHpByCombatantId)
    {
        var combat = await combats.GetByIdAsync(combatId, campaignId, userId);
        if (combat is null) return;

        var combatants = await combats.GetCombatantsAsync(combatId, campaignId, userId);

        foreach (var c in combatants.Where(c => c is { IsPlayer: true, HeroId: not null }))
        {
            var hero = await heroes.GetByIdAsync(c.HeroId!.Value);
            var latest = hero?.LatestSnapshot;
            if (latest is null) continue;

            var approvedHp = approvedHpByCombatantId.TryGetValue(c.Id, out var hp) ? hp : c.CurrentHp;
            var sheet = latest.Sheet;
            sheet.CurrentHitPoints = approvedHp;
            await heroes.SaveSnapshotAsync(c.HeroId.Value, latest.SessionNumber, $"Post-combat: {combat.Name}", sheet);
        }

        await combats.EndAsync(combatId, campaignId, userId);

        var payload = new CombatLogPayload(
            combat.Name,
            combat.Edition.ToString(),
            combat.Round,
            CombatantOrder.Sort(combatants)
                .Select(c => new CombatCombatantLog(c.Name, c.IsPlayer, c.InitiativeRoll, c.CurrentHp, c.MaxHp))
                .ToList());
        await log.AddCombatAsync(userId, campaignId, payload, combat.Name);
    }
```

Add `using DndMcpAICsharpFun.Domain;` to `CombatService.cs` if not present (for `CampaignLogKind` is in Domain; `CombatLogPayload`/`CombatCombatantLog` are in `Features.Campaigns` — already imported).

Note: `hero.LatestSnapshot.Sheet` is a deserialized `CharacterSheet` instance; mutating `CurrentHitPoints` then `SaveSnapshotAsync` appends a fresh row (append-only). The read used `AsNoTracking`, so mutating the in-memory copy is safe and does not alter the source row.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~CombatServiceEndTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add Features/Combat/CombatService.cs Features/Campaigns/CampaignLogRepository.cs DndMcpAICsharpFun.Tests/Combat/CombatServiceEndTests.cs
git commit -m "feat(combat): EndCombatAsync approval write-back to hero snapshots + log breadcrumb"
```

---

### Task 10: DI registration

**Files:**
- Modify: `Extensions/DatabaseExtensions.cs`
- Modify: `Extensions/ServiceCollectionExtensions.cs`
- Modify: `Program.cs`
- Test: existing `DndMcpAICsharpFun.Tests/Di/FullContainerScopeValidationTests.cs` (no change; must still pass)

**Interfaces:**
- Produces: `CombatRepository` (Singleton, in `AddDatabase`); `AddCombat` extension registering `CombatService` (Scoped) + pulling `AddDice`.

- [ ] **Step 1: Register the repository**

In `Extensions/DatabaseExtensions.cs`, add after the `CampaignLogRepository` registration:

```csharp
        services.AddSingleton<DndMcpAICsharpFun.Features.Combat.CombatRepository>();
```

- [ ] **Step 2: Add the AddCombat extension**

In `Extensions/ServiceCollectionExtensions.cs`, next to `AddDice`, add:

```csharp
    internal static IServiceCollection AddCombat(this IServiceCollection services)
    {
        // CombatService depends on the scoped DiceRoller, so pull AddDice in here (idempotent) and
        // register the service scoped. CombatRepository, HeroRepository, and CampaignLogRepository
        // come from AddDatabase (singletons over IDbContextFactory).
        services.AddDice();
        services.AddScoped<DndMcpAICsharpFun.Features.Combat.CombatService>();
        return services;
    }
```

- [ ] **Step 3: Wire it into Program.cs**

In `Program.cs`, after `builder.Services.AddDatabase(builder.Configuration);` (line ~47), add:

```csharp
builder.Services.AddCombat();
```

- [ ] **Step 4: Build + run the scope-validation test**

Run: `dotnet build` then `dotnet test --filter FullyQualifiedName~FullContainerScopeValidationTests` (with `dangerouslyDisableSandbox: true`)
Expected: Build 0/0; `BuildServiceProvider_WithAllExtensionGroups_DoesNotThrow` PASS (confirms `CombatService`'s scoped/singleton graph resolves).

- [ ] **Step 5: Commit**

```bash
git add Extensions/DatabaseExtensions.cs Extensions/ServiceCollectionExtensions.cs Program.cs
git commit -m "feat(combat): DI registration (CombatRepository + AddCombat, wired into Program)"
```

---

### Task 11: EncounterPanel exposes the built encounter

**Files:**
- Modify: `CompanionUI/Components/EncounterPanel.razor`

**Interfaces:**
- Produces: `[Parameter] public EventCallback<BuiltEncounter> OnBuilt` on `EncounterPanel`, invoked after a successful build so the play page can feed the monsters to the tracker.

- [ ] **Step 1: Add the callback parameter**

In `CompanionUI/Components/EncounterPanel.razor` `@code` block, add next to the other parameters:

```csharp
    [Parameter] public EventCallback<BuiltEncounter> OnBuilt { get; set; }
```

- [ ] **Step 2: Invoke it after a successful build**

In `BuildAsync`, after `_error = null;` (inside the `try`, once `_built` is set), add:

```csharp
            if (_built is not null)
                await OnBuilt.InvokeAsync(_built);
```

- [ ] **Step 3: Build to confirm it compiles**

Run: `dotnet build` (with `dangerouslyDisableSandbox: true`)
Expected: Build 0/0. (No behavior change when `OnBuilt` is not wired — `EventCallback` no-ops.)

- [ ] **Step 4: Commit**

```bash
git add CompanionUI/Components/EncounterPanel.razor
git commit -m "feat(combat): EncounterPanel raises OnBuilt so the play page can draft its monsters"
```

---

### Task 12: InitiativeTracker component

**Files:**
- Create: `CompanionUI/Components/InitiativeTracker.razor`

**Interfaces:**
- Consumes: `CombatRepository`, `CombatService` (DI); `CombatantOrder`; `MonsterRef` (from the page); `DndVersion`, `Condition`.
- Produces: `InitiativeTracker` component with `[Parameter] long CampaignId`, `[Parameter] long UserId`, and a public `Task AddMonstersAsync(IReadOnlyList<MonsterRef> monsters)` the page calls when an encounter is built.

- [ ] **Step 1: Write the component**

`CompanionUI/Components/InitiativeTracker.razor`:

```razor
@inject DndMcpAICsharpFun.Features.Combat.CombatRepository CombatRepo
@inject DndMcpAICsharpFun.Features.Combat.CombatService CombatSvc
@using DndMcpAICsharpFun.Domain
@using DndMcpAICsharpFun.Features.Combat
@using DndMcpAICsharpFun.Features.Encounters

<div class="initiative-tracker">
    <h2>Initiative Tracker</h2>

    @if (_error is not null)
    {
        <p class="error">@_error</p>
    }

    @if (_combat is null)
    {
        <div class="combat-start">
            <input @bind="_newName" placeholder="Combat name (e.g. Goblin Ambush)" />
            <select @bind="_newEdition">
                <option value="@DndVersion.Edition2014">2014</option>
                <option value="@DndVersion.Edition2024">2024</option>
            </select>
            <button @onclick="StartAsync">Start combat</button>
        </div>
    }
    else
    {
        <div class="combat-active">
            <div class="combat-head">
                <strong>@_combat.Name</strong>
                <span class="muted">Round @_combat.Round · @_combat.Edition</span>
                <button @onclick="AdvanceAsync">Advance turn ▸</button>
                <button @onclick="BeginEnd">End combat</button>
            </div>

            <div class="combat-add-row">
                <button @onclick="DraftPartyAsync">+ Party</button>
                <input @bind="_manualName" placeholder="Add combatant" />
                <input type="number" @bind="_manualHp" placeholder="HP" />
                <label><input type="checkbox" @bind="_manualIsPlayer" /> Player</label>
                <button @onclick="AddManualAsync">Add</button>
            </div>

            <ol class="combatant-list">
                @{ var ordered = CombatantOrder.Sort(_combatants); var i = 0; }
                @foreach (var c in ordered)
                {
                    var isCurrent = i == _combat.CurrentTurnIndex;
                    <li class="@(isCurrent ? "combatant current" : "combatant")">
                        <span class="c-init">
                            <input type="number" value="@c.InitiativeRoll"
                                   @onchange="e => SetInitiativeAsync(c, e.Value?.ToString())" />
                        </span>
                        <span class="c-name">@c.Name @(c.IsPlayer ? "" : "(NPC)")</span>
                        <span class="c-hp">
                            <button @onclick="() => AdjustHpAsync(c, -1)">−</button>
                            @c.CurrentHp / @c.MaxHp
                            <button @onclick="() => AdjustHpAsync(c, +1)">+</button>
                        </span>
                        <span class="c-conditions">
                            @foreach (var cond in Enum.GetValues<Condition>())
                            {
                                var on = c.Conditions.Contains(cond);
                                <button class="@(on ? "cond on" : "cond")"
                                        @onclick="() => ToggleConditionAsync(c, cond)">@cond</button>
                            }
                        </span>
                        <button class="c-remove" @onclick="() => RemoveAsync(c)">✕</button>
                    </li>
                    i++;
                }
                @if (ordered.Count == 0)
                {
                    <li class="muted">No combatants yet — add the party or build an encounter above.</li>
                }
            </ol>
        </div>

        @if (_ending)
        {
            <div class="end-combat-review">
                <h3>End combat — review player HP</h3>
                @foreach (var c in _combatants.Where(c => c is { IsPlayer: true, HeroId: not null }))
                {
                    <div class="hp-review-row">
                        <span>@c.Name</span>
                        <span class="muted">now @c.CurrentHp</span>
                        <input type="number" @bind="_approvedHp[c.Id]" />
                    </div>
                }
                <button @onclick="ConfirmEndAsync">Approve &amp; end</button>
                <button @onclick="() => _ending = false">Cancel</button>
            </div>
        }
    }

    @if (_history.Count > 0)
    {
        <details class="combat-history">
            <summary>Past combats (@_history.Count)</summary>
            <ul>
                @foreach (var h in _history)
                {
                    <li>@h.Name · @h.Round rounds · @(h.EndedAt?.ToString("yyyy-MM-dd HH:mm"))</li>
                }
            </ul>
        </details>
    }
</div>

@code {
    [Parameter] public long CampaignId { get; set; }
    [Parameter] public long UserId { get; set; }

    private Combat? _combat;
    private IReadOnlyList<Combatant> _combatants = [];
    private IReadOnlyList<Combat> _history = [];
    private string? _error;

    private string _newName = "";
    private DndVersion _newEdition = DndVersion.Edition2014;

    private string _manualName = "";
    private int _manualHp = 1;
    private bool _manualIsPlayer;

    private bool _ending;
    private readonly Dictionary<long, int> _approvedHp = new();

    protected override async Task OnInitializedAsync() => await ReloadAsync();

    /// <summary>Called by the play page when an encounter is built, to draft its monsters.</summary>
    public async Task AddMonstersAsync(IReadOnlyList<MonsterRef> monsters)
    {
        if (_combat is null) { _error = "Start a combat before adding monsters."; return; }
        try
        {
            await CombatSvc.DraftMonstersAsync(_combat.Id, CampaignId, UserId, monsters);
            await ReloadAsync();
        }
        catch (Exception ex) { _error = ex.Message; }
    }

    private async Task ReloadAsync()
    {
        try
        {
            _combat = await CombatRepo.GetActiveAsync(CampaignId, UserId);
            _combatants = _combat is null
                ? []
                : await CombatRepo.GetCombatantsAsync(_combat.Id, CampaignId, UserId);
            _history = await CombatRepo.GetHistoryAsync(CampaignId, UserId);
            _error = null;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            _combatants = [];
        }
    }

    private async Task StartAsync()
    {
        if (string.IsNullOrWhiteSpace(_newName)) { _error = "Name the combat first."; return; }
        var id = await CombatRepo.StartAsync(UserId, CampaignId, _newName.Trim(), _newEdition);
        if (id is null) { _error = "A combat is already active for this campaign."; return; }
        _newName = "";
        await ReloadAsync();
    }

    private async Task DraftPartyAsync()
    {
        if (_combat is null) return;
        await CombatSvc.DraftPartyAsync(_combat.Id, CampaignId, UserId);
        await ReloadAsync();
    }

    private async Task AddManualAsync()
    {
        if (_combat is null || string.IsNullOrWhiteSpace(_manualName)) return;
        await CombatSvc.AddManualAsync(_combat.Id, CampaignId, UserId, _manualName.Trim(),
            Math.Max(0, _manualHp), ac: null, _manualIsPlayer, initiativeModifier: 0);
        _manualName = "";
        _manualHp = 1;
        _manualIsPlayer = false;
        await ReloadAsync();
    }

    private async Task AdvanceAsync()
    {
        if (_combat is null) return;
        await CombatRepo.AdvanceTurnAsync(_combat.Id, CampaignId, UserId);
        await ReloadAsync();
    }

    private async Task AdjustHpAsync(Combatant c, int delta)
    {
        if (_combat is null) return;
        var hp = Math.Clamp(c.CurrentHp + delta, 0, c.MaxHp > 0 ? c.MaxHp : int.MaxValue);
        await CombatRepo.UpdateCombatantAsync(c.Id, _combat.Id, CampaignId, UserId,
            hp, c.InitiativeRoll, c.InitiativeModifier, c.Conditions);
        await ReloadAsync();
    }

    private async Task SetInitiativeAsync(Combatant c, string? raw)
    {
        if (_combat is null) return;
        int? init = int.TryParse(raw, out var v) ? v : null;
        await CombatRepo.UpdateCombatantAsync(c.Id, _combat.Id, CampaignId, UserId,
            c.CurrentHp, init, c.InitiativeModifier, c.Conditions);
        await ReloadAsync();
    }

    private async Task ToggleConditionAsync(Combatant c, Condition cond)
    {
        if (_combat is null) return;
        var set = c.Conditions.ToList();
        if (!set.Remove(cond)) set.Add(cond);
        await CombatRepo.UpdateCombatantAsync(c.Id, _combat.Id, CampaignId, UserId,
            c.CurrentHp, c.InitiativeRoll, c.InitiativeModifier, set);
        await ReloadAsync();
    }

    private async Task RemoveAsync(Combatant c)
    {
        if (_combat is null) return;
        await CombatRepo.RemoveCombatantAsync(c.Id, _combat.Id, CampaignId, UserId);
        await ReloadAsync();
    }

    private void BeginEnd()
    {
        _approvedHp.Clear();
        foreach (var c in _combatants.Where(c => c is { IsPlayer: true, HeroId: not null }))
            _approvedHp[c.Id] = c.CurrentHp;
        _ending = true;
    }

    private async Task ConfirmEndAsync()
    {
        if (_combat is null) return;
        await CombatSvc.EndCombatAsync(_combat.Id, CampaignId, UserId, new Dictionary<long, int>(_approvedHp));
        _ending = false;
        await ReloadAsync();
    }
}
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build` (with `dangerouslyDisableSandbox: true`)
Expected: Build 0/0. (Razor compiles as part of the main project build.)

- [ ] **Step 3: Commit**

```bash
git add CompanionUI/Components/InitiativeTracker.razor
git commit -m "feat(combat): InitiativeTracker component (start/draft/track/advance/end with approval)"
```

---

### Task 13: Play page + CampaignDetail relocation

**Files:**
- Create: `CompanionUI/Pages/Campaigns/CampaignTable.razor`
- Modify: `CompanionUI/Pages/Campaigns/CampaignDetail.razor`
- Modify: `CompanionUI/Components/CampaignLog.razor` (render `Combat`-kind entries — Step 3)
- Test: `DndMcpAICsharpFun.Tests/Combat/CombatLogPayloadTests.cs` (add a FormatEntry combat case — Step 3)

**Interfaces:**
- Consumes: `CampaignRepository.GetByIdAsync`; `DiceRollerPanel`, `EncounterPanel` (+ its `OnBuilt`), `CampaignLog`, `InitiativeTracker`.

- [ ] **Step 1: Create the play page**

`CompanionUI/Pages/Campaigns/CampaignTable.razor`:

```razor
@page "/campaigns/{Id:long}/table"
@rendermode InteractiveServer
@using Microsoft.AspNetCore.Authorization
@using System.Security.Claims
@using DndMcpAICsharpFun.Features.Campaigns
@using DndMcpAICsharpFun.Features.Encounters
@attribute [Authorize]
@inject CampaignRepository CampaignRepo
@inject NavigationManager Nav
@inject AuthenticationStateProvider Auth

<PageTitle>@(_campaign?.Name ?? "Table") — Table</PageTitle>

@if (_campaign is null)
{
    <p>Loading...</p>
}
else
{
    <div class="campaign-table">
        <nav><a href="/campaigns/@Id">← @_campaign.Name</a></nav>
        <h1>@_campaign.Name — Table</h1>

        <DiceRollerPanel CampaignId="Id" UserId="_userId" OnLogged="RefreshLog" />

        <EncounterPanel CampaignId="Id" UserId="_userId" OnSaved="RefreshLog" OnBuilt="OnEncounterBuilt" />

        <InitiativeTracker @ref="_tracker" CampaignId="Id" UserId="_userId" />

        <CampaignLog @ref="_log" CampaignId="Id" UserId="_userId" />
    </div>
}

@code {
    [Parameter] public long Id { get; set; }

    private Campaign? _campaign;
    private long _userId;
    private CampaignLog? _log;
    private InitiativeTracker? _tracker;

    protected override async Task OnInitializedAsync()
    {
        var state = await Auth.GetAuthenticationStateAsync();
        _userId = long.Parse(state.User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        _campaign = await CampaignRepo.GetByIdAsync(Id, _userId);
        if (_campaign is null)
            Nav.NavigateTo("/campaigns");
    }

    private async Task RefreshLog()
    {
        if (_log is not null)
            await _log.RefreshAsync();
    }

    private async Task OnEncounterBuilt(BuiltEncounter built)
    {
        if (_tracker is not null)
            await _tracker.AddMonstersAsync(built.Assessment.Monsters);
    }
}
```

- [ ] **Step 2: Remove the three panels from CampaignDetail and add the run-session link**

In `CompanionUI/Pages/Campaigns/CampaignDetail.razor`, DELETE these three lines (62, 64, 66):

```razor
        <DiceRollerPanel CampaignId="Id" UserId="_userId" OnLogged="RefreshLog" />

        <EncounterPanel CampaignId="Id" UserId="_userId" OnSaved="RefreshLog" />

        <CampaignLog @ref="_log" CampaignId="Id" UserId="_userId" />
```

Replace them with a link (place it just after the `</div>` closing `hero-roster`, or wherever it reads well near the top of the campaign body):

```razor
        <p class="run-session"><a class="button" href="/campaigns/@Id/table">▶ Run session</a></p>
```

Then remove the now-unused log plumbing from `@code`: delete the `private CampaignLog? _log;` field and the `RefreshLog` method **only if** nothing else references them. (After removing the three panels, `_log` and `RefreshLog` are unused — the compiler with warnings-as-errors will flag the unused private members, so they MUST be removed.) Verify with a build.

- [ ] **Step 3: Render Combat-kind entries in the campaign log**

The `campaign-log-history` MODIFIED delta requires the timeline to render combat entries (label,
combat name, round count). Currently `CompanionUI/Components/CampaignLog.razor`'s `FormatEntry`
routes every non-`Roll` kind into the `Encounter` branch, so a `Combat` entry would deserialize as
an `EncounterLogPayload`, find `Monsters` null, and be silently skipped. Add an explicit `Combat`
branch. Edit `internal static string? FormatEntry(CampaignLogEntry e)` so its body is:

```csharp
        try
        {
            if (e.Kind == CampaignLogKind.Roll)
            {
                var payload = JsonSerializer.Deserialize<RollLogPayload>(e.PayloadJson);
                if (payload is null) return null;
                var breakdown = payload.Breakdown ?? "";
                return $"{e.Label} · {breakdown} · {payload.Total}";
            }
            else if (e.Kind == CampaignLogKind.Combat)
            {
                var payload = JsonSerializer.Deserialize<CombatLogPayload>(e.PayloadJson);
                if (payload is null) return null;
                var count = payload.Combatants?.Count ?? 0;
                return $"{payload.CombatName} · {payload.Rounds} rounds · {count} combatants";
            }
            else
            {
                var payload = JsonSerializer.Deserialize<EncounterLogPayload>(e.PayloadJson);
                if (payload is null || payload.Monsters is null) return null;
                var names = string.Join(", ", payload.Monsters.Select(m => m?.Name ?? "?"));
                return $"{e.Label} · {payload.Difficulty} · {names}";
            }
        }
        catch (Exception)
        {
            return null;
        }
```

Add a test to `DndMcpAICsharpFun.Tests/Combat/CombatLogPayloadTests.cs` (FormatEntry is `internal`; `InternalsVisibleTo` is already configured):

```csharp
    [Fact]
    public void FormatEntry_renders_a_combat_entry_with_name_round_and_count()
    {
        var payload = new CombatLogPayload("Goblin Ambush", "Edition2014", 3,
            new[] { new CombatCombatantLog("Aria", true, 17, 5, 12), new CombatCombatantLog("Goblin 1", false, 14, 0, 7) });
        var entry = new DndMcpAICsharpFun.Domain.CampaignLogEntry
        {
            Kind = DndMcpAICsharpFun.Domain.CampaignLogKind.Combat,
            Label = "Goblin Ambush",
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload),
        };

        var line = DndMcpAICsharpFun.CompanionUI.Components.CampaignLog.FormatEntry(entry);

        line.Should().NotBeNull();
        line.Should().Contain("Goblin Ambush").And.Contain("3 rounds").And.Contain("2 combatants");
    }
```

(Confirm the `CampaignLog` component's namespace for the test's fully-qualified reference — read the top of `CampaignLog.razor` / its generated type; adjust the `DndMcpAICsharpFun.CompanionUI.Components.CampaignLog` reference if the component's namespace differs. An existing `FormatEntry` test in the suite already references it — mirror that reference exactly.)

- [ ] **Step 4: Build + run the log-rendering test**

Run: `dotnet build` then `dotnet test --filter FullyQualifiedName~CombatLogPayloadTests` (with `dangerouslyDisableSandbox: true`)
Expected: Build 0/0 (if it fails on an unused `_log`/`RefreshLog`, remove them from `CampaignDetail.razor`); the new FormatEntry combat test passes.

- [ ] **Step 5: Commit**

```bash
git add CompanionUI/Pages/Campaigns/CampaignTable.razor CompanionUI/Pages/Campaigns/CampaignDetail.razor CompanionUI/Components/CampaignLog.razor DndMcpAICsharpFun.Tests/Combat/CombatLogPayloadTests.cs
git commit -m "feat(combat): dedicated play page + campaign-log renders Combat entries; CampaignDetail links to it"
```

---

### Task 14: Full verification + whole-branch review

**Files:** none (verification only)

- [ ] **Step 1: Full build**

Run: `dotnet build` (with `dangerouslyDisableSandbox: true`)
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 2: Full test suite**

Run: `dotnet test` (Docker running for the Postgres Testcontainer, `dangerouslyDisableSandbox: true`)
Expected: all tests pass, including every new `DndMcpAICsharpFun.Tests/Combat/*` test and `FullContainerScopeValidationTests`. Record the final count (should be prior total + the new tests).

- [ ] **Step 3: Confirm no HTTP/MCP surface changed**

Run: `git diff --name-only main -- DndMcpAICsharpFun.http dnd-mcp-api.insomnia.json`
Expected: empty output (no changes — the feature adds no route/tool).

- [ ] **Step 4: Spec-coverage self-check**

Re-read `openspec/changes/combat-initiative-tracker/specs/**/*.md`. Confirm a task covers each requirement: persisted model (T1,T4), ownership-scoped (T5,T6), one-active (T5), history (T5), drafting (T8), ordering + auto-roll (T2,T8), conditions (T1,T6), write-back approval (T9,T12), play page (T13), rehydration (T12). Confirm the two MODIFIED deltas hold: panels now on the play page (T13), new `Combat` log kind + breadcrumb (T3,T9).

- [ ] **Step 5: Whole-branch review on opus**

Dispatch the final whole-branch review (subagent-driven-development's terminal review). Trace cross-path invariants explicitly:
- Ownership enforced on EVERY `CombatRepository` command (each re-checks the parent combat's `UserId`) — verified by the intruder test (T6).
- Cascade on BOTH delete paths: combat delete (FK cascade, T6) and campaign delete (explicit lines, T7).
- Write-back happens ONLY after `ConfirmEndAsync` (approval) and ONLY for player combatants with a `HeroId` (T9,T12); Cancel writes nothing.
- Migration is additive-only (T4).
- No unused members left in `CampaignDetail` after the panel removal (T13).

- [ ] **Step 6: Commit any review fixes**

```bash
git add -A
git commit -m "fix(combat): address whole-branch review findings"
```

---

## Self-Review

**Spec coverage:** Every requirement in the new-capability spec maps to a task (see Task 14 Step 4). Both MODIFIED deltas are implemented: render relocation (Task 13) and the `Combat` log kind + breadcrumb (Tasks 3, 9). The `dice-roller` delta (component now on the play page) is satisfied by Task 13 moving `DiceRollerPanel` to `CampaignTable.razor`.

**Placeholder scan:** No TBD/TODO; every code step contains complete code; every test step contains the actual test; commands include expected output.

**Type consistency:** `StartAsync` returns `long?` (used consistently in Tasks 5–9 and the component). `AddCombatantAsync` returns `long` (0 = not owned), used in Task 6/8. `UpdateCombatantAsync` signature `(combatantId, combatId, campaignId, userId, currentHp, initiativeRoll, initiativeModifier, conditions)` is identical in Tasks 6 and 12. `EndCombatAsync(combatId, campaignId, userId, IReadOnlyDictionary<long,int>)` matches between Tasks 9 and 12. `CombatLogPayload(CombatName, Edition, Rounds, Combatants)` and `CombatCombatantLog(Name, IsPlayer, InitiativeRoll, CurrentHp, MaxHp)` match between Tasks 3 and 9. `CombatantOrder.Sort` (Task 2) is used in Tasks 9 and 12.
