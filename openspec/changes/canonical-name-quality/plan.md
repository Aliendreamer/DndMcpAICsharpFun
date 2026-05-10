# Canonical Name Quality Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix ALL-CAPS entity names produced by qwen3:8b PDF extraction by adding a naming rule to the extraction prompt, a `NeedsReview` flag to mark OCR-garbled entries, and a one-time Python script to normalize existing canonical JSONs.

**Architecture:** Three layers: (1) the C# extraction pipeline learns to set `NeedsReview` from LLM confidence + a name heuristic; (2) `EntityEnvelope` carries `NeedsReview` through to Qdrant payload; (3) a standalone Python script normalizes existing canonical JSONs without re-extraction, and validation reports flagged entities as warnings.

**Tech Stack:** .NET 10, C#, xUnit, FluentAssertions, Python 3, pytest, JSON Schema draft-04, Qdrant

---

### Task 1: EntityEnvelope.NeedsReview + EntityPayloadFields

**Files:**
- Modify: `Domain/Entities/EntityEnvelope.cs`
- Modify: `Infrastructure/Qdrant/EntityPayloadFields.cs`
- Test: `DndMcpAICsharpFun.Tests/Entities/EntityEnvelopeTests.cs` (create if absent)

- [ ] **Step 1: Write the failing test**

Create `DndMcpAICsharpFun.Tests/Entities/EntityEnvelopeTests.cs`:

```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities;

public class EntityEnvelopeTests
{
    private static EntityEnvelope MakeEnvelope(bool needsReview = false) =>
        new(
            Id: "tce.subclass.circle-of-spores",
            Type: EntityType.Subclass,
            Name: "Circle of Spores",
            SourceBook: "TCE",
            Edition: "Edition2014",
            Page: null,
            FirstAppearedIn: new FirstAppearance("TCE", "Edition2014"),
            RevisedIn: Array.Empty<Revision>(),
            SettingTags: Array.Empty<string>(),
            CanonicalText: "",
            Fields: JsonDocument.Parse("{}").RootElement,
            NeedsReview: needsReview);

    [Fact]
    public void NeedsReview_defaults_to_false()
    {
        var e = new EntityEnvelope(
            Id: "x", Type: EntityType.Class, Name: "N", SourceBook: "PHB",
            Edition: "Edition2014", Page: null,
            FirstAppearedIn: new FirstAppearance("PHB", "Edition2014"),
            RevisedIn: Array.Empty<Revision>(),
            SettingTags: Array.Empty<string>(),
            CanonicalText: "", Fields: JsonDocument.Parse("{}").RootElement);
        e.NeedsReview.Should().BeFalse();
    }

    [Fact]
    public void NeedsReview_can_be_set_true()
    {
        var e = MakeEnvelope(needsReview: true);
        e.NeedsReview.Should().BeTrue();
    }

    [Fact]
    public void With_expression_propagates_NeedsReview()
    {
        var original = MakeEnvelope(needsReview: true);
        var copy = original with { Name = "Other" };
        copy.NeedsReview.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run the test to confirm it fails**

```bash
cd /home/aliendreamer/projects/DndMcpAICsharpFun
dotnet test --filter "EntityEnvelopeTests" 2>&1 | tail -10
```

Expected: compile error — `NeedsReview` not defined.

- [ ] **Step 3: Add NeedsReview to EntityEnvelope**

In `Domain/Entities/EntityEnvelope.cs`, add `bool NeedsReview = false` after `BasicRules2024`:

```csharp
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
    string DataSource = "",
    bool Srd = false,
    bool Srd52 = false,
    bool BasicRules2024 = false,
    bool NeedsReview = false,
    IReadOnlyList<string> Keywords = null!)
{
    public IReadOnlyList<string> Keywords { get; init; } = Keywords ?? [];
}
```

- [ ] **Step 4: Add NeedsReview to EntityPayloadFields**

In `Infrastructure/Qdrant/EntityPayloadFields.cs`, add after `BasicRules2024`:

```csharp
public const string NeedsReview    = "needs_review";
```

- [ ] **Step 5: Run the test to confirm it passes**

```bash
dotnet test --filter "EntityEnvelopeTests" 2>&1 | tail -5
```

Expected: all 3 tests PASS.

- [ ] **Step 6: Verify the build is clean**

```bash
dotnet build 2>&1 | grep -E "error|warning" | grep -v "warning CS" | head -20
```

Expected: 0 errors.

- [ ] **Step 7: Commit**

```bash
git add Domain/Entities/EntityEnvelope.cs \
        Infrastructure/Qdrant/EntityPayloadFields.cs \
        DndMcpAICsharpFun.Tests/Entities/EntityEnvelopeTests.cs
