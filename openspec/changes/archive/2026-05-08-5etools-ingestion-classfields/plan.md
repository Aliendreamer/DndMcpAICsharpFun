# 5etools Ingestion + ClassFields Alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a direct 5etools JSON ingestion pipeline (`POST /admin/5etools/import`), align ClassFields/SubclassFields C# records to the 5etools schema, add per-type canonical text renderers for all entity types, and add source-provenance tracking so 5etools data wins over LLM data but never overwrites manual corrections.

**Architecture:** Extend `EntityIngestionOrchestrator` with a `DataSource` provenance field on `EntityEnvelope`. Add a `FivetoolsIngestionService` that reads from a static `FivetoolsSourceRegistry`, maps each 5etools JSON entry to an `EntityEnvelope`, computes `CanonicalText` via the shared `EntityCanonicalTextDispatcher`, and upserts directly to `dnd_entities`. `EntityCanonicalTextDispatcher` gains typed renderers for Class/Subclass and JsonElement-based renderers for every other entity type — eliminating the pre-computed text fallback for 5etools-sourced entities.

**Tech Stack:** C# 13 / .NET 10, NSubstitute (mocks), xUnit + FluentAssertions, Qdrant, 5etools JSON files in `5etools/` directory.

---

## File Map

| Action | Path |
|--------|------|
| Modify | `Domain/Entities/EntityEnvelope.cs` |
| Modify | `Domain/Entities/Fields/ClassFields.cs` |
| Modify | `Domain/Entities/Fields/SubclassFields.cs` |
| Modify | `Infrastructure/Qdrant/EntityPayloadFields.cs` |
| Modify | `Features/VectorStore/Entities/IEntityVectorStore.cs` |
| Modify | `Features/VectorStore/Entities/QdrantEntityVectorStore.cs` |
| Modify | `Features/Ingestion/Entities/EntityIngestionOrchestrator.cs` |
| Modify | `Features/Entities/CanonicalText/EntityCanonicalTextDispatcher.cs` |
| Modify | `Features/Admin/BooksAdminEndpoints.cs` (add 5etools group) |
| Modify | `Extensions/ServiceCollectionExtensions.cs` |
| Modify | `Program.cs` |
| Modify | `DndMcpAICsharpFun.http` |
| Create | `Features/Entities/CanonicalText/ClassCanonicalTextRenderer.cs` |
| Create | `Features/Entities/CanonicalText/SubclassCanonicalTextRenderer.cs` |
| Create | `Features/Entities/CanonicalText/SimpleEntityRenderers.cs` (Race, Background, Feat, Item, MagicItem, Weapon, Armor, God, Trap, Condition, DiseasePoison, VehicleMount, Plane, Faction, Location, Lore, Rule, Subrace) |
| Create | `Features/Ingestion/FivetoolsIngestion/FivetoolsSourceRegistry.cs` |
| Create | `Features/Ingestion/FivetoolsIngestion/IFivetoolsEntityMapper.cs` |
| Create | `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsClassMapper.cs` |
| Create | `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsSubclassMapper.cs` |
| Create | `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsSpellMapper.cs` |
| Create | `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsMonsterMapper.cs` |
| Create | `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsRaceMapper.cs` |
| Create | `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsBackgroundMapper.cs` |
| Create | `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsFeatMapper.cs` |
| Create | `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsItemMapper.cs` |
| Create | `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsGodMapper.cs` |
| Create | `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsTrapMapper.cs` |
| Create | `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsConditionMapper.cs` |
| Create | `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsVehicleMapper.cs` |
| Create | `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsRuleMapper.cs` |
| Create | `Features/Ingestion/FivetoolsIngestion/FivetoolsIngestionService.cs` |
| Create | `Features/Admin/FivetoolsAdminEndpoints.cs` |
| Modify | `DndMcpAICsharpFun.Tests/Entities/CanonicalTextRendererTests.cs` |
| Create | `DndMcpAICsharpFun.Tests/Entities/CanonicalText/SimpleEntityRendererTests.cs` |
| Create | `DndMcpAICsharpFun.Tests/Entities/Ingestion/FivetoolsIngestionServiceTests.cs` |
| Modify | `DndMcpAICsharpFun.Tests/Entities/Ingestion/EntityIngestionOrchestratorTests.cs` |
| Create | `DndMcpAICsharpFun.Tests/Entities/Ingestion/FivetoolsSourceRegistryTests.cs` |
| Create | `DndMcpAICsharpFun.Tests/Entities/Ingestion/FivetoolsMappersTests.cs` |

---

## Task 1: ClassFields + SubclassFields C# record alignment

**Files:**
- Modify: `Domain/Entities/Fields/ClassFields.cs`
- Modify: `Domain/Entities/Fields/SubclassFields.cs`
- Modify: `DndMcpAICsharpFun.Tests/Entities/CanonicalJsonLoaderTests.cs`

- [x] **Step 1: Write failing test for typed ClassFields deserialization**

```csharp
// In DndMcpAICsharpFun.Tests/Entities/CanonicalJsonLoaderTests.cs
// Replace the existing Load_deserialises_class_fields test with:
[Fact]
public async Task Load_deserialises_class_fields_typed()
{
    var loader = new CanonicalJsonLoader();
    var file = await loader.LoadAsync(FixturePath, CancellationToken.None);
    var fighter = file.Entities.Single(e => e.Id == "test-book.class.fighter");
    var fields = loader.DeserialiseFields<ClassFields>(fighter);
    fields.Hd.Should().NotBeNull();
    fields.Hd!.Faces.Should().Be(10);
    fields.Hd.Number.Should().Be(1);
    fields.ClassFeatures.Should().NotBeNull().And.HaveCount(3);
    fields.Proficiency.Should().Contain("str").And.Contain("con");
}

[Fact]
public async Task Load_deserialises_subclass_fields_typed()
{
    var loader = new CanonicalJsonLoader();
    var file = await loader.LoadAsync(FixturePath, CancellationToken.None);
    var battleMaster = file.Entities.Single(e => e.Id == "test-book.subclass.battle-master");
    var fields = loader.DeserialiseFields<SubclassFields>(battleMaster);
    fields.ClassName.Should().Be("Fighter");
    fields.ShortName.Should().Be("Battle Master");
    fields.SubclassFeatures.Should().NotBeNull().And.HaveCount(2);
}
```

- [x] **Step 2: Run tests to verify they fail**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "Load_deserialises_class_fields_typed|Load_deserialises_subclass_fields_typed" -v
```
Expected: FAIL — `ClassFields` has no `Hd` property.

- [x] **Step 3: Replace ClassFields.cs with 5etools-aligned records**

```csharp
// Domain/Entities/Fields/ClassFields.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record HitDice(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("faces")]  int Faces);

public sealed record ClassFields(
    [property: JsonPropertyName("hd")]                   HitDice? Hd,
    [property: JsonPropertyName("proficiency")]           IReadOnlyList<string>? Proficiency,
    [property: JsonPropertyName("startingProficiencies")] JsonElement? StartingProficiencies,
    [property: JsonPropertyName("classFeatures")]         IReadOnlyList<JsonElement>? ClassFeatures,
    [property: JsonPropertyName("multiclassing")]         JsonElement? Multiclassing,
    [property: JsonPropertyName("entries")]               IReadOnlyList<JsonElement>? Entries,
    [property: JsonPropertyName("subclassTitle")]         string? SubclassTitle);
```

- [x] **Step 4: Replace SubclassFields.cs with 5etools-aligned record**

```csharp
// Domain/Entities/Fields/SubclassFields.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record SubclassFields(
    [property: JsonPropertyName("className")]        string ClassName,
    [property: JsonPropertyName("classSource")]      string? ClassSource,
    [property: JsonPropertyName("shortName")]        string? ShortName,
    [property: JsonPropertyName("subclassFeatures")] IReadOnlyList<JsonElement>? SubclassFeatures,
    [property: JsonPropertyName("entries")]          IReadOnlyList<JsonElement>? Entries);
```

- [x] **Step 5: Run tests**

```bash
dotnet test DndMcpAICsharpFun.Tests -v
```
Expected: all tests pass. `CanonicalJsonLoaderTests` should no longer have the old raw-JSON workaround comment.

- [x] **Step 6: Commit**

```bash
git add Domain/Entities/Fields/ClassFields.cs Domain/Entities/Fields/SubclassFields.cs \
    DndMcpAICsharpFun.Tests/Entities/CanonicalJsonLoaderTests.cs
git commit -m "feat(domain): align ClassFields + SubclassFields records to 5etools shape"
```

---

## Task 2: ClassCanonicalTextRenderer + SubclassCanonicalTextRenderer

**Files:**
- Create: `Features/Entities/CanonicalText/ClassCanonicalTextRenderer.cs`
- Create: `Features/Entities/CanonicalText/SubclassCanonicalTextRenderer.cs`
- Modify: `Features/Entities/CanonicalText/EntityCanonicalTextDispatcher.cs`
- Modify: `DndMcpAICsharpFun.Tests/Entities/CanonicalTextRendererTests.cs`

- [x] **Step 1: Write failing renderer tests**

```csharp
// In DndMcpAICsharpFun.Tests/Entities/CanonicalTextRendererTests.cs, add:
[Fact]
public void Class_renderer_includes_hitdie_proficiencies_and_features()
{
    var features = new[]
    {
        JsonSerializer.Deserialize<JsonElement>("{\"classFeature\":\"Fighting Style|Fighter||1\",\"level\":1}"),
        JsonSerializer.Deserialize<JsonElement>("{\"classFeature\":\"Second Wind|Fighter||1\",\"level\":1}"),
        JsonSerializer.Deserialize<JsonElement>("{\"classFeature\":\"Action Surge|Fighter||2\",\"level\":2}"),
    };
    var fields = new ClassFields(
        Hd: new HitDice(1, 10),
        Proficiency: new[] { "str", "con" },
        StartingProficiencies: null,
        ClassFeatures: features,
        Multiclassing: null,
        Entries: null,
        SubclassTitle: "Martial Archetype");
    var text = new ClassCanonicalTextRenderer().Render("Fighter", fields);
    text.Should().Contain("d10")
        .And.Contain("STR, CON")
        .And.Contain("Fighting Style (1)")
        .And.Contain("Action Surge (2)");
}

