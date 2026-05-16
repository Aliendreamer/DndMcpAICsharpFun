# Entity Schema Refinement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `Lore` and `Rule` entity types, tighten required fields on `God`/`Plane`/`Monster`/`Location`/`Faction`, add a `variants` array to `MagicItem`, and update the extraction prompt — so section headings are rejected, new content types are routed correctly, and magic item variants are captured efficiently.

**Architecture:** Pure data/config changes — new JSON schemas, updated `EntityType` enum, updated `ExtractionPromptBuilder` system prompt. No pipeline logic changes. The existing schema validation path (NJsonSchema) and the existing extraction orchestrator pick up the changes automatically once schemas and enum are updated.

**Tech Stack:** C# / .NET 10, NJsonSchema (schema validation), JSON Schema Draft 4, xUnit + FluentAssertions

---

## File Map

| File | Action |
| --- | --- |
| `Domain/Entities/EntityType.cs` | Add `Lore`, `Rule` |
| `Schemas/canonical/LoreFields.schema.json` | Create |
| `Schemas/canonical/RuleFields.schema.json` | Create |
| `Schemas/canonical/GodFields.schema.json` | Add `required` |
| `Schemas/canonical/PlaneFields.schema.json` | Add `required` |
| `Schemas/canonical/MonsterFields.schema.json` | Add `required` |
| `Schemas/canonical/LocationFields.schema.json` | Add `required` |
| `Schemas/canonical/FactionFields.schema.json` | Add `required` |
| `Schemas/canonical/MagicItemFields.schema.json` | Add `variants` array |
| `Features/Ingestion/EntityExtraction/ExtractionPromptBuilder.cs` | Add heading filter, type routing, variant hint |
| `DndMcpAICsharpFun.Tests/Entities/EntityTypeTests.cs` | Update count 20→22, add new types |
| `DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs` | Add `Lore`/`Rule` fixture + schema test |
| `DndMcpAICsharpFun.Tests/Entities/Extraction/ExtractionPromptBuilderTests.cs` | Add tests for new prompt lines |
| `DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json` | Add `Lore`/`Rule`/`MagicItem` variant fixture entities |

---

### Task 1: Add `Lore` and `Rule` to `EntityType` enum

**Files:**
- Modify: `Domain/Entities/EntityType.cs`
- Modify: `DndMcpAICsharpFun.Tests/Entities/EntityTypeTests.cs`

- [ ] **Step 1: Update the failing test — change count and add new types**

```csharp
// DndMcpAICsharpFun.Tests/Entities/EntityTypeTests.cs
[Fact]
public void Has_All_Twenty_Two_Types()
{
    var values = Enum.GetValues<EntityType>();
    values.Should().HaveCount(22);
    values.Should().Contain(new[]
    {
        EntityType.Class, EntityType.Subclass, EntityType.Race, EntityType.Subrace,
        EntityType.Background, EntityType.Feat, EntityType.Spell,
        EntityType.Weapon, EntityType.Armor, EntityType.Item, EntityType.MagicItem,
        EntityType.Monster, EntityType.Trap, EntityType.DiseasePoison, EntityType.VehicleMount,
        EntityType.God, EntityType.Plane, EntityType.Faction, EntityType.Location,
        EntityType.Condition, EntityType.Lore, EntityType.Rule,
    });
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test --filter "Has_All_Twenty_Two_Types" -v quiet
```
Expected: FAIL — `Expected collection to have 22 item(s), but found 20.`

- [ ] **Step 3: Add `Lore` and `Rule` to the enum**

```csharp
// Domain/Entities/EntityType.cs
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
    Lore,
    Rule,
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test --filter "Has_All_Twenty_Two_Types" -v quiet
```
Expected: PASS

- [ ] **Step 5: Run the full suite**

```bash
dotnet test -v quiet
```
Expected: all 189 existing tests still pass (plus the 1 new test = 190 total).

- [ ] **Step 6: Commit**

```bash
git add Domain/Entities/EntityType.cs DndMcpAICsharpFun.Tests/Entities/EntityTypeTests.cs
git commit -m "feat(entities): add Lore and Rule to EntityType enum"
```

---

### Task 2: Create `LoreFields` and `RuleFields` schemas + fixture entities

**Files:**
- Create: `Schemas/canonical/LoreFields.schema.json`
- Create: `Schemas/canonical/RuleFields.schema.json`
- Modify: `DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json`
- Modify: `DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs`