git commit -m "feat(entity): add NeedsReview flag to EntityEnvelope and payload fields"
```

---

### Task 2: QdrantEntityVectorStore — persist NeedsReview

**Files:**
- Modify: `Features/VectorStore/Entities/QdrantEntityVectorStore.cs`

`NeedsReview` follows the same boolean-as-string pattern used by `Srd`, `Srd52`, and `BasicRules2024`. No separate test is needed — the integration path is covered by the ingestion/retrieval round-trip already tested in `EntityIngestionOrchestratorTests`.

- [ ] **Step 1: Write NeedsReview into the Qdrant payload in ToPoint**

In `Features/VectorStore/Entities/QdrantEntityVectorStore.cs`, in the `ToPoint` method, find the block that writes the three Srd fields and add `NeedsReview` after `BasicRules2024`:

```csharp
[EntityPayloadFields.Srd]           = p.Envelope.Srd            ? "true" : "false",
[EntityPayloadFields.Srd52]         = p.Envelope.Srd52           ? "true" : "false",
[EntityPayloadFields.BasicRules2024]= p.Envelope.BasicRules2024  ? "true" : "false",
[EntityPayloadFields.NeedsReview]   = p.Envelope.NeedsReview     ? "true" : "false",
```

- [ ] **Step 2: Read NeedsReview back in ToEnvelope**

In the same file, in the `ToEnvelope` method, find the three `Srd`/`Srd52`/`BasicRules2024` lines and add `NeedsReview` after them:

```csharp
Srd:            p.TryGetValue(EntityPayloadFields.Srd,            out var srdV)   && srdV.StringValue   == "true",
Srd52:          p.TryGetValue(EntityPayloadFields.Srd52,          out var srd52V) && srd52V.StringValue  == "true",
BasicRules2024: p.TryGetValue(EntityPayloadFields.BasicRules2024, out var brV)    && brV.StringValue     == "true",
NeedsReview:    p.TryGetValue(EntityPayloadFields.NeedsReview,    out var nrV)    && nrV.StringValue     == "true",
```

- [ ] **Step 3: Build and run tests**

```bash
dotnet build 2>&1 | grep "error" | head -10
dotnet test 2>&1 | tail -5
```

Expected: 0 build errors, all tests pass.

- [ ] **Step 4: Commit**

```bash
git add Features/VectorStore/Entities/QdrantEntityVectorStore.cs
git commit -m "feat(qdrant): persist NeedsReview flag in entity payload"
```

---

### Task 3: Extraction prompt — naming-case rule

**Files:**
- Modify: `Features/Ingestion/EntityExtraction/ExtractionPromptBuilder.cs`
- Modify: `DndMcpAICsharpFun.Tests/Entities/Extraction/ExtractionPromptBuilderTests.cs`

- [ ] **Step 1: Write the failing test**

In `DndMcpAICsharpFun.Tests/Entities/Extraction/ExtractionPromptBuilderTests.cs`, add:

```csharp
[Fact]
public void System_prompt_includes_title_case_naming_rule()
{
    var b = new ExtractionPromptBuilder();
    var prompt = b.BuildSystemPrompt("Player's Handbook", "Edition2014", EntityType.Class);
    prompt.Should().Contain("title case");
    prompt.Should().Contain("ALL CAPS");
}
```

- [ ] **Step 2: Run the test to confirm it fails**

```bash
dotnet test --filter "System_prompt_includes_title_case_naming_rule" 2>&1 | tail -5
```

Expected: FAIL — prompt does not yet contain "title case".

- [ ] **Step 3: Add the naming rule to BuildSystemPrompt**

In `Features/Ingestion/EntityExtraction/ExtractionPromptBuilder.cs`, in `BuildSystemPrompt`, after the line `sb.AppendLine("If uncertain, pick the most specific applicable type over Class.");`, add:

```csharp
sb.AppendLine();
sb.AppendLine("IMPORTANT — Entity name casing:");
sb.AppendLine("Entity names MUST be written in title case following D&D conventions.");
sb.AppendLine("Capitalize all words except articles and short prepositions " +
              "(of, the, a, an, in, on, at, to, and, or, but, for, nor) unless they start the name.");
sb.AppendLine("PDF headings appear in ALL CAPS — you MUST convert them: " +
              "\"CIRCLE OF SPORES\" → \"Circle of Spores\", \"FIREBALL\" → \"Fireball\".");
sb.AppendLine("Correct apostrophe-S: \"TASHA'S\" → \"Tasha's\" (not \"Tasha'S\").");
```

- [ ] **Step 4: Run the test to confirm it passes**

```bash
dotnet test --filter "System_prompt_includes_title_case_naming_rule" 2>&1 | tail -5
```

Expected: PASS.

- [ ] **Step 5: Run the full test suite**

```bash
dotnet test 2>&1 | tail -5
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add Features/Ingestion/EntityExtraction/ExtractionPromptBuilder.cs \
        DndMcpAICsharpFun.Tests/Entities/Extraction/ExtractionPromptBuilderTests.cs