[Fact]
public void Subclass_renderer_includes_classname_and_features()
{
    var features = new[]
    {
        JsonSerializer.Deserialize<JsonElement>("{\"subclassFeature\":\"Combat Superiority|Fighter|PHB|Battle Master|PHB|3\",\"level\":3}"),
        JsonSerializer.Deserialize<JsonElement>("{\"subclassFeature\":\"Know Your Enemy|Fighter|PHB|Battle Master|PHB|7\",\"level\":7}"),
    };
    var fields = new SubclassFields(
        ClassName: "Fighter",
        ClassSource: "PHB",
        ShortName: "Battle Master",
        SubclassFeatures: features,
        Entries: null);
    var text = new SubclassCanonicalTextRenderer().Render("Battle Master", fields);
    text.Should().Contain("Fighter subclass")
        .And.Contain("Combat Superiority (3)")
        .And.Contain("Know Your Enemy (7)");
}
```

- [x] **Step 2: Run tests to verify they fail**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "Class_renderer|Subclass_renderer" -v
```
Expected: FAIL — types not found.

- [x] **Step 3: Create ClassCanonicalTextRenderer.cs**

```csharp
// Features/Entities/CanonicalText/ClassCanonicalTextRenderer.cs
using System.Text;
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities.Fields;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public sealed class ClassCanonicalTextRenderer : IEntityCanonicalTextRenderer<ClassFields>
{
    public string Render(string name, ClassFields f)
    {
        var sb = new StringBuilder();
        var hitDie = f.Hd is { } hd ? $"d{hd.Faces}" : "?";
        sb.AppendLine($"{name} — {hitDie} hit die.");

        if (f.Proficiency is { Count: > 0 })
        {
            var saves = string.Join(", ", f.Proficiency.Select(p => p.ToUpperInvariant()));
            sb.AppendLine($"Saving throws: {saves}.");
        }

        if (f.ClassFeatures is { Count: > 0 })
        {
            var features = f.ClassFeatures
                .Select(ExtractFeatureEntry)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .OrderBy(x => x.Level)
                .Select(x => $"{x.Name} ({x.Level})")
                .ToList();
            if (features.Count > 0)
                sb.AppendLine($"Features: {string.Join(", ", features)}.");
        }

        return sb.ToString();
    }

    private static (string Name, int Level)? ExtractFeatureEntry(JsonElement e)
    {
        // classFeatures items are either strings "FeatureName|Class||Level"
        // or objects { "classFeature": "...", "level": N }
        if (e.ValueKind == JsonValueKind.String)
        {
            var raw = e.GetString() ?? string.Empty;
            var parts = raw.Split('|');
            if (parts.Length >= 3 && int.TryParse(parts[^1], out var lv))
                return (StripPipe(parts[0]), lv);
            return null;
        }
        if (e.ValueKind == JsonValueKind.Object
            && e.TryGetProperty("classFeature", out var cf)
            && e.TryGetProperty("level", out var lv2))
        {
            var raw = cf.GetString() ?? string.Empty;
            var name = StripPipe(raw.Split('|')[0]);
            return (name, lv2.GetInt32());
        }
        return null;
    }

    private static string StripPipe(string s)
    {
        var idx = s.IndexOf('|');
        return idx >= 0 ? s[..idx] : s;
    }
}
```

- [x] **Step 4: Create SubclassCanonicalTextRenderer.cs**

```csharp
// Features/Entities/CanonicalText/SubclassCanonicalTextRenderer.cs
using System.Text;
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities.Fields;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public sealed class SubclassCanonicalTextRenderer : IEntityCanonicalTextRenderer<SubclassFields>
{
    public string Render(string name, SubclassFields f)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{name} ({f.ClassName} subclass).");

        if (f.SubclassFeatures is { Count: > 0 })
        {
            var features = f.SubclassFeatures
                .Select(ExtractFeatureEntry)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .OrderBy(x => x.Level)
                .Select(x => $"{x.Name} ({x.Level})")
                .ToList();
            if (features.Count > 0)
                sb.AppendLine($"Features: {string.Join(", ", features)}.");
        }

        return sb.ToString();
    }

    private static (string Name, int Level)? ExtractFeatureEntry(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.String)
        {
            var raw = e.GetString() ?? string.Empty;
            var parts = raw.Split('|');
            if (parts.Length >= 1 && int.TryParse(parts[^1], out var lv))
                return (parts[0], lv);
            return null;
        }
        if (e.ValueKind == JsonValueKind.Object
            && e.TryGetProperty("subclassFeature", out var sf)
            && e.TryGetProperty("level", out var lv2))
        {
            var raw = sf.GetString() ?? string.Empty;
            return (raw.Split('|')[0], lv2.GetInt32());
        }
        return null;
    }
}
```

- [x] **Step 5: Update EntityCanonicalTextDispatcher to wire Class + Subclass**

```csharp
// Features/Entities/CanonicalText/EntityCanonicalTextDispatcher.cs
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Entities;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public sealed class EntityCanonicalTextDispatcher
{
    private readonly MonsterCanonicalTextRenderer _monsterR = new();
    private readonly SpellCanonicalTextRenderer _spellR = new();
    private readonly ClassCanonicalTextRenderer _classR = new();
    private readonly SubclassCanonicalTextRenderer _subclassR = new();
    private readonly CanonicalJsonLoader _loader = new();

    public string Render(EntityEnvelope envelope)
    {
        return envelope.Type switch
        {
            EntityType.Monster  => _monsterR.Render(envelope.Name, _loader.DeserialiseFields<MonsterFields>(envelope)),
            EntityType.Spell    => _spellR.Render(envelope.Name, _loader.DeserialiseFields<SpellFields>(envelope)),
            EntityType.Class    => _classR.Render(envelope.Name, _loader.DeserialiseFields<ClassFields>(envelope)),
            EntityType.Subclass => _subclassR.Render(envelope.Name, _loader.DeserialiseFields<SubclassFields>(envelope)),
            _ => envelope.CanonicalText,
        };
    }
}
```

- [x] **Step 6: Run all tests**

```bash
dotnet test DndMcpAICsharpFun.Tests -v
```
Expected: all pass.

- [x] **Step 7: Commit**

```bash
git add Features/Entities/CanonicalText/ClassCanonicalTextRenderer.cs \
    Features/Entities/CanonicalText/SubclassCanonicalTextRenderer.cs \
    Features/Entities/CanonicalText/EntityCanonicalTextDispatcher.cs \
    DndMcpAICsharpFun.Tests/Entities/CanonicalTextRendererTests.cs
git commit -m "feat(renderers): add ClassCanonicalTextRenderer + SubclassCanonicalTextRenderer; wire dispatcher"
```

---

## Task 3: SimpleEntityRenderers for all remaining types

**Files:**
- Create: `Features/Entities/CanonicalText/SimpleEntityRenderers.cs`
- Modify: `Features/Entities/CanonicalText/EntityCanonicalTextDispatcher.cs`
- Create: `DndMcpAICsharpFun.Tests/Entities/CanonicalText/SimpleEntityRendererTests.cs`

These renderers all implement a shared interface `ISimpleEntityRenderer` that takes `(string name, JsonElement fields)` — no typed C# record needed since these types are JSON-schema-validated only.

- [x] **Step 1: Write failing tests**

