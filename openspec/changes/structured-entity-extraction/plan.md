# Structured Entity Extraction — Plan 1: Entity Foundation + Retrieval

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Series tracking:** This is **Plan 1 of 3**. See `series.md` in this directory for the full plan series. Plans 2 (LLM extraction) and 3 (backfill/rollout) are written after this one ships.

**Goal:** Build the entity data model, canonical JSON loader, Qdrant entity collection, ingestion path, and retrieval endpoints — using hand-written canonical JSON fixtures, **without any LLM dependency**.

**Architecture:** A new `dnd_entities` Qdrant collection sits alongside the existing `dnd_blocks`. A canonical JSON file at `data/canonical/<book-slug>.json` is loaded, validated, embedded, and upserted into the entity collection. New `/retrieval/entities/*` endpoints expose lookup-by-id and vector search with structured filters. The block index and its endpoints are unchanged.

**Tech Stack:** .NET 10, ASP.NET Core minimal API, System.Text.Json, Qdrant .NET client, EF Core (SQLite), xUnit, FluentAssertions, Testcontainers (existing), Serena for all code edits.

**Spec coverage in this plan:** `structured-entities` spec (full), `entity-vector-store` spec (full), `rag-retrieval` delta (full), `ingestion-pipeline` delta (entity-ingestion parts; entity-extraction parts deferred to Plan 2).

---

## File Structure

### Domain types (new)
- `Domain/Entities/EntityType.cs` — enum of 20 entity types
- `Domain/Entities/Provenance.cs` — `FirstAppearance`, `Revision` records
- `Domain/Entities/EntityEnvelope.cs` — common envelope record (generic over `TFields`)
- `Domain/Entities/Spellcasting.cs` — shared spellcasting block (used by Class & Monster)
- `Domain/Entities/Fields/ClassFields.cs` — Class-specific fields
- `Domain/Entities/Fields/MonsterFields.cs` — Monster-specific fields
- `Domain/Entities/Fields/SpellFields.cs` — Spell-specific fields (used in fixture)
- `Domain/Entities/Fields/<Other>Fields.cs` — 14 remaining per-type field records
- `Domain/Entities/CanonicalJsonFile.cs` — top-level JSON envelope (schemaVersion, book, entities[])

### Slug + ID (new)
- `Domain/Entities/EntityIdSlug.cs` — deterministic slug generator

### Canonical JSON loading (new)
- `Features/Entities/CanonicalJsonLoader.cs` — reads, validates, deserialises a canonical JSON file
- `Features/Entities/EntityReferenceResolver.cs` — flags dangling cross-entity refs as warnings
- `Features/Entities/CanonicalText/IEntityCanonicalTextRenderer.cs` — interface
- `Features/Entities/CanonicalText/ClassCanonicalTextRenderer.cs` — Class renderer
- `Features/Entities/CanonicalText/MonsterCanonicalTextRenderer.cs` — Monster renderer
- `Features/Entities/CanonicalText/SpellCanonicalTextRenderer.cs` — Spell renderer (fixture)
- `Features/Entities/CanonicalText/EntityCanonicalTextDispatcher.cs` — picks renderer by type

### Qdrant entity collection (new)
- `Infrastructure/Qdrant/EntityPayloadFields.cs` — payload key constants
- `Features/VectorStore/Entities/EntityPoint.cs` — record bundling (envelope, vector) for upsert
- `Features/VectorStore/Entities/IEntityVectorStore.cs` — upsert / delete-by-book / get-by-id / search
- `Features/VectorStore/Entities/QdrantEntityVectorStore.cs` — Qdrant impl

### Ingestion (modified + new)
- `Features/Ingestion/IIngestionQueue.cs` — extend `IngestionWorkType` enum (modify)
- `Features/Ingestion/IngestionQueueWorker.cs` — dispatch new work-item type (modify)
- `Features/Ingestion/Entities/IEntityIngestionOrchestrator.cs` — interface
- `Features/Ingestion/Entities/EntityIngestionOrchestrator.cs` — impl
- `Infrastructure/Sqlite/IngestionStatus.cs` — add new statuses (modify)
- `Features/Admin/BooksAdminEndpoints.cs` — add `/ingest-entities` endpoint (modify)
- `Features/Ingestion/BookDeletionService.cs` — extend deletion to cover entity points + canonical JSON (modify)

### Retrieval (new)
- `Features/Retrieval/Entities/EntityRetrievalEndpoints.cs` — minimal API endpoints
- `Features/Retrieval/Entities/IEntityRetrievalService.cs` — interface
- `Features/Retrieval/Entities/EntityRetrievalService.cs` — impl
- `Features/Retrieval/Entities/EntitySearchQuery.cs` — query record
- `Features/Retrieval/Entities/EntitySearchResult.cs` — result record (envelope-only, with score)
- `Features/Retrieval/Entities/EntityFullResult.cs` — full record with `fields` JSON

### Config (modified)
- `Infrastructure/Qdrant/QdrantOptions.cs` — add `EntitiesCollectionName` (modify)
- `Infrastructure/Qdrant/QdrantCollectionInitializer.cs` — bootstrap entity collection + indexes (modify)
- `Config/appsettings.json` — add new key (modify)

### Test fixtures (new)
- `DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json` — minimal: 1 Class (Fighter), 1 Monster (Bullywug), 1 Spell (Fireball)

### Tests (new)
- `DndMcpAICsharpFun.Tests/Entities/EntityIdSlugTests.cs`
- `DndMcpAICsharpFun.Tests/Entities/CanonicalJsonLoaderTests.cs`
- `DndMcpAICsharpFun.Tests/Entities/EntityReferenceResolverTests.cs`
- `DndMcpAICsharpFun.Tests/Entities/CanonicalTextRendererTests.cs`
- `DndMcpAICsharpFun.Tests/Entities/Ingestion/EntityIngestionOrchestratorTests.cs`
- `DndMcpAICsharpFun.Tests/Entities/Retrieval/EntityRetrievalEndpointsTests.cs`
- `DndMcpAICsharpFun.Tests/Entities/Deletion/EntityBookDeletionTests.cs`

### HTTP reference (modified)
- `DndMcpAICsharpFun.http` — add example requests for the new endpoints

---

## Conventions

- **All file edits use Serena.** Built-in Edit/Write on `.cs` files is forbidden by user rule. Memory files and `.http` / `.md` use Write/Edit.
- **Run all tests after every task that touches code:** `dotnet test --filter "FullyQualifiedName!~Integration"` for unit-only fast feedback; `dotnet test` for full run.
- **Commit after every passing task** with a focused message. Never amend across tasks.
- **Never `git add -A`.** Stage only the files the task touched.
- **Worktree:** if not already in one, create one (memory rule: worktrees are automatic, never ask).

---

## Task 1: EntityType enum

**Files:**
- Create: `Domain/Entities/EntityType.cs`
- Test: `DndMcpAICsharpFun.Tests/Entities/EntityTypeTests.cs` (smoke only)

- [ ] **Step 1: Write the failing test**

```csharp
// DndMcpAICsharpFun.Tests/Entities/EntityTypeTests.cs
using DndMcpAICsharpFun.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities;

public class EntityTypeTests
{
    [Fact]
    public void Has_All_Seventeen_Types()
    {
        var values = Enum.GetValues<EntityType>();
        values.Should().HaveCount(17);
        values.Should().Contain(new[]
        {
            EntityType.Class, EntityType.Subclass, EntityType.Race, EntityType.Subrace,
            EntityType.Background, EntityType.Feat, EntityType.Spell,
            EntityType.Weapon, EntityType.Armor, EntityType.Item, EntityType.MagicItem,
            EntityType.Monster, EntityType.Trap, EntityType.DiseasePoison, EntityType.VehicleMount,
            EntityType.God, EntityType.Plane, EntityType.Faction, EntityType.Location,
            EntityType.Condition,
        });
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~EntityTypeTests"`
Expected: FAIL with "EntityType could not be found".

- [ ] **Step 3: Implement the enum (via Serena)**

Use `mcp__plugin_serena_serena__create_text_file` to create `Domain/Entities/EntityType.cs`:

```csharp
namespace DndMcpAICsharpFun.Domain.Entities;

public enum EntityType
{
    Class,
    Subclass,
    Race,
    Subrace,
    Background,
    Feat,
    Spell,
    Weapon,
    Armor,
    Item,
    MagicItem,
    Monster,
    Trap,
    DiseasePoison,
    VehicleMount,
    God,
    Plane,
    Faction,
    Location,
    Condition,
}
```

