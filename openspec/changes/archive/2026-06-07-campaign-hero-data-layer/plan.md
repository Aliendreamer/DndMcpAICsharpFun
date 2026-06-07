# Campaign & Hero Data Layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Add personal campaign and character management to the companion so users can track their D&D parties and full character progression over time using the Memento pattern.

**Architecture:** Three SQLite tables (Campaigns, Heroes, HeroSnapshots) added to `companion.db` via raw ADO.NET following the existing `UserRepository` pattern. Full character sheet stored as a JSON blob per snapshot. Three Blazor Server pages for campaign list, campaign detail, and hero detail with structured character sheet view/edit. Tests use shared-mode in-memory SQLite (`Mode=Memory;Cache=Shared`) with a keepalive connection.

**Tech Stack:** `Microsoft.Data.Sqlite` (already in companion project), `System.Text.Json` (SDK), Blazor Server, xUnit + FluentAssertions (companion test project).

---

## File Map

**Create:**

- `DndMcpAICompanion/Features/Campaign/CharacterSheet.cs` — mutable class, all character fields, static modifier helpers
- `DndMcpAICompanion/Features/Campaign/CampaignRepository.cs` — Campaign/CampaignSummary records, all 3 table CREATE, CRUD
- `DndMcpAICompanion/Features/Campaign/HeroRepository.cs` — Hero/HeroSnapshot/HeroSnapshotMeta records, snapshot CRUD
- `DndMcpAICompanion/Components/Pages/Campaign/Campaigns.razor` — `/campaigns`
- `DndMcpAICompanion/Components/Pages/Campaign/CampaignDetail.razor` — `/campaigns/{Id:int}`
- `DndMcpAICompanion/Components/Pages/Campaign/HeroDetail.razor` — `/campaigns/{CampaignId:int}/heroes/{HeroId:int}`
- `DndMcpAICompanion.Tests/Campaign/CharacterSheetSerializationTests.cs`
- `DndMcpAICompanion.Tests/Campaign/CampaignRepositoryTests.cs`
- `DndMcpAICompanion.Tests/Campaign/HeroRepositoryTests.cs`

**Modify:**

- `DndMcpAICompanion/Program.cs` — register both repositories as singletons, call `InitializeAsync`

---

## Task 1: CharacterSheet Model + Serialization Tests

**Files:**

- Create: `DndMcpAICompanion/Features/Campaign/CharacterSheet.cs`
- Create: `DndMcpAICompanion.Tests/Campaign/CharacterSheetSerializationTests.cs`

- [x] **Step 1.1: Write the failing test**

Create `DndMcpAICompanion.Tests/Campaign/CharacterSheetSerializationTests.cs`:

```csharp
using System.Text.Json;
using DndMcpAICompanion.Features.Campaign;
using FluentAssertions;
using Xunit;

namespace DndMcpAICompanion.Tests.Campaign;

public sealed class CharacterSheetSerializationTests
{
    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var sheet = new CharacterSheet
        {
            Race = "Elf", Class = "Wizard", Subclass = "Divination",
            Background = "Sage", Level = 5, Alignment = "Neutral Good", ExperiencePoints = 6500,
            Strength = 8, Dexterity = 16, Constitution = 14,
            Intelligence = 18, Wisdom = 12, Charisma = 10,
            MaxHitPoints = 32, CurrentHitPoints = 28, ArmorClass = 13,
            Speed = 30, Initiative = 3, ProficiencyBonus = 3,
            SpellcastingAbility = "Intelligence", SpellSaveDC = 15, SpellAttackBonus = 7,
            SpellSlots = [4, 3, 2, 1, 0, 0, 0, 0, 0],
            UsedSpellSlots = [1, 0, 0, 0, 0, 0, 0, 0, 0],
            SpellsKnown = ["Fireball", "Counterspell"],
            WeaponProficiencies = ["Daggers", "Darts"],
            Languages = ["Common", "Elvish"],
            SkillProficiencies = ["Arcana", "History"],
            Features = [new CharacterFeature { Name = "Portent", Description = "Roll 2d20 at dawn." }],
            Equipment = ["Spellbook", "Wand of Magic Missile"]
        };

        var json = JsonSerializer.Serialize(sheet);
        var restored = JsonSerializer.Deserialize<CharacterSheet>(json)!;

        restored.Race.Should().Be("Elf");
        restored.Level.Should().Be(5);
        restored.SpellSlots.Should().Equal([4, 3, 2, 1, 0, 0, 0, 0, 0]);
        restored.SpellsKnown.Should().Equal(["Fireball", "Counterspell"]);
        restored.Features.Should().HaveCount(1);
        restored.Features[0].Name.Should().Be("Portent");
        restored.Features[0].Description.Should().Be("Roll 2d20 at dawn.");
        restored.Equipment.Should().Equal(["Spellbook", "Wand of Magic Missile"]);
    }

    [Fact]
    public void Modifier_ComputesCorrectly()
    {
        CharacterSheet.Modifier(10).Should().Be(0);
        CharacterSheet.Modifier(8).Should().Be(-1);
        CharacterSheet.Modifier(16).Should().Be(3);
        CharacterSheet.Modifier(20).Should().Be(5);
    }

    [Fact]
    public void ModifierStr_FormatsWithSign()
    {
        CharacterSheet.ModifierStr(16).Should().Be("+3");
        CharacterSheet.ModifierStr(8).Should().Be("-1");
        CharacterSheet.ModifierStr(10).Should().Be("+0");
    }
}
```

- [x] **Step 1.2: Run test — confirm build failure**

```bash
dotnet test DndMcpAICompanion.Tests/DndMcpAICompanion.Tests.csproj \
  --filter "FullyQualifiedName~CharacterSheetSerializationTests" 2>&1 | tail -6
```

Expected: build error — `CharacterSheet` not found.

- [x] **Step 1.3: Create `DndMcpAICompanion/Features/Campaign/CharacterSheet.cs`**