```csharp
// DndMcpAICsharpFun.Tests/Entities/CanonicalText/SimpleEntityRendererTests.cs
using System.Text.Json;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.CanonicalText;

public class SimpleEntityRendererTests
{
    private static JsonElement J(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    [Fact]
    public void Race_renderer_includes_size_and_speed()
    {
        var fields = J("{\"size\":[\"M\"],\"speed\":30,\"traitTags\":[\"Darkvision\"]}");
        var text = new RaceCanonicalTextRenderer().Render("Human", fields);
        text.Should().Contain("Medium").And.Contain("30 ft").And.Contain("Darkvision");
    }

    [Fact]
    public void Background_renderer_includes_skill_proficiencies()
    {
        var fields = J("{\"skillProficiencies\":[{\"history\":true,\"insight\":true}],\"entries\":[\"A sage studies arcane lore.\"]}");
        var text = new BackgroundCanonicalTextRenderer().Render("Sage", fields);
        text.Should().Contain("Sage").And.Contain("history").And.Contain("insight");
    }

    [Fact]
    public void Feat_renderer_includes_prerequisite_and_entries()
    {
        var fields = J("{\"prerequisite\":[{\"level\":{\"level\":4}}],\"entries\":[\"You master the technique of fighting in two weapons.\"]}");
        var text = new FeatCanonicalTextRenderer().Render("Dual Wielder", fields);
        text.Should().Contain("Dual Wielder").And.Contain("fighting in two weapons");
    }

    [Fact]
    public void God_renderer_includes_domains_and_alignment()
    {
        var fields = J("{\"alignment\":[\"L\",\"G\"],\"domains\":[\"Life\",\"Light\"],\"symbol\":\"Sun\",\"pantheon\":\"Forgotten Realms\",\"entries\":[\"Lathander is the god of birth.\"]}");
        var text = new GodCanonicalTextRenderer().Render("Lathander", fields);
        text.Should().Contain("Forgotten Realms").And.Contain("Life").And.Contain("Lathander");
    }

    [Fact]
    public void Rule_renderer_includes_ruletype_and_entries()
    {
        var fields = J("{\"ruleType\":\"O\",\"entries\":[\"This optional rule lets players do something cool.\"]}");
        var text = new RuleCanonicalTextRenderer().Render("Flanking", fields);
        text.Should().Contain("Flanking").And.Contain("optional").And.Contain("cool");
    }

    [Fact]
    public void Condition_renderer_includes_entries()
    {
        var fields = J("{\"entries\":[\"A blinded creature cannot see.\",\"Attack rolls against the creature have advantage.\"]}");
        var text = new ConditionCanonicalTextRenderer().Render("Blinded", fields);
        text.Should().Contain("Blinded").And.Contain("cannot see");
    }

    [Fact]
    public void MagicItem_renderer_includes_rarity_and_attunement()
    {
        var fields = J("{\"type\":\"W\",\"rarity\":\"rare\",\"reqAttune\":true,\"entries\":[\"This wand crackles with electricity.\"]}");
        var text = new MagicItemCanonicalTextRenderer().Render("Wand of Lightning Bolts", fields);
        text.Should().Contain("rare").And.Contain("attunement").And.Contain("electricity");
    }
}
```

- [x] **Step 2: Run to verify failure**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "SimpleEntityRendererTests" -v
```
Expected: FAIL — types not found.

- [x] **Step 3: Create SimpleEntityRenderers.cs**

```csharp
// Features/Entities/CanonicalText/SimpleEntityRenderers.cs
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public interface ISimpleEntityRenderer
{
    string Render(string name, JsonElement fields);
}

internal static class RendererHelpers
{
    private static readonly Regex TagRx = new(@"\{@\w+\s([^|}]+)[^}]*\}", RegexOptions.Compiled);
    private static readonly Dictionary<string, string> SizeMap = new(StringComparer.OrdinalIgnoreCase)
        { ["T"] = "Tiny", ["S"] = "Small", ["M"] = "Medium", ["L"] = "Large", ["H"] = "Huge", ["G"] = "Gargantuan" };
    private static readonly Dictionary<string, string> AlignMap = new(StringComparer.OrdinalIgnoreCase)
        { ["L"] = "Lawful", ["C"] = "Chaotic", ["G"] = "Good", ["E"] = "Evil", ["N"] = "Neutral", ["U"] = "Unaligned", ["A"] = "Any" };

    public static string StripTags(string s) => TagRx.Replace(s, "$1");
    public static string MapSize(string code) => SizeMap.TryGetValue(code, out var v) ? v : code;
    public static string MapAlign(string code) => AlignMap.TryGetValue(code, out var v) ? v : code;

    public static string FirstEntryText(JsonElement fields)
    {
        if (!fields.TryGetProperty("entries", out var entries)) return string.Empty;
        if (entries.ValueKind != JsonValueKind.Array) return string.Empty;
        var first = entries.EnumerateArray().FirstOrDefault();
        if (first.ValueKind == JsonValueKind.String)
            return StripTags(first.GetString()!);
        return string.Empty;
    }

    public static string StringProp(JsonElement e, string key)
    {
        return e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()! : string.Empty;
    }

    public static IEnumerable<string> StringArray(JsonElement e, string key)
    {
        if (!e.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Enumerable.Empty<string>();
        return arr.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()!);
    }
}

public sealed class RaceCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var sb = new StringBuilder();
        var sizes = RendererHelpers.StringArray(f, "size")
            .Select(RendererHelpers.MapSize).ToList();
        var sizeText = sizes.Count > 0 ? string.Join("/", sizes) : "Unknown";
        sb.Append($"{name} — {sizeText} race.");
        if (f.TryGetProperty("speed", out var spd))
        {
            var spdText = spd.ValueKind == JsonValueKind.Number ? $" Speed: {spd.GetInt32()} ft." : string.Empty;
            sb.Append(spdText);
        }
        var traits = RendererHelpers.StringArray(f, "traitTags").ToList();
        if (traits.Count > 0) sb.Append($" Traits: {string.Join(", ", traits)}.");
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class SubraceCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var raceName = RendererHelpers.StringProp(f, "raceName");
        var sb = new StringBuilder($"{name}");
        if (!string.IsNullOrEmpty(raceName)) sb.Append($" ({raceName} subrace).");
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class BackgroundCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var sb = new StringBuilder($"{name}.");
        if (f.TryGetProperty("skillProficiencies", out var sp) && sp.ValueKind == JsonValueKind.Array)
        {
            var skills = sp.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.Object)
                .SelectMany(x => x.EnumerateObject().Select(p => p.Name))
                .ToList();
            if (skills.Count > 0) sb.Append($" Skills: {string.Join(", ", skills)}.");
        }
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class FeatCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var sb = new StringBuilder($"{name}.");
        if (f.TryGetProperty("prerequisite", out var pre) && pre.ValueKind == JsonValueKind.Array)
        {
            var preText = pre.GetRawText();
            if (preText.Length > 2) sb.Append($" Prerequisite: {preText}.");
        }
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class ItemCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var type = RendererHelpers.StringProp(f, "type");
        var sb = new StringBuilder($"{name}.");
        if (!string.IsNullOrEmpty(type)) sb.Append($" Type: {type}.");
        if (f.TryGetProperty("value", out var val) && val.TryGetInt32(out var v))
            sb.Append($" Value: {v / 100} gp.");
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class MagicItemCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var rarity = RendererHelpers.StringProp(f, "rarity");
        var type = RendererHelpers.StringProp(f, "type");
        var sb = new StringBuilder($"{name}. {rarity} magic item");
        if (!string.IsNullOrEmpty(type)) sb.Append($" ({type})");
        sb.Append('.');
        var reqAttune = f.TryGetProperty("reqAttune", out var ra)
            && ra.ValueKind != JsonValueKind.False && ra.ValueKind != JsonValueKind.Null;
        if (reqAttune) sb.Append(" Requires attunement.");
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class WeaponCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var cat = RendererHelpers.StringProp(f, "weaponCategory");
        var dmg = RendererHelpers.StringProp(f, "dmg1");
        var dmgType = RendererHelpers.StringProp(f, "dmgType");
        var sb = new StringBuilder($"{name}.");
        if (!string.IsNullOrEmpty(cat)) sb.Append($" {cat} weapon.");
        if (!string.IsNullOrEmpty(dmg)) sb.Append($" Damage: {dmg} {dmgType}.");
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class ArmorCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var type = RendererHelpers.StringProp(f, "type");
        var sb = new StringBuilder($"{name}. {type} armor.");
        if (f.TryGetProperty("ac", out var ac) && ac.TryGetInt32(out var acv))
            sb.Append($" AC: {acv}.");
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class GodCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var pantheon = RendererHelpers.StringProp(f, "pantheon");
        var symbol = RendererHelpers.StringProp(f, "symbol");
        var aligns = RendererHelpers.StringArray(f, "alignment")
            .Select(RendererHelpers.MapAlign).ToList();
        var domains = RendererHelpers.StringArray(f, "domains").ToList();
        var sb = new StringBuilder($"{name}.");
        if (!string.IsNullOrEmpty(pantheon)) sb.Append($" {pantheon} pantheon.");
        if (aligns.Count > 0) sb.Append($" Alignment: {string.Join(" ", aligns)}.");
        if (domains.Count > 0) sb.Append($" Domains: {string.Join(", ", domains)}.");
        if (!string.IsNullOrEmpty(symbol)) sb.Append($" Symbol: {symbol}.");
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class TrapCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var trapType = RendererHelpers.StringProp(f, "trapHazType");
        var sb = new StringBuilder($"{name}.");
        if (!string.IsNullOrEmpty(trapType)) sb.Append($" {trapType} trap.");
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class ConditionCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var summary = RendererHelpers.FirstEntryText(f);
        return string.IsNullOrEmpty(summary) ? name : $"{name}. {summary}";
    }
}

public sealed class DiseasePoisonCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var summary = RendererHelpers.FirstEntryText(f);
        return string.IsNullOrEmpty(summary) ? name : $"{name}. {summary}";
    }
}