git commit -m "feat(extraction): add title-case naming rule to extraction system prompt"
```

---

### Task 4: Schema confidence injection + orchestrator NeedsReview

**Files:**
- Modify: `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs`
- Test: `DndMcpAICsharpFun.Tests/Entities/Extraction/NeedsReviewHeuristicTests.cs` (create)

The JSON schemas in `Schemas/canonical/` have `"additionalProperties": false`, so we must inject the `confidence` field programmatically rather than editing all 22 files. We do this in `LoadSchemas()`.

After the LLM responds, we:
1. Read `confidence` from `ToolInput`
2. Strip it from the JSON so it's not persisted in `Fields`
3. Apply heuristic to the entity name
4. Set `NeedsReview` accordingly

- [ ] **Step 1: Write failing tests for the heuristic and confidence logic**

Create `DndMcpAICsharpFun.Tests/Entities/Extraction/NeedsReviewHeuristicTests.cs`:

```csharp
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public class NeedsReviewHeuristicTests
{
    [Theory]
    [InlineData("Circle of Spores", false)]
    [InlineData("Fireball", false)]
    [InlineData("Tasha's Cauldron of Everything", false)]
    public void Clean_names_do_not_trigger_heuristic(string name, bool expected)
        => ExtractionNeedsReview.HasOcrArtifacts(name).Should().Be(expected);

    [Theory]
    [InlineData("CIRCLE OF SPORES", true)]          // all caps
    [InlineData("Path of the Beast f eature", true)] // split word
    [InlineData("Gons OF YouR WoRLD", true)]         // alternating case
    [InlineData("Some ..... Thing", true)]            // noise
    public void Artifact_names_trigger_heuristic(string name, bool expected)
        => ExtractionNeedsReview.HasOcrArtifacts(name).Should().Be(expected);

    [Fact]
    public void Low_confidence_sets_needs_review_regardless_of_name()
        => ExtractionNeedsReview.Derive("Circle of Spores", "low").Should().BeTrue();

    [Fact]
    public void Medium_confidence_sets_needs_review_regardless_of_name()
        => ExtractionNeedsReview.Derive("Fireball", "medium").Should().BeTrue();

    [Fact]
    public void High_confidence_clean_name_does_not_set_needs_review()
        => ExtractionNeedsReview.Derive("Circle of Spores", "high").Should().BeFalse();

    [Fact]
    public void High_confidence_artifact_name_still_sets_needs_review()
        => ExtractionNeedsReview.Derive("CIRCLE OF SPORES", "high").Should().BeTrue();

    [Fact]
    public void Null_confidence_uses_heuristic_only()
        => ExtractionNeedsReview.Derive("Circle of Spores", null).Should().BeFalse();
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
dotnet test --filter "NeedsReviewHeuristicTests" 2>&1 | tail -5
```

Expected: compile error — `ExtractionNeedsReview` not found.

- [ ] **Step 3: Create ExtractionNeedsReview static class**

Create `Features/Ingestion/EntityExtraction/ExtractionNeedsReview.cs`:

```csharp
using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public static class ExtractionNeedsReview
{
    private static readonly Regex SplitWordPattern =
        new(@"\b[a-z] [a-z]\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NoisePattern =
        new(@"\.{3,}", RegexOptions.Compiled);

    public static bool Derive(string name, string? confidence) =>
        confidence is "low" or "medium" || HasOcrArtifacts(name);

    public static bool HasOcrArtifacts(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name.Length > 1 && name == name.ToUpperInvariant() && name.Any(char.IsLetter))
            return true;
        if (SplitWordPattern.IsMatch(name.ToLowerInvariant()))
            return true;
        if (NoisePattern.IsMatch(name))
            return true;
        foreach (var word in name.Split(' ', '-'))
        {
            if (CountCaseAlternations(word) > 3) return true;
        }
        return false;
    }

    private static int CountCaseAlternations(string word)
    {
        var letters = word.Where(char.IsLetter).ToArray();
        int count = 0;
        for (int i = 1; i < letters.Length; i++)
            if (char.IsUpper(letters[i]) != char.IsUpper(letters[i - 1]))
                count++;
        return count;
    }
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
dotnet test --filter "NeedsReviewHeuristicTests" 2>&1 | tail -5
```

Expected: all 8 tests PASS.

- [ ] **Step 5: Inject confidence into schemas in LoadSchemas()**

In `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs`, add a private static helper method before `LoadSchemas()`:

```csharp
private static JsonElement InjectConfidenceField(JsonElement schema)
{
    using var ms = new System.IO.MemoryStream();
    using var writer = new Utf8JsonWriter(ms);
    writer.WriteStartObject();
    foreach (var prop in schema.EnumerateObject())
    {
        if (prop.Name == "properties")
        {
            writer.WritePropertyName("properties");
            writer.WriteStartObject();
            foreach (var p in prop.Value.EnumerateObject())
                p.WriteTo(writer);
            writer.WritePropertyName("confidence");
            writer.WriteRawValue("{\"type\":\"string\",\"enum\":[\"low\",\"medium\",\"high\"]}");
            writer.WriteEndObject();
        }
        else
        {
            prop.WriteTo(writer);
        }
    }
    writer.WriteEndObject();
    writer.Flush();
    return JsonDocument.Parse(ms.ToArray()).RootElement.Clone();
}

private static JsonElement StripConfidence(JsonElement toolInput)
{
    using var ms = new System.IO.MemoryStream();
    using var writer = new Utf8JsonWriter(ms);
    writer.WriteStartObject();
    foreach (var prop in toolInput.EnumerateObject())
        if (!string.Equals(prop.Name, "confidence", StringComparison.Ordinal))
            prop.WriteTo(writer);
    writer.WriteEndObject();
    writer.Flush();
    return JsonDocument.Parse(ms.ToArray()).RootElement.Clone();
}
```

In `LoadSchemas()`, change the line `dict[type] = doc.RootElement.Clone();` to:

```csharp
dict[type] = InjectConfidenceField(doc.RootElement.Clone());
```

- [ ] **Step 6: Set NeedsReview on the envelope in the extraction loop**

In `EntityExtractionOrchestrator.cs`, find the block at line ~210 that creates `var envelope = new EntityEnvelope(...)`. Replace it with:

```csharp
var rawInput = response.ToolInput!.Value;
string? confidence = rawInput.TryGetProperty("confidence", out var cp) ? cp.GetString() : null;
var fields = StripConfidence(rawInput);
var needsReview = ExtractionNeedsReview.Derive(candidate.DisplayName, confidence);

var envelope = new EntityEnvelope(
    Id:              id,
    Type:            candidate.Type,
    Name:            candidate.DisplayName,
    SourceBook:      sourceBook,
    Edition:         edition,
    Page:            candidate.Page,
    FirstAppearedIn: new FirstAppearance(sourceBook, edition, candidate.Page),
    RevisedIn:       Array.Empty<Revision>(),
    SettingTags:     Array.Empty<string>(),
    CanonicalText:   string.Empty,
    Fields:          fields,
    NeedsReview:     needsReview);
```

There is a second identical `new EntityEnvelope(...)` block further in the file (around line 400, in the `errorsOnly` re-extraction path). Apply the same change there.

- [ ] **Step 7: Build and run all tests**

```bash
dotnet build 2>&1 | grep "error" | head -10
dotnet test 2>&1 | tail -5
```

Expected: 0 errors, all tests pass.

- [ ] **Step 8: Commit**

```bash
git add Features/Ingestion/EntityExtraction/ExtractionNeedsReview.cs \
        Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs \
        DndMcpAICsharpFun.Tests/Entities/Extraction/NeedsReviewHeuristicTests.cs
git commit -m "feat(extraction): derive NeedsReview from LLM confidence + OCR heuristic"
```

---

### Task 5: Validation — NeedsReview warnings

**Files:**
- Modify: `Features/Admin/CanonicalValidationReport.cs`
- Modify: `Features/Admin/CanonicalValidationService.cs`
- Modify: `DndMcpAICsharpFun.Tests/Entities/Admin/CanonicalValidationEndpointTests.cs`

- [ ] **Step 1: Write the failing test**

In `DndMcpAICsharpFun.Tests/Entities/Admin/CanonicalValidationEndpointTests.cs`, add:

```csharp
[Fact]
public async Task Entities_with_needsReview_produce_warning()
{
    var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(dir);
    try
    {
        // Write a minimal canonical JSON with 2 needsReview entities
        var json = """
        {
          "schemaVersion": "1.0",
          "book": "test",
          "entities": [
            {
              "id": "test.class.alpha",
              "type": "Class",
              "name": "ALPHA",
              "sourceBook": "TEST",
              "edition": "Edition2014",
              "needsReview": true,
              "dataSource": "",
              "srd": false, "srd52": false, "basicRules2024": false,
              "firstAppearedIn": { "book": "TEST", "edition": "Edition2014" },
              "revisedIn": [], "settingTags": [], "canonicalText": "",
              "fields": {}
            },
            {
              "id": "test.class.beta",
              "type": "Class",
              "name": "Beta",
              "sourceBook": "TEST",
              "edition": "Edition2014",
              "needsReview": true,
              "dataSource": "",
              "srd": false, "srd52": false, "basicRules2024": false,
              "firstAppearedIn": { "book": "TEST", "edition": "Edition2014" },
              "revisedIn": [], "settingTags": [], "canonicalText": "",
              "fields": {}
            }
          ]
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(dir, "test.json"), json);

        var svc = new CanonicalValidationService(
            new CanonicalJsonLoader(),
            new EntityReferenceResolver(),
            Options.Create(new EntityExtractionOptions { CanonicalDirectory = dir }),
            NullLogger<CanonicalValidationService>.Instance);

        var report = await svc.ValidateAsync(CancellationToken.None);
        report.Failures.Should().BeEmpty();
        report.NeedsReview.Should().HaveCount(1);
        report.NeedsReview[0].File.Should().Be("test.json");
        report.NeedsReview[0].Count.Should().Be(2);
    }
    finally
    {
        Directory.Delete(dir, recursive: true);
    }
}

[Fact]
public async Task No_needsReview_entities_produces_no_warning()
{
    var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(dir);
    try
    {
        var json = """
        {
          "schemaVersion": "1.0",
          "book": "test",
          "entities": [
            {
              "id": "test.class.clean",
              "type": "Class",
              "name": "Clean",
              "sourceBook": "TEST",
              "edition": "Edition2014",
              "needsReview": false,
              "dataSource": "",
              "srd": false, "srd52": false, "basicRules2024": false,
              "firstAppearedIn": { "book": "TEST", "edition": "Edition2014" },
              "revisedIn": [], "settingTags": [], "canonicalText": "",
              "fields": {}
            }
          ]
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(dir, "test.json"), json);

        var svc = new CanonicalValidationService(
            new CanonicalJsonLoader(),
            new EntityReferenceResolver(),
            Options.Create(new EntityExtractionOptions { CanonicalDirectory = dir }),
            NullLogger<CanonicalValidationService>.Instance);

        var report = await svc.ValidateAsync(CancellationToken.None);
        report.NeedsReview.Should().BeEmpty();
    }
    finally
    {
        Directory.Delete(dir, recursive: true);
    }
}
```

- [ ] **Step 2: Run the tests to confirm they fail**

```bash
dotnet test --filter "needsReview" 2>&1 | tail -5
```

Expected: compile errors — `NeedsReview` not on `CanonicalValidationReport`.

- [ ] **Step 3: Add CanonicalNeedsReviewWarning and update the report**

In `Features/Admin/CanonicalValidationReport.cs`, add `CanonicalNeedsReviewWarning` and extend `CanonicalValidationReport`:

```csharp
namespace DndMcpAICsharpFun.Features.Admin;

public sealed record CanonicalValidationFailure(string File, string Kind, string Detail);

public sealed record CanonicalValidationWarning(string File, string SourceEntityId, string FieldPath, string MissingTargetId);

public sealed record CanonicalNeedsReviewWarning(string File, int Count);

public sealed record CanonicalValidationReport(
    int FilesScanned,
    int TotalEntities,
    IReadOnlyList<CanonicalValidationFailure> Failures,
    IReadOnlyList<CanonicalValidationWarning> Warnings,
    IReadOnlyList<CanonicalNeedsReviewWarning> NeedsReview = null!)
{
    public IReadOnlyList<CanonicalNeedsReviewWarning> NeedsReview { get; init; } = NeedsReview ?? [];
}
```

- [ ] **Step 4: Count needsReview entities in CanonicalValidationService**

In `Features/Admin/CanonicalValidationService.cs`, add a `needsReviewWarnings` list, populate it inside the per-file loop, and pass it to the return value:

```csharp
public async Task<CanonicalValidationReport> ValidateAsync(CancellationToken ct)
{
    var failures = new List<CanonicalValidationFailure>();
    var warnings = new List<CanonicalValidationWarning>();
    var needsReviewWarnings = new List<CanonicalNeedsReviewWarning>();
    var allEntities = new List<EntityEnvelope>();
    var seenIds = new Dictionary<string, string>(StringComparer.Ordinal);

    if (!Directory.Exists(_opts.CanonicalDirectory))
        return new CanonicalValidationReport(0, 0, failures, warnings, needsReviewWarnings);

    var files = Directory.GetFiles(_opts.CanonicalDirectory, "*.json", SearchOption.TopDirectoryOnly)
        .Where(f => !f.EndsWith(".errors.json", StringComparison.Ordinal)
                 && !f.EndsWith(".warnings.json", StringComparison.Ordinal)
                 && !f.EndsWith(".progress.json", StringComparison.Ordinal)
                 && !f.EndsWith(".progress.errors.json", StringComparison.Ordinal))
        .OrderBy(f => f)
        .ToList();

    foreach (var path in files)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var loaded = await loader.LoadAsync(path, ct);
            var fileName = Path.GetFileName(path);

            foreach (var entity in loaded.Entities)
            {
                if (seenIds.TryGetValue(entity.Id, out var existingFile))
                {
                    failures.Add(new CanonicalValidationFailure(
                        File: fileName,
                        Kind: "duplicate_id",
                        Detail: $"id '{entity.Id}' also defined in {Path.GetFileName(existingFile)}"));
                }
                else
                {
                    seenIds[entity.Id] = path;
                }
            }

            var reviewCount = loaded.Entities.Count(e => e.NeedsReview);
            if (reviewCount > 0)
                needsReviewWarnings.Add(new CanonicalNeedsReviewWarning(
                    File: fileName,
                    Count: reviewCount));

            allEntities.AddRange(loaded.Entities);
        }
        catch (CanonicalJsonSchemaException ex)
        {
            failures.Add(new CanonicalValidationFailure(
                File: Path.GetFileName(path),
                Kind: "schema_validation_failure",
                Detail: ex.Message));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load canonical JSON file {Path}", path);
            failures.Add(new CanonicalValidationFailure(
                File: Path.GetFileName(path),
                Kind: "load_error",
                Detail: ex.Message));
        }
    }

    var refWarnings = resolver.Resolve(allEntities).ToList();
    foreach (var w in refWarnings)
    {
        var sourceBookSlug = w.SourceEntityId.Split('.')[0];
        var targetBookSlug = w.MissingTargetId.Split('.')[0];
        if (string.Equals(sourceBookSlug, targetBookSlug, StringComparison.Ordinal))
        {
            failures.Add(new CanonicalValidationFailure(
                File: $"{sourceBookSlug}.json",
                Kind: "intra_book_dangling_ref_post_extraction",
                Detail: $"{w.SourceEntityId} references missing intra-book {w.MissingTargetId} at {w.FieldPath}"));
        }
        else
        {
            warnings.Add(new CanonicalValidationWarning(
                File: $"{sourceBookSlug}.json",
                SourceEntityId: w.SourceEntityId,
                FieldPath: w.FieldPath,
                MissingTargetId: w.MissingTargetId));
        }
    }

    return new CanonicalValidationReport(files.Count, allEntities.Count, failures, warnings, needsReviewWarnings);
}
```

- [ ] **Step 5: Run tests**

```bash
dotnet test --filter "needsReview" 2>&1 | tail -5
```

Expected: 2 new tests PASS.

- [ ] **Step 6: Run full test suite**

```bash
dotnet test 2>&1 | tail -5
```

Expected: all tests pass.

- [ ] **Step 7: Commit**

```bash
git add Features/Admin/CanonicalValidationReport.cs \
        Features/Admin/CanonicalValidationService.cs \
        DndMcpAICsharpFun.Tests/Entities/Admin/CanonicalValidationEndpointTests.cs
