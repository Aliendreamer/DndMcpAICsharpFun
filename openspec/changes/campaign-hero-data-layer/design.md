## Context

The companion already has per-user auth (SQLite, cookie auth) and a chat page. This feature adds campaign and character management so users can track their D&D parties and character progression over time.

Campaigns and heroes are personal — one user owns them, no sharing. A hero belongs to exactly one campaign. The Memento pattern is used for character progression: each save of a hero creates an immutable snapshot labeled with a session number. The latest snapshot is the current character state; older snapshots form the progression history.

## Goals / Non-Goals

**Goals:**

- Create, view, and delete campaigns per user
- Add heroes to a campaign, view and edit their full character sheet
- Each hero edit creates a new snapshot (Memento) labeled with a session number/label
- View progression history: past snapshots as read-only character sheets
- Structured character sheet UI (ability scores block, combat stats, spells, features, equipment)
- Unit tests for repositories and CharacterSheet serialization

**Non-Goals:**

- Sharing campaigns between users
- Importing characters from D&D Beyond or external formats
- AI integration with party data (separate spec)
- Real-time collaboration
- Deleting individual snapshots

## Architecture

```
/campaigns                     Campaigns.razor
  /campaigns/{id}              CampaignDetail.razor
  /campaigns/{id}/heroes/{hId} HeroDetail.razor
          │
          ▼
    CampaignRepository          HeroRepository
    (raw ADO.NET, companion.db) (raw ADO.NET, companion.db)
          │
          ▼
    Campaigns table    Heroes table    HeroSnapshots table
                                       (CharacterSheet JSON blob)
```

No new HTTP endpoints — all data access is server-side Blazor (same pattern as existing auth/chat pages).

## Data Model

### Campaigns
```sql
CREATE TABLE IF NOT EXISTS Campaigns (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId      INTEGER NOT NULL,
    Name        TEXT    NOT NULL,
    Description TEXT    NOT NULL DEFAULT '',
    CreatedAt   TEXT    NOT NULL
);
```

### Heroes
```sql
CREATE TABLE IF NOT EXISTS Heroes (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    CampaignId INTEGER NOT NULL REFERENCES Campaigns(Id),
    Name       TEXT    NOT NULL,
    CreatedAt  TEXT    NOT NULL
);
```

### HeroSnapshots
```sql
CREATE TABLE IF NOT EXISTS HeroSnapshots (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    HeroId         INTEGER NOT NULL REFERENCES Heroes(Id),
    SessionNumber  INTEGER NOT NULL,
    SessionLabel   TEXT    NOT NULL DEFAULT '',
    CreatedAt      TEXT    NOT NULL,
    CharacterJson  TEXT    NOT NULL
);
```

Latest snapshot for a hero = `SELECT * FROM HeroSnapshots WHERE HeroId = @id ORDER BY CreatedAt DESC LIMIT 1`. Saving a hero always inserts a new row — never updates existing ones.

## CharacterSheet JSON Model

```csharp
public sealed record CharacterSheet
{
    // Identity
    public string Race { get; init; } = "";
    public string Class { get; init; } = "";
    public string Subclass { get; init; } = "";
    public string Background { get; init; } = "";
    public int Level { get; init; }
    public string Alignment { get; init; } = "";
    public int ExperiencePoints { get; init; }

    // Ability scores
    public int Strength { get; init; }
    public int Dexterity { get; init; }
    public int Constitution { get; init; }
    public int Intelligence { get; init; }
    public int Wisdom { get; init; }
    public int Charisma { get; init; }

    // Combat
    public int MaxHitPoints { get; init; }
    public int CurrentHitPoints { get; init; }
    public int ArmorClass { get; init; }
    public int Speed { get; init; }
    public int Initiative { get; init; }
    public int ProficiencyBonus { get; init; }

    // Spellcasting
    public string SpellcastingAbility { get; init; } = "";
    public int SpellSaveDC { get; init; }
    public int SpellAttackBonus { get; init; }
    public int[] SpellSlots { get; init; } = new int[9];      // slots by level 1-9
    public int[] UsedSpellSlots { get; init; } = new int[9];
    public List<string> SpellsKnown { get; init; } = [];

    // Proficiencies
    public List<string> ArmorProficiencies { get; init; } = [];
    public List<string> WeaponProficiencies { get; init; } = [];
    public List<string> ToolProficiencies { get; init; } = [];
    public List<string> Languages { get; init; } = [];
    public List<string> SkillProficiencies { get; init; } = [];

    // Features & Equipment
    public List<CharacterFeature> Features { get; init; } = [];
    public List<string> Equipment { get; init; } = [];
}

public sealed record CharacterFeature(string Name, string Description);
```