public sealed class VehicleMountCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var vType = RendererHelpers.StringProp(f, "vehicleType");
        var sb = new StringBuilder($"{name}.");
        if (!string.IsNullOrEmpty(vType)) sb.Append($" {vType}.");
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class PlaneCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var cat = RendererHelpers.StringProp(f, "category");
        var summary = RendererHelpers.FirstEntryText(f);
        var sb = new StringBuilder($"{name}.");
        if (!string.IsNullOrEmpty(cat)) sb.Append($" {cat}.");
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class FactionCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var hq = RendererHelpers.StringProp(f, "headquarters");
        var goals = RendererHelpers.StringArray(f, "goals").Take(2).ToList();
        var sb = new StringBuilder($"{name}.");
        if (!string.IsNullOrEmpty(hq)) sb.Append($" HQ: {hq}.");
        if (goals.Count > 0) sb.Append($" Goals: {string.Join("; ", goals)}.");
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class LocationCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var cat = RendererHelpers.StringProp(f, "category");
        var setting = RendererHelpers.StringProp(f, "setting");
        var sb = new StringBuilder($"{name}.");
        if (!string.IsNullOrEmpty(cat)) sb.Append($" {cat}");
        if (!string.IsNullOrEmpty(setting)) sb.Append($" in {setting}");
        sb.Append('.');
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class LoreCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var cat = RendererHelpers.StringProp(f, "category");
        var summary = RendererHelpers.FirstEntryText(f);
        var sb = new StringBuilder($"{name}.");
        if (!string.IsNullOrEmpty(cat)) sb.Append($" {cat} lore.");
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class RuleCanonicalTextRenderer : ISimpleEntityRenderer
{
    private static readonly Dictionary<string, string> RuleTypeMap = new(StringComparer.OrdinalIgnoreCase)
        { ["C"] = "core", ["O"] = "optional", ["V"] = "variant" };

    public string Render(string name, JsonElement f)
    {
        var ruleType = RendererHelpers.StringProp(f, "ruleType");
        var ruleDisplay = RuleTypeMap.TryGetValue(ruleType, out var d) ? d : ruleType;
        var summary = RendererHelpers.FirstEntryText(f);
        var sb = new StringBuilder($"{name}.");
        if (!string.IsNullOrEmpty(ruleDisplay)) sb.Append($" {ruleDisplay} rule.");
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}
```

- [x] **Step 4: Update EntityCanonicalTextDispatcher to use simple renderers**

```csharp
// Features/Entities/CanonicalText/EntityCanonicalTextDispatcher.cs
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Entities;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public sealed class EntityCanonicalTextDispatcher
{
    private readonly MonsterCanonicalTextRenderer _monsterR = new();
    private readonly SpellCanonicalTextRenderer _spellR = new();
    private readonly ClassCanonicalTextRenderer _classR = new();
    private readonly SubclassCanonicalTextRenderer _subclassR = new();
    private readonly CanonicalJsonLoader _loader = new();

    private readonly Dictionary<EntityType, ISimpleEntityRenderer> _simpleRenderers = new()
    {
        [EntityType.Race]          = new RaceCanonicalTextRenderer(),
        [EntityType.Subrace]       = new SubraceCanonicalTextRenderer(),
        [EntityType.Background]    = new BackgroundCanonicalTextRenderer(),
        [EntityType.Feat]          = new FeatCanonicalTextRenderer(),
        [EntityType.Item]          = new ItemCanonicalTextRenderer(),
        [EntityType.MagicItem]     = new MagicItemCanonicalTextRenderer(),
        [EntityType.Weapon]        = new WeaponCanonicalTextRenderer(),
        [EntityType.Armor]         = new ArmorCanonicalTextRenderer(),
        [EntityType.God]           = new GodCanonicalTextRenderer(),
        [EntityType.Trap]          = new TrapCanonicalTextRenderer(),
        [EntityType.Condition]     = new ConditionCanonicalTextRenderer(),
        [EntityType.DiseasePoison] = new DiseasePoisonCanonicalTextRenderer(),
        [EntityType.VehicleMount]  = new VehicleMountCanonicalTextRenderer(),
        [EntityType.Plane]         = new PlaneCanonicalTextRenderer(),
        [EntityType.Faction]       = new FactionCanonicalTextRenderer(),
        [EntityType.Location]      = new LocationCanonicalTextRenderer(),
        [EntityType.Lore]          = new LoreCanonicalTextRenderer(),
        [EntityType.Rule]          = new RuleCanonicalTextRenderer(),
    };

    public string Render(EntityEnvelope envelope)
    {
        return envelope.Type switch
        {
            EntityType.Monster  => _monsterR.Render(envelope.Name, _loader.DeserialiseFields<MonsterFields>(envelope)),
            EntityType.Spell    => _spellR.Render(envelope.Name, _loader.DeserialiseFields<SpellFields>(envelope)),
            EntityType.Class    => _classR.Render(envelope.Name, _loader.DeserialiseFields<ClassFields>(envelope)),
            EntityType.Subclass => _subclassR.Render(envelope.Name, _loader.DeserialiseFields<SubclassFields>(envelope)),
            _ when _simpleRenderers.TryGetValue(envelope.Type, out var r) => r.Render(envelope.Name, envelope.Fields),
            _ => envelope.CanonicalText,
        };
    }
}
```

- [x] **Step 5: Run all tests**

```bash
dotnet test DndMcpAICsharpFun.Tests -v
```
Expected: all pass.

- [x] **Step 6: Commit**

```bash
git add Features/Entities/CanonicalText/SimpleEntityRenderers.cs \
    Features/Entities/CanonicalText/EntityCanonicalTextDispatcher.cs \
    DndMcpAICsharpFun.Tests/Entities/CanonicalText/SimpleEntityRendererTests.cs
git commit -m "feat(renderers): add per-type simple renderers for all remaining entity types"
```

---

## Task 4: EntityEnvelope DataSource field + provenance tracking

**Files:**
- Modify: `Domain/Entities/EntityEnvelope.cs`
- Modify: `Infrastructure/Qdrant/EntityPayloadFields.cs`
- Modify: `Features/VectorStore/Entities/IEntityVectorStore.cs`
- Modify: `Features/VectorStore/Entities/QdrantEntityVectorStore.cs`
- Modify: `Features/Ingestion/Entities/EntityIngestionOrchestrator.cs`
- Modify: `DndMcpAICsharpFun.Tests/Entities/Ingestion/EntityIngestionOrchestratorTests.cs`

- [x] **Step 1: Write failing test for provenance**

```csharp
// Add to DndMcpAICsharpFun.Tests/Entities/Ingestion/EntityIngestionOrchestratorTests.cs
[Fact]
public async Task Ingests_entities_with_llm_data_source_stamp()
{
    var tracker = Substitute.For<IIngestionTracker>();
    var record = new IngestionRecord { Id = 1, DisplayName = "Test Book", FileHash = "deadbeef" };
    tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(record);
    var embeddings = Substitute.For<IEmbeddingService>();
    embeddings.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
        .Returns(ci => Task.FromResult<IList<float[]>>(
            Enumerable.Range(0, ci.Arg<IList<string>>().Count).Select(_ => new float[1024]).ToList()));
    var store = Substitute.For<IEntityVectorStore>();
    var canonicalDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "canonical");
    var orchestrator = new EntityIngestionOrchestrator(
        tracker, new CanonicalJsonLoader(), new EntityCanonicalTextDispatcher(),
        new EntityReferenceResolver(), embeddings, store,
        Options.Create(new EntityIngestionOptions { CanonicalDirectory = canonicalDir }),
        NullLogger<EntityIngestionOrchestrator>.Instance);

    await orchestrator.IngestEntitiesAsync(1, CancellationToken.None);

    await store.Received(1).UpsertAsync(
        Arg.Is<IList<EntityPoint>>(pts => pts.All(p => p.Envelope.DataSource == "llm")),
        Arg.Any<CancellationToken>());
}
```

- [x] **Step 2: Run to verify failure**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "Ingests_entities_with_llm_data_source_stamp" -v
```
Expected: FAIL — `DataSource` property does not exist.

- [x] **Step 3: Add DataSource to EntityEnvelope**

```csharp
// Domain/Entities/EntityEnvelope.cs
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
    JsonElement Fields,
    string DataSource = "");
```

- [x] **Step 4: Add payload field constant**

```csharp
// Infrastructure/Qdrant/EntityPayloadFields.cs — add after FileHash:
public const string DataSource = "data_source";
```

- [x] **Step 5: Update QdrantEntityVectorStore to store and retrieve DataSource**

In `ToPoint()`, add after the `FileHash` line:
```csharp
[EntityPayloadFields.DataSource] = p.Envelope.DataSource,
```

In `ToEnvelope()`, update to read DataSource:
```csharp
// Add DataSource as the last argument to EntityEnvelope constructor call:
DataSource: p.TryGetValue(EntityPayloadFields.DataSource, out var ds) ? ds.StringValue : "");
```

The full updated `ToEnvelope` method:
```csharp
private static EntityEnvelope ToEnvelope(Google.Protobuf.Collections.MapField<string, Value> p)
{
    var fieldsJson = p.TryGetValue(EntityPayloadFields.FieldsJson, out var fv) ? fv.StringValue : "{}";
    var fields = JsonDocument.Parse(fieldsJson).RootElement.Clone();
    return new EntityEnvelope(
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
        Fields: fields,
        DataSource: p.TryGetValue(EntityPayloadFields.DataSource, out var ds) ? ds.StringValue : "");
}
```

- [x] **Step 6: Add GetDataSourcesAsync to IEntityVectorStore**

```csharp
// Features/VectorStore/Entities/IEntityVectorStore.cs — add to interface:
Task<IReadOnlyDictionary<string, string>> GetDataSourcesAsync(
    IReadOnlyList<string> entityIds, CancellationToken ct = default);
```

- [x] **Step 7: Implement GetDataSourcesAsync in QdrantEntityVectorStore**