Note: enum has 20 members but the test asserts 17 from the user-ratified spec list (Class+Subclass, Race+Subrace, plus 15 unique categories — 17 logical types, 20 enum members because Subclass & Subrace are sub-types). **Update the test:** the spec list is 17 (Class, Subclass count as 2 types in the enum, but the spec's "17 types" referenced grouped categories). Re-read the proposal: it lists 17 types explicitly:

> Class, Subclass, Race, Subrace, Background, Feat, Spell, Weapon, Armor, Item, Magic Item, Monster, Trap, Disease/Poison, Vehicle/Mount, God, Plane, Faction, Location, Condition

That's 20. Spec mentions "17 entity types" but enumerates 20. **Resolve ambiguity in favor of the enumerated list.** Update test to assert `HaveCount(20)` and update the proposal.md / design.md / spec language to say "20 entity types". (Plan does not silently let inconsistencies live.)

Updated test count: `values.Should().HaveCount(20);`

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~EntityTypeTests"`
Expected: PASS.

- [ ] **Step 5: Update spec/proposal/design language to "20 entity types"**

Edit (via Serena) the three documents to replace "17 entity types" / "17 types" with "20 entity types" / "20 types":
- `openspec/changes/structured-entity-extraction/proposal.md`
- `openspec/changes/structured-entity-extraction/design.md`
- `openspec/changes/structured-entity-extraction/specs/structured-entities/spec.md`

- [ ] **Step 6: Commit**

```bash
git add Domain/Entities/EntityType.cs DndMcpAICsharpFun.Tests/Entities/EntityTypeTests.cs \
        openspec/changes/structured-entity-extraction/proposal.md \
        openspec/changes/structured-entity-extraction/design.md \
        openspec/changes/structured-entity-extraction/specs/structured-entities/spec.md
git commit -m "feat(entities): introduce EntityType enum (20 types)"
```

---

## Task 2: Provenance + Spellcasting shared records

**Files:**
- Create: `Domain/Entities/Provenance.cs`
- Create: `Domain/Entities/Spellcasting.cs`

- [ ] **Step 1: Implement Provenance.cs (via Serena)**

```csharp
namespace DndMcpAICsharpFun.Domain.Entities;

public sealed record FirstAppearance(string Book, string Edition, int? Page = null);

public sealed record Revision(string Book, string Edition, string Summary);
```

- [ ] **Step 2: Implement Spellcasting.cs (via Serena)**

```csharp
namespace DndMcpAICsharpFun.Domain.Entities;

public enum SpellcastingType { Full, Half, Third, Pact, Innate, None }

public enum SpellPreparation { Spellbook, Known, Prepared }

public sealed record SpellSlotsRow(int Level, IReadOnlyList<int> SlotsByLevel);

public sealed record PactSlotRow(int Level, int Slots, int SlotLevel);

public sealed record SpellcastingBlock(
    SpellcastingType Type,
    string? Ability,
    bool RitualCasting = false,
    SpellPreparation? Preparation = null,
    IReadOnlyList<int>? CantripsKnownByLevel = null,
    IReadOnlyList<int>? SpellsKnownByLevel = null,
    string? SpellList = null,
    IReadOnlyList<SpellSlotsRow>? SpellSlotsByLevel = null,
    IReadOnlyList<PactSlotRow>? PactSlotsByLevel = null);
```

- [ ] **Step 3: Build to verify compile**

Run: `dotnet build`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add Domain/Entities/Provenance.cs Domain/Entities/Spellcasting.cs
git commit -m "feat(entities): add Provenance and Spellcasting records"
```

---

## Task 3: EntityEnvelope (open envelope, fields as JsonElement)

**Files:**
- Create: `Domain/Entities/EntityEnvelope.cs`
- Test: included in Task 5 (loader tests).

- [ ] **Step 1: Implement EntityEnvelope.cs (via Serena)**

The envelope keeps `fields` as a raw `JsonElement` so the loader can dispatch to per-type deserialisation by `type`. This avoids generics-everywhere and allows loading a heterogeneous list.

```csharp
using System.Text.Json;

namespace DndMcpAICsharpFun.Domain.Entities;

public sealed record EntityEnvelope(
    string Id,
    EntityType Type,
    string Name,
    string SourceBook,
    string Edition,
    int? Page,
    FirstAppearance FirstAppearedIn,
    IReadOnlyList<Revision> RevisedIn,
    IReadOnlyList<string> SettingTags,
    string CanonicalText,
    JsonElement Fields);
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add Domain/Entities/EntityEnvelope.cs
git commit -m "feat(entities): add EntityEnvelope record"
```

---

## Task 4: ClassFields, MonsterFields, SpellFields (the three with full schemas)

**Files:**
- Create: `Domain/Entities/Fields/ClassFields.cs`
- Create: `Domain/Entities/Fields/MonsterFields.cs`
- Create: `Domain/Entities/Fields/SpellFields.cs`

- [ ] **Step 1: ClassFields.cs (via Serena)**

```csharp
using System.Text.Json.Serialization;

namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record SkillChoice(int Count, IReadOnlyList<string> Options);

public sealed record EquipmentChoice(int Choose, IReadOnlyList<string> Options);

public sealed record MulticlassPrerequisites(
    [property: JsonPropertyName("operator")] string Operator,
    IReadOnlyDictionary<string, int> Abilities);

public sealed record MulticlassBlock(
    MulticlassPrerequisites Prerequisites,
    IReadOnlyList<string> ProficienciesGained);

public sealed record FeatureRef(string Name, string Ref, string Summary);

public sealed record ClassLevelEntry(
    int Level,
    int ProficiencyBonus,
    IReadOnlyList<FeatureRef> Features);

public sealed record ClassFields(
    string HitDie,
    IReadOnlyList<string> PrimaryAbilities,
    IReadOnlyList<string> SavingThrowProficiencies,
    IReadOnlyList<string> ArmorProficiencies,
    IReadOnlyList<string> WeaponProficiencies,
    IReadOnlyList<string> ToolProficiencies,
    SkillChoice SkillChoices,
    IReadOnlyList<EquipmentChoice> StartingEquipment,
    MulticlassBlock Multiclass,
    SpellcastingBlock? Spellcasting,
    int SubclassSelectionLevel,
    IReadOnlyList<string> Subclasses,
    IReadOnlyList<int> AsiLevels,
    IReadOnlyList<ClassLevelEntry> FeaturesByLevel);
```

- [ ] **Step 2: MonsterFields.cs (via Serena)**

```csharp
namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record ArmorClass(int Value, string? Source);

public sealed record HitPoints(int Average, string Dice);

public sealed record AbilityScores(
    int Strength, int Dexterity, int Constitution,
    int Intelligence, int Wisdom, int Charisma);

public sealed record ChallengeRating(string Cr, double CrNumeric, int Xp, int ProficiencyBonus);

public sealed record TraitRef(string Name, string Ref, string Summary);

public sealed record SaveBlock(string Ability, int Dc);

public sealed record AttackRange(int Normal, int? Long);

public sealed record DamagePart(string Dice, int Average, string Type, string? Versatile = null);

public enum ActionType
{
    Multiattack,
    MeleeWeaponAttack,
    RangedWeaponAttack,
    MeleeOrRangedWeaponAttack,
    Save,
    Passive,
    Other
}

public sealed record MonsterAction(
    string Name,
    ActionType Type,
    string Summary,
    int? AttackBonus = null,
    int? Reach = null,
    AttackRange? Range = null,
    string? Targets = null,
    IReadOnlyList<DamagePart>? Damage = null,
    string? Recharge = null,
    SaveBlock? Save = null);

public sealed record LegendaryAction(string Name, int Cost, string Summary);

public sealed record LegendaryBlock(int PerTurn, IReadOnlyList<LegendaryAction> Actions);

public sealed record LairActionEntry(string Summary);

public sealed record RegionalEffect(string Summary);

public sealed record LairBlock(
    int InitiativeCount,
    IReadOnlyList<LairActionEntry> Actions,
    IReadOnlyList<RegionalEffect> RegionalEffects);

public sealed record MonsterFields(
    string Size,
    string Type,
    IReadOnlyList<string> Subtypes,
    string Alignment,
    ArmorClass ArmorClass,
    HitPoints HitPoints,
    IReadOnlyDictionary<string, int> Speed,
    AbilityScores AbilityScores,
    IReadOnlyDictionary<string, int> SavingThrows,
    IReadOnlyDictionary<string, int> Skills,
    IReadOnlyList<string> DamageVulnerabilities,
    IReadOnlyList<string> DamageResistances,
    IReadOnlyList<string> DamageImmunities,
    IReadOnlyList<string> ConditionImmunities,
    IReadOnlyDictionary<string, int> Senses,
    IReadOnlyList<string> Languages,
    ChallengeRating ChallengeRating,
    IReadOnlyList<string> Environment,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<TraitRef> Traits,
    IReadOnlyList<MonsterAction> Actions,
    IReadOnlyList<MonsterAction> BonusActions,
    IReadOnlyList<MonsterAction> Reactions,
    SpellcastingBlock? Spellcasting = null,
    LegendaryBlock? LegendaryActions = null,
    LairBlock? LairActions = null,
    IReadOnlyList<string>? VariantForms = null);
```

- [ ] **Step 3: SpellFields.cs (via Serena)**

```csharp
namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record SpellComponents(bool V, bool S, bool M, string? Material = null);

public sealed record SpellFields(
    int Level,
    string School,
    string CastingTime,
    string Range,
    SpellComponents Components,
    string Duration,
    bool Ritual,
    bool Concentration,
    string Description,
    string? AtHigherLevels,
    IReadOnlyList<string> Classes,
    IReadOnlyList<string> DamageTypes);
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Domain/Entities/Fields/ClassFields.cs Domain/Entities/Fields/MonsterFields.cs Domain/Entities/Fields/SpellFields.cs
git commit -m "feat(entities): add Class, Monster, Spell fields records"
```

---

## Task 5: Remaining 17 per-type field records

Each follows the same pattern: a `record <Type>Fields(...)` with the fields specified in the spec. Implement minimal viable shapes; refine in Plan 2 when extracting from real books.

**Files (all created via Serena):**

- `Domain/Entities/Fields/SubclassFields.cs`
- `Domain/Entities/Fields/RaceFields.cs`
- `Domain/Entities/Fields/SubraceFields.cs`
- `Domain/Entities/Fields/BackgroundFields.cs`
- `Domain/Entities/Fields/FeatFields.cs`
- `Domain/Entities/Fields/WeaponFields.cs`
- `Domain/Entities/Fields/ArmorFields.cs`
- `Domain/Entities/Fields/ItemFields.cs`
- `Domain/Entities/Fields/MagicItemFields.cs`
- `Domain/Entities/Fields/TrapFields.cs`
- `Domain/Entities/Fields/DiseasePoisonFields.cs`
- `Domain/Entities/Fields/VehicleMountFields.cs`
- `Domain/Entities/Fields/GodFields.cs`
- `Domain/Entities/Fields/PlaneFields.cs`
- `Domain/Entities/Fields/FactionFields.cs`
- `Domain/Entities/Fields/LocationFields.cs`
- `Domain/Entities/Fields/ConditionFields.cs`

- [ ] **Step 1: Create each record (via Serena)**

Each file follows this template — adapt fields per type. Examples for the most-used types:

```csharp
// SubclassFields.cs
namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record SubclassFields(
    string ParentClass,                                     // ref to Class id
    SpellcastingBlock? Spellcasting,
    IReadOnlyList<ClassLevelEntry> FeaturesByLevel);        // sparse — only levels with subclass features
```

```csharp
// RaceFields.cs
namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record AbilityBonus(string Ability, int Bonus);

public sealed record RaceFields(
    string Size,
    int Speed,
    IReadOnlyList<AbilityBonus> AbilityBonuses,
    IReadOnlyList<string> Languages,
    IReadOnlyList<TraitRef> Traits,
    IReadOnlyList<string> Subraces);
```

```csharp
// SubraceFields.cs
public sealed record SubraceFields(
    string ParentRace,
    IReadOnlyList<AbilityBonus> AbilityBonuses,
    IReadOnlyList<TraitRef> Traits);
```

```csharp
// BackgroundFields.cs
public sealed record BackgroundFields(
    IReadOnlyList<string> SkillProficiencies,
    IReadOnlyList<string> ToolProficiencies,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Equipment,
    string FeatureName,
    string FeatureSummary);
```

```csharp
// FeatFields.cs
public sealed record FeatFields(
    IReadOnlyList<string> Prerequisites,
    string Description,
    IReadOnlyList<string> Grants);                     // free-form: ability bumps, proficiencies, ...
```

```csharp
// WeaponFields.cs
public sealed record WeaponFields(
    string Category,                                   // simple | martial
    string WeaponType,                                 // melee | ranged
    int CostCp,                                        // cost in copper pieces
    double WeightLb,
    DamagePart Damage,
    AttackRange? Range,
    IReadOnlyList<string> Properties);                 // heavy, two-handed, finesse, ...
```

```csharp
// ArmorFields.cs
public sealed record ArmorFields(
    string Category,                                   // light | medium | heavy | shield
    int CostGp,
    double WeightLb,
    string AcFormula,                                  // "11 + Dex modifier", "16", "+2", ...
    int? StrengthRequirement,
    bool StealthDisadvantage);
```

```csharp
// ItemFields.cs (mundane gear)
public sealed record ItemFields(
    int CostCp,
    double WeightLb,
    string Description);
```

```csharp
// MagicItemFields.cs
public sealed record MagicItemFields(
    string Rarity,                                     // common | uncommon | rare | very-rare | legendary | artifact
    string ItemCategory,                               // weapon | armor | wondrous-item | potion | scroll | ring | rod | staff | wand
    string Attunement,                                 // "no" | "yes" | "by a class:Wizard" ...
    string Description);
```

```csharp
// TrapFields.cs
public sealed record TrapFields(
    string Difficulty,
    int? DetectDc,
    int? DisarmDc,
    string Description);
```

```csharp
// DiseasePoisonFields.cs
public sealed record DiseasePoisonFields(
    string Kind,                                       // disease | injury-poison | contact-poison | ingested-poison | inhaled-poison
    string SaveDc,
    string Description);
```

```csharp
// VehicleMountFields.cs
public sealed record VehicleMountFields(
    string Kind,                                       // mount | vehicle
    int? Speed,
    int? CapacityLb,
    string Description);
```

```csharp
// GodFields.cs
public sealed record GodFields(
    string Alignment,
    IReadOnlyList<string> Domains,
    string? Symbol,
    string? Pantheon,
    string? Plane,
    string Description);
```

```csharp
// PlaneFields.cs
public sealed record PlaneFields(
    string Category,                                   // material | inner | outer | transitive | demiplane
    string Description,
    IReadOnlyList<string> RelatedPlanes);
```

```csharp
// FactionFields.cs
public sealed record FactionFields(
    string? Headquarters,
    IReadOnlyList<string> Goals,
    string Description);
```

```csharp
// LocationFields.cs
public sealed record LocationFields(
    string Category,                                   // city | region | dungeon | ...
    string? Setting,
    string Description);
```

```csharp
// ConditionFields.cs
public sealed record ConditionFields(string Description);
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: PASS — all field records compile.

- [ ] **Step 3: Commit**

```bash
git add Domain/Entities/Fields/
git commit -m "feat(entities): add remaining per-type fields records"
```

---

## Task 6: EntityIdSlug generator

**Files:**
- Create: `Domain/Entities/EntityIdSlug.cs`
- Create: `DndMcpAICsharpFun.Tests/Entities/EntityIdSlugTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// DndMcpAICsharpFun.Tests/Entities/EntityIdSlugTests.cs
using DndMcpAICsharpFun.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities;

public class EntityIdSlugTests
{
    [Theory]
    [InlineData("Player's Handbook 2014", EntityType.Class, "Fighter",   "phb14.class.fighter")]
    [InlineData("Monster Manual 2014",    EntityType.Monster, "Aboleth", "mm14.monster.aboleth")]
    [InlineData("Tasha's Cauldron of Everything", EntityType.Subclass, "Swashbuckler", "tasha.subclass.swashbuckler")]
    public void Generates_expected_slug(string book, EntityType type, string name, string expected)
    {
        EntityIdSlug.For(book, type, name).Should().Be(expected);
    }

    [Fact]
    public void Folds_non_ascii_characters()
    {
        EntityIdSlug.For("Player's Handbook 2014", EntityType.Spell, "Déjà Vu")
            .Should().Be("phb14.spell.deja-vu");
    }

    [Fact]
    public void Same_input_yields_same_slug()
    {
        var a = EntityIdSlug.For("Monster Manual 2014", EntityType.Monster, "Adult Red Dragon");
        var b = EntityIdSlug.For("Monster Manual 2014", EntityType.Monster, "Adult Red Dragon");
        a.Should().Be(b);
    }
}
```

- [ ] **Step 2: Run tests to confirm failure**

Run: `dotnet test --filter "FullyQualifiedName~EntityIdSlugTests"`
Expected: compile error or test fail (no `EntityIdSlug` exists).

- [ ] **Step 3: Implement EntityIdSlug.cs (via Serena)**

```csharp
using System.Globalization;
using System.Text;

namespace DndMcpAICsharpFun.Domain.Entities;

public static class EntityIdSlug
{
    private static readonly Dictionary<string, string> BookOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Player's Handbook 2014"] = "phb14",
        ["Player's Handbook 2024"] = "phb24",
        ["Monster Manual 2014"]    = "mm14",
        ["Monster Manual 2024"]    = "mm24",
        ["Dungeon Master's Guide 2014"] = "dmg14",
        ["Dungeon Master's Guide 2024"] = "dmg24",
        ["Tasha's Cauldron of Everything"] = "tasha",
        ["Xanathar's Guide to Everything"] = "xanathar",
        ["Volo's Guide to Monsters"] = "volo",
        ["Mordenkainen Presents: Monsters of the Multiverse"] = "motm",
        ["Eberron: Rising from the Last War"] = "eberron",
    };

    public static string For(string book, EntityType type, string name)
    {
        var bookSlug = BookOverrides.TryGetValue(book, out var s) ? s : SlugifyBook(book);
        var typeSlug = type.ToString().ToLowerInvariant();
        var nameSlug = SlugifyName(name);
        return $"{bookSlug}.{typeSlug}.{nameSlug}";
    }

    private static string SlugifyBook(string book) => SlugifyName(book);

    private static string SlugifyName(string text)
    {
        var folded = FoldToAscii(text);
        var sb = new StringBuilder(folded.Length);
        var lastWasHyphen = true;
        foreach (var ch in folded.ToLowerInvariant())
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
            {
                sb.Append(ch);
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen)
            {
                sb.Append('-');
                lastWasHyphen = true;
            }
        }
        return sb.ToString().Trim('-');
    }

    private static string FoldToAscii(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~EntityIdSlugTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Domain/Entities/EntityIdSlug.cs DndMcpAICsharpFun.Tests/Entities/EntityIdSlugTests.cs
git commit -m "feat(entities): deterministic entity ID slug generator"
```

---

## Task 7: CanonicalJsonFile envelope record

**Files:**
- Create: `Domain/Entities/CanonicalJsonFile.cs`

- [ ] **Step 1: Implement (via Serena)**

```csharp
namespace DndMcpAICsharpFun.Domain.Entities;

public sealed record CanonicalBookMetadata(
    string SourceBook,
    string Edition,
    string FileHash,
    string DisplayName);

public sealed record CanonicalJsonFile(
    string SchemaVersion,
    CanonicalBookMetadata Book,
    IReadOnlyList<EntityEnvelope> Entities);

public static class CanonicalJsonSchema
{
    public const string CurrentVersion = "1";
}
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add Domain/Entities/CanonicalJsonFile.cs
git commit -m "feat(entities): add CanonicalJsonFile envelope and schema version constant"
```

---

## Task 8: Hand-written test fixture canonical JSON

**Files:**
- Create: `DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json`

- [ ] **Step 1: Write the fixture**

Create `DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json`:

```json
{
  "schemaVersion": "1",
  "book": {
    "sourceBook": "Test Book",
    "edition": "Edition2014",
    "fileHash": "deadbeef",
    "displayName": "Test Book"
  },
  "entities": [
    {
      "id": "test-book.class.fighter",
      "type": "Class",
      "name": "Fighter",
      "sourceBook": "Test Book",
      "edition": "Edition2014",
      "page": 70,
      "firstAppearedIn": { "book": "Player's Handbook", "edition": "Edition2014" },
      "revisedIn": [],
      "settingTags": ["core"],
      "canonicalText": "Fighter — Hit Die: d10. STR/CON saves. Heavy armor and martial weapons. ...",
      "fields": {
        "hitDie": "d10",
        "primaryAbilities": ["Strength", "Dexterity"],
        "savingThrowProficiencies": ["Strength", "Constitution"],
        "armorProficiencies": ["light", "medium", "heavy", "shields"],
        "weaponProficiencies": ["simple", "martial"],
        "toolProficiencies": [],
        "skillChoices": { "count": 2, "options": ["Acrobatics", "Athletics"] },
        "startingEquipment": [
          { "choose": 1, "options": ["chain mail", "leather armor"] }
        ],
        "multiclass": {
          "prerequisites": { "operator": "or", "abilities": { "Strength": 13, "Dexterity": 13 } },
          "proficienciesGained": ["light armor", "shields", "simple weapons", "martial weapons"]
        },
        "spellcasting": null,
        "subclassSelectionLevel": 3,
        "subclasses": [],
        "asiLevels": [4, 6, 8, 12, 14, 16, 19],
        "featuresByLevel": [
          {
            "level": 1,
            "proficiencyBonus": 2,
            "features": [
              { "name": "Fighting Style", "ref": "test-book.feature.fighting-style", "summary": "Pick a Fighting Style." }
            ]
          }
        ]
      }
    },
    {
      "id": "test-book.monster.bullywug",
      "type": "Monster",
      "name": "Bullywug",
      "sourceBook": "Test Book",
      "edition": "Edition2014",
      "page": 35,
      "firstAppearedIn": { "book": "Monster Manual", "edition": "Edition2014" },
      "revisedIn": [],
      "settingTags": ["core"],
      "canonicalText": "Bullywug — Medium humanoid (bullywug), neutral evil. AC 14. HP 11 (2d8+2). ...",
      "fields": {
        "size": "Medium",
        "type": "humanoid",
        "subtypes": ["bullywug"],
        "alignment": "neutral evil",
        "armorClass": { "value": 14, "source": "leather armor, shield" },
        "hitPoints": { "average": 11, "dice": "2d8 + 2" },
        "speed": { "walk": 20, "swim": 40 },
        "abilityScores": { "strength": 12, "dexterity": 12, "constitution": 13, "intelligence": 7, "wisdom": 10, "charisma": 7 },
        "savingThrows": {},
        "skills": { "Stealth": 3 },
        "damageVulnerabilities": [],
        "damageResistances": [],
        "damageImmunities": [],
        "conditionImmunities": [],
        "senses": { "darkvision": 60, "passivePerception": 10 },
        "languages": ["Bullywug"],
        "challengeRating": { "cr": "1/4", "crNumeric": 0.25, "xp": 50, "proficiencyBonus": 2 },
        "environment": ["swamp"],
        "keywords": ["amphibian"],
        "traits": [
          { "name": "Amphibious", "ref": "test-book.trait.amphibious", "summary": "Can breathe air and water." }
        ],
        "actions": [
          { "name": "Bite", "type": "MeleeWeaponAttack", "summary": "+3 to hit, reach 5 ft. Hit: 3 (1d4+1) piercing.",
            "attackBonus": 3, "reach": 5, "targets": "one target",
            "damage": [ { "dice": "1d4 + 1", "average": 3, "type": "piercing" } ] }
        ],
        "bonusActions": [],
        "reactions": []
      }
    },
    {
      "id": "test-book.spell.fireball",
      "type": "Spell",
      "name": "Fireball",
      "sourceBook": "Test Book",
      "edition": "Edition2014",
      "page": 241,
      "firstAppearedIn": { "book": "Player's Handbook", "edition": "Edition2014" },
      "revisedIn": [],
      "settingTags": ["core"],
      "canonicalText": "Fireball — 3rd-level evocation. ...",
      "fields": {
        "level": 3,
        "school": "evocation",
        "castingTime": "1 action",
        "range": "150 feet",
        "components": { "v": true, "s": true, "m": true, "material": "a tiny ball of bat guano and sulfur" },
        "duration": "Instantaneous",
        "ritual": false,
        "concentration": false,
        "description": "A bright streak flashes from your pointing finger to a point you choose...",
        "atHigherLevels": "When you cast this spell using a spell slot of 4th level or higher, the damage increases by 1d6 for each slot level above 3rd.",
        "classes": ["Sorcerer", "Wizard"],
        "damageTypes": ["fire"]
      }
    }
  ]
}
```

In the test project file `DndMcpAICsharpFun.Tests.csproj`, ensure fixtures copy to output. Add (via Serena `replace_content`):

```xml
<ItemGroup>
  <None Update="Fixtures\canonical\**\*.json">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

- [ ] **Step 2: Build to verify fixture is copied**

Run: `dotnet build DndMcpAICsharpFun.Tests`
Then verify the fixture is at the bin path:
```bash
find DndMcpAICsharpFun.Tests/bin -name "test-book.json"
```
Expected: prints a path.

- [ ] **Step 3: Commit**

```bash
git add DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj
git commit -m "test(entities): add canonical JSON fixture (Fighter, Bullywug, Fireball)"
```

---

## Task 9: CanonicalJsonLoader (read + validate envelope, dispatch fields per type)

**Files:**
- Create: `Features/Entities/CanonicalJsonLoader.cs`
- Create: `DndMcpAICsharpFun.Tests/Entities/CanonicalJsonLoaderTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// DndMcpAICsharpFun.Tests/Entities/CanonicalJsonLoaderTests.cs
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Entities;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities;

public class CanonicalJsonLoaderTests
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "canonical", "test-book.json");

    [Fact]
    public async Task Load_returns_three_entities()
    {
        var loader = new CanonicalJsonLoader();
        var file = await loader.LoadAsync(FixturePath, CancellationToken.None);
        file.Entities.Should().HaveCount(3);
        file.Book.SourceBook.Should().Be("Test Book");
        file.SchemaVersion.Should().Be("1");
    }

    [Fact]
    public async Task Load_deserialises_class_fields()
    {
        var loader = new CanonicalJsonLoader();
        var file = await loader.LoadAsync(FixturePath, CancellationToken.None);
        var fighter = file.Entities.Single(e => e.Id == "test-book.class.fighter");
        var fields = loader.DeserialiseFields<ClassFields>(fighter);
        fields.HitDie.Should().Be("d10");
        fields.AsiLevels.Should().Equal(4, 6, 8, 12, 14, 16, 19);
    }

    [Fact]
    public async Task Load_deserialises_monster_fields_with_keywords()
    {
        var loader = new CanonicalJsonLoader();
        var file = await loader.LoadAsync(FixturePath, CancellationToken.None);
        var bullywug = file.Entities.Single(e => e.Id == "test-book.monster.bullywug");
        var fields = loader.DeserialiseFields<MonsterFields>(bullywug);
        fields.Keywords.Should().Contain("amphibian");
        fields.ChallengeRating.CrNumeric.Should().Be(0.25);
    }

    [Fact]
    public async Task Load_rejects_mismatched_schema_version()
    {
        var tmp = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmp, """{"schemaVersion":"0.5","book":{"sourceBook":"x","edition":"e","fileHash":"h","displayName":"x"},"entities":[]}""");
        try
        {
            var loader = new CanonicalJsonLoader();
            var act = async () => await loader.LoadAsync(tmp, CancellationToken.None);
            await act.Should().ThrowAsync<CanonicalJsonSchemaException>()
                     .WithMessage("*schemaVersion*0.5*");
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public async Task Load_rejects_duplicate_ids()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "canonical", "test-book.json");
        var content = await File.ReadAllTextAsync(path);
        var dup = content.Replace("test-book.spell.fireball", "test-book.class.fighter"); // duplicate id
        var tmp = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmp, dup);
        try
        {
            var loader = new CanonicalJsonLoader();
            var act = async () => await loader.LoadAsync(tmp, CancellationToken.None);
            await act.Should().ThrowAsync<CanonicalJsonSchemaException>()
                     .WithMessage("*duplicate*");
        }
        finally { File.Delete(tmp); }
    }
}
```

- [ ] **Step 2: Run tests to confirm failure**

Run: `dotnet test --filter "FullyQualifiedName~CanonicalJsonLoaderTests"`
Expected: compile errors (loader missing).

- [ ] **Step 3: Implement loader (via Serena)**

```csharp
// Features/Entities/CanonicalJsonLoader.cs
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Entities;