```csharp
namespace DndMcpAICompanion.Features.Campaign;

public sealed class CharacterSheet
{
    public string Race { get; set; } = "";
    public string Class { get; set; } = "";
    public string Subclass { get; set; } = "";
    public string Background { get; set; } = "";
    public int Level { get; set; }
    public string Alignment { get; set; } = "";
    public int ExperiencePoints { get; set; }

    public int Strength { get; set; }
    public int Dexterity { get; set; }
    public int Constitution { get; set; }
    public int Intelligence { get; set; }
    public int Wisdom { get; set; }
    public int Charisma { get; set; }

    public int MaxHitPoints { get; set; }
    public int CurrentHitPoints { get; set; }
    public int ArmorClass { get; set; }
    public int Speed { get; set; }
    public int Initiative { get; set; }
    public int ProficiencyBonus { get; set; }

    public string SpellcastingAbility { get; set; } = "";
    public int SpellSaveDC { get; set; }
    public int SpellAttackBonus { get; set; }
    public int[] SpellSlots { get; set; } = new int[9];
    public int[] UsedSpellSlots { get; set; } = new int[9];
    public List<string> SpellsKnown { get; set; } = [];

    public List<string> ArmorProficiencies { get; set; } = [];
    public List<string> WeaponProficiencies { get; set; } = [];
    public List<string> ToolProficiencies { get; set; } = [];
    public List<string> Languages { get; set; } = [];
    public List<string> SkillProficiencies { get; set; } = [];

    public List<CharacterFeature> Features { get; set; } = [];
    public List<string> Equipment { get; set; } = [];

    public static int Modifier(int score) => (score - 10) / 2;
    public static string ModifierStr(int score)
    {
        var m = Modifier(score);
        return m >= 0 ? $"+{m}" : $"{m}";
    }
}

public sealed class CharacterFeature
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}
```

- [x] **Step 1.4: Run tests — all 3 must pass**

```bash
dotnet test DndMcpAICompanion.Tests/DndMcpAICompanion.Tests.csproj \
  --filter "FullyQualifiedName~CharacterSheetSerializationTests" 2>&1 | tail -5
```

Expected: `Passed! - Failed: 0, Passed: 3`

- [x] **Step 1.5: Commit**

```bash
git add DndMcpAICompanion/Features/Campaign/CharacterSheet.cs \
  DndMcpAICompanion.Tests/Campaign/CharacterSheetSerializationTests.cs
git commit -m "feat(campaign): add CharacterSheet model with serialization"
```

---

## Task 2: CampaignRepository + Tests

**Files:**

- Create: `DndMcpAICompanion/Features/Campaign/CampaignRepository.cs`
- Create: `DndMcpAICompanion.Tests/Campaign/CampaignRepositoryTests.cs`

- [x] **Step 2.1: Write failing tests**

Create `DndMcpAICompanion.Tests/Campaign/CampaignRepositoryTests.cs`:

```csharp
using DndMcpAICompanion.Features.Campaign;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DndMcpAICompanion.Tests.Campaign;

public sealed class CampaignRepositoryTests : IAsyncLifetime
{
    private readonly string _connStr = $"Data Source=camp_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private SqliteConnection _keepAlive = null!;
    private CampaignRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _keepAlive = new SqliteConnection(_connStr);
        await _keepAlive.OpenAsync();
        _repo = new CampaignRepository(_connStr);
        await _repo.InitializeAsync();
    }

    public async Task DisposeAsync() => _keepAlive.Dispose();

    private async Task<long> ScalarAsync(string sql, Action<SqliteCommand> setup)
    {
        await using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = sql;
        setup(cmd);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task ExecAsync(string sql, Action<SqliteCommand> setup)
    {
        await using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = sql;
        setup(cmd);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task CreateAndGetAll_ReturnsOnlyUserCampaigns()
    {
        await _repo.CreateAsync(1, "Campaign A", "desc");
        await _repo.CreateAsync(1, "Campaign B", "");
        await _repo.CreateAsync(2, "Other User", "");

        var campaigns = await _repo.GetAllAsync(1);

        campaigns.Should().HaveCount(2);
        campaigns.Select(c => c.Name).Should().BeEquivalentTo(["Campaign A", "Campaign B"]);
    }

    [Fact]
    public async Task GetById_ReturnsNull_ForWrongUser()
    {
        var id = await _repo.CreateAsync(1, "Secret", "");

        var result = await _repo.GetByIdAsync(id, 2);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetById_ReturnsCorrectFields()
    {
        var id = await _repo.CreateAsync(1, "My Campaign", "D&D adventure");

        var result = await _repo.GetByIdAsync(id, 1);

        result.Should().NotBeNull();
        result!.Name.Should().Be("My Campaign");
        result.Description.Should().Be("D&D adventure");
    }

    [Fact]
    public async Task Delete_RemovesCampaignAndHeroes()
    {
        var id = await _repo.CreateAsync(1, "ToDelete", "");
        await ExecAsync(
            "INSERT INTO Heroes (CampaignId, Name, CreatedAt) VALUES (@c, 'Hero', @t)",
            cmd => { cmd.Parameters.AddWithValue("@c", id); cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O")); });

        await _repo.DeleteAsync(id, 1);

        (await _repo.GetAllAsync(1)).Should().BeEmpty();
        var heroCount = await ScalarAsync("SELECT COUNT(*) FROM Heroes WHERE CampaignId = @id",
            cmd => cmd.Parameters.AddWithValue("@id", id));
        heroCount.Should().Be(0);
    }

    [Fact]
    public async Task GetAll_IncludesHeroCount()
    {
        var id = await _repo.CreateAsync(1, "Party", "");
        await ExecAsync(
            "INSERT INTO Heroes (CampaignId, Name, CreatedAt) VALUES (@c, 'A', @t), (@c, 'B', @t)",
            cmd => { cmd.Parameters.AddWithValue("@c", id); cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O")); });

        var campaigns = await _repo.GetAllAsync(1);

        campaigns.Single().HeroCount.Should().Be(2);
    }
}
```

- [x] **Step 2.2: Run to confirm build failure**

```bash
dotnet test DndMcpAICompanion.Tests/DndMcpAICompanion.Tests.csproj \
  --filter "FullyQualifiedName~CampaignRepositoryTests" 2>&1 | tail -5
```

Expected: build error — `CampaignRepository` not found.

- [x] **Step 2.3: Create `DndMcpAICompanion/Features/Campaign/CampaignRepository.cs`**