git commit -m "feat(validation): report needsReview entity count per file as warnings"
```

---

### Task 6: Python normalization script

**Files:**
- Create: `scripts/normalize_canonical_names.py`
- Create: `tests/test_normalize_names.py`

- [ ] **Step 1: Write the pytest tests first**

Create `tests/test_normalize_names.py`:

```python
import sys
sys.path.insert(0, 'scripts')
from normalize_canonical_names import dnd_title_case, has_ocr_artifacts, normalize_entity

# --- dnd_title_case ---

def test_all_caps_simple():
    assert dnd_title_case("FIREBALL") == "Fireball"

def test_all_caps_with_small_word():
    assert dnd_title_case("CIRCLE OF SPORES") == "Circle of Spores"

def test_all_caps_starts_with_small_word():
    assert dnd_title_case("OF MICE AND MEN") == "Of Mice and Men"

def test_apostrophe_corrected():
    assert dnd_title_case("TASHA'S CAULDRON") == "Tasha's Cauldron"

def test_hyphenated_word():
    assert dnd_title_case("SPIDER-CLIMB") == "Spider-Climb"

# --- has_ocr_artifacts ---

def test_clean_name_no_artifact():
    assert not has_ocr_artifacts("Circle of Spores")

def test_all_caps_is_artifact():
    assert has_ocr_artifacts("CIRCLE OF SPORES")