public sealed class CanonicalJsonSchemaException(string message) : Exception(message);

public sealed class CanonicalJsonLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public async Task<CanonicalJsonFile> LoadAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var file = await JsonSerializer.DeserializeAsync<CanonicalJsonFile>(stream, JsonOptions, ct)
                   ?? throw new CanonicalJsonSchemaException($"Failed to deserialise canonical JSON at {path}");

        if (file.SchemaVersion != CanonicalJsonSchema.CurrentVersion)
            throw new CanonicalJsonSchemaException(
                $"Unsupported schemaVersion '{file.SchemaVersion}' (expected '{CanonicalJsonSchema.CurrentVersion}') in {path}");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in file.Entities)
        {
            if (string.IsNullOrEmpty(e.Id))
                throw new CanonicalJsonSchemaException($"Entity with empty id in {path}");
            if (!seen.Add(e.Id))
                throw new CanonicalJsonSchemaException($"duplicate id '{e.Id}' in {path}");
        }

        return file;
    }

    public TFields DeserialiseFields<TFields>(EntityEnvelope envelope)
    {
        return envelope.Fields.Deserialize<TFields>(JsonOptions)
               ?? throw new CanonicalJsonSchemaException(
                   $"Failed to deserialise fields for entity {envelope.Id} as {typeof(TFields).Name}");
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~CanonicalJsonLoaderTests"`
Expected: PASS (all 5 tests).

- [ ] **Step 5: Commit**

```bash
git add Features/Entities/CanonicalJsonLoader.cs DndMcpAICsharpFun.Tests/Entities/CanonicalJsonLoaderTests.cs
git commit -m "feat(entities): canonical JSON loader with schema/dup-id validation"
```

---

## Task 10: Reference resolver (warn on dangling cross-entity references)

**Files:**
- Create: `Features/Entities/EntityReferenceResolver.cs`
- Create: `DndMcpAICsharpFun.Tests/Entities/EntityReferenceResolverTests.cs`

- [ ] **Step 1: Write tests**

```csharp
// DndMcpAICsharpFun.Tests/Entities/EntityReferenceResolverTests.cs
using DndMcpAICsharpFun.Features.Entities;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities;

public class EntityReferenceResolverTests
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "canonical", "test-book.json");

    [Fact]
    public async Task Empty_subclasses_yields_no_warnings()
    {
        var loader = new CanonicalJsonLoader();
        var file = await loader.LoadAsync(FixturePath, CancellationToken.None);
        var resolver = new EntityReferenceResolver();
        var warnings = resolver.Resolve(file.Entities).ToList();
        warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Dangling_subclass_reference_emits_warning()
    {
        var loader = new CanonicalJsonLoader();
        var file = await loader.LoadAsync(FixturePath, CancellationToken.None);
        // Mutate fixture in-memory: add subclass ref that doesn't exist.
        var modifiedFighter = file.Entities.Single(e => e.Id == "test-book.class.fighter") with
        {
            Fields = System.Text.Json.JsonSerializer.SerializeToElement(new
            {
                hitDie = "d10",
                primaryAbilities = new[] { "Strength" },
                savingThrowProficiencies = new[] { "Strength" },
                armorProficiencies = new[] { "heavy" },
                weaponProficiencies = new[] { "martial" },
                toolProficiencies = Array.Empty<string>(),
                skillChoices = new { count = 2, options = new[] { "Athletics" } },
                startingEquipment = Array.Empty<object>(),
                multiclass = new { prerequisites = new { @operator = "or", abilities = new Dictionary<string,int> { ["Strength"] = 13 } }, proficienciesGained = Array.Empty<string>() },
                spellcasting = (object?)null,
                subclassSelectionLevel = 3,
                subclasses = new[] { "test-book.subclass.does-not-exist" },
                asiLevels = Array.Empty<int>(),
                featuresByLevel = Array.Empty<object>(),
            })
        };
        var entities = file.Entities.Where(e => e.Id != "test-book.class.fighter").Append(modifiedFighter).ToList();

        var resolver = new EntityReferenceResolver();
        var warnings = resolver.Resolve(entities).ToList();

        warnings.Should().ContainSingle(w =>
            w.SourceEntityId == "test-book.class.fighter" &&
            w.MissingTargetId == "test-book.subclass.does-not-exist");
    }
}
```

- [ ] **Step 2: Run tests to confirm failure**

Run: `dotnet test --filter "FullyQualifiedName~EntityReferenceResolverTests"`
Expected: compile errors.

- [ ] **Step 3: Implement EntityReferenceResolver (via Serena)**

```csharp
// Features/Entities/EntityReferenceResolver.cs
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Entities;

public sealed record EntityReferenceWarning(string SourceEntityId, string FieldPath, string MissingTargetId);

public sealed class EntityReferenceResolver
{
    public IEnumerable<EntityReferenceWarning> Resolve(IReadOnlyList<EntityEnvelope> entities)
    {
        var ids = entities.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var entity in entities)
        {
            foreach (var (path, value) in WalkStringValues(entity.Fields, prefix: "fields"))
            {
                if (LooksLikeEntityReference(value) && !ids.Contains(value))
                    yield return new EntityReferenceWarning(entity.Id, path, value);
            }
        }
    }

    private static bool LooksLikeEntityReference(string s)
        => s.Count(c => c == '.') == 2
           && s.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-');

    private static IEnumerable<(string Path, string Value)> WalkStringValues(JsonElement element, string prefix)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                    foreach (var pair in WalkStringValues(prop.Value, $"{prefix}.{prop.Name}"))
                        yield return pair;
                break;
            case JsonValueKind.Array:
                int idx = 0;
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var pair in WalkStringValues(item, $"{prefix}[{idx}]"))
                        yield return pair;
                    idx++;
                }
                break;
            case JsonValueKind.String:
                yield return (prefix, element.GetString() ?? "");
                break;
        }
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~EntityReferenceResolverTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Features/Entities/EntityReferenceResolver.cs DndMcpAICsharpFun.Tests/Entities/EntityReferenceResolverTests.cs
git commit -m "feat(entities): cross-entity reference resolver emits dangling-ref warnings"
```

---

## Task 11: Canonical text renderers (Class, Monster, Spell)

**Files:**
- Create: `Features/Entities/CanonicalText/IEntityCanonicalTextRenderer.cs`
- Create: `Features/Entities/CanonicalText/ClassCanonicalTextRenderer.cs`
- Create: `Features/Entities/CanonicalText/MonsterCanonicalTextRenderer.cs`
- Create: `Features/Entities/CanonicalText/SpellCanonicalTextRenderer.cs`
- Create: `Features/Entities/CanonicalText/EntityCanonicalTextDispatcher.cs`
- Create: `DndMcpAICsharpFun.Tests/Entities/CanonicalTextRendererTests.cs`

- [ ] **Step 1: Write tests (round-trip determinism)**

```csharp
// DndMcpAICsharpFun.Tests/Entities/CanonicalTextRendererTests.cs
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities;

public class CanonicalTextRendererTests
{
    [Fact]
    public void Class_text_includes_hitdie_and_saves_and_level1_features()
    {
        var fields = new ClassFields(
            HitDie: "d10",
            PrimaryAbilities: new[] { "Strength" },
            SavingThrowProficiencies: new[] { "Strength", "Constitution" },
            ArmorProficiencies: Array.Empty<string>(),
            WeaponProficiencies: Array.Empty<string>(),
            ToolProficiencies: Array.Empty<string>(),
            SkillChoices: new SkillChoice(2, new[] { "Athletics" }),
            StartingEquipment: Array.Empty<EquipmentChoice>(),
            Multiclass: new MulticlassBlock(
                new MulticlassPrerequisites("or", new Dictionary<string, int> { ["Strength"] = 13 }),
                Array.Empty<string>()),
            Spellcasting: null,
            SubclassSelectionLevel: 3,
            Subclasses: Array.Empty<string>(),
            AsiLevels: Array.Empty<int>(),
            FeaturesByLevel: new[] {
                new ClassLevelEntry(1, 2, new[] {
                    new FeatureRef("Fighting Style", "x", "Pick a Fighting Style.")
                })
            });

        var text1 = new ClassCanonicalTextRenderer().Render("Fighter", fields);
        var text2 = new ClassCanonicalTextRenderer().Render("Fighter", fields);
        text1.Should().Be(text2); // determinism
        text1.Should().Contain("Fighter").And.Contain("d10").And.Contain("Strength, Constitution").And.Contain("Fighting Style");
    }

    [Fact]
    public void Spell_text_includes_at_higher_levels()
    {
        var fields = new SpellFields(
            Level: 3, School: "evocation",
            CastingTime: "1 action", Range: "150 feet",
            Components: new SpellComponents(true, true, true, "guano"),
            Duration: "Instantaneous", Ritual: false, Concentration: false,
            Description: "A bright streak.",
            AtHigherLevels: "Damage increases.",
            Classes: new[] { "Wizard" }, DamageTypes: new[] { "fire" });
        var text = new SpellCanonicalTextRenderer().Render("Fireball", fields);
        text.Should().Contain("3rd-level evocation").And.Contain("Damage increases.");
    }
}
```

- [ ] **Step 2: Run tests to confirm failure**

Run: `dotnet test --filter "FullyQualifiedName~CanonicalTextRendererTests"`
Expected: compile errors.

- [ ] **Step 3: Implement renderers (via Serena)**

```csharp
// Features/Entities/CanonicalText/IEntityCanonicalTextRenderer.cs
namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public interface IEntityCanonicalTextRenderer<TFields>
{
    string Render(string name, TFields fields);
}
```

```csharp
// Features/Entities/CanonicalText/ClassCanonicalTextRenderer.cs
using System.Text;
using DndMcpAICsharpFun.Domain.Entities.Fields;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public sealed class ClassCanonicalTextRenderer : IEntityCanonicalTextRenderer<ClassFields>
{
    public string Render(string name, ClassFields f)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{name} — Hit Die: {f.HitDie}");
        sb.AppendLine($"Primary abilities: {string.Join(", ", f.PrimaryAbilities)}");
        sb.AppendLine($"Saving throws: {string.Join(", ", f.SavingThrowProficiencies)}");
        sb.AppendLine($"Armor: {string.Join(", ", f.ArmorProficiencies)}");
        sb.AppendLine($"Weapons: {string.Join(", ", f.WeaponProficiencies)}");
        if (f.Spellcasting is { } sc) sb.AppendLine($"Spellcasting: {sc.Type} ({sc.Ability})");
        sb.AppendLine($"Subclass at level {f.SubclassSelectionLevel}");
        sb.AppendLine("Features by level:");
        foreach (var entry in f.FeaturesByLevel)
        {
            sb.AppendLine($"  L{entry.Level} (PB +{entry.ProficiencyBonus}):");
            foreach (var feat in entry.Features)
                sb.AppendLine($"    - {feat.Name}: {feat.Summary}");
        }
        return sb.ToString();
    }
}
```

```csharp
// Features/Entities/CanonicalText/MonsterCanonicalTextRenderer.cs
using System.Text;
using DndMcpAICsharpFun.Domain.Entities.Fields;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public sealed class MonsterCanonicalTextRenderer : IEntityCanonicalTextRenderer<MonsterFields>
{
    public string Render(string name, MonsterFields f)
    {
        var sb = new StringBuilder();
        var subtypes = f.Subtypes.Count == 0 ? "" : $" ({string.Join(", ", f.Subtypes)})";
        sb.AppendLine($"{name} — {f.Size} {f.Type}{subtypes}, {f.Alignment}");
        sb.AppendLine($"AC {f.ArmorClass.Value}{(f.ArmorClass.Source is null ? "" : $" ({f.ArmorClass.Source})")}");
        sb.AppendLine($"HP {f.HitPoints.Average} ({f.HitPoints.Dice})");
        sb.AppendLine($"Speed {string.Join(", ", f.Speed.Select(kv => $"{kv.Key} {kv.Value} ft."))}");
        sb.AppendLine($"STR {f.AbilityScores.Strength} DEX {f.AbilityScores.Dexterity} CON {f.AbilityScores.Constitution} INT {f.AbilityScores.Intelligence} WIS {f.AbilityScores.Wisdom} CHA {f.AbilityScores.Charisma}");
        sb.AppendLine($"Challenge {f.ChallengeRating.Cr} ({f.ChallengeRating.Xp} XP), proficiency +{f.ChallengeRating.ProficiencyBonus}");
        if (f.Keywords.Count > 0) sb.AppendLine($"Keywords: {string.Join(", ", f.Keywords)}");
        if (f.Environment.Count > 0) sb.AppendLine($"Environment: {string.Join(", ", f.Environment)}");
        foreach (var t in f.Traits) sb.AppendLine($"Trait — {t.Name}: {t.Summary}");
        foreach (var a in f.Actions) sb.AppendLine($"Action — {a.Name}: {a.Summary}");
        return sb.ToString();
    }
}
```

```csharp
// Features/Entities/CanonicalText/SpellCanonicalTextRenderer.cs
using System.Text;
using DndMcpAICsharpFun.Domain.Entities.Fields;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public sealed class SpellCanonicalTextRenderer : IEntityCanonicalTextRenderer<SpellFields>
{
    public string Render(string name, SpellFields f)
    {
        var sb = new StringBuilder();
        var levelText = f.Level == 0 ? $"{f.School} cantrip" : $"{Ordinal(f.Level)}-level {f.School}";
        sb.AppendLine($"{name} — {levelText}");
        sb.AppendLine($"Casting Time: {f.CastingTime}");
        sb.AppendLine($"Range: {f.Range}");
        var comps = new List<string>();
        if (f.Components.V) comps.Add("V");
        if (f.Components.S) comps.Add("S");
        if (f.Components.M) comps.Add(f.Components.Material is null ? "M" : $"M ({f.Components.Material})");
        sb.AppendLine($"Components: {string.Join(", ", comps)}");
        sb.AppendLine($"Duration: {(f.Concentration ? "Concentration, up to " : "")}{f.Duration}");
        if (f.Classes.Count > 0) sb.AppendLine($"Classes: {string.Join(", ", f.Classes)}");
        sb.AppendLine();
        sb.AppendLine(f.Description);
        if (!string.IsNullOrEmpty(f.AtHigherLevels))
        {
            sb.AppendLine();
            sb.AppendLine("At Higher Levels: " + f.AtHigherLevels);
        }
        return sb.ToString();
    }

    private static string Ordinal(int n) => n switch
    {
        1 => "1st", 2 => "2nd", 3 => "3rd",
        _ => $"{n}th"
    };
}
```

```csharp
// Features/Entities/CanonicalText/EntityCanonicalTextDispatcher.cs
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Domain.Entities.Fields;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public sealed class EntityCanonicalTextDispatcher
{
    private readonly ClassCanonicalTextRenderer _classR = new();
    private readonly MonsterCanonicalTextRenderer _monsterR = new();
    private readonly SpellCanonicalTextRenderer _spellR = new();
    private readonly CanonicalJsonLoader _loader = new();

    public string Render(EntityEnvelope envelope)
    {
        // For Plan 1 we only render Class/Monster/Spell. Other types embed their author-provided canonicalText
        // unchanged (it's already deterministic from the LLM/hand-written JSON).
        return envelope.Type switch
        {
            EntityType.Class   => _classR.Render(envelope.Name, _loader.DeserialiseFields<ClassFields>(envelope)),
            EntityType.Monster => _monsterR.Render(envelope.Name, _loader.DeserialiseFields<MonsterFields>(envelope)),
            EntityType.Spell   => _spellR.Render(envelope.Name, _loader.DeserialiseFields<SpellFields>(envelope)),
            _ => envelope.CanonicalText,
        };
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~CanonicalTextRendererTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Features/Entities/CanonicalText/ DndMcpAICsharpFun.Tests/Entities/CanonicalTextRendererTests.cs
git commit -m "feat(entities): canonical text renderers for Class, Monster, Spell"
```

---

## Task 12: EntityPayloadFields constants + QdrantOptions update

**Files:**
- Create: `Infrastructure/Qdrant/EntityPayloadFields.cs`
- Modify: `Infrastructure/Qdrant/QdrantOptions.cs` (add `EntitiesCollectionName`)

- [ ] **Step 1: Implement EntityPayloadFields.cs (via Serena create_text_file)**

```csharp
namespace DndMcpAICsharpFun.Infrastructure.Qdrant;

public static class EntityPayloadFields
{
    public const string Id              = "id";
    public const string Type            = "type";
    public const string Name            = "name";
    public const string SourceBook      = "source_book";
    public const string Edition         = "edition";
    public const string BookType        = "book_type";
    public const string Page            = "page";
    public const string CanonicalText   = "canonical_text";
    public const string SettingTags     = "setting_tags";
    public const string Keywords        = "keywords";
    public const string CrNumeric       = "cr_numeric";
    public const string SpellLevel      = "spell_level";
    public const string DamageType      = "damage_type";
    public const string FirstBook       = "first_book";
    public const string FirstEdition    = "first_edition";
    public const string FieldsJson      = "fields_json";
    public const string FileHash        = "file_hash";
}
```

- [ ] **Step 2: Modify QdrantOptions.cs (via Serena replace_content)**

Find the existing `QdrantOptions` record/class and add `EntitiesCollectionName` with default value `"dnd_entities"`. Example shape (adapt to actual file):

```csharp
public string EntitiesCollectionName { get; set; } = "dnd_entities";
```

Add the same key to `Config/appsettings.json` under the `Qdrant` section:

```json
"EntitiesCollectionName": "dnd_entities"
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add Infrastructure/Qdrant/EntityPayloadFields.cs Infrastructure/Qdrant/QdrantOptions.cs Config/appsettings.json
git commit -m "feat(qdrant): add EntityPayloadFields and EntitiesCollectionName option"
```

---

## Task 13: Extend QdrantCollectionInitializer to bootstrap dnd_entities + indexes

**Files:**
- Modify: `Infrastructure/Qdrant/QdrantCollectionInitializer.cs`
- Test: integration test (add to existing or create) — see Task 14.

- [ ] **Step 1: Modify the initializer (via Serena replace_content)**

In `StartAsync`, after the `BlocksCollectionName` `EnsureCollectionAsync` call, add an analogous call for `EntitiesCollectionName`. Then add a new method `CreateEntityPayloadIndexesAsync` and call it from `EnsureCollectionAsync` when the collection name matches the entities collection.

Pattern:

```csharp
private async Task EnsureCollectionAsync(string name, CancellationToken ct)
{
    if (await client.CollectionExistsAsync(name, ct))
    {
        LogCollectionExists(logger, name);
        return;
    }
    await client.CreateCollectionAsync(
        name,
        new VectorParams { Size = (ulong)_options.VectorSize, Distance = Distance.Cosine },
        cancellationToken: ct);
    LogCollectionCreated(logger, name, _options.VectorSize);
    if (string.Equals(name, _options.EntitiesCollectionName, StringComparison.Ordinal))
        await CreateEntityPayloadIndexesAsync(name, ct);
    else
        await CreatePayloadIndexesAsync(name, ct);
}

private async Task CreateEntityPayloadIndexesAsync(string collection, CancellationToken ct)
{
    string[] keywordFields =
    [
        EntityPayloadFields.Type,
        EntityPayloadFields.SourceBook,
        EntityPayloadFields.Edition,
        EntityPayloadFields.BookType,
        EntityPayloadFields.SettingTags,
        EntityPayloadFields.Keywords,
        EntityPayloadFields.DamageType,
        EntityPayloadFields.FirstBook,
        EntityPayloadFields.FirstEdition,
        EntityPayloadFields.FileHash,
    ];
    foreach (var field in keywordFields)
        await client.CreatePayloadIndexAsync(collection, field, PayloadSchemaType.Keyword, cancellationToken: ct);

    await client.CreatePayloadIndexAsync(collection, EntityPayloadFields.SpellLevel, PayloadSchemaType.Integer, cancellationToken: ct);
    await client.CreatePayloadIndexAsync(collection, EntityPayloadFields.Page, PayloadSchemaType.Integer, cancellationToken: ct);
    await client.CreatePayloadIndexAsync(collection, EntityPayloadFields.CrNumeric, PayloadSchemaType.Float, cancellationToken: ct);

    LogPayloadIndexesCreated(logger, collection);
}
```

In `StartAsync`, add a second `EnsureCollectionAsync(_options.EntitiesCollectionName, ct)` call inside the same retry loop.

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add Infrastructure/Qdrant/QdrantCollectionInitializer.cs
git commit -m "feat(qdrant): bootstrap dnd_entities collection with payload indexes"
```

---

## Task 14: IEntityVectorStore + QdrantEntityVectorStore impl

**Files:**
- Create: `Features/VectorStore/Entities/EntityPoint.cs`
- Create: `Features/VectorStore/Entities/IEntityVectorStore.cs`
- Create: `Features/VectorStore/Entities/QdrantEntityVectorStore.cs`
- Create: `DndMcpAICsharpFun.Tests/Entities/QdrantEntityVectorStoreTests.cs` (integration; uses Testcontainers Qdrant if existing infra supports it; otherwise mark `[Trait("Category","Integration")]` and skip in CI by default)

- [ ] **Step 1: Implement EntityPoint.cs (via Serena)**

```csharp
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.VectorStore.Entities;

public sealed record EntityPoint(EntityEnvelope Envelope, float[] Vector, string FileHash);

public sealed record EntitySearchHit(EntityEnvelope Envelope, float Score, string PointId);
```

- [ ] **Step 2: Implement IEntityVectorStore.cs (via Serena)**

```csharp
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.VectorStore.Entities;

public sealed record EntityFilters(
    EntityType? Type = null,
    string? SourceBook = null,
    string? Edition = null,
    string? BookType = null,
    string? SettingTag = null,
    string? Keyword = null,
    double? CrNumericLte = null,
    double? CrNumericGte = null,
    int? SpellLevel = null,
    string? DamageType = null);

public interface IEntityVectorStore
{
    Task UpsertAsync(IList<EntityPoint> points, CancellationToken ct = default);
    Task DeleteByFileHashAsync(string fileHash, CancellationToken ct = default);
    Task<EntityEnvelope?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<IList<EntitySearchHit>> SearchAsync(float[] queryVector, EntityFilters filters, int topK, CancellationToken ct = default);
}
```

- [ ] **Step 3: Implement QdrantEntityVectorStore.cs (via Serena)**

```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace DndMcpAICsharpFun.Features.VectorStore.Entities;

public sealed class QdrantEntityVectorStore(
    QdrantClient client,
    IOptions<QdrantOptions> options) : IEntityVectorStore
{
    private readonly string _collection = options.Value.EntitiesCollectionName;

    public async Task UpsertAsync(IList<EntityPoint> points, CancellationToken ct = default)
    {
        if (points.Count == 0) return;
        var qdrantPoints = points.Select(ToPoint).ToList();
        await client.UpsertAsync(_collection, qdrantPoints, cancellationToken: ct);
    }

    public async Task DeleteByFileHashAsync(string fileHash, CancellationToken ct = default)
    {
        var filter = MatchKeyword(EntityPayloadFields.FileHash, fileHash);
        await client.DeleteAsync(_collection, filter, cancellationToken: ct);
    }

    public async Task<EntityEnvelope?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var filter = MatchKeyword(EntityPayloadFields.Id, id);
        var results = await client.ScrollAsync(_collection, filter, limit: 1, payloadSelector: true, cancellationToken: ct);
        var first = results.Result.FirstOrDefault();
        return first is null ? null : ToEnvelope(first);
    }

    public async Task<IList<EntitySearchHit>> SearchAsync(float[] queryVector, EntityFilters filters, int topK, CancellationToken ct = default)
    {
        var filter = BuildFilter(filters);
        var results = await client.SearchAsync(
            _collection,
            queryVector,
            filter: filter,
            limit: (ulong)topK,
            payloadSelector: true,
            cancellationToken: ct);
        return results.Select(p => new EntitySearchHit(ToEnvelope(p), p.Score, p.Id.Uuid)).ToList();
    }

    private PointStruct ToPoint(EntityPoint p)
    {
        var payload = new Dictionary<string, Value>
        {
            [EntityPayloadFields.Id]            = p.Envelope.Id,
            [EntityPayloadFields.Type]          = p.Envelope.Type.ToString(),
            [EntityPayloadFields.Name]          = p.Envelope.Name,
            [EntityPayloadFields.SourceBook]    = p.Envelope.SourceBook,
            [EntityPayloadFields.Edition]       = p.Envelope.Edition,
            [EntityPayloadFields.CanonicalText] = p.Envelope.CanonicalText,
            [EntityPayloadFields.FirstBook]     = p.Envelope.FirstAppearedIn.Book,
            [EntityPayloadFields.FirstEdition]  = p.Envelope.FirstAppearedIn.Edition,
            [EntityPayloadFields.FileHash]      = p.FileHash,
            [EntityPayloadFields.SettingTags]   = StringList(p.Envelope.SettingTags),
            [EntityPayloadFields.FieldsJson]    = p.Envelope.Fields.GetRawText(),
        };
        if (p.Envelope.Page is { } page) payload[EntityPayloadFields.Page] = page;

        // Surface filterable subset from `fields` for index-friendly access.
        FlattenIndexedFields(p.Envelope, payload);

        var point = new PointStruct
        {
            Id = new PointId { Uuid = Guid.NewGuid().ToString() },
            Vectors = p.Vector,
        };
        foreach (var (k, v) in payload) point.Payload[k] = v;
        return point;
    }

    private static void FlattenIndexedFields(EntityEnvelope envelope, Dictionary<string, Value> payload)
    {
        if (envelope.Type == EntityType.Monster && envelope.Fields.TryGetProperty("challengeRating", out var cr)
            && cr.TryGetProperty("crNumeric", out var crn) && crn.TryGetDouble(out var crd))
            payload[EntityPayloadFields.CrNumeric] = crd;

        if (envelope.Type == EntityType.Monster && envelope.Fields.TryGetProperty("keywords", out var kw)
            && kw.ValueKind == JsonValueKind.Array)
            payload[EntityPayloadFields.Keywords] = StringList(kw.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList());

        if (envelope.Type == EntityType.Spell && envelope.Fields.TryGetProperty("level", out var lvl)
            && lvl.TryGetInt32(out var lvlInt))
            payload[EntityPayloadFields.SpellLevel] = lvlInt;

        if (envelope.Type == EntityType.Spell && envelope.Fields.TryGetProperty("damageTypes", out var dt)
            && dt.ValueKind == JsonValueKind.Array)
        {
            var types = dt.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();
            if (types.Count > 0) payload[EntityPayloadFields.DamageType] = StringList(types);
        }

        if (envelope.Type == EntityType.Weapon && envelope.Fields.TryGetProperty("damage", out var dmg)
            && dmg.TryGetProperty("type", out var dmgType) && dmgType.ValueKind == JsonValueKind.String)
            payload[EntityPayloadFields.DamageType] = StringList(new[] { dmgType.GetString()! });
    }

    private static Value StringList(IEnumerable<string> values)
    {
        var list = new ListValue();
        foreach (var v in values) list.Values.Add(v);
        return new Value { ListValue = list };
    }

    private static EntityEnvelope ToEnvelope(RetrievedPoint point)
    {
        var p = point.Payload;
        var fieldsJson = p.TryGetValue(EntityPayloadFields.FieldsJson, out var fv) ? fv.StringValue : "{}";
        var fields = JsonDocument.Parse(fieldsJson).RootElement.Clone();
        var envelope = new EntityEnvelope(
            Id: p[EntityPayloadFields.Id].StringValue,
            Type: Enum.Parse<EntityType>(p[EntityPayloadFields.Type].StringValue),
            Name: p[EntityPayloadFields.Name].StringValue,
            SourceBook: p[EntityPayloadFields.SourceBook].StringValue,
            Edition: p[EntityPayloadFields.Edition].StringValue,
            Page: p.TryGetValue(EntityPayloadFields.Page, out var pp) ? (int?)pp.IntegerValue : null,
            FirstAppearedIn: new FirstAppearance(
                p[EntityPayloadFields.FirstBook].StringValue,
                p[EntityPayloadFields.FirstEdition].StringValue),
            RevisedIn: Array.Empty<Revision>(),
            SettingTags: p.TryGetValue(EntityPayloadFields.SettingTags, out var st)
                ? st.ListValue.Values.Select(v => v.StringValue).ToList()
                : Array.Empty<string>(),
            CanonicalText: p[EntityPayloadFields.CanonicalText].StringValue,
            Fields: fields);
        return envelope;
    }

    private static Filter MatchKeyword(string field, string value)
    {
        return new Filter
        {
            Must =
            {
                new Condition { Field = new FieldCondition { Key = field, Match = new Match { Keyword = value } } }
            }
        };
    }

    private Filter? BuildFilter(EntityFilters f)
    {
        var must = new List<Condition>();
        if (f.Type is { } t)
            must.Add(KW(EntityPayloadFields.Type, t.ToString()));
        if (!string.IsNullOrEmpty(f.SourceBook)) must.Add(KW(EntityPayloadFields.SourceBook, f.SourceBook));
        if (!string.IsNullOrEmpty(f.Edition))    must.Add(KW(EntityPayloadFields.Edition, f.Edition));
        if (!string.IsNullOrEmpty(f.BookType))   must.Add(KW(EntityPayloadFields.BookType, f.BookType));
        if (!string.IsNullOrEmpty(f.SettingTag)) must.Add(KW(EntityPayloadFields.SettingTags, f.SettingTag));
        if (!string.IsNullOrEmpty(f.Keyword))    must.Add(KW(EntityPayloadFields.Keywords, f.Keyword));
        if (!string.IsNullOrEmpty(f.DamageType)) must.Add(KW(EntityPayloadFields.DamageType, f.DamageType));
        if (f.SpellLevel is { } sl)              must.Add(new Condition { Field = new FieldCondition { Key = EntityPayloadFields.SpellLevel, Match = new Match { Integer = sl } } });
        if (f.CrNumericLte is { } lte || f.CrNumericGte is { } gte)
        {
            var range = new Qdrant.Client.Grpc.Range();
            if (f.CrNumericLte is { } v1) range.Lte = v1;
            if (f.CrNumericGte is { } v2) range.Gte = v2;
            must.Add(new Condition { Field = new FieldCondition { Key = EntityPayloadFields.CrNumeric, Range = range } });
        }
        if (must.Count == 0) return null;
        var filter = new Filter();
        foreach (var c in must) filter.Must.Add(c);
        return filter;
    }

    private static Condition KW(string field, string value) =>
        new() { Field = new FieldCondition { Key = field, Match = new Match { Keyword = value } } };
}
```

- [ ] **Step 4: Register in Program.cs (via Serena replace_content)**

Add to DI registration alongside existing `IVectorStoreService`:

```csharp
builder.Services.AddSingleton<IEntityVectorStore, QdrantEntityVectorStore>();
```

- [ ] **Step 5: Build**

Run: `dotnet build`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Features/VectorStore/Entities/ Program.cs
git commit -m "feat(vectorstore): add IEntityVectorStore with Qdrant impl + filter builder"
```