```csharp
// Add to QdrantEntityVectorStore:
public async Task<IReadOnlyDictionary<string, string>> GetDataSourcesAsync(
    IReadOnlyList<string> entityIds, CancellationToken ct = default)
{
    if (entityIds.Count == 0) return new Dictionary<string, string>();
    // Scroll with MatchAny on the id field
    var matchAny = new Match();
    foreach (var id in entityIds) matchAny.Keywords.Add(id);
    var filter = new Filter();
    filter.Must.Add(new Condition
    {
        Field = new FieldCondition { Key = EntityPayloadFields.Id, Match = matchAny }
    });
    var result = await client.ScrollAsync(
        _collection, filter,
        limit: (uint)entityIds.Count,
        payloadSelector: true,
        cancellationToken: ct);
    return result.Result
        .Where(p => p.Payload.ContainsKey(EntityPayloadFields.Id))
        .ToDictionary(
            p => p.Payload[EntityPayloadFields.Id].StringValue,
            p => p.Payload.TryGetValue(EntityPayloadFields.DataSource, out var ds) ? ds.StringValue : "");
}
```

- [x] **Step 8: Update EntityIngestionOrchestrator to stamp "llm" on all envelopes**

In `IngestEntitiesAsync`, when building `renderedEnvelopes`, stamp `DataSource = "llm"`:
```csharp
// Replace:
renderedEnvelopes.Add(envelope with { CanonicalText = text });
// With:
renderedEnvelopes.Add(envelope with { CanonicalText = text, DataSource = "llm" });
```

- [x] **Step 9: Run all tests**

```bash
dotnet test DndMcpAICsharpFun.Tests -v
```
Expected: all pass including the new provenance test.

- [x] **Step 10: Commit**

```bash
git add Domain/Entities/EntityEnvelope.cs \
    Infrastructure/Qdrant/EntityPayloadFields.cs \
    Features/VectorStore/Entities/IEntityVectorStore.cs \
    Features/VectorStore/Entities/QdrantEntityVectorStore.cs \
    Features/Ingestion/Entities/EntityIngestionOrchestrator.cs \
    DndMcpAICsharpFun.Tests/Entities/Ingestion/EntityIngestionOrchestratorTests.cs
git commit -m "feat(provenance): add DataSource field to EntityEnvelope; stamp llm on canonical-JSON ingestion"
```

---

## Task 5: FivetoolsSourceRegistry

**Files:**
- Create: `Features/Ingestion/FivetoolsIngestion/FivetoolsSourceRegistry.cs`
- Create: `DndMcpAICsharpFun.Tests/Entities/Ingestion/FivetoolsSourceRegistryTests.cs`

The registry is a static list of `FivetoolsFileEntry` records describing every 5etools file to process and which entity type + JSON array key to read from each file.

- [x] **Step 1: Write failing test**

```csharp
// DndMcpAICsharpFun.Tests/Entities/Ingestion/FivetoolsSourceRegistryTests.cs
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Ingestion;

public class FivetoolsSourceRegistryTests
{
    [Fact]
    public void Registry_contains_class_and_subclass_entries_for_fighter()
    {
        var entries = FivetoolsSourceRegistry.AllEntries;
        entries.Should().Contain(e =>
            e.EntityType == EntityType.Class &&
            e.RelativePath.Contains("class-fighter") &&
            e.JsonArrayKey == "class");
        entries.Should().Contain(e =>
            e.EntityType == EntityType.Subclass &&
            e.RelativePath.Contains("class-fighter") &&
            e.JsonArrayKey == "subclass");
    }

    [Fact]
    public void Registry_contains_spell_bestiary_and_global_entries()
    {
        var entries = FivetoolsSourceRegistry.AllEntries;
        entries.Should().Contain(e => e.EntityType == EntityType.Spell);
        entries.Should().Contain(e => e.EntityType == EntityType.Monster);
        entries.Should().Contain(e => e.EntityType == EntityType.Race && e.JsonArrayKey == "race");
        entries.Should().Contain(e => e.EntityType == EntityType.God && e.JsonArrayKey == "deity");
        entries.Should().Contain(e => e.EntityType == EntityType.Rule && e.JsonArrayKey == "variantrule");
    }
}
```

- [x] **Step 2: Run to verify failure**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "FivetoolsSourceRegistryTests" -v
```
Expected: FAIL — type not found.

- [x] **Step 3: Create FivetoolsSourceRegistry.cs**

```csharp
// Features/Ingestion/FivetoolsIngestion/FivetoolsSourceRegistry.cs
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

public sealed record FivetoolsFileEntry(
    string RelativePath,
    EntityType EntityType,
    string JsonArrayKey);

public static class FivetoolsSourceRegistry
{
    private const string Base = "5etools";

    public static IReadOnlyList<FivetoolsFileEntry> AllEntries { get; } = Build();

    private static IReadOnlyList<FivetoolsFileEntry> Build()
    {
        var entries = new List<FivetoolsFileEntry>();

        // ── Classes & subclasses (one file per class, contains both class[] and subclass[])
        var classDir = Path.Combine(Base, "class");
        if (Directory.Exists(classDir))
        {
            foreach (var file in Directory.GetFiles(classDir, "class-*.json")
                         .Where(f => !Path.GetFileName(f).StartsWith("fluff-")))
            {
                entries.Add(new(file, EntityType.Class,    "class"));
                entries.Add(new(file, EntityType.Subclass, "subclass"));
            }
        }

        // ── Spells (one file per source)
        var spellDir = Path.Combine(Base, "spells");
        if (Directory.Exists(spellDir))
        {
            foreach (var file in Directory.GetFiles(spellDir, "spells-*.json")
                         .Where(f => !Path.GetFileName(f).StartsWith("fluff-") &&
                                     !Path.GetFileName(f).Contains("index") &&
                                     !Path.GetFileName(f).Contains("foundry")))
                entries.Add(new(file, EntityType.Spell, "spell"));
        }

        // ── Bestiary (one file per source)
        var bestiaryDir = Path.Combine(Base, "bestiary");
        if (Directory.Exists(bestiaryDir))
        {
            foreach (var file in Directory.GetFiles(bestiaryDir, "bestiary-*.json")
                         .Where(f => !Path.GetFileName(f).StartsWith("fluff-")))
                entries.Add(new(file, EntityType.Monster, "monster"));
        }

        // ── Global combined files
        void AddGlobal(string relPath, EntityType type, string key)
        {
            var full = Path.Combine(Base, relPath);
            if (File.Exists(full)) entries.Add(new(full, type, key));
        }

        AddGlobal("races.json",              EntityType.Race,          "race");
        AddGlobal("races.json",              EntityType.Subrace,       "subrace");
        AddGlobal("backgrounds.json",        EntityType.Background,    "background");
        AddGlobal("feats.json",              EntityType.Feat,          "feat");
        AddGlobal("items.json",              EntityType.Item,          "item");
        AddGlobal("items.json",              EntityType.MagicItem,     "item");
        AddGlobal("items-base.json",         EntityType.Weapon,        "baseitem");
        AddGlobal("items-base.json",         EntityType.Armor,         "baseitem");
        AddGlobal("deities.json",            EntityType.God,           "deity");
        AddGlobal("trapshazards.json",       EntityType.Trap,          "trap");
        AddGlobal("conditionsdiseases.json", EntityType.Condition,     "condition");
        AddGlobal("conditionsdiseases.json", EntityType.DiseasePoison, "disease");
        AddGlobal("vehicles.json",           EntityType.VehicleMount,  "vehicle");
        AddGlobal("variantrules.json",       EntityType.Rule,          "variantrule");

        return entries;
    }
}
```

- [x] **Step 4: Run tests**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "FivetoolsSourceRegistryTests" -v
```
Expected: PASS (files exist at those paths in `5etools/`).

- [x] **Step 5: Commit**

```bash
git add Features/Ingestion/FivetoolsIngestion/FivetoolsSourceRegistry.cs \
    DndMcpAICsharpFun.Tests/Entities/Ingestion/FivetoolsSourceRegistryTests.cs
git commit -m "feat(5etools): add FivetoolsSourceRegistry with all known file entries"
```

---

## Task 6: Mapper infrastructure + Class/Subclass/Monster/Spell mappers

**Files:**
- Create: `Features/Ingestion/FivetoolsIngestion/IFivetoolsEntityMapper.cs`
- Create: `Features/Ingestion/FivetoolsIngestion/FivetoolsMapperBase.cs`
- Create: `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsClassMapper.cs`
- Create: `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsSubclassMapper.cs`
- Create: `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsSpellMapper.cs`
- Create: `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsMonsterMapper.cs`
- Create: `DndMcpAICsharpFun.Tests/Entities/Ingestion/FivetoolsMappersTests.cs`

Each mapper reads a `JsonElement` (one entity from a 5etools array) and returns an `EntityEnvelope`. The `EntityIdSlug.For(sourceBook, type, name)` generates the ID. `DataSource` is always `"5etools"`.

- [x] **Step 1: Write failing mapper tests**