```csharp
using Microsoft.Data.Sqlite;

namespace DndMcpAICompanion.Features.Campaign;

public sealed record Campaign(long Id, long UserId, string Name, string Description, DateTime CreatedAt);
public sealed record CampaignSummary(long Id, long UserId, string Name, string Description, DateTime CreatedAt, int HeroCount);

public sealed class CampaignRepository(string connectionString)
{
    public async Task InitializeAsync()
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Campaigns (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId      INTEGER NOT NULL,
                Name        TEXT    NOT NULL,
                Description TEXT    NOT NULL DEFAULT '',
                CreatedAt   TEXT    NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Heroes (
                Id         INTEGER PRIMARY KEY AUTOINCREMENT,
                CampaignId INTEGER NOT NULL,
                Name       TEXT    NOT NULL,
                CreatedAt  TEXT    NOT NULL
            );
            CREATE TABLE IF NOT EXISTS HeroSnapshots (
                Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                HeroId        INTEGER NOT NULL,
                SessionNumber INTEGER NOT NULL,
                SessionLabel  TEXT    NOT NULL DEFAULT '',
                Level         INTEGER NOT NULL DEFAULT 0,
                CreatedAt     TEXT    NOT NULL,
                CharacterJson TEXT    NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<CampaignSummary>> GetAllAsync(long userId)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT c.Id, c.UserId, c.Name, c.Description, c.CreatedAt,
                   (SELECT COUNT(*) FROM Heroes WHERE CampaignId = c.Id) AS HeroCount
            FROM Campaigns c
            WHERE c.UserId = @u
            ORDER BY c.CreatedAt DESC
            """;
        cmd.Parameters.AddWithValue("@u", userId);
        var results = new List<CampaignSummary>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(new CampaignSummary(
                reader.GetInt64(0), reader.GetInt64(1),
                reader.GetString(2), reader.GetString(3),
                DateTime.Parse(reader.GetString(4)), (int)reader.GetInt64(5)));
        return results;
    }

    public async Task<Campaign?> GetByIdAsync(long id, long userId)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, UserId, Name, Description, CreatedAt FROM Campaigns WHERE Id = @id AND UserId = @u LIMIT 1";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@u", userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new Campaign(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), DateTime.Parse(reader.GetString(4)));
    }

    public async Task<long> CreateAsync(long userId, string name, string description)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Campaigns (UserId, Name, Description, CreatedAt) VALUES (@u, @n, @d, @c); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@u", userId);
        cmd.Parameters.AddWithValue("@n", name);
        cmd.Parameters.AddWithValue("@d", description);
        cmd.Parameters.AddWithValue("@c", DateTime.UtcNow.ToString("O"));
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task DeleteAsync(long id, long userId)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM HeroSnapshots WHERE HeroId IN (SELECT Id FROM Heroes WHERE CampaignId = @id);
            DELETE FROM Heroes WHERE CampaignId = @id;
            DELETE FROM Campaigns WHERE Id = @id AND UserId = @u;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@u", userId);
        await cmd.ExecuteNonQueryAsync();
    }
}
```

- [x] **Step 2.4: Run tests — all 5 must pass**

```bash
dotnet test DndMcpAICompanion.Tests/DndMcpAICompanion.Tests.csproj \
  --filter "FullyQualifiedName~CampaignRepositoryTests" 2>&1 | tail -5
```

Expected: `Passed! - Failed: 0, Passed: 5`

- [x] **Step 2.5: Commit**

```bash
git add DndMcpAICompanion/Features/Campaign/CampaignRepository.cs \
  DndMcpAICompanion.Tests/Campaign/CampaignRepositoryTests.cs
git commit -m "feat(campaign): add CampaignRepository with SQLite schema"
```

---

## Task 3: HeroRepository + Tests

**Files:**

- Create: `DndMcpAICompanion/Features/Campaign/HeroRepository.cs`
- Create: `DndMcpAICompanion.Tests/Campaign/HeroRepositoryTests.cs`

- [x] **Step 3.1: Write failing tests**

Create `DndMcpAICompanion.Tests/Campaign/HeroRepositoryTests.cs`:

```csharp
using System.Text.Json;
using DndMcpAICompanion.Features.Campaign;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DndMcpAICompanion.Tests.Campaign;

public sealed class HeroRepositoryTests : IAsyncLifetime
{
    private readonly string _connStr = $"Data Source=hero_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private SqliteConnection _keepAlive = null!;
    private CampaignRepository _campRepo = null!;
    private HeroRepository _repo = null!;
    private long _campaignId;

    public async Task InitializeAsync()
    {
        _keepAlive = new SqliteConnection(_connStr);
        await _keepAlive.OpenAsync();
        _campRepo = new CampaignRepository(_connStr);
        await _campRepo.InitializeAsync();
        _repo = new HeroRepository(_connStr);
        _campaignId = await _campRepo.CreateAsync(1, "Test Campaign", "");
    }

    public async Task DisposeAsync() => _keepAlive.Dispose();

    [Fact]
    public async Task CreateAsync_InsertsHeroWithBlankSnapshot()
    {
        var id = await _repo.CreateAsync(_campaignId, "Gandalf");

        var hero = await _repo.GetByIdAsync(id);

        hero.Should().NotBeNull();
        hero!.Name.Should().Be("Gandalf");
        hero.LatestSnapshot.Should().NotBeNull();
        hero.LatestSnapshot!.SessionNumber.Should().Be(0);
    }

    [Fact]
    public async Task SaveSnapshotAsync_AddsNewSnapshot()
    {
        var id = await _repo.CreateAsync(_campaignId, "Frodo");
        var sheet = new CharacterSheet { Level = 3, Class = "Rogue" };

        await _repo.SaveSnapshotAsync(id, 2, "After Moria", sheet);

        var hero = await _repo.GetByIdAsync(id);
        hero!.LatestSnapshot!.SessionNumber.Should().Be(2);
        hero.LatestSnapshot.SessionLabel.Should().Be("After Moria");
        hero.LatestSnapshot.Sheet.Level.Should().Be(3);
        hero.LatestSnapshot.Sheet.Class.Should().Be("Rogue");
    }

    [Fact]
    public async Task GetSnapshotsAsync_ReturnsAllInDescendingOrder()
    {
        var id = await _repo.CreateAsync(_campaignId, "Aragorn");
        await _repo.SaveSnapshotAsync(id, 1, "Session 1", new CharacterSheet { Level = 1 });
        await _repo.SaveSnapshotAsync(id, 2, "Session 2", new CharacterSheet { Level = 2 });

        var snapshots = await _repo.GetSnapshotsAsync(id);

        // 3 total: initial blank + 2 saved; latest first
        snapshots.Should().HaveCount(3);
        snapshots[0].SessionNumber.Should().Be(2);
        snapshots[1].SessionNumber.Should().Be(1);
    }

    [Fact]
    public async Task GetSnapshotAsync_ReturnsFullSheet()
    {
        var id = await _repo.CreateAsync(_campaignId, "Legolas");
        var sheet = new CharacterSheet { Level = 7, Race = "Elf", Class = "Ranger" };
        await _repo.SaveSnapshotAsync(id, 3, "Helm's Deep", sheet);

        var snapshots = await _repo.GetSnapshotsAsync(id);
        var full = await _repo.GetSnapshotAsync(snapshots[0].Id);

        full.Should().NotBeNull();
        full!.Sheet.Level.Should().Be(7);
        full.Sheet.Race.Should().Be("Elf");
    }

    [Fact]
    public async Task GetByCampaignAsync_ReturnsAllHeroesWithLatestSnapshot()
    {
        await _repo.CreateAsync(_campaignId, "Hero A");
        await _repo.CreateAsync(_campaignId, "Hero B");

        var heroes = await _repo.GetByCampaignAsync(_campaignId);

        heroes.Should().HaveCount(2);
        heroes.All(h => h.LatestSnapshot != null).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_RemovesHeroAndSnapshots()
    {
        var id = await _repo.CreateAsync(_campaignId, "Boromir");
        await _repo.SaveSnapshotAsync(id, 1, "S1", new CharacterSheet());

        await _repo.DeleteAsync(id);

        var hero = await _repo.GetByIdAsync(id);
        hero.Should().BeNull();

        var snapshots = await _repo.GetSnapshotsAsync(id);
        snapshots.Should().BeEmpty();
    }
}
```