---

## Task 15: Extend IngestionWorkType with IngestEntities

**Files:**
- Modify: `Features/Ingestion/IIngestionQueue.cs` (add enum value)
- Modify: `Features/Ingestion/IngestionQueueWorker.cs` (dispatch new type)

- [ ] **Step 1: Modify enum (via Serena)**

```csharp
public enum IngestionWorkType { IngestBlocks, IngestEntities }
```

- [ ] **Step 2: Modify IngestionQueueWorker dispatch (via Serena)**

In the dispatch switch, add a case for `IngestEntities` that resolves `IEntityIngestionOrchestrator` from DI and calls `IngestEntitiesAsync(item.BookId, ct)`. Pattern:

```csharp
switch (item.Type)
{
    case IngestionWorkType.IngestBlocks:
        await blockOrchestrator.IngestBlocksAsync(item.BookId, ct);
        break;
    case IngestionWorkType.IngestEntities:
        await entityOrchestrator.IngestEntitiesAsync(item.BookId, ct);
        break;
}
```

(`IEntityIngestionOrchestrator` is added in Task 16; this code may not compile until then. Sequence Task 15 → 16 strictly.)

- [ ] **Step 3: Defer build verification to end of Task 16**

Skip `dotnet build` here; will verify after Task 16.

- [ ] **Step 4: Commit (after Task 16, combined)**