- [ ] **Step 1: Add failing tests for both new schemas**

Add two new `[InlineData]` rows to `SchemaGenerationTests`:

```csharp
// DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs
[Theory]
[InlineData("test-book.class.fighter", "ClassFields")]
[InlineData("test-book.monster.bullywug", "MonsterFields")]
[InlineData("test-book.spell.fireball", "SpellFields")]
[InlineData("test-book.lore.the-planes", "LoreFields")]
[InlineData("test-book.rule.encounter-building", "RuleFields")]
public async Task Generated_schema_validates_fixture_entity(string entityId, string schemaName)
{
    var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "canonical", "test-book.json");
    var loader = new CanonicalJsonLoader();
    var file = await loader.LoadAsync(fixturePath, CancellationToken.None);
    var entity = file.Entities.Single(e => e.Id == entityId);

    var schemaPath = Path.Combine(AppContext.BaseDirectory, "Schemas", "canonical", $"{schemaName}.schema.json");
    File.Exists(schemaPath).Should().BeTrue($"expected schema at {schemaPath}");

    var schema = await JsonSchema.FromFileAsync(schemaPath);
    var fieldsJson = entity.Fields.GetRawText();
    var errors = schema.Validate(fieldsJson);
    errors.Should().BeEmpty(string.Join(", ", errors.Select(e => e.ToString())));
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "Generated_schema_validates_fixture_entity" -v quiet
```
Expected: 2 new rows FAIL — fixture entities not found.

- [ ] **Step 3: Add fixture entities to `test-book.json`**

Open `DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json` and append two entities to the `entities` array:

```json
{
  "id": "test-book.lore.the-planes",
  "type": "Lore",
  "name": "The Planes",
  "sourceBook": "Test Book",
  "edition": "Edition2014",
  "page": 42,
  "firstAppearedIn": { "book": "Test Book", "edition": "Edition2014" },
  "revisedIn": [],
  "settingTags": [],
  "canonicalText": "The planes of existence are the realms of the multiverse.",
  "fields": {
    "category": "Cosmology",
    "description": "An overview of the planes of existence and how they relate to the material world."
  }
},
{
  "id": "test-book.rule.encounter-building",
  "type": "Rule",
  "name": "Encounter Building",
  "sourceBook": "Test Book",
  "edition": "Edition2014",
  "page": 81,
  "firstAppearedIn": { "book": "Test Book", "edition": "Edition2014" },
  "revisedIn": [],
  "settingTags": [],
  "canonicalText": "To build a balanced encounter, calculate the XP budget by party level and size.",
  "fields": {
    "category": "EncounterBuilding",
    "description": "Procedure for calculating an XP budget and selecting monsters for a balanced encounter."
  }
}
```

- [ ] **Step 4: Create `LoreFields.schema.json`**

```json
// Schemas/canonical/LoreFields.schema.json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "LoreFields",
  "type": "object",
  "additionalProperties": false,
  "required": ["category", "description"],
  "properties": {
    "category": {
      "type": "string"
    },
    "description": {
      "type": "string"
    },
    "settingContext": {
      "type": ["null", "string"]
    }
  }
}
```

- [ ] **Step 5: Create `RuleFields.schema.json`**

```json
// Schemas/canonical/RuleFields.schema.json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "RuleFields",
  "type": "object",
  "additionalProperties": false,
  "required": ["category", "description"],
  "properties": {
    "category": {
      "type": "string"
    },
    "description": {
      "type": "string"
    },
    "sourceTable": {
      "type": ["null", "string"]
    }
  }
}
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet test --filter "Generated_schema_validates_fixture_entity" -v quiet
```
Expected: all 5 rows PASS.

- [ ] **Step 7: Run full suite**

```bash
dotnet test -v quiet
```
Expected: all tests pass.

- [ ] **Step 8: Commit**

```bash
git add Schemas/canonical/LoreFields.schema.json Schemas/canonical/RuleFields.schema.json \
  DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json \
  DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs
git commit -m "feat(schemas): add LoreFields and RuleFields schemas"
```

---

### Task 3: Tighten required fields on God, Plane, Monster, Location, Faction

**Files:**
- Modify: `Schemas/canonical/GodFields.schema.json`
- Modify: `Schemas/canonical/PlaneFields.schema.json`
- Modify: `Schemas/canonical/MonsterFields.schema.json`
- Modify: `Schemas/canonical/LocationFields.schema.json`
- Modify: `Schemas/canonical/FactionFields.schema.json`
- Modify: `DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs`