- [x] **Step 3.2: Run to confirm build failure**

```bash
dotnet test DndMcpAICompanion.Tests/DndMcpAICompanion.Tests.csproj \
  --filter "FullyQualifiedName~HeroRepositoryTests" 2>&1 | tail -5
```

Expected: build error — `HeroRepository` not found.

- [x] **Step 3.3: Create `DndMcpAICompanion/Features/Campaign/HeroRepository.cs`**

```csharp
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace DndMcpAICompanion.Features.Campaign;

public sealed record Hero(long Id, long CampaignId, string Name, DateTime CreatedAt, HeroSnapshot? LatestSnapshot);
public sealed record HeroSnapshot(long Id, long HeroId, int SessionNumber, string SessionLabel, int Level, DateTime CreatedAt, CharacterSheet Sheet);
public sealed record HeroSnapshotMeta(long Id, long HeroId, int SessionNumber, string SessionLabel, int Level, DateTime CreatedAt);

public sealed class HeroRepository(string connectionString)
{
    public async Task<List<Hero>> GetByCampaignAsync(long campaignId)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT h.Id, h.CampaignId, h.Name, h.CreatedAt,
                   s.Id, s.SessionNumber, s.SessionLabel, s.Level, s.CreatedAt, s.CharacterJson
            FROM Heroes h
            LEFT JOIN HeroSnapshots s ON s.Id = (
                SELECT Id FROM HeroSnapshots WHERE HeroId = h.Id ORDER BY CreatedAt DESC LIMIT 1
            )
            WHERE h.CampaignId = @cid
            ORDER BY h.CreatedAt ASC
            """;
        cmd.Parameters.AddWithValue("@cid", campaignId);
        var results = new List<Hero>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(ReadHero(reader));
        return results;
    }

    public async Task<Hero?> GetByIdAsync(long id)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT h.Id, h.CampaignId, h.Name, h.CreatedAt,
                   s.Id, s.SessionNumber, s.SessionLabel, s.Level, s.CreatedAt, s.CharacterJson
            FROM Heroes h
            LEFT JOIN HeroSnapshots s ON s.Id = (
                SELECT Id FROM HeroSnapshots WHERE HeroId = h.Id ORDER BY CreatedAt DESC LIMIT 1
            )
            WHERE h.Id = @id
            """;
        cmd.Parameters.AddWithValue("@id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return ReadHero(reader);
    }

    public async Task<List<HeroSnapshotMeta>> GetSnapshotsAsync(long heroId)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, HeroId, SessionNumber, SessionLabel, Level, CreatedAt FROM HeroSnapshots WHERE HeroId = @id ORDER BY CreatedAt DESC";
        cmd.Parameters.AddWithValue("@id", heroId);
        var results = new List<HeroSnapshotMeta>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(new HeroSnapshotMeta(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetString(3), reader.GetInt32(4), DateTime.Parse(reader.GetString(5))));
        return results;
    }

    public async Task<HeroSnapshot?> GetSnapshotAsync(long snapshotId)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, HeroId, SessionNumber, SessionLabel, Level, CreatedAt, CharacterJson FROM HeroSnapshots WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", snapshotId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new HeroSnapshot(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetString(3), reader.GetInt32(4), DateTime.Parse(reader.GetString(5)),
            JsonSerializer.Deserialize<CharacterSheet>(reader.GetString(6))!);
    }

    public async Task<long> CreateAsync(long campaignId, string name)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Heroes (CampaignId, Name, CreatedAt) VALUES (@c, @n, @t); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@c", campaignId);
        cmd.Parameters.AddWithValue("@n", name);
        cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O"));
        var heroId = (long)(await cmd.ExecuteScalarAsync())!;

        await using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "INSERT INTO HeroSnapshots (HeroId, SessionNumber, SessionLabel, Level, CreatedAt, CharacterJson) VALUES (@h, 0, 'Created', 0, @t, @j)";
        cmd2.Parameters.AddWithValue("@h", heroId);
        cmd2.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O"));
        cmd2.Parameters.AddWithValue("@j", JsonSerializer.Serialize(new CharacterSheet()));
        await cmd2.ExecuteNonQueryAsync();
        return heroId;
    }

    public async Task SaveSnapshotAsync(long heroId, int sessionNumber, string sessionLabel, CharacterSheet sheet)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO HeroSnapshots (HeroId, SessionNumber, SessionLabel, Level, CreatedAt, CharacterJson) VALUES (@h, @sn, @sl, @lv, @t, @j)";
        cmd.Parameters.AddWithValue("@h", heroId);
        cmd.Parameters.AddWithValue("@sn", sessionNumber);
        cmd.Parameters.AddWithValue("@sl", sessionLabel);
        cmd.Parameters.AddWithValue("@lv", sheet.Level);
        cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@j", JsonSerializer.Serialize(sheet));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(long id)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM HeroSnapshots WHERE HeroId = @id; DELETE FROM Heroes WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    private static Hero ReadHero(SqliteDataReader reader)
    {
        HeroSnapshot? snapshot = null;
        if (!reader.IsDBNull(4))
        {
            snapshot = new HeroSnapshot(
                reader.GetInt64(4), reader.GetInt64(0),
                reader.GetInt32(5), reader.GetString(6), reader.GetInt32(7),
                DateTime.Parse(reader.GetString(8)),
                JsonSerializer.Deserialize<CharacterSheet>(reader.GetString(9))!);
        }
        return new Hero(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), DateTime.Parse(reader.GetString(3)), snapshot);
    }
}
```

- [x] **Step 3.4: Run tests — all 6 must pass**

```bash
dotnet test DndMcpAICompanion.Tests/DndMcpAICompanion.Tests.csproj \
  --filter "FullyQualifiedName~HeroRepositoryTests" 2>&1 | tail -5
```

Expected: `Passed! - Failed: 0, Passed: 6`