```csharp
// DndMcpAICsharpFun.Tests/Entities/Ingestion/FivetoolsMappersTests.cs
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Ingestion;

public class FivetoolsMappersTests
{
    private static JsonElement J(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    [Fact]
    public void ClassMapper_maps_fighter_to_envelope()
    {
        var json = J("{\"name\":\"Fighter\",\"source\":\"PHB\",\"page\":70,\"hd\":{\"number\":1,\"faces\":10},\"proficiency\":[\"str\",\"con\"],\"classFeatures\":[\"Fighting Style|Fighter||1\"]}");
        var envelope = new FivetoolsClassMapper().Map(json);
        envelope.Should().NotBeNull();
        envelope!.Type.Should().Be(EntityType.Class);
        envelope.Name.Should().Be("Fighter");
        envelope.SourceBook.Should().Be("PHB");
        envelope.DataSource.Should().Be("5etools");
        envelope.Fields.TryGetProperty("hd", out _).Should().BeTrue();
    }

    [Fact]
    public void ClassMapper_returns_null_for_entry_missing_name()
    {
        var json = J("{\"source\":\"PHB\",\"hd\":{\"number\":1,\"faces\":10}}");
        var envelope = new FivetoolsClassMapper().Map(json);
        envelope.Should().BeNull();
    }

    [Fact]
    public void SubclassMapper_maps_battle_master()
    {
        var json = J("{\"name\":\"Battle Master\",\"source\":\"PHB\",\"className\":\"Fighter\",\"classSource\":\"PHB\",\"shortName\":\"Battle Master\",\"subclassFeatures\":[\"Combat Superiority|Fighter|PHB|Battle Master|PHB|3\"]}");
        var envelope = new FivetoolsSubclassMapper().Map(json);
        envelope!.Type.Should().Be(EntityType.Subclass);
        envelope.Name.Should().Be("Battle Master");
        envelope.DataSource.Should().Be("5etools");
    }

    [Fact]
    public void SpellMapper_maps_fireball()
    {
        var json = J("{\"name\":\"Fireball\",\"source\":\"PHB\",\"level\":3,\"school\":\"V\",\"time\":[{\"number\":1,\"unit\":\"action\"}],\"range\":{\"type\":\"point\",\"distance\":{\"type\":\"feet\",\"amount\":150}},\"components\":{\"v\":true,\"s\":true,\"m\":\"a tiny ball of bat guano and sulfur\"},\"duration\":[{\"type\":\"instant\"}],\"entries\":[\"A bright streak flashes.\"]}");
        var envelope = new FivetoolsSpellMapper().Map(json);
        envelope!.Type.Should().Be(EntityType.Spell);
        envelope.Name.Should().Be("Fireball");
        envelope.DataSource.Should().Be("5etools");
        envelope.Fields.TryGetProperty("school", out var school).Should().BeTrue();
        school.GetString().Should().Be("V");
    }

    [Fact]
    public void MonsterMapper_maps_aboleth()
    {
        var json = J("{\"name\":\"Aboleth\",\"source\":\"MM\",\"size\":[\"L\"],\"type\":\"aberration\",\"alignment\":[\"L\",\"E\"],\"ac\":[16],\"hp\":{\"average\":135,\"formula\":\"18d10+36\"},\"speed\":{\"walk\":10,\"swim\":40},\"str\":21,\"dex\":9,\"con\":15,\"int\":18,\"wis\":15,\"cha\":18,\"cr\":\"10\",\"entries\":[\"A grotesque fish-like creature.\"]}");
        var envelope = new FivetoolsMonsterMapper().Map(json);
        envelope!.Type.Should().Be(EntityType.Monster);
        envelope.Name.Should().Be("Aboleth");
        envelope.DataSource.Should().Be("5etools");
        envelope.Fields.TryGetProperty("str", out var str).Should().BeTrue();
        str.GetInt32().Should().Be(21);
    }
}
```

- [x] **Step 2: Run to verify failure**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "FivetoolsMappersTests" -v
```
Expected: FAIL — types not found.

- [x] **Step 3: Create IFivetoolsEntityMapper.cs**

```csharp
// Features/Ingestion/FivetoolsIngestion/IFivetoolsEntityMapper.cs
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

public interface IFivetoolsEntityMapper
{
    // Returns null if the JSON entry should be skipped (e.g., no name, reprint, etc.)
    EntityEnvelope? Map(JsonElement entry);
}
```

- [x] **Step 4: Create FivetoolsMapperBase.cs**

```csharp
// Features/Ingestion/FivetoolsIngestion/FivetoolsMapperBase.cs
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

public abstract class FivetoolsMapperBase : IFivetoolsEntityMapper
{
    protected abstract EntityType EntityType { get; }

    public EntityEnvelope? Map(JsonElement entry)
    {
        if (!entry.TryGetProperty("name", out var nameProp)
            || nameProp.ValueKind != JsonValueKind.String)
            return null;
        var name = nameProp.GetString()!;
        if (string.IsNullOrWhiteSpace(name)) return null;

        var source = entry.TryGetProperty("source", out var src)
            && src.ValueKind == JsonValueKind.String ? src.GetString()! : "Unknown";
        int? page = entry.TryGetProperty("page", out var pg) && pg.TryGetInt32(out var pv) ? pv : null;

        var id = EntityIdSlug.For(source, EntityType, name);

        return new EntityEnvelope(
            Id: id,
            Type: EntityType,
            Name: name,
            SourceBook: source,
            Edition: "Edition2014",
            Page: page,
            FirstAppearedIn: new FirstAppearance(source, "Edition2014", page),
            RevisedIn: Array.Empty<Revision>(),
            SettingTags: Array.Empty<string>(),
            CanonicalText: "",
            Fields: BuildFields(entry),
            DataSource: "5etools");
    }

    // Override to filter only the relevant fields from the 5etools entry.
    // Default: pass through the entire entry as-is.
    protected virtual JsonElement BuildFields(JsonElement entry) => entry;
}
```

- [x] **Step 5: Create the four mappers**

```csharp
// Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsClassMapper.cs
using DndMcpAICsharpFun.Domain.Entities;
namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;
public sealed class FivetoolsClassMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.Class;
}

// Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsSubclassMapper.cs
using DndMcpAICsharpFun.Domain.Entities;
namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;
public sealed class FivetoolsSubclassMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.Subclass;
}

// Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsSpellMapper.cs
using DndMcpAICsharpFun.Domain.Entities;
namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;
public sealed class FivetoolsSpellMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.Spell;
}

// Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsMonsterMapper.cs
using DndMcpAICsharpFun.Domain.Entities;
namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;
public sealed class FivetoolsMonsterMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.Monster;
}
```

- [x] **Step 6: Run tests**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "FivetoolsMappersTests" -v
```
Expected: all 5 pass.

- [x] **Step 7: Commit**

```bash
git add Features/Ingestion/FivetoolsIngestion/ \
    DndMcpAICsharpFun.Tests/Entities/Ingestion/FivetoolsMappersTests.cs
git commit -m "feat(5etools): add mapper infra + Class/Subclass/Monster/Spell mappers"
```

---

## Task 7: Race/Subrace/Background/Feat/Item/MagicItem/Weapon/Armor mappers

**Files:**
- Create: `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsRaceMapper.cs`
- Create: `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsSubraceMapper.cs`
- Create: `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsBackgroundMapper.cs`
- Create: `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsFeatMapper.cs`
- Create: `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsItemMapper.cs`
- Create: `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsMagicItemMapper.cs`
- Create: `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsWeaponMapper.cs`
- Create: `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsArmorMapper.cs`
- Modify: `DndMcpAICsharpFun.Tests/Entities/Ingestion/FivetoolsMappersTests.cs`

Item and MagicItem both read from `items.json`. `FivetoolsItemMapper` only maps entries where `rarity` is absent or `"none"`. `FivetoolsMagicItemMapper` only maps entries where `rarity` is present and not `"none"`. `FivetoolsWeaponMapper` and `FivetoolsArmorMapper` read from `items-base.json` filtering by `weaponCategory` or armor `type` codes.

- [x] **Step 1: Add tests for new mappers**

```csharp
// Add to FivetoolsMappersTests.cs:
[Fact]
public void RaceMapper_maps_elf()
{
    var json = J("{\"name\":\"Elf\",\"source\":\"PHB\",\"size\":[\"M\"],\"speed\":30,\"entries\":[\"Elves are a magical people.\"]}");
    var envelope = new FivetoolsRaceMapper().Map(json);
    envelope!.Type.Should().Be(EntityType.Race);
    envelope.DataSource.Should().Be("5etools");
}

[Fact]
public void BackgroundMapper_maps_sage()
{
    var json = J("{\"name\":\"Sage\",\"source\":\"PHB\",\"skillProficiencies\":[{\"arcana\":true,\"history\":true}],\"entries\":[\"Scholars study.\"]}");
    var envelope = new FivetoolsBackgroundMapper().Map(json);
    envelope!.Type.Should().Be(EntityType.Background);
}

[Fact]
public void ItemMapper_skips_magic_items()
{
    var json = J("{\"name\":\"Sword +1\",\"source\":\"DMG\",\"rarity\":\"uncommon\",\"type\":\"S\"}");
    var envelope = new FivetoolsItemMapper().Map(json);
    envelope.Should().BeNull("magic items are handled by MagicItemMapper");
}

[Fact]
public void MagicItemMapper_maps_uncommon_magic_item()
{
    var json = J("{\"name\":\"Sword +1\",\"source\":\"DMG\",\"rarity\":\"uncommon\",\"type\":\"S\",\"entries\":[\"A finely crafted blade.\"]}");
    var envelope = new FivetoolsMagicItemMapper().Map(json);
    envelope!.Type.Should().Be(EntityType.MagicItem);
    envelope.DataSource.Should().Be("5etools");
}

[Fact]
public void MagicItemMapper_skips_nonmagic_items()
{
    var json = J("{\"name\":\"Rope\",\"source\":\"PHB\",\"type\":\"G\"}");
    var envelope = new FivetoolsMagicItemMapper().Map(json);
    envelope.Should().BeNull("non-magic items are handled by ItemMapper");
}
```