Combine into Task 16's commit.

---

## Task 16: IEntityIngestionOrchestrator + EntityIngestionOrchestrator

**Files:**
- Create: `Features/Ingestion/Entities/IEntityIngestionOrchestrator.cs`
- Create: `Features/Ingestion/Entities/EntityIngestionOrchestrator.cs`
- Create: `DndMcpAICsharpFun.Tests/Entities/Ingestion/EntityIngestionOrchestratorTests.cs`

- [ ] **Step 1: Implement interface (via Serena)**

```csharp
namespace DndMcpAICsharpFun.Features.Ingestion.Entities;

public interface IEntityIngestionOrchestrator
{
    Task IngestEntitiesAsync(int bookId, CancellationToken ct = default);
}
```

- [ ] **Step 2: Implement orchestrator (via Serena)**

```csharp
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.Entities;

public sealed class EntityIngestionOptions
{
    public string CanonicalDirectory { get; set; } = "data/canonical";
}

public sealed class EntityIngestionOrchestrator(
    IIngestionTracker tracker,
    CanonicalJsonLoader loader,
    EntityCanonicalTextDispatcher textDispatcher,
    EntityReferenceResolver refResolver,
    IEmbeddingService embeddings,
    IEntityVectorStore store,
    IOptions<EntityIngestionOptions> options,
    ILogger<EntityIngestionOrchestrator> logger) : IEntityIngestionOrchestrator
{
    private readonly EntityIngestionOptions _opts = options.Value;

    public async Task IngestEntitiesAsync(int bookId, CancellationToken ct = default)
    {
        var record = await tracker.GetAsync(bookId, ct)
                     ?? throw new InvalidOperationException($"No ingestion record {bookId}");

        var bookSlug = Domain.Entities.EntityIdSlug
            .For(record.DisplayName, EntityType.Class, "x")
            .Split('.')[0];
        var path = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Canonical JSON not found for book {bookId} at {path}", path);

        var file = await loader.LoadAsync(path, ct);

        foreach (var w in refResolver.Resolve(file.Entities))
            logger.LogWarning("Dangling entity reference: {Source} → {Target} ({Path})",
                w.SourceEntityId, w.MissingTargetId, w.FieldPath);

        await store.DeleteByFileHashAsync(record.FileHash, ct);

        var points = new List<EntityPoint>();
        foreach (var envelope in file.Entities)
        {
            ct.ThrowIfCancellationRequested();
            var text = textDispatcher.Render(envelope);
            var renderedEnvelope = envelope with { CanonicalText = text };
            var vector = await embeddings.EmbedAsync(text, ct);
            points.Add(new EntityPoint(renderedEnvelope, vector, record.FileHash));
        }
        await store.UpsertAsync(points, ct);

        await tracker.MarkEntitiesIngestedAsync(bookId, points.Count, ct);
        logger.LogInformation("Entity ingestion complete: book {BookId}, {Count} entities", bookId, points.Count);
    }
}
```