- [x] **Step 3.5: Run full companion test suite — no regressions**

```bash
dotnet test DndMcpAICompanion.Tests/DndMcpAICompanion.Tests.csproj 2>&1 | tail -5
```

Expected: all previously passing tests still pass.

- [x] **Step 3.6: Commit**

```bash
git add DndMcpAICompanion/Features/Campaign/HeroRepository.cs \
  DndMcpAICompanion.Tests/Campaign/HeroRepositoryTests.cs
git commit -m "feat(campaign): add HeroRepository with Memento snapshot support"
```

---

## Task 4: Register Repositories in Program.cs

**Files:**

- Modify: `DndMcpAICompanion/Program.cs`

- [x] **Step 4.1: Add registrations to Program.cs**

After the line `builder.Services.AddSingleton(new UserRepository(connectionString));`, add:

```csharp
builder.Services.AddSingleton(new CampaignRepository(connectionString));
builder.Services.AddSingleton(new HeroRepository(connectionString));
```

Add this using at the top with the other using statements:

```csharp
using DndMcpAICompanion.Features.Campaign;
```

After `await userRepo.InitializeAsync();` in the startup section, add:

```csharp
var campaignRepo = app.Services.GetRequiredService<CampaignRepository>();
await campaignRepo.InitializeAsync();
```

- [x] **Step 4.2: Build to verify**

```bash
dotnet build DndMcpAICompanion/DndMcpAICompanion.csproj 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Error(s)`

- [x] **Step 4.3: Commit**

```bash
git add DndMcpAICompanion/Program.cs
git commit -m "feat(campaign): register CampaignRepository and HeroRepository at startup"
```

---

## Task 5: Campaigns.razor

**Files:**

- Create: `DndMcpAICompanion/Components/Pages/Campaign/Campaigns.razor`

- [x] **Step 5.1: Create `DndMcpAICompanion/Components/Pages/Campaign/Campaigns.razor`**

```razor
@page "/campaigns"
@rendermode InteractiveServer
@using Microsoft.AspNetCore.Authorization
@using System.Security.Claims
@using DndMcpAICompanion.Features.Campaign
@attribute [Authorize]
@inject CampaignRepository CampaignRepo
@inject NavigationManager Nav
@inject AuthenticationStateProvider Auth

<PageTitle>Campaigns</PageTitle>

<div class="campaigns-page">
    <h1>My Campaigns</h1>

    @if (_campaigns is null)
    {
        <p>Loading...</p>
    }
    else if (_campaigns.Count == 0 && !_showCreateForm)
    {
        <p>No campaigns yet. Create your first one!</p>
    }
    else
    {
        <div class="campaign-grid">
            @foreach (var c in _campaigns)
            {
                <div class="campaign-card">
                    <div class="campaign-card-body" @onclick="() => Nav.NavigateTo($\"/campaigns/{c.Id}\")">
                        <h3>@c.Name</h3>
                        @if (!string.IsNullOrEmpty(c.Description))
                        {
                            <p>@c.Description</p>
                        }
                        <small>@c.HeroCount hero@(c.HeroCount == 1 ? "" : "es") · @c.CreatedAt.ToString("yyyy-MM-dd")</small>
                    </div>
                    <div class="campaign-card-actions">
                        @if (_deleteConfirmId == c.Id)
                        {
                            <button class="btn-danger" @onclick="() => ConfirmDeleteAsync(c.Id)">Confirm</button>
                            <button @onclick="() => _deleteConfirmId = null">Cancel</button>
                        }
                        else
                        {
                            <button @onclick="() => _deleteConfirmId = c.Id">Delete</button>
                        }
                    </div>
                </div>
            }
        </div>
    }

    @if (_showCreateForm)
    {
        <div class="create-form">
            <h3>New Campaign</h3>
            <label>Name <input @bind="_newName" placeholder="e.g. Curse of Strahd" /></label>
            <label>Description <input @bind="_newDescription" placeholder="Optional" /></label>
            @if (!string.IsNullOrEmpty(_createError))
            {
                <p class="error">@_createError</p>
            }
            <div class="form-actions">
                <button @onclick="CreateAsync">Create</button>
                <button @onclick="() => { _showCreateForm = false; _createError = \"\"; }">Cancel</button>
            </div>
        </div>
    }
    else
    {
        <button class="btn-primary" @onclick="() => _showCreateForm = true">+ New Campaign</button>
    }
</div>

@code {
    private List<CampaignSummary>? _campaigns;
    private long _userId;
    private bool _showCreateForm;
    private string _newName = "";
    private string _newDescription = "";
    private string _createError = "";
    private long? _deleteConfirmId;

    protected override async Task OnInitializedAsync()
    {
        var state = await Auth.GetAuthenticationStateAsync();
        _userId = long.Parse(state.User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _campaigns = await CampaignRepo.GetAllAsync(_userId);
    }

    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(_newName))
        {
            _createError = "Name is required.";
            return;
        }
        await CampaignRepo.CreateAsync(_userId, _newName.Trim(), _newDescription.Trim());
        _newName = "";
        _newDescription = "";
        _createError = "";
        _showCreateForm = false;
        await LoadAsync();
    }

    private async Task ConfirmDeleteAsync(long id)
    {
        await CampaignRepo.DeleteAsync(id, _userId);
        _deleteConfirmId = null;
        await LoadAsync();
    }
}
```

- [x] **Step 5.2: Build companion**

```bash
dotnet build DndMcpAICompanion/DndMcpAICompanion.csproj 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Error(s)`

- [x] **Step 5.3: Commit**

```bash
git add DndMcpAICompanion/Components/Pages/Campaign/Campaigns.razor
git commit -m "feat(campaign): add Campaigns list page"
```

---

## Task 6: CampaignDetail.razor

**Files:**

- Create: `DndMcpAICompanion/Components/Pages/Campaign/CampaignDetail.razor`

- [x] **Step 6.1: Create `DndMcpAICompanion/Components/Pages/Campaign/CampaignDetail.razor`**