- [x] **Step 2: Run to verify failure**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "FivetoolsMappersTests" -v
```

- [x] **Step 3: Create the mappers**

```csharp
// Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsRaceMapper.cs
using DndMcpAICsharpFun.Domain.Entities;
namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;
public sealed class FivetoolsRaceMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.Race;
}

// Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsSubraceMapper.cs
using DndMcpAICsharpFun.Domain.Entities;
namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;
public sealed class FivetoolsSubraceMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.Subrace;
}

// Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsBackgroundMapper.cs
using DndMcpAICsharpFun.Domain.Entities;
namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;
public sealed class FivetoolsBackgroundMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.Background;
}

// Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsFeatMapper.cs
using DndMcpAICsharpFun.Domain.Entities;
namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;
public sealed class FivetoolsFeatMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.Feat;
}

// Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsItemMapper.cs
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;
public sealed class FivetoolsItemMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.Item;
    public override EntityEnvelope? Map(JsonElement entry)
    {
        // Only map non-magic items (no rarity, or rarity == "none")
        if (entry.TryGetProperty("rarity", out var r)
            && r.ValueKind == JsonValueKind.String
            && !string.Equals(r.GetString(), "none", StringComparison.OrdinalIgnoreCase))
            return null;
        return base.Map(entry);
    }
}

// Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsMagicItemMapper.cs
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;
public sealed class FivetoolsMagicItemMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.MagicItem;
    public override EntityEnvelope? Map(JsonElement entry)
    {
        // Only map entries with a non-"none" rarity
        if (!entry.TryGetProperty("rarity", out var r)
            || r.ValueKind != JsonValueKind.String
            || string.Equals(r.GetString(), "none", StringComparison.OrdinalIgnoreCase))
            return null;
        return base.Map(entry);
    }
}

// Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsWeaponMapper.cs
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;
public sealed class FivetoolsWeaponMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.Weapon;
    public override EntityEnvelope? Map(JsonElement entry)
    {
        // Only map entries that have a weaponCategory
        if (!entry.TryGetProperty("weaponCategory", out var wc)
            || wc.ValueKind != JsonValueKind.String) return null;
        return base.Map(entry);
    }
}

// Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsArmorMapper.cs
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;
public sealed class FivetoolsArmorMapper : FivetoolsMapperBase
{
    // 5etools armor type codes
    private static readonly HashSet<string> ArmorTypes = new(StringComparer.OrdinalIgnoreCase)
        { "LA", "MA", "HA", "S" };
    protected override EntityType EntityType => EntityType.Armor;
    public override EntityEnvelope? Map(JsonElement entry)
    {
        if (!entry.TryGetProperty("type", out var t)
            || t.ValueKind != JsonValueKind.String
            || !ArmorTypes.Contains(t.GetString()!)) return null;
        return base.Map(entry);
    }
}
```

- [x] **Step 4: Run tests**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "FivetoolsMappersTests" -v
```
Expected: all pass.

- [x] **Step 5: Commit**

```bash
git add Features/Ingestion/FivetoolsIngestion/Mappers/ \
    DndMcpAICsharpFun.Tests/Entities/Ingestion/FivetoolsMappersTests.cs
git commit -m "feat(5etools): add Race/Subrace/Background/Feat/Item/MagicItem/Weapon/Armor mappers"
```

---

## Task 8: God/Trap/Condition/DiseasePoison/VehicleMount/Rule mappers

**Files:**
- Create: `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsGodMapper.cs`
- Create: `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsTrapMapper.cs`
- Create: `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsConditionMapper.cs`
- Create: `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsDiseasePoisonMapper.cs`
- Create: `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsVehicleMapper.cs`
- Create: `Features/Ingestion/FivetoolsIngestion/Mappers/FivetoolsRuleMapper.cs`
- Modify: `DndMcpAICsharpFun.Tests/Entities/Ingestion/FivetoolsMappersTests.cs`

- [x] **Step 1: Add tests**

```csharp
// Add to FivetoolsMappersTests.cs:
[Fact]
public void GodMapper_maps_deity()
{
    var json = J("{\"name\":\"Tyr\",\"source\":\"MTF\",\"pantheon\":\"Forgotten Realms\",\"alignment\":[\"L\",\"G\"],\"domains\":[\"War\"],\"symbol\":\"Balanced scales\",\"entries\":[\"God of justice.\"]}");
    var envelope = new FivetoolsGodMapper().Map(json);
    envelope!.Type.Should().Be(EntityType.God);
    envelope.DataSource.Should().Be("5etools");
}

[Fact]
public void TrapMapper_maps_trap()
{
    var json = J("{\"name\":\"Pit Trap\",\"source\":\"DMG\",\"trapHazType\":\"MECH\",\"entries\":[\"A covered pit.\"]}");
    var envelope = new FivetoolsTrapMapper().Map(json);
    envelope!.Type.Should().Be(EntityType.Trap);
}

[Fact]
public void ConditionMapper_maps_condition()
{
    var json = J("{\"name\":\"Blinded\",\"source\":\"PHB\",\"entries\":[\"A blinded creature cannot see.\"]}");
    var envelope = new FivetoolsConditionMapper().Map(json);
    envelope!.Type.Should().Be(EntityType.Condition);
}

[Fact]
public void RuleMapper_maps_variantrule()
{
    var json = J("{\"name\":\"Flanking\",\"source\":\"DMG\",\"ruleType\":\"O\",\"entries\":[\"When a creature and its ally are on opposite sides of an enemy.\"]}");
    var envelope = new FivetoolsRuleMapper().Map(json);
    envelope!.Type.Should().Be(EntityType.Rule);
    envelope.DataSource.Should().Be("5etools");
}
```

- [x] **Step 2: Run to verify failure**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "FivetoolsMappersTests" -v
```

- [x] **Step 3: Create the mappers**

```csharp
// All six are trivial thin wrappers over FivetoolsMapperBase:

// FivetoolsGodMapper.cs
using DndMcpAICsharpFun.Domain.Entities;
namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;
public sealed class FivetoolsGodMapper : FivetoolsMapperBase { protected override EntityType EntityType => EntityType.God; }

// FivetoolsTrapMapper.cs
using DndMcpAICsharpFun.Domain.Entities;
namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;
public sealed class FivetoolsTrapMapper : FivetoolsMapperBase { protected override EntityType EntityType => EntityType.Trap; }

// FivetoolsConditionMapper.cs
using DndMcpAICsharpFun.Domain.Entities;
namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;
public sealed class FivetoolsConditionMapper : FivetoolsMapperBase { protected override EntityType EntityType => EntityType.Condition; }

// FivetoolsDiseasePoisonMapper.cs
using DndMcpAICsharpFun.Domain.Entities;
namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;
public sealed class FivetoolsDiseasePoisonMapper : FivetoolsMapperBase { protected override EntityType EntityType => EntityType.DiseasePoison; }

// FivetoolsVehicleMapper.cs
using DndMcpAICsharpFun.Domain.Entities;
namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;
public sealed class FivetoolsVehicleMapper : FivetoolsMapperBase { protected override EntityType EntityType => EntityType.VehicleMount; }

// FivetoolsRuleMapper.cs
using DndMcpAICsharpFun.Domain.Entities;
namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;
public sealed class FivetoolsRuleMapper : FivetoolsMapperBase { protected override EntityType EntityType => EntityType.Rule; }
```

- [x] **Step 4: Run tests**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "FivetoolsMappersTests" -v
```
Expected: all pass.

- [x] **Step 5: Commit**

```bash
git add Features/Ingestion/FivetoolsIngestion/Mappers/ \
    DndMcpAICsharpFun.Tests/Entities/Ingestion/FivetoolsMappersTests.cs
git commit -m "feat(5etools): add God/Trap/Condition/DiseasePoison/Vehicle/Rule mappers"
```

---

## Task 9: FivetoolsIngestionService + endpoint + DI wiring

**Files:**
- Create: `Features/Ingestion/FivetoolsIngestion/FivetoolsIngestionService.cs`
- Create: `Features/Admin/FivetoolsAdminEndpoints.cs`
- Modify: `Extensions/ServiceCollectionExtensions.cs`
- Modify: `Program.cs`
- Modify: `DndMcpAICsharpFun.http`
- Create: `DndMcpAICsharpFun.Tests/Entities/Ingestion/FivetoolsIngestionServiceTests.cs`

- [x] **Step 1: Write failing service test**