Ability modifier = `(score - 10) / 2` (integer division, computed in the UI, not stored).

## Components

### `Features/Campaign/CharacterSheet.cs`
The strongly-typed record above. Serialized with `System.Text.Json` (default options). No custom converters needed.

### `Features/Campaign/CampaignRepository.cs`
Raw ADO.NET, `Microsoft.Data.Sqlite`. Methods:

- `InitializeAsync()` — creates all three tables if not exists
- `GetAllAsync(long userId)` — returns all campaigns for user
- `GetByIdAsync(long id, long userId)` — single campaign, null if not found or wrong user
- `CreateAsync(long userId, string name, string description)` — inserts, returns new id
- `DeleteAsync(long id, long userId)` — deletes hero+snapshot rows first, then campaign row (SQLite foreign keys are disabled by default; deletion order enforced in code)

### `Features/Campaign/HeroRepository.cs`
Raw ADO.NET. Methods:

- `GetByCampaignAsync(long campaignId)` — all heroes for a campaign (with latest snapshot)
- `GetByIdAsync(long id)` — single hero + latest snapshot
- `GetSnapshotsAsync(long heroId)` — all snapshots ordered by CreatedAt DESC (metadata only, no JSON)
- `GetSnapshotAsync(long snapshotId)` — single snapshot with JSON (for history view)
- `CreateAsync(long campaignId, string name)` — inserts hero, inserts blank snapshot at session 0
- `SaveSnapshotAsync(long heroId, int sessionNumber, string sessionLabel, CharacterSheet sheet)` — inserts new snapshot
- `DeleteAsync(long id)` — deletes hero and all its snapshots

### Blazor Pages

**`Components/Pages/Campaign/Campaigns.razor`** (`/campaigns`)

- Grid of campaign cards: name, hero count, last updated
- "New Campaign" inline form (name + description)
- Delete button per campaign (with confirm)
- Requires auth (`[Authorize]`)

**`Components/Pages/Campaign/CampaignDetail.razor`** (`/campaigns/{Id:int}`)

- Campaign name + description header
- Party roster: one card per hero (name, class, level, current HP / max HP)
- "Add Hero" button (name input, then redirect to hero detail)
- Clicking a hero card navigates to hero detail

**`Components/Pages/Campaign/HeroDetail.razor`** (`/campaigns/{CampaignId:int}/heroes/{HeroId:int}`)

View mode — structured sheet layout:

- **Header**: name, race, class/subclass, level, background, XP
- **Ability Scores**: 6-block grid with score and computed modifier
- **Combat**: HP bar (current/max), AC, Speed, Initiative, Proficiency Bonus
- **Spellcasting**: ability, save DC, attack bonus, slot grid (9 levels), spells known
- **Proficiencies & Languages**: grouped chips/list
- **Features & Traits**: expandable list (name bold, description below)
- **Equipment**: item list
- **Progression history**: snapshot list (session label, date, level); clicking shows read-only past sheet

Edit mode — same layout, each field becomes an input. On "Save": modal prompts for session number + label → `SaveSnapshotAsync` → back to view mode.

## Error Handling

- Campaign not found or wrong user → redirect to `/campaigns`
- Hero not found → redirect to campaign detail
- Save failure → inline error message, form data preserved, no snapshot written
- All repository methods use parameterized queries (no SQL injection risk)

## Testing

- `CampaignRepositoryTests` — in-memory SQLite; create, get, delete, wrong-user isolation
- `HeroRepositoryTests` — create hero, save snapshot, get latest, get history, delete
- `CharacterSheetSerializationTests` — round-trip: populate all fields → serialize → deserialize → assert equality
- No Blazor component tests