```razor
@page "/campaigns/{Id:long}"
@rendermode InteractiveServer
@using Microsoft.AspNetCore.Authorization
@using System.Security.Claims
@using DndMcpAICompanion.Features.Campaign
@attribute [Authorize]
@inject CampaignRepository CampaignRepo
@inject HeroRepository HeroRepo
@inject NavigationManager Nav
@inject AuthenticationStateProvider Auth

<PageTitle>@(_campaign?.Name ?? "Campaign")</PageTitle>

@if (_campaign is null)
{
    <p>Loading...</p>
}
else
{
    <div class="campaign-detail">
        <nav><a href="/campaigns">← Campaigns</a></nav>
        <h1>@_campaign.Name</h1>
        @if (!string.IsNullOrEmpty(_campaign.Description))
        {
            <p class="campaign-desc">@_campaign.Description</p>
        }

        <h2>Party</h2>
        <div class="hero-roster">
            @foreach (var hero in _heroes)
            {
                var sheet = hero.LatestSnapshot?.Sheet;
                <div class="hero-card" @onclick="() => Nav.NavigateTo($\"/campaigns/{Id}/heroes/{hero.Id}\")">
                    <strong>@hero.Name</strong>
                    @if (sheet is not null)
                    {
                        <span>@sheet.Class @(string.IsNullOrEmpty(sheet.Subclass) ? "" : $"({sheet.Subclass})") · Lv @sheet.Level</span>
                        <span>HP @sheet.CurrentHitPoints / @sheet.MaxHitPoints · AC @sheet.ArmorClass</span>
                    }
                </div>
            }
        </div>

        @if (_showAddHero)
        {
            <div class="add-hero-form">
                <input @bind="_newHeroName" placeholder="Hero name" />
                @if (!string.IsNullOrEmpty(_addHeroError))
                {
                    <p class="error">@_addHeroError</p>
                }
                <button @onclick="AddHeroAsync">Add</button>
                <button @onclick="() => { _showAddHero = false; _addHeroError = \"\"; }">Cancel</button>
            </div>
        }
        else
        {
            <button @onclick="() => _showAddHero = true">+ Add Hero</button>
        }
    </div>
}

@code {
    [Parameter] public long Id { get; set; }

    private Campaign? _campaign;
    private List<Hero> _heroes = [];
    private long _userId;
    private bool _showAddHero;
    private string _newHeroName = "";
    private string _addHeroError = "";

    protected override async Task OnInitializedAsync()
    {
        var state = await Auth.GetAuthenticationStateAsync();
        _userId = long.Parse(state.User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        _campaign = await CampaignRepo.GetByIdAsync(Id, _userId);
        if (_campaign is null)
        {
            Nav.NavigateTo("/campaigns");
            return;
        }
        _heroes = await HeroRepo.GetByCampaignAsync(Id);
    }

    private async Task AddHeroAsync()
    {
        if (string.IsNullOrWhiteSpace(_newHeroName))
        {
            _addHeroError = "Name is required.";
            return;
        }
        var heroId = await HeroRepo.CreateAsync(Id, _newHeroName.Trim());
        Nav.NavigateTo($"/campaigns/{Id}/heroes/{heroId}");
    }
}
```

- [x] **Step 6.2: Build companion**

```bash
dotnet build DndMcpAICompanion/DndMcpAICompanion.csproj 2>&1 | tail -5
```

Expected: `Build succeeded. 0 Error(s)`

- [x] **Step 6.3: Commit**

```bash
git add DndMcpAICompanion/Components/Pages/Campaign/CampaignDetail.razor
git commit -m "feat(campaign): add CampaignDetail party roster page"
```

---

## Task 7: HeroDetail.razor

**Files:**

- Create: `DndMcpAICompanion/Components/Pages/Campaign/HeroDetail.razor`

- [x] **Step 7.1: Create `DndMcpAICompanion/Components/Pages/Campaign/HeroDetail.razor`**