- [ ] **Step 1: Add failing tests — schema rejects heading-like objects**

Add a new test class `SchemaRequiredFieldsTests` in `DndMcpAICsharpFun.Tests/Entities/Schemas/`:

```csharp
// DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaRequiredFieldsTests.cs
using FluentAssertions;
using NJsonSchema;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Schemas;

public class SchemaRequiredFieldsTests
{
    private static async Task<JsonSchema> LoadSchema(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Schemas", "canonical", $"{name}.schema.json");
        return await JsonSchema.FromFileAsync(path);
    }

    [Fact]
    public async Task GodFields_rejects_description_only()
    {
        var schema = await LoadSchema("GodFields");
        var errors = schema.Validate("""{"description":"A section heading"}""");
        errors.Should().NotBeEmpty("alignment and domains are required");
    }

    [Fact]
    public async Task GodFields_accepts_full_god()
    {
        var schema = await LoadSchema("GodFields");
        var errors = schema.Validate("""{"alignment":"Lawful Good","domains":["Life","Light"],"description":"The Morninglord."}""");
        errors.Should().BeEmpty(string.Join(", ", errors.Select(e => e.ToString())));
    }

    [Fact]
    public async Task PlaneFields_rejects_description_only()
    {
        var schema = await LoadSchema("PlaneFields");
        var errors = schema.Validate("""{"description":"A chapter heading"}""");
        errors.Should().NotBeEmpty("category is required");
    }

    [Fact]
    public async Task PlaneFields_accepts_full_plane()
    {
        var schema = await LoadSchema("PlaneFields");
        var errors = schema.Validate("""{"category":"Outer","description":"The Nine Hells of Baator."}""");
        errors.Should().BeEmpty(string.Join(", ", errors.Select(e => e.ToString())));
    }

    [Fact]
    public async Task MonsterFields_rejects_empty_object()
    {
        var schema = await LoadSchema("MonsterFields");
        var errors = schema.Validate("""{}""");
        errors.Should().NotBeEmpty("challengeRating, size, and type are required");
    }

    [Fact]
    public async Task LocationFields_rejects_empty_object()
    {
        var schema = await LoadSchema("LocationFields");
        var errors = schema.Validate("""{}""");
        errors.Should().NotBeEmpty("description is required");
    }

    [Fact]
    public async Task FactionFields_rejects_empty_object()
    {
        var schema = await LoadSchema("FactionFields");
        var errors = schema.Validate("""{}""");
        errors.Should().NotBeEmpty("description is required");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "SchemaRequiredFieldsTests" -v quiet
```
Expected: `GodFields_rejects_description_only`, `PlaneFields_rejects_description_only`, `MonsterFields_rejects_empty_object`, `LocationFields_rejects_empty_object`, `FactionFields_rejects_empty_object` all FAIL (currently no required fields).

- [ ] **Step 3: Update `GodFields.schema.json`**

```json
// Schemas/canonical/GodFields.schema.json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "GodFields",
  "type": "object",
  "additionalProperties": false,
  "required": ["alignment", "domains", "description"],
  "properties": {
    "alignment": {
      "type": "string"
    },
    "domains": {
      "type": "array",
      "items": {
        "type": "string"
      }
    },
    "symbol": {
      "type": ["null", "string"]
    },
    "pantheon": {
      "type": ["null", "string"]
    },
    "plane": {
      "type": ["null", "string"]
    },
    "description": {
      "type": "string"
    }
  }
}
```

- [ ] **Step 4: Update `PlaneFields.schema.json`**

```json
// Schemas/canonical/PlaneFields.schema.json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "PlaneFields",
  "type": "object",
  "additionalProperties": false,
  "required": ["category", "description"],
  "properties": {
    "category": {
      "type": "string"
    },
    "description": {
      "type": "string"
    },
    "relatedPlanes": {
      "type": "array",
      "items": {
        "type": "string"
      }
    }
  }
}
```

- [ ] **Step 5: Update `MonsterFields.schema.json`**

Add `"required": ["challengeRating", "size", "type"]` after `"additionalProperties": false`:

```json
// Schemas/canonical/MonsterFields.schema.json  (only the top-level object block shown)
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "MonsterFields",
  "type": "object",
  "additionalProperties": false,
  "required": ["challengeRating", "size", "type"],
  "properties": {
    ... (all existing properties unchanged)
  },
  "definitions": {
    ... (all existing definitions unchanged)
  }
}
```