def test_split_word_is_artifact():
    assert has_ocr_artifacts("Path of the Beast f eature")

def test_noise_dots_is_artifact():
    assert has_ocr_artifacts("Some ..... Thing")

def test_alternating_case_is_artifact():
    assert has_ocr_artifacts("Gons OF YouR WoRLD")

def test_single_word_no_artifact():
    assert not has_ocr_artifacts("Fighter")

# --- normalize_entity ---

def test_all_caps_entity_gets_title_cased():
    e = {"id": "x", "name": "CIRCLE OF SPORES", "needsReview": False}
    result = normalize_entity(e)
    assert result["name"] == "Circle of Spores"
    assert result["needsReview"] is False

def test_garbled_entity_gets_flagged():
    e = {"id": "x", "name": "Path of the Beast f eature"}
    result = normalize_entity(e)
    assert result["name"] == "Path of the Beast f eature"
    assert result["needsReview"] is True

def test_clean_entity_unchanged():
    e = {"id": "x", "name": "Fighter"}
    result = normalize_entity(e)
    assert result["name"] == "Fighter"
    assert result["needsReview"] is False

def test_idempotent_all_caps():
    e1 = {"id": "x", "name": "FIREBALL"}
    e2 = normalize_entity(dict(e1))
    e3 = normalize_entity(dict(e2))
    assert e2 == e3