```csharp
// DndMcpAICsharpFun.Tests/Entities/Ingestion/FivetoolsIngestionServiceTests.cs
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Ingestion;

public class FivetoolsIngestionServiceTests
{
    [Fact]
    public async Task Service_skips_entities_with_manual_data_source()
    {
        var store = Substitute.For<IEntityVectorStore>();
        var embeddings = Substitute.For<IEmbeddingService>();
        var dispatcher = new EntityCanonicalTextDispatcher();

        // Simulate: one entity already exists with DataSource = "manual"
        var manualId = "phb.class.fighter";
        store.GetDataSourcesAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string> { [manualId] = "manual" });
        embeddings.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IList<float[]>>(
                Enumerable.Range(0, ci.Arg<IList<string>>().Count)
                    .Select(_ => new float[1024]).ToList()));

        var envelopes = new[]
        {
            new EntityEnvelope(manualId, EntityType.Class, "Fighter", "PHB", "Edition2014",
                null, new FirstAppearance("PHB","Edition2014"), Array.Empty<Revision>(),
                Array.Empty<string>(), "", default, "5etools"),
            new EntityEnvelope("phb.class.wizard", EntityType.Class, "Wizard", "PHB", "Edition2014",
                null, new FirstAppearance("PHB","Edition2014"), Array.Empty<Revision>(),
                Array.Empty<string>(), "", default, "5etools"),
        };

        var service = new FivetoolsIngestionService(store, embeddings, dispatcher,
            NullLogger<FivetoolsIngestionService>.Instance);

        await service.IngestEnvelopesAsync(envelopes, CancellationToken.None);

        // Only Wizard should be upserted (Fighter is manual-protected)
        await store.Received(1).UpsertAsync(
            Arg.Is<IList<EntityPoint>>(pts => pts.Count == 1 && pts[0].Envelope.Name == "Wizard"),
            Arg.Any<CancellationToken>());
    }
}
```

- [x] **Step 2: Run to verify failure**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "FivetoolsIngestionServiceTests" -v
```

- [x] **Step 3: Create FivetoolsIngestionService.cs**

```csharp
// Features/Ingestion/FivetoolsIngestion/FivetoolsIngestionService.cs
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;
using DndMcpAICsharpFun.Features.VectorStore.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

public sealed class FivetoolsIngestionService(
    IEntityVectorStore store,
    IEmbeddingService embeddings,
    EntityCanonicalTextDispatcher dispatcher,
    ILogger<FivetoolsIngestionService> logger)
{
    private static readonly Dictionary<EntityType, IFivetoolsEntityMapper> Mappers = new()
    {
        [EntityType.Class]          = new FivetoolsClassMapper(),
        [EntityType.Subclass]       = new FivetoolsSubclassMapper(),
        [EntityType.Spell]          = new FivetoolsSpellMapper(),
        [EntityType.Monster]        = new FivetoolsMonsterMapper(),
        [EntityType.Race]           = new FivetoolsRaceMapper(),
        [EntityType.Subrace]        = new FivetoolsSubraceMapper(),
        [EntityType.Background]     = new FivetoolsBackgroundMapper(),
        [EntityType.Feat]           = new FivetoolsFeatMapper(),
        [EntityType.Item]           = new FivetoolsItemMapper(),
        [EntityType.MagicItem]      = new FivetoolsMagicItemMapper(),
        [EntityType.Weapon]         = new FivetoolsWeaponMapper(),
        [EntityType.Armor]          = new FivetoolsArmorMapper(),
        [EntityType.God]            = new FivetoolsGodMapper(),
        [EntityType.Trap]           = new FivetoolsTrapMapper(),
        [EntityType.Condition]      = new FivetoolsConditionMapper(),
        [EntityType.DiseasePoison]  = new FivetoolsDiseasePoisonMapper(),
        [EntityType.VehicleMount]   = new FivetoolsVehicleMapper(),
        [EntityType.Rule]           = new FivetoolsRuleMapper(),
    };

    public async Task ImportAllAsync(CancellationToken ct = default)
    {
        var allEnvelopes = new List<EntityEnvelope>();

        foreach (var entry in FivetoolsSourceRegistry.AllEntries)
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(entry.RelativePath))
            {
                logger.LogWarning("5etools file not found, skipping: {Path}", entry.RelativePath);
                continue;
            }
            if (!Mappers.TryGetValue(entry.EntityType, out var mapper))
            {
                logger.LogWarning("No mapper for entity type {Type}, skipping {Path}", entry.EntityType, entry.RelativePath);
                continue;
            }

            await using var stream = File.OpenRead(entry.RelativePath);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty(entry.JsonArrayKey, out var arr)
                || arr.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in arr.EnumerateArray())
            {
                var envelope = mapper.Map(item);
                if (envelope is not null)
                    allEnvelopes.Add(envelope);
            }
        }

        logger.LogInformation("5etools import: {Count} entities mapped", allEnvelopes.Count);
        await IngestEnvelopesAsync(allEnvelopes, ct);
    }

    public async Task IngestEnvelopesAsync(IReadOnlyList<EntityEnvelope> envelopes, CancellationToken ct = default)
    {
        if (envelopes.Count == 0) return;

        // Provenance check: skip anything already marked "manual"
        var ids = envelopes.Select(e => e.Id).ToList();
        var existingSources = await store.GetDataSourcesAsync(ids, ct);
        var toIngest = envelopes
            .Where(e => !existingSources.TryGetValue(e.Id, out var src) || src != "manual")
            .ToList();

        if (toIngest.Count < envelopes.Count)
            logger.LogInformation("Skipped {Count} manually-corrected entities", envelopes.Count - toIngest.Count);

        // Render canonical text
        var renderedEnvelopes = toIngest.Select(e =>
        {
            try { return e with { CanonicalText = dispatcher.Render(e) }; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to render canonical text for {Id}", e.Id);
                return e;
            }
        }).ToList();

        var texts = renderedEnvelopes.Select(e => e.CanonicalText).ToList();
        IList<float[]> vectors = texts.Count == 0
            ? Array.Empty<float[]>()
            : await embeddings.EmbedAsync(texts, ct);

        var points = renderedEnvelopes
            .Select((e, i) => new EntityPoint(e, vectors[i], $"5etools:{e.SourceBook}"))
            .ToList();

        await store.UpsertAsync(points, ct);
        logger.LogInformation("5etools import: upserted {Count} entities", points.Count);
    }
}
```

- [x] **Step 4: Run the service test**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "FivetoolsIngestionServiceTests" -v
```
Expected: PASS.

- [x] **Step 5: Create FivetoolsAdminEndpoints.cs**

```csharp
// Features/Admin/FivetoolsAdminEndpoints.cs
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

namespace DndMcpAICsharpFun.Features.Admin;

public static class FivetoolsAdminEndpoints
{
    public static RouteGroupBuilder MapFivetoolsAdmin(this RouteGroupBuilder group)
    {
        group.MapPost("/5etools/import", ImportAll);
        return group;
    }

    private static async Task<IResult> ImportAll(
        FivetoolsIngestionService service,
        CancellationToken ct)
    {
        await service.ImportAllAsync(ct);
        return Results.Accepted();
    }
}
```

- [x] **Step 6: Register service in DI and wire route**

In `Extensions/ServiceCollectionExtensions.cs`, inside `AddIngestionPipeline()`, add:
```csharp
services.AddScoped<FivetoolsIngestionService>();
```

In `Program.cs`, update the admin group:
```csharp
// Replace:
app.MapGroup("/admin").MapBooksAdmin();
// With:
var admin = app.MapGroup("/admin");
admin.MapBooksAdmin();
admin.MapFivetoolsAdmin();
```

- [x] **Step 7: Update DndMcpAICsharpFun.http**

Add after the canonical validate entry:
```
### Admin — Import all 5etools JSON files directly into dnd_entities
POST {{baseUrl}}/admin/5etools/import
X-Admin-Api-Key: {{adminKey}}
```

- [x] **Step 8: Run all tests**

```bash
dotnet test DndMcpAICsharpFun.Tests -v
```
Expected: all pass.

- [x] **Step 9: Commit**

```bash
git add Features/Ingestion/FivetoolsIngestion/FivetoolsIngestionService.cs \
    Features/Admin/FivetoolsAdminEndpoints.cs \
    Extensions/ServiceCollectionExtensions.cs \
    Program.cs \
    DndMcpAICsharpFun.http \
    DndMcpAICsharpFun.Tests/Entities/Ingestion/FivetoolsIngestionServiceTests.cs
git commit -m "feat(5etools): add FivetoolsIngestionService + POST /admin/5etools/import endpoint"
```

---

## Self-Review

**Spec coverage:**
- ✅ `POST /admin/5etools/import` — Task 9
- ✅ Static source registry — Task 5
- ✅ All entity types covered — Tasks 6/7/8
- ✅ Conflict resolution (source-aware provenance) — Task 4 + Task 9
- ✅ Direct-to-dnd_entities, no canonical JSON file — Task 9
- ✅ Per-type renderers for all types — Tasks 2/3
- ✅ ClassFields C# alignment — Task 1
- ✅ SubclassFields C# alignment — Task 1
- ✅ ClassCanonicalTextRenderer re-created — Task 2
- ✅ Dispatcher re-wired — Tasks 2/3
- ✅ Stale LLM files: no new tooling needed (user deletes manually, re-runs extract-entities) — doc only

**Placeholder scan:** None found.

**Type consistency:**
- `FivetoolsMapperBase.Map()` → returns `EntityEnvelope?` — matches `IFivetoolsEntityMapper` interface throughout
- `DataSource` field added to `EntityEnvelope` as last positional parameter with default `""` — all existing `with` expressions in orchestrator are unaffected
- `GetDataSourcesAsync` on `IEntityVectorStore` matches usage in `FivetoolsIngestionService`
- `ClassFields.Hd` (Task 1) used in `ClassCanonicalTextRenderer` (Task 2) — consistent
- `SubclassFields.SubclassFeatures` (Task 1) used in `SubclassCanonicalTextRenderer` (Task 2) — consistent