- [ ] **Step 6: Update `LocationFields.schema.json`**

```json
// Schemas/canonical/LocationFields.schema.json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "LocationFields",
  "type": "object",
  "additionalProperties": false,
  "required": ["description"],
  "properties": {
    "category": {
      "type": "string"
    },
    "setting": {
      "type": ["null", "string"]
    },
    "description": {
      "type": "string"
    }
  }
}
```

- [ ] **Step 7: Update `FactionFields.schema.json`**

```json
// Schemas/canonical/FactionFields.schema.json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "FactionFields",
  "type": "object",
  "additionalProperties": false,
  "required": ["description"],
  "properties": {
    "headquarters": {
      "type": ["null", "string"]
    },
    "goals": {
      "type": "array",
      "items": {
        "type": "string"
      }
    },
    "description": {
      "type": "string"
    }
  }
}
```

- [ ] **Step 8: Run tests to verify they pass**

```bash
dotnet test --filter "SchemaRequiredFieldsTests" -v quiet
```
Expected: all 7 tests PASS.

- [ ] **Step 9: Run full suite**

```bash
dotnet test -v quiet
```
Expected: all tests pass (existing monster fixture entity must already have `challengeRating`, `size`, `type` — if not, the fixture will need those fields added).

> **Note:** If `test-book.monster.bullywug` fails schema validation, add the missing required fields to its `fields` object in `test-book.json`:
> ```json
> "challengeRating": { "cr": "1/4", "crNumeric": 0.25, "xp": 50, "proficiencyBonus": 2 },
> "size": "Medium",
> "type": "Humanoid"
> ```

- [ ] **Step 10: Commit**

```bash
git add Schemas/canonical/GodFields.schema.json Schemas/canonical/PlaneFields.schema.json \
  Schemas/canonical/MonsterFields.schema.json Schemas/canonical/LocationFields.schema.json \
  Schemas/canonical/FactionFields.schema.json \
  DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaRequiredFieldsTests.cs \
  DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json
git commit -m "feat(schemas): add required fields to God, Plane, Monster, Location, Faction"
```

---

### Task 4: Add `variants` array to `MagicItemFields`

**Files:**
- Modify: `Schemas/canonical/MagicItemFields.schema.json`
- Modify: `DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json`
- Modify: `DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs`

- [ ] **Step 1: Add a failing test — MagicItem fixture with variants validates**

Add `[InlineData("test-book.magic-item.armor", "MagicItemFields")]` to the theory in `SchemaGenerationTests`:

```csharp
[InlineData("test-book.magic-item.armor", "MagicItemFields")]
```

- [ ] **Step 2: Add the fixture entity to `test-book.json`**

```json
{
  "id": "test-book.magic-item.armor",
  "type": "MagicItem",
  "name": "Armor",
  "sourceBook": "Test Book",
  "edition": "Edition2014",
  "page": 152,
  "firstAppearedIn": { "book": "Test Book", "edition": "Edition2014" },
  "revisedIn": [],
  "settingTags": [],
  "canonicalText": "Armor +1, +2, or +3. You have a bonus to AC while wearing this armor.",
  "fields": {
    "rarity": "Varies",
    "itemCategory": "Armor",
    "attunement": "No",
    "description": "Armor with a magical bonus to AC. Comes in +1, +2, and +3 tiers.",
    "variants": [
      { "suffix": "+1", "rarity": "Rare", "bonus": 1, "description": null },
      { "suffix": "+2", "rarity": "Very Rare", "bonus": 2, "description": null },
      { "suffix": "+3", "rarity": "Legendary", "bonus": 3, "description": null }
    ]
  }
}
```

- [ ] **Step 3: Run test to verify it fails**

```bash
dotnet test --filter "Generated_schema_validates_fixture_entity" -v quiet
```
Expected: the new `armor` row FAIL — `additionalProperties` violation since `variants` is not in the schema yet.

- [ ] **Step 4: Update `MagicItemFields.schema.json`**