```razor
@page "/campaigns/{CampaignId:long}/heroes/{HeroId:long}"
@rendermode InteractiveServer
@using Microsoft.AspNetCore.Authorization
@using System.Text.Json
@using DndMcpAICompanion.Features.Campaign
@attribute [Authorize]
@inject HeroRepository HeroRepo
@inject NavigationManager Nav

<PageTitle>@(_hero?.Name ?? "Hero")</PageTitle>

@if (_hero is null)
{
    <p>Loading...</p>
}
else
{
    <div class="hero-detail">
        <nav><a href="/campaigns/@CampaignId">← Campaign</a></nav>

        <div class="hero-header-bar">
            <h1>@_hero.Name</h1>
            @if (!_editMode)
            {
                <button @onclick="EnterEdit">Edit</button>
            }
        </div>

        @{
            var sheet = _editMode ? _editSheet : (_viewingSnapshot?.Sheet ?? _hero.LatestSnapshot?.Sheet ?? new CharacterSheet());
            var readOnly = !_editMode;
        }

        @* Identity *@
        <div class="sheet-section identity-section">
            @if (readOnly)
            {
                <div class="identity-grid">
                    <span><b>Race:</b> @sheet.Race</span>
                    <span><b>Class:</b> @sheet.Class @(string.IsNullOrEmpty(sheet.Subclass) ? "" : $"({sheet.Subclass})")</span>
                    <span><b>Level:</b> @sheet.Level</span>
                    <span><b>Background:</b> @sheet.Background</span>
                    <span><b>Alignment:</b> @sheet.Alignment</span>
                    <span><b>XP:</b> @sheet.ExperiencePoints</span>
                </div>
            }
            else
            {
                <div class="edit-grid">
                    <label>Race <input @bind="_editSheet.Race" /></label>
                    <label>Class <input @bind="_editSheet.Class" /></label>
                    <label>Subclass <input @bind="_editSheet.Subclass" /></label>
                    <label>Level <input type="number" @bind="_editSheet.Level" /></label>
                    <label>Background <input @bind="_editSheet.Background" /></label>
                    <label>Alignment <input @bind="_editSheet.Alignment" /></label>
                    <label>XP <input type="number" @bind="_editSheet.ExperiencePoints" /></label>
                </div>
            }
        </div>

        @* Ability Scores *@
        <div class="sheet-section">
            <h3>Ability Scores</h3>
            <div class="ability-grid">
                @foreach (var (label, score, setter) in AbilityScores(sheet))
                {
                    <div class="ability-block">
                        <span class="ability-label">@label</span>
                        @if (readOnly)
                        {
                            <span class="ability-score">@score</span>
                            <span class="ability-mod">@CharacterSheet.ModifierStr(score)</span>
                        }
                        else
                        {
                            <input type="number" class="ability-input" value="@score" @onchange="e => setter(int.TryParse(e.Value?.ToString(), out var v) ? v : score)" />
                            <span class="ability-mod">@CharacterSheet.ModifierStr(score)</span>
                        }
                    </div>
                }
            </div>
        </div>

        @* Combat *@
        <div class="sheet-section">
            <h3>Combat</h3>
            @if (readOnly)
            {
                <div class="combat-stats">
                    <span><b>HP:</b> @sheet.CurrentHitPoints / @sheet.MaxHitPoints</span>
                    <span><b>AC:</b> @sheet.ArmorClass</span>
                    <span><b>Speed:</b> @sheet.Speed ft</span>
                    <span><b>Initiative:</b> @CharacterSheet.ModifierStr(sheet.Initiative + 10)</span>
                    <span><b>Prof. Bonus:</b> +@sheet.ProficiencyBonus</span>
                </div>
            }
            else
            {
                <div class="edit-grid">
                    <label>Max HP <input type="number" @bind="_editSheet.MaxHitPoints" /></label>
                    <label>Current HP <input type="number" @bind="_editSheet.CurrentHitPoints" /></label>
                    <label>AC <input type="number" @bind="_editSheet.ArmorClass" /></label>
                    <label>Speed <input type="number" @bind="_editSheet.Speed" /></label>
                    <label>Initiative <input type="number" @bind="_editSheet.Initiative" /></label>
                    <label>Prof. Bonus <input type="number" @bind="_editSheet.ProficiencyBonus" /></label>
                </div>
            }
        </div>

        @* Spellcasting *@
        @if (!string.IsNullOrEmpty(sheet.SpellcastingAbility) || _editMode)
        {
            <div class="sheet-section">
                <h3>Spellcasting</h3>
                @if (readOnly)
                {
                    <p>Ability: @sheet.SpellcastingAbility · Save DC: @sheet.SpellSaveDC · Attack: +@sheet.SpellAttackBonus</p>
                    <div class="spell-slots">
                        @for (int i = 0; i < 9; i++)
                        {
                            if (sheet.SpellSlots[i] > 0)
                            {
                                <span>Lv @(i + 1): @(sheet.SpellSlots[i] - sheet.UsedSpellSlots[i])/@sheet.SpellSlots[i]</span>
                            }
                        }
                    </div>
                    @if (sheet.SpellsKnown.Count > 0)
                    {
                        <p><b>Spells:</b> @string.Join(", ", sheet.SpellsKnown)</p>
                    }
                }
                else
                {
                    <div class="edit-grid">
                        <label>Casting Ability <input @bind="_editSheet.SpellcastingAbility" /></label>
                        <label>Save DC <input type="number" @bind="_editSheet.SpellSaveDC" /></label>
                        <label>Attack Bonus <input type="number" @bind="_editSheet.SpellAttackBonus" /></label>
                    </div>
                    <div class="slot-grid">
                        @for (int i = 0; i < 9; i++)
                        {
                            var lvl = i;
                            <label>Lv @(lvl + 1) slots <input type="number" value="@_editSheet.SpellSlots[lvl]" @onchange="e => _editSheet.SpellSlots[lvl] = int.TryParse(e.Value?.ToString(), out var v) ? v : 0" /></label>
                        }
                    </div>
                    <label>Spells Known (one per line)
                        <textarea rows="4" @bind="_spellsText"></textarea>
                    </label>
                }
            </div>
        }

        @* Proficiencies *@
        <div class="sheet-section">
            <h3>Proficiencies & Languages</h3>
            @if (readOnly)
            {
                @if (sheet.ArmorProficiencies.Count > 0) { <p><b>Armor:</b> @string.Join(", ", sheet.ArmorProficiencies)</p> }
                @if (sheet.WeaponProficiencies.Count > 0) { <p><b>Weapons:</b> @string.Join(", ", sheet.WeaponProficiencies)</p> }
                @if (sheet.ToolProficiencies.Count > 0) { <p><b>Tools:</b> @string.Join(", ", sheet.ToolProficiencies)</p> }
                @if (sheet.Languages.Count > 0) { <p><b>Languages:</b> @string.Join(", ", sheet.Languages)</p> }
                @if (sheet.SkillProficiencies.Count > 0) { <p><b>Skills:</b> @string.Join(", ", sheet.SkillProficiencies)</p> }
            }
            else
            {
                <label>Armor (one per line) <textarea rows="2" @bind="_armorProfText"></textarea></label>
                <label>Weapons (one per line) <textarea rows="2" @bind="_weaponProfText"></textarea></label>
                <label>Tools (one per line) <textarea rows="2" @bind="_toolProfText"></textarea></label>
                <label>Languages (one per line) <textarea rows="2" @bind="_languagesText"></textarea></label>
                <label>Skills (one per line) <textarea rows="3" @bind="_skillProfText"></textarea></label>
            }
        </div>

        @* Features *@
        <div class="sheet-section">
            <h3>Features & Traits</h3>
            @if (readOnly)
            {
                @foreach (var f in sheet.Features)
                {
                    <div class="feature-item">
                        <b>@f.Name</b>
                        @if (!string.IsNullOrEmpty(f.Description)) { <p>@f.Description</p> }
                    </div>
                }
            }
            else
            {
                @for (int i = 0; i < _editSheet.Features.Count; i++)
                {
                    var idx = i;
                    <div class="feature-edit">
                        <input placeholder="Feature name" @bind="_editSheet.Features[idx].Name" />
                        <input placeholder="Description" @bind="_editSheet.Features[idx].Description" />
                        <button @onclick="() => _editSheet.Features.RemoveAt(idx)">Remove</button>
                    </div>
                }
                <button @onclick="() => _editSheet.Features.Add(new CharacterFeature())">+ Add Feature</button>
            }
        </div>

        @* Equipment *@
        <div class="sheet-section">
            <h3>Equipment</h3>
            @if (readOnly)
            {
                <ul>@foreach (var e in sheet.Equipment) { <li>@e</li> }</ul>
            }
            else
            {
                <label>Equipment (one per line) <textarea rows="4" @bind="_equipmentText"></textarea></label>
            }
        </div>

        @* Edit actions *@
        @if (_editMode)
        {
            <div class="edit-actions">
                @if (_showSavePrompt)
                {
                    <div class="save-prompt">
                        <label>Session # <input type="number" @bind="_sessionNumber" /></label>
                        <label>Label <input @bind="_sessionLabel" placeholder="e.g. After Session 3" /></label>
                        @if (!string.IsNullOrEmpty(_saveError)) { <p class="error">@_saveError</p> }
                        <button @onclick="ConfirmSaveAsync">Save Snapshot</button>
                        <button @onclick="() => _showSavePrompt = false">Cancel</button>
                    </div>
                }
                else
                {
                    <button class="btn-primary" @onclick="() => _showSavePrompt = true">Save Character</button>
                    <button @onclick="CancelEdit">Cancel</button>
                }
            </div>
        }

        @* Progression History *@
        <div class="sheet-section history-section">
            <h3>Progression History</h3>
            @if (_viewingSnapshot is not null)
            {
                <p class="history-notice">Viewing snapshot: Session @_viewingSnapshot.SessionNumber — @_viewingSnapshot.SessionLabel
                    <button @onclick="() => _viewingSnapshot = null">Back to current</button>
                </p>
            }
            @foreach (var snap in _snapshots)
            {
                <div class="history-item @(_viewingSnapshot?.Id == snap.Id ? "active" : "")"
                     @onclick="() => ViewSnapshotAsync(snap.Id)">
                    <b>Session @snap.SessionNumber</b>
                    @if (!string.IsNullOrEmpty(snap.SessionLabel)) { <span>— @snap.SessionLabel</span> }
                    <small>Level @snap.Level · @snap.CreatedAt.ToString("yyyy-MM-dd")</small>
                </div>
            }
        </div>
    </div>
}

@code {
    [Parameter] public long CampaignId { get; set; }
    [Parameter] public long HeroId { get; set; }

    private Hero? _hero;
    private List<HeroSnapshotMeta> _snapshots = [];
    private HeroSnapshot? _viewingSnapshot;

    private bool _editMode;
    private CharacterSheet _editSheet = new();
    private bool _showSavePrompt;
    private int _sessionNumber;
    private string _sessionLabel = "";
    private string _saveError = "";

    // Textarea bindings for list fields
    private string _spellsText = "";
    private string _armorProfText = "";
    private string _weaponProfText = "";
    private string _toolProfText = "";
    private string _languagesText = "";
    private string _skillProfText = "";
    private string _equipmentText = "";

    protected override async Task OnInitializedAsync()
    {
        _hero = await HeroRepo.GetByIdAsync(HeroId);
        if (_hero is null)
        {
            Nav.NavigateTo($"/campaigns/{CampaignId}");
            return;
        }
        _snapshots = await HeroRepo.GetSnapshotsAsync(HeroId);
    }

    private void EnterEdit()
    {
        var src = _hero!.LatestSnapshot?.Sheet ?? new CharacterSheet();
        // Deep copy via JSON
        _editSheet = JsonSerializer.Deserialize<CharacterSheet>(JsonSerializer.Serialize(src))!;
        _spellsText = string.Join("\n", _editSheet.SpellsKnown);
        _armorProfText = string.Join("\n", _editSheet.ArmorProficiencies);
        _weaponProfText = string.Join("\n", _editSheet.WeaponProficiencies);
        _toolProfText = string.Join("\n", _editSheet.ToolProficiencies);
        _languagesText = string.Join("\n", _editSheet.Languages);
        _skillProfText = string.Join("\n", _editSheet.SkillProficiencies);
        _equipmentText = string.Join("\n", _editSheet.Equipment);
        _editMode = true;
        _viewingSnapshot = null;
    }

    private void CancelEdit()
    {
        _editMode = false;
        _showSavePrompt = false;
        _saveError = "";
    }

    private async Task ConfirmSaveAsync()
    {
        // Sync list fields from textarea bindings
        _editSheet.SpellsKnown = Split(_spellsText);
        _editSheet.ArmorProficiencies = Split(_armorProfText);
        _editSheet.WeaponProficiencies = Split(_weaponProfText);
        _editSheet.ToolProficiencies = Split(_toolProfText);
        _editSheet.Languages = Split(_languagesText);
        _editSheet.SkillProficiencies = Split(_skillProfText);
        _editSheet.Equipment = Split(_equipmentText);

        try
        {
            await HeroRepo.SaveSnapshotAsync(HeroId, _sessionNumber, _sessionLabel.Trim(), _editSheet);
            _hero = await HeroRepo.GetByIdAsync(HeroId);
            _snapshots = await HeroRepo.GetSnapshotsAsync(HeroId);
            _editMode = false;
            _showSavePrompt = false;
            _sessionLabel = "";
            _saveError = "";
        }
        catch (Exception ex)
        {
            _saveError = $"Save failed: {ex.Message}";
        }
    }

    private async Task ViewSnapshotAsync(long snapshotId)
    {
        if (_viewingSnapshot?.Id == snapshotId)
        {
            _viewingSnapshot = null;
            return;
        }
        _viewingSnapshot = await HeroRepo.GetSnapshotAsync(snapshotId);
    }

    private static List<string> Split(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static IEnumerable<(string Label, int Score, Action<int> Setter)> AbilityScores(CharacterSheet s) =>
    [
        ("STR", s.Strength, _ => { }),
        ("DEX", s.Dexterity, _ => { }),
        ("CON", s.Constitution, _ => { }),
        ("INT", s.Intelligence, _ => { }),
        ("WIS", s.Wisdom, _ => { }),
        ("CHA", s.Charisma, _ => { }),
    ];

    // Edit-mode ability setters (called by onchange handlers above)
    private void SetStr(int v) => _editSheet.Strength = v;
    private void SetDex(int v) => _editSheet.Dexterity = v;
    private void SetCon(int v) => _editSheet.Constitution = v;
    private void SetInt(int v) => _editSheet.Intelligence = v;
    private void SetWis(int v) => _editSheet.Wisdom = v;
    private void SetCha(int v) => _editSheet.Charisma = v;
}
```