- [ ] **Step 3: Implement minimal test (mock-based, unit-level)**

```csharp
// DndMcpAICsharpFun.Tests/Entities/Ingestion/EntityIngestionOrchestratorTests.cs
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Ingestion;

public class EntityIngestionOrchestratorTests
{
    [Fact]
    public async Task Ingests_three_entities_from_fixture_and_calls_upsert_once()
    {
        var tracker = Substitute.For<IIngestionTracker>();
        var record = new IngestionRecord { Id = 1, DisplayName = "test-book", FileHash = "deadbeef" };
        tracker.GetAsync(1, Arg.Any<CancellationToken>()).Returns(record);

        var embeddings = Substitute.For<IEmbeddingService>();
        embeddings.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new float[1024]);

        var store = Substitute.For<IEntityVectorStore>();

        var canonicalDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "canonical");
        // Rename test-book.json to match the expected slug "test-book".
        // Already named test-book.json — fixture matches the slug derived from record.DisplayName.

        var orchestrator = new EntityIngestionOrchestrator(
            tracker,
            new CanonicalJsonLoader(),
            new EntityCanonicalTextDispatcher(),
            new EntityReferenceResolver(),
            embeddings,
            store,
            Options.Create(new EntityIngestionOptions { CanonicalDirectory = canonicalDir }),
            NullLogger<EntityIngestionOrchestrator>.Instance);

        await orchestrator.IngestEntitiesAsync(1, CancellationToken.None);

        await store.Received(1).DeleteByFileHashAsync("deadbeef", Arg.Any<CancellationToken>());
        await store.Received(1).UpsertAsync(Arg.Is<IList<EntityPoint>>(p => p.Count == 3), Arg.Any<CancellationToken>());
        await tracker.Received(1).MarkEntitiesIngestedAsync(1, 3, Arg.Any<CancellationToken>());
    }
}
```