```json
// Schemas/canonical/MagicItemFields.schema.json
{
  "$schema": "http://json-schema.org/draft-04/schema#",
  "title": "MagicItemFields",
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "rarity": {
      "type": "string"
    },
    "itemCategory": {
      "type": "string"
    },
    "attunement": {
      "type": "string"
    },
    "description": {
      "type": "string"
    },
    "variants": {
      "type": ["array", "null"],
      "items": {
        "type": "object",
        "additionalProperties": false,
        "properties": {
          "suffix": {
            "type": "string"
          },
          "rarity": {
            "type": "string"
          },
          "bonus": {
            "type": ["integer", "null"],
            "format": "int32"
          },
          "description": {
            "type": ["null", "string"]
          }
        }
      }
    }
  }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test --filter "Generated_schema_validates_fixture_entity" -v quiet
```
Expected: all rows including `armor` PASS.

- [ ] **Step 6: Run full suite**

```bash
dotnet test -v quiet
```
Expected: all tests pass.

- [ ] **Step 7: Commit**

```bash
git add Schemas/canonical/MagicItemFields.schema.json \
  DndMcpAICsharpFun.Tests/Fixtures/canonical/test-book.json \
  DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs
git commit -m "feat(schemas): add variants array to MagicItemFields"
```

---

### Task 5: Update `ExtractionPromptBuilder` with heading filter, type routing, and variant hint

**Files:**
- Modify: `Features/Ingestion/EntityExtraction/ExtractionPromptBuilder.cs`
- Modify: `DndMcpAICsharpFun.Tests/Entities/Extraction/ExtractionPromptBuilderTests.cs`

- [ ] **Step 1: Write failing tests**

Add to `ExtractionPromptBuilderTests`:

```csharp
[Fact]
public void System_prompt_contains_heading_filter()
{
    var b = new ExtractionPromptBuilder();
    var prompt = b.BuildSystemPrompt("DMG", "Edition2014", EntityType.God);
    prompt.Should().Contain("chapter titles")
          .And.Contain("section headings")
          .And.Contain("named, discrete game elements");
}

[Fact]
public void System_prompt_for_God_contains_type_routing_hint()
{
    var b = new ExtractionPromptBuilder();
    var prompt = b.BuildSystemPrompt("DMG", "Edition2014", EntityType.God);
    prompt.Should().Contain("named deity")
          .And.Contain("alignment")
          .And.Contain("domain");
}

[Fact]
public void System_prompt_for_Plane_contains_type_routing_hint()
{
    var b = new ExtractionPromptBuilder();
    var prompt = b.BuildSystemPrompt("DMG", "Edition2014", EntityType.Plane);
    prompt.Should().Contain("named plane of existence")
          .And.Contain("Inner, Outer, Transitive");
}

[Fact]
public void System_prompt_for_Monster_contains_stat_block_hint()
{
    var b = new ExtractionPromptBuilder();
    var prompt = b.BuildSystemPrompt("DMG", "Edition2014", EntityType.Monster);
    prompt.Should().Contain("stat block")
          .And.Contain("challenge rating");
}

[Fact]
public void System_prompt_for_Lore_contains_lore_routing_hint()
{
    var b = new ExtractionPromptBuilder();
    var prompt = b.BuildSystemPrompt("DMG", "Edition2014", EntityType.Lore);
    prompt.Should().Contain("worldbuilding")
          .And.Contain("not a discrete game entity");
}

[Fact]
public void System_prompt_for_Rule_contains_rule_routing_hint()
{
    var b = new ExtractionPromptBuilder();
    var prompt = b.BuildSystemPrompt("DMG", "Edition2014", EntityType.Rule);
    prompt.Should().Contain("encounter tables")
          .And.Contain("random tables");
}

[Fact]
public void System_prompt_for_MagicItem_contains_variants_hint()
{
    var b = new ExtractionPromptBuilder();
    var prompt = b.BuildSystemPrompt("DMG", "Edition2014", EntityType.MagicItem);
    prompt.Should().Contain("variants")
          .And.Contain("+1");
}

[Fact]
public void Tool_name_for_Lore_is_emit_lore_fields()
{
    new ExtractionPromptBuilder().ToolName(EntityType.Lore).Should().Be("emit_lore_fields");
}

[Fact]
public void Tool_name_for_Rule_is_emit_rule_fields()
{
    new ExtractionPromptBuilder().ToolName(EntityType.Rule).Should().Be("emit_rule_fields");
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "ExtractionPromptBuilderTests" -v quiet
```
Expected: 9 new tests FAIL, 3 existing tests still PASS.

- [ ] **Step 3: Update `ExtractionPromptBuilder.BuildSystemPrompt`**