def test_idempotent_already_clean():
    e = {"id": "x", "name": "Fireball", "needsReview": False}
    assert normalize_entity(dict(e)) == normalize_entity(normalize_entity(dict(e)))
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
cd /home/aliendreamer/projects/DndMcpAICsharpFun
python3 -m pytest tests/test_normalize_names.py -v 2>&1 | tail -10
```

Expected: ModuleNotFoundError — script does not exist yet.

- [ ] **Step 3: Create the normalization script**

Create `scripts/normalize_canonical_names.py`:

```python
#!/usr/bin/env python3
"""Normalize entity names in canonical JSON files.

Usage:
    python3 scripts/normalize_canonical_names.py              # process all data/canonical/*.json
    python3 scripts/normalize_canonical_names.py --dry-run    # print changes, do not write
    python3 scripts/normalize_canonical_names.py --file tce.json  # single file
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

CANONICAL_DIR = Path("data/canonical")

SMALL_WORDS = frozenset([
    "a", "an", "the", "and", "but", "or", "for", "nor",
    "on", "at", "to", "by", "in", "of", "up", "as", "with",
])

SPLIT_WORD_RE = re.compile(r"\b[a-z] [a-z]\b")
NOISE_RE = re.compile(r"\.{3,}")


def dnd_title_case(name: str) -> str:
    """Convert an all-caps D&D name to proper title case."""
    def convert_word(word: str, is_first: bool) -> str:
        low = word.lower()
        if is_first or low not in SMALL_WORDS:
            # capitalize, preserving apostrophes
            cap = low.capitalize()
            # Fix 'S → 's  (Python capitalize produces 'S after apostrophe)
            cap = re.sub(r"'[A-Z]", lambda m: m.group(0).lower(), cap)
            return cap
        return low

    parts = name.split(" ")
    result = []
    for i, part in enumerate(parts):
        if "-" in part:
            sub = part.split("-")
            result.append("-".join(convert_word(s, i == 0 and j == 0) for j, s in enumerate(sub)))
        else:
            result.append(convert_word(part, i == 0))
    return " ".join(result)


def count_case_alternations(word: str) -> int:
    letters = [c for c in word if c.isalpha()]
    return sum(1 for i in range(1, len(letters)) if letters[i].isupper() != letters[i - 1].isupper())


def has_ocr_artifacts(name: str) -> bool:
    """Return True if the name has OCR-quality problems."""
    if not name:
        return False
    if len(name) > 1 and name == name.upper() and any(c.isalpha() for c in name):
        return True
    lower = name.lower()
    if SPLIT_WORD_RE.search(lower):
        return True
    if NOISE_RE.search(name):
        return True
    for word in re.split(r"[\s\-]", name):
        if count_case_alternations(word) > 3:
            return True
    return False


def normalize_entity(entity: dict) -> dict:
    """Normalize a single entity dict. Modifies in-place and returns it."""
    name = entity.get("name", "")
    if not isinstance(name, str) or not name:
        entity.setdefault("needsReview", False)
        return entity

    is_all_caps = name == name.upper() and any(c.isalpha() for c in name) and len(name) > 1
    # Check OTHER artifacts on the name (excluding the all-caps check itself)
    has_other_artifacts = (
        SPLIT_WORD_RE.search(name.lower()) is not None
        or NOISE_RE.search(name) is not None
        or any(count_case_alternations(w) > 3 for w in re.split(r"[\s\-]", name))
    )

    if is_all_caps and not has_other_artifacts:
        entity["name"] = dnd_title_case(name)
        entity.setdefault("needsReview", False)
    elif has_ocr_artifacts(name):
        entity["needsReview"] = True
    else:
        entity.setdefault("needsReview", False)

    return entity


def process_file(path: Path, dry_run: bool) -> tuple[int, int, int]:
    """Process one canonical JSON file. Returns (title_cased, flagged, unchanged)."""
    with open(path, encoding="utf-8") as f:
        data = json.load(f)

    entities = data.get("entities", [])
    title_cased = flagged = unchanged = 0

    for entity in entities:
        old_name = entity.get("name", "")
        old_review = entity.get("needsReview", False)
        normalize_entity(entity)
        new_name = entity.get("name", "")
        new_review = entity.get("needsReview", False)

        if new_name != old_name:
            title_cased += 1
            if dry_run:
                print(f"  CASE  {path.name}: {old_name!r} → {new_name!r}")
        elif new_review and not old_review:
            flagged += 1
            if dry_run:
                print(f"  FLAG  {path.name}: {old_name!r}")
        else:
            unchanged += 1

    if not dry_run:
        with open(path, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2, ensure_ascii=False)
            f.write("\n")

    return title_cased, flagged, unchanged


def main() -> int:
    parser = argparse.ArgumentParser(description="Normalize entity names in canonical JSON files.")
    parser.add_argument("--dry-run", action="store_true", help="Print changes without writing.")
    parser.add_argument("--file", metavar="FILENAME", help="Process only this file (name only, e.g. tce.json).")
    args = parser.parse_args()

    if args.file:
        paths = [CANONICAL_DIR / args.file]
    else:
        paths = sorted(p for p in CANONICAL_DIR.glob("*.json")
                       if not any(p.name.endswith(s) for s in
                                  [".errors.json", ".warnings.json", ".progress.json", ".progress.errors.json"]))

    total_cased = total_flagged = total_unchanged = 0
    for path in paths:
        if not path.exists():
            print(f"ERROR: {path} not found", file=sys.stderr)
            return 1
        c, f, u = process_file(path, args.dry_run)
        print(f"{path.name}: {c} title-cased, {f} flagged, {u} unchanged")
        total_cased += c
        total_flagged += f
        total_unchanged += u

    print(f"\nTotal: {total_cased} title-cased, {total_flagged} flagged, {total_unchanged} unchanged")
    if args.dry_run:
        print("(dry-run — no files written)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **Step 4: Run pytest to confirm tests pass**

```bash
python3 -m pytest tests/test_normalize_names.py -v 2>&1 | tail -15
```

Expected: all tests PASS.

- [ ] **Step 5: Run the script in dry-run mode to preview changes**

```bash
python3 scripts/normalize_canonical_names.py --dry-run 2>&1 | head -40
```

Expected: output shows title-case conversions for tce.json and dmg14.json, flags garbled names.

- [ ] **Step 6: Commit the script and tests**

```bash
git add scripts/normalize_canonical_names.py tests/test_normalize_names.py
git commit -m "feat(scripts): add normalize_canonical_names.py with pytest tests"
```

---

### Task 7: Apply normalization to existing data and re-ingest

- [ ] **Step 1: Apply script to existing canonical JSONs**

```bash
python3 scripts/normalize_canonical_names.py
```

Expected output (approximate):
```
tce.json: 234 title-cased, N flagged, M unchanged
dmg14.json: 506 title-cased, N flagged, M unchanged
phb14.json: 0 title-cased, 0 flagged, 3 unchanged
```

- [ ] **Step 2: Validate canonical JSONs**

```bash
curl -s -X POST http://localhost:5101/admin/canonical/validate \
  -H "X-Admin-Api-Key: devXXXdev" | python3 -m json.tool 2>/dev/null
```

Expected: HTTP 200, 0 failures, `needsReview` warnings listing the flagged-entity counts per file.

- [ ] **Step 3: Re-ingest both books**

```bash
curl -s -X POST http://localhost:5101/admin/books/1/ingest-entities \
  -H "X-Admin-Api-Key: devXXXdev"
# Wait for completion (watch logs)
curl -s -X POST http://localhost:5101/admin/books/2/ingest-entities \
  -H "X-Admin-Api-Key: devXXXdev"
```

Watch with:
```bash
docker compose logs --follow --tail=20 app 2>/dev/null | grep -E "Entity ingestion complete|INF"
```

- [ ] **Step 4: Spot-check the fixed entity**

```bash
curl -s http://localhost:5101/retrieval/entities/tce.subclass.circle-of-spores | \
  python3 -c "import sys,json; e=json.load(sys.stdin)['envelope']; print('name:', e['name'])"
```

Expected: `name: Circle of Spores` (not `CIRCLE OF SPORES`).

- [ ] **Step 5: Commit the normalized data files**

```bash
git add data/canonical/tce.json data/canonical/dmg14.json data/canonical/phb14.json
git commit -m "data: normalize entity names in canonical JSONs (title-case + needsReview flags)"
```