(Add NSubstitute to the test project if not already present: `dotnet add DndMcpAICsharpFun.Tests package NSubstitute`.)

- [ ] **Step 4: Implement IIngestionTracker.MarkEntitiesIngestedAsync (via Serena)**

Modify `Features/Ingestion/Tracking/IIngestionTracker.cs` to add:

```csharp
Task MarkEntitiesIngestedAsync(int bookId, int entityCount, CancellationToken ct);
```

Implement in `SqliteIngestionTracker.cs`. (Status enum extension covered in Task 17.)

- [ ] **Step 5: Register in DI (via Serena, modify Program.cs)**

```csharp
builder.Services.Configure<EntityIngestionOptions>(builder.Configuration.GetSection("EntityIngestion"));
builder.Services.AddSingleton<CanonicalJsonLoader>();
builder.Services.AddSingleton<EntityCanonicalTextDispatcher>();
builder.Services.AddSingleton<EntityReferenceResolver>();
builder.Services.AddScoped<IEntityIngestionOrchestrator, EntityIngestionOrchestrator>();
```

- [ ] **Step 6: Run tests + build**

Run: `dotnet build && dotnet test --filter "FullyQualifiedName~EntityIngestionOrchestratorTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add Features/Ingestion/Entities/ Features/Ingestion/IIngestionQueue.cs Features/Ingestion/IngestionQueueWorker.cs Features/Ingestion/Tracking/ Program.cs DndMcpAICsharpFun.Tests/Entities/Ingestion/
git commit -m "feat(ingestion): EntityIngestionOrchestrator + queue dispatch + tracker hook"
```

---

## Task 17: IngestionStatus extension

**Files:**
- Modify: `Infrastructure/Sqlite/IngestionStatus.cs`
- Modify: `Infrastructure/Sqlite/IngestionRecord.cs` (if a separate sub-status track is preferred — but for Plan 1 we extend the single enum)

- [ ] **Step 1: Modify status enum (via Serena)**

```csharp
public enum IngestionStatus
{
    Pending,
    Processing,
    Failed,
    Duplicate,
    JsonIngested,
    EntitiesIngesting,
    EntitiesIngested,
    EntitiesFailed,
}
```

- [ ] **Step 2: Update SqliteIngestionTracker.MarkEntitiesIngestedAsync to set the new statuses**

Implementation pattern in `SqliteIngestionTracker.cs`:

```csharp
public async Task MarkEntitiesIngestedAsync(int bookId, int entityCount, CancellationToken ct)
{
    var record = await db.IngestionRecords.FindAsync([bookId], ct)
                 ?? throw new InvalidOperationException($"Record {bookId} not found");
    record.Status = IngestionStatus.EntitiesIngested;
    await db.SaveChangesAsync(ct);
}
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: PASS.

- [ ] **Step 4: Add EF Core migration for the enum (no schema change since `Status` is stored as string, but verify model snapshot consistent)**

Run: `dotnet ef migrations add AddEntityIngestionStatuses --project DndMcpAICsharpFun --startup-project DndMcpAICsharpFun --output-dir Migrations`
Expected: a new migration is generated; if empty, that's fine — string-stored enum values just gain new accepted strings.

If migration is empty, delete it (`rm Migrations/<file>*`). Otherwise commit it.

- [ ] **Step 5: Commit**

```bash
git add Infrastructure/Sqlite/IngestionStatus.cs Features/Ingestion/Tracking/SqliteIngestionTracker.cs Migrations/
git commit -m "feat(ingestion): IngestionStatus values for entity ingestion phases"
```

---

## Task 18: POST /admin/books/{id}/ingest-entities endpoint

**Files:**
- Modify: `Features/Admin/BooksAdminEndpoints.cs`
- Test: `DndMcpAICsharpFun.Tests/Entities/Admin/IngestEntitiesEndpointTests.cs`

- [ ] **Step 1: Write failing test (integration WAF-style)**

```csharp
// DndMcpAICsharpFun.Tests/Entities/Admin/IngestEntitiesEndpointTests.cs
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

public class IngestEntitiesEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public IngestEntitiesEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Returns_404_for_unknown_book()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", "test-admin-key");
        var response = await client.PostAsync("/admin/books/9999/ingest-entities", null);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }
}
```

(Test infrastructure: ensure the `Program` class is `public partial` if not already; existing block-ingest endpoint tests use the same pattern — copy from `BlockIngestionEndpointTests` if it exists.)

- [ ] **Step 2: Run to confirm failure**

Run: `dotnet test --filter "FullyQualifiedName~IngestEntitiesEndpointTests"`
Expected: FAIL — endpoint not registered.

- [ ] **Step 3: Add endpoint in BooksAdminEndpoints.cs (via Serena)**

Inside the existing endpoint mapping group (already protected by admin API-key middleware), add:

```csharp
admin.MapPost("/books/{id:int}/ingest-entities", async (
    int id,
    IIngestionTracker tracker,
    IIngestionQueue queue,
    CancellationToken ct) =>
{
    var record = await tracker.GetAsync(id, ct);
    if (record is null) return Results.NotFound();
    if (record.Status == IngestionStatus.Processing) return Results.Conflict();

    queue.TryEnqueue(new IngestionWorkItem(IngestionWorkType.IngestEntities, id));
    return Results.Accepted($"/admin/books/{id}");
});
```

- [ ] **Step 4: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~IngestEntitiesEndpointTests"`
Expected: PASS.

- [ ] **Step 5: Update DndMcpAICsharpFun.http (via Edit)**

Add an example request:

```http
### Ingest entities for a registered book (reads data/canonical/<book>.json)
POST {{host}}/admin/books/{{bookId}}/ingest-entities
X-Admin-Api-Key: {{adminKey}}
```

- [ ] **Step 6: Commit**

```bash
git add Features/Admin/BooksAdminEndpoints.cs DndMcpAICsharpFun.http DndMcpAICsharpFun.Tests/Entities/Admin/
git commit -m "feat(admin): POST /admin/books/{id}/ingest-entities endpoint"
```

---

## Task 19: Entity retrieval endpoints (by-id + search + admin)

**Files:**
- Create: `Features/Retrieval/Entities/EntitySearchQuery.cs`
- Create: `Features/Retrieval/Entities/EntitySearchResult.cs`
- Create: `Features/Retrieval/Entities/EntityFullResult.cs`
- Create: `Features/Retrieval/Entities/IEntityRetrievalService.cs`
- Create: `Features/Retrieval/Entities/EntityRetrievalService.cs`
- Create: `Features/Retrieval/Entities/EntityRetrievalEndpoints.cs`
- Test: `DndMcpAICsharpFun.Tests/Entities/Retrieval/EntityRetrievalEndpointsTests.cs`

- [ ] **Step 1: Implement records (via Serena)**

```csharp
// EntitySearchQuery.cs
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities;

public sealed record EntitySearchQuery(
    string QueryText,
    EntityType? Type,
    string? SourceBook,
    string? Edition,
    string? BookType,
    string? SettingTag,
    string? Keyword,
    double? CrNumericLte,
    double? CrNumericGte,
    int? SpellLevel,
    string? DamageType,
    int TopK);
```

```csharp
// EntitySearchResult.cs (no fields block — caller fetches by id for details)
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities;

public sealed record EntitySearchResult(
    string Id,
    EntityType Type,
    string Name,
    string SourceBook,
    string Edition,
    int? Page,
    IReadOnlyList<string> SettingTags,
    string Snippet,
    float Score);

public sealed record EntityDiagnosticResult(
    string Id,
    EntityType Type,
    string Name,
    string SourceBook,
    string Edition,
    int? Page,
    IReadOnlyList<string> SettingTags,
    string PointId,
    System.Text.Json.JsonElement Fields,
    float Score);
```

```csharp
// EntityFullResult.cs
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities;

public sealed record EntityFullResult(EntityEnvelope Envelope);
```

```csharp
// IEntityRetrievalService.cs
namespace DndMcpAICsharpFun.Features.Retrieval.Entities;

public interface IEntityRetrievalService
{
    Task<EntityFullResult?> GetByIdAsync(string id, CancellationToken ct);
    Task<IList<EntitySearchResult>> SearchAsync(EntitySearchQuery query, CancellationToken ct);
    Task<IList<EntityDiagnosticResult>> SearchDiagnosticAsync(EntitySearchQuery query, CancellationToken ct);
}
```

```csharp
// EntityRetrievalService.cs
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities;

public sealed class EntityRetrievalService(
    IEmbeddingService embeddings,
    IEntityVectorStore store,
    IOptions<RetrievalOptions> retrievalOptions) : IEntityRetrievalService
{
    private readonly RetrievalOptions _retrieval = retrievalOptions.Value;

    public async Task<EntityFullResult?> GetByIdAsync(string id, CancellationToken ct)
    {
        var envelope = await store.GetByIdAsync(id, ct);
        return envelope is null ? null : new EntityFullResult(envelope);
    }

    public async Task<IList<EntitySearchResult>> SearchAsync(EntitySearchQuery q, CancellationToken ct)
    {
        var hits = await ExecuteAsync(q, ct);
        return hits.Select(h => new EntitySearchResult(
            h.Envelope.Id, h.Envelope.Type, h.Envelope.Name,
            h.Envelope.SourceBook, h.Envelope.Edition, h.Envelope.Page,
            h.Envelope.SettingTags, Truncate(h.Envelope.CanonicalText, 240), h.Score
        )).ToList();
    }

    public async Task<IList<EntityDiagnosticResult>> SearchDiagnosticAsync(EntitySearchQuery q, CancellationToken ct)
    {
        var hits = await ExecuteAsync(q, ct);
        return hits.Select(h => new EntityDiagnosticResult(
            h.Envelope.Id, h.Envelope.Type, h.Envelope.Name,
            h.Envelope.SourceBook, h.Envelope.Edition, h.Envelope.Page,
            h.Envelope.SettingTags, h.PointId, h.Envelope.Fields, h.Score
        )).ToList();
    }

    private async Task<IList<EntitySearchHit>> ExecuteAsync(EntitySearchQuery q, CancellationToken ct)
    {
        var topK = Math.Min(q.TopK <= 0 ? 10 : q.TopK, _retrieval.MaxTopK);
        var vector = await embeddings.EmbedAsync(q.QueryText, ct);
        return await store.SearchAsync(vector, new EntityFilters(
            Type: q.Type, SourceBook: q.SourceBook, Edition: q.Edition,
            BookType: q.BookType, SettingTag: q.SettingTag, Keyword: q.Keyword,
            CrNumericLte: q.CrNumericLte, CrNumericGte: q.CrNumericGte,
            SpellLevel: q.SpellLevel, DamageType: q.DamageType
        ), topK, ct);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max) + "…";
}
```