```csharp
// Features/Ingestion/EntityExtraction/ExtractionPromptBuilder.cs
using System.Text;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class ExtractionPromptBuilder
{
    public string BuildSystemPrompt(string sourceBook, string edition, EntityType type)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are extracting structured D&D rules data from official rulebook text.");
        sb.AppendLine($"Source book: {sourceBook} ({edition}).");
        sb.AppendLine($"Entity type: {type}.");
        sb.AppendLine();
        sb.AppendLine($"Call the tool `{ToolName(type)}` with a JSON object that conforms exactly to its input_schema.");
        sb.AppendLine("Do not include any prose. The tool's input is the only output we read.");
        sb.AppendLine("If the source text is incomplete or ambiguous, leave optional fields null/absent rather than guessing.");
        sb.AppendLine("The source text may contain OCR artifacts (e.g. 'gons' → 'gods', 'lhe' → 'the', 'encaunters' → 'encounters'). Use surrounding context to infer the correct meaning.");
        sb.AppendLine("Cross-entity references must use existing slug-style IDs of form `<book-slug>.<type-slug>.<entity-slug>`.");
        sb.AppendLine();
        sb.AppendLine("Do not extract chapter titles, section headings, or table headers as entities. Only extract named, discrete game elements.");
        sb.AppendLine();
        sb.AppendLine("Type routing guidance:");
        sb.AppendLine("- Use `Lore` for named worldbuilding concepts, cosmology descriptions, pantheon overviews, religious philosophies, and cultural/setting flavour that is not a discrete game entity.");
        sb.AppendLine("- Use `Rule` for mechanical procedures, encounter tables, adventure design guidelines, random tables, and DMing system explanations.");
        sb.AppendLine("- Use `God` only when the entity is a named deity with known alignment and at least one domain.");
        sb.AppendLine("- Use `Plane` only when the entity is a named plane of existence with a defined category (Inner, Outer, Transitive, Material, etc.).");
        sb.AppendLine("- Use `Monster` only when the entity has a stat block with a challenge rating.");
        if (type == EntityType.MagicItem)
            sb.AppendLine("If the source text describes multiple tiers or variants of the same item (e.g. +1/+2/+3), extract them as a single entity with a `variants` array rather than separate entities.");
        return sb.ToString();
    }

    public string BuildUserPrompt(EntityCandidate candidate)
    {
        var pageNote = candidate.Page is { } p ? $" (page {p})" : "";
        var sb = new StringBuilder();
        sb.AppendLine($"Entity: {candidate.DisplayName}{pageNote}");
        sb.AppendLine();
        sb.AppendLine("Source text:");
        sb.AppendLine("```");
        sb.Append(candidate.Text);
        sb.AppendLine();
        sb.AppendLine("```");
        return sb.ToString();
    }

    public string ToolName(EntityType type)
    {
        var camel = type.ToString();
        var sb = new StringBuilder("emit_");
        for (int i = 0; i < camel.Length; i++)
        {
            var c = camel[i];
            if (i > 0 && char.IsUpper(c)) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        sb.Append("_fields");
        return sb.ToString();
    }

    public string ToolDescription(EntityType type) =>
        $"Emit a structured {type} entity's `fields` object. The input MUST validate against the provided schema.";
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test --filter "ExtractionPromptBuilderTests" -v quiet
```
Expected: all 12 tests PASS.

- [ ] **Step 5: Run full suite**

```bash
dotnet test -v quiet
```
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add Features/Ingestion/EntityExtraction/ExtractionPromptBuilder.cs \
  DndMcpAICsharpFun.Tests/Entities/Extraction/ExtractionPromptBuilderTests.cs
git commit -m "feat(extraction): add heading filter, type routing, and variant hint to system prompt"
```

---

## Self-Review

**Spec coverage:**
- ✅ `Lore` + `Rule` enum values — Task 1
- ✅ `LoreFields.schema.json` + `RuleFields.schema.json` — Task 2
- ✅ Required fields: God, Plane, Monster, Location, Faction — Task 3
- ✅ `variants` array in `MagicItemFields` — Task 4
- ✅ Heading filter in prompt — Task 5
- ✅ Type routing guidance in prompt — Task 5
- ✅ MagicItem variant hint in prompt — Task 5
- ✅ `EntitySearchFilters` — no change needed; `EntityType` enum is the source of truth, retrieval filters parse `type` query param by enum name which auto-includes new values

**Placeholder scan:** None found.

**Type consistency:** `EntityType.Lore` and `EntityType.Rule` introduced in Task 1, used in Task 5 tests — consistent throughout.