> **Note:** The `AbilityScores` helper returns no-op setters in view mode. In edit mode, the `@onchange` handlers directly set the `_editSheet` fields. If the compiler objects to the lambda in the `@onchange` handler referencing `score`, replace the shared `AbilityScores()` loop with six explicit ability-score blocks (STR/DEX/CON/INT/WIS/CHA) with direct bindings: `<input type="number" @bind="_editSheet.Strength" />` etc.

- [x] **Step 7.2: Build companion**

```bash
dotnet build DndMcpAICompanion/DndMcpAICompanion.csproj 2>&1 | tail -8
```

If there are build errors in the razor file (common with complex lambda expressions in Blazor), simplify the ability scores section by replacing the `AbilityScores()` loop with six explicit blocks:

```razor
@* Replace the ability loop in edit mode with: *@
<label>STR <input type="number" @bind="_editSheet.Strength" /></label>
<label>DEX <input type="number" @bind="_editSheet.Dexterity" /></label>
<label>CON <input type="number" @bind="_editSheet.Constitution" /></label>
<label>INT <input type="number" @bind="_editSheet.Intelligence" /></label>
<label>WIS <input type="number" @bind="_editSheet.Wisdom" /></label>
<label>CHA <input type="number" @bind="_editSheet.Charisma" /></label>
```

Expected: `Build succeeded. 0 Error(s)`

- [x] **Step 7.3: Run full test suite**

```bash
dotnet test DndMcpAICompanion.Tests/DndMcpAICompanion.Tests.csproj 2>&1 | tail -5
```

Expected: all tests pass.

- [x] **Step 7.4: Commit**

```bash
git add DndMcpAICompanion/Components/Pages/Campaign/HeroDetail.razor
git commit -m "feat(campaign): add HeroDetail structured character sheet with edit and history"
```