```csharp
// EntityRetrievalEndpoints.cs
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities;

public static class EntityRetrievalEndpoints
{
    public static WebApplication MapEntityRetrievalEndpoints(this WebApplication app)
    {
        app.MapGet("/retrieval/entities/{id}", GetById);
        app.MapGet("/retrieval/entities/search", SearchPublic);
        app.MapGroup("/admin").MapGet("/retrieval/entities/search", SearchDiagnostic);
        return app;
    }

    private static async Task<IResult> GetById(string id, IEntityRetrievalService svc, CancellationToken ct)
    {
        var result = await svc.GetByIdAsync(id, ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> SearchPublic(
        string? q, string? type, string? sourceBook, string? edition, string? bookType,
        string? settingTag, string? keyword, double? crNumeric_lte, double? crNumeric_gte,
        int? spellLevel, string? damageType, int topK,
        IEntityRetrievalService svc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q)) return Results.BadRequest("Query parameter 'q' is required.");
        var results = await svc.SearchAsync(BuildQuery(q, type, sourceBook, edition, bookType, settingTag, keyword, crNumeric_lte, crNumeric_gte, spellLevel, damageType, topK), ct);
        return Results.Ok(results);
    }

    private static async Task<IResult> SearchDiagnostic(
        string? q, string? type, string? sourceBook, string? edition, string? bookType,
        string? settingTag, string? keyword, double? crNumeric_lte, double? crNumeric_gte,
        int? spellLevel, string? damageType, int topK,
        IEntityRetrievalService svc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q)) return Results.BadRequest("Query parameter 'q' is required.");
        var results = await svc.SearchDiagnosticAsync(BuildQuery(q, type, sourceBook, edition, bookType, settingTag, keyword, crNumeric_lte, crNumeric_gte, spellLevel, damageType, topK), ct);
        return Results.Ok(results);
    }

    private static EntitySearchQuery BuildQuery(
        string q, string? type, string? sourceBook, string? edition, string? bookType,
        string? settingTag, string? keyword, double? crLte, double? crGte,
        int? spellLevel, string? damageType, int topK)
    {
        EntityType? parsedType = Enum.TryParse<EntityType>(type, ignoreCase: true, out var t) ? t : null;
        return new EntitySearchQuery(
            q, parsedType, sourceBook, edition, bookType, settingTag, keyword,
            crLte, crGte, spellLevel, damageType, topK <= 0 ? 10 : topK);
    }
}
```

- [ ] **Step 2: Wire DI and endpoint mapping in Program.cs (via Serena)**

```csharp
builder.Services.AddSingleton<IEntityRetrievalService, EntityRetrievalService>();
// after app build:
app.MapEntityRetrievalEndpoints();
```

- [ ] **Step 3: Update DndMcpAICsharpFun.http**

```http
### Get entity by id
GET {{host}}/retrieval/entities/test-book.class.fighter

### Vector search over entities
GET {{host}}/retrieval/entities/search?q=swashbuckler&type=Subclass

### Search monsters by CR range and keyword
GET {{host}}/retrieval/entities/search?q=swamp&type=Monster&crNumeric_lte=2&keyword=amphibian

### Admin diagnostic search (full fields + pointId)
GET {{host}}/admin/retrieval/entities/search?q=fireball&type=Spell&spellLevel=3
X-Admin-Api-Key: {{adminKey}}
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Features/Retrieval/Entities/ Program.cs DndMcpAICsharpFun.http
git commit -m "feat(retrieval): /retrieval/entities/* endpoints (by-id, search, admin diagnostic)"
```

---

## Task 20: Endpoint integration tests

**Files:**
- Create: `DndMcpAICsharpFun.Tests/Entities/Retrieval/EntityRetrievalEndpointsTests.cs`

- [ ] **Step 1: Write tests (use the `WebApplicationFactory<Program>` pattern from existing tests)**

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Retrieval;

public class EntityRetrievalEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public EntityRetrievalEndpointsTests(WebApplicationFactory<Program> factory) { _factory = factory; }

    [Fact]
    public async Task Get_by_id_returns_404_for_unknown_id()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/retrieval/entities/does-not-exist");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Search_returns_400_when_q_missing()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/retrieval/entities/search");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

(Population-of-collection happy-path tests run against the Qdrant Testcontainer if your existing infra has one; otherwise they belong in the integration-test category.)

- [ ] **Step 2: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~EntityRetrievalEndpointsTests"`
Expected: PASS (these two negative paths don't need Qdrant).

- [ ] **Step 3: Commit**

```bash
git add DndMcpAICsharpFun.Tests/Entities/Retrieval/
git commit -m "test(retrieval): negative-path tests for entity endpoints"
```

---

## Task 21: Extend DELETE /admin/books/{id} to clean up entity points + canonical JSON

**Files:**
- Modify: `Features/Ingestion/BookDeletionService.cs`
- Modify: `Features/Ingestion/IBookDeletionService.cs` (signature unchanged, but impl widens)
- Test: `DndMcpAICsharpFun.Tests/Entities/Deletion/EntityBookDeletionTests.cs`

- [ ] **Step 1: Modify BookDeletionService.DeleteAsync (via Serena)**

After the existing `IVectorStoreService.DeleteBlocksByHashAsync` call, add:
```csharp
await entityStore.DeleteByFileHashAsync(record.FileHash, ct);

var canonicalSlug = EntityIdSlug.For(record.DisplayName, EntityType.Class, "x").Split('.')[0];
var canonicalPath = Path.Combine(entityIngestionOptions.Value.CanonicalDirectory, canonicalSlug + ".json");
if (File.Exists(canonicalPath))
{
    File.Delete(canonicalPath);
    logger.LogInformation("Deleted canonical JSON for book {BookId}: {Path}", record.Id, canonicalPath);
}
```

Inject `IEntityVectorStore` and `IOptions<EntityIngestionOptions>` into the constructor.

- [ ] **Step 2: Write test**

```csharp
// DndMcpAICsharpFun.Tests/Entities/Deletion/EntityBookDeletionTests.cs
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Deletion;

public class EntityBookDeletionTests
{
    [Fact]
    public async Task Delete_calls_entity_store_delete_and_removes_canonical_json()
    {
        var tracker = Substitute.For<IIngestionTracker>();
        var record = new IngestionRecord { Id = 7, DisplayName = "test-book", FileHash = "deadbeef", FilePath = "/tmp/x.pdf", ChunkCount = 10 };
        tracker.GetAsync(7, Arg.Any<CancellationToken>()).Returns(record);

        var blockStore = Substitute.For<IVectorStoreService>();
        var entityStore = Substitute.For<IEntityVectorStore>();
        var canonicalDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(canonicalDir);
        var canonicalPath = Path.Combine(canonicalDir, "test-book.json");
        await File.WriteAllTextAsync(canonicalPath, "{}");

        var pdfPath = Path.GetTempFileName();
        record.FilePath = pdfPath;

        var svc = new BookDeletionService(
            tracker, blockStore, entityStore,
            Options.Create(new EntityIngestionOptions { CanonicalDirectory = canonicalDir }),
            NullLogger<BookDeletionService>.Instance);

        var result = await svc.DeleteAsync(7, CancellationToken.None);
        result.Should().Be(DeleteBookResult.Deleted);

        await entityStore.Received(1).DeleteByFileHashAsync("deadbeef", Arg.Any<CancellationToken>());
        File.Exists(canonicalPath).Should().BeFalse();
        Directory.Delete(canonicalDir, recursive: true);
    }
}
```

- [ ] **Step 3: Run tests + build**

Run: `dotnet build && dotnet test --filter "FullyQualifiedName~EntityBookDeletionTests"`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add Features/Ingestion/BookDeletionService.cs DndMcpAICsharpFun.Tests/Entities/Deletion/
git commit -m "feat(deletion): book deletion clears entity points and canonical JSON"
```

---

## Task 22: End-to-end smoke validation (manual, with hand-written JSON)

This task confirms the whole vertical slice works. No new code.

- [ ] **Step 1: Author a tiny real-book canonical JSON by hand**

Create `data/canonical/phb14.json` with 1 Class (Fighter), 1 Monster (Aboleth), and 1 Spell (Fireball). Use the test fixture as a template. Aim for 200–500 lines.

- [ ] **Step 2: Boot the stack**

```bash
./start.sh Development
```
Wait for healthy.

- [ ] **Step 3: Register a placeholder PDF** (any small PDF will do; the canonical JSON is what populates entities)

```bash
curl -X POST http://localhost:5101/admin/books/register \
     -H "X-Admin-Api-Key: $ADMIN_KEY" \
     -F file=@/path/to/any.pdf -F version=Edition2014 -F displayName=phb14 -F bookType=Core
```

Note the returned `bookId`.

- [ ] **Step 4: Trigger entity ingestion**

```bash
curl -X POST http://localhost:5101/admin/books/<bookId>/ingest-entities \
     -H "X-Admin-Api-Key: $ADMIN_KEY"
```

- [ ] **Step 5: Query the entities**

```bash
# By id
curl http://localhost:5101/retrieval/entities/phb14.class.fighter
# Vector search
curl "http://localhost:5101/retrieval/entities/search?q=fighter"
# Filter
curl "http://localhost:5101/retrieval/entities/search?q=monster&type=Monster"
```

Each call should return populated, sensible JSON.

- [ ] **Step 6: Document the vertical slice in CLAUDE.md**

Add a short section under "Architecture" explaining the dual-collection setup and the canonical JSON workflow. Note that LLM-driven extraction lands in Plan 2.

- [ ] **Step 7: Commit the seed canonical JSON + CLAUDE.md update**

```bash
git add data/canonical/phb14.json CLAUDE.md
git commit -m "feat(entities): seed canonical JSON for phb14 (Fighter, Aboleth, Fireball)"
```

---

## Plan 1 self-review

Before marking complete, re-check the plan against the relevant specs:

**`structured-entities` spec** — every requirement covered:
- Common envelope (Tasks 3, 9): ✓
- Slug scheme (Task 6): ✓
- 20 entity types (Task 1, 4, 5): ✓
- Class Tier 3 fields (Task 4): ✓
- Monster CR string + numeric (Task 4 — `ChallengeRating` record): ✓
- Action type enum + multi-damage (Task 4 — `ActionType`, `DamagePart` array): ✓
- Provenance fields (Task 2): ✓
- Setting tags (Task 3): ✓
- canonicalText embedded representation (Tasks 11, 16): ✓
- Schema versioning + version mismatch error (Tasks 7, 9): ✓
- Cross-entity refs + dangling-warning resolver (Task 10): ✓

**`entity-vector-store` spec** — covered:
- Separate collection (Tasks 12, 13): ✓
- Embed canonicalText (Task 16): ✓
- Per-book ingestion endpoint with idempotency (Tasks 14, 16, 18): ✓
- Payload indexes for filterable fields (Task 13): ✓
- By-ID lookup (Tasks 14, 19): ✓
- Vector search with structured filters (Tasks 14, 19): ✓
- Admin diagnostic with point-id + full fields (Task 19): ✓
- Block index untouched (no edits to existing block path): ✓

**`rag-retrieval` delta** — covered:
- Entity-aware endpoints alongside block search (Task 19): ✓
- Server-side structured filters with composing AND semantics (Tasks 14, 19): ✓
- Envelope-only search results vs full fields by ID (Task 19): ✓

**`ingestion-pipeline` delta (entity-ingestion parts only — extraction in Plan 2)** — covered:
- Queue work-item type for `IngestEntities` (Task 15): ✓
- Status track for entity-ingestion phases (Task 17): ✓
- Deletion cleanup (Task 21): ✓
- Extraction work-item + endpoint: **deferred to Plan 2** (intentional, per series.md)

No placeholders. All steps have full code or full commands. Type names are consistent across tasks (`EntityEnvelope`, `IEntityVectorStore`, `IEntityIngestionOrchestrator`, `EntityIdSlug`, `CanonicalJsonLoader`).

---

## Execution

Per the project's persistent rule (`feedback_always_subagent_option1.md`), execute via `superpowers:subagent-driven-development` immediately on user go-ahead. Do not present a numbered choice.
