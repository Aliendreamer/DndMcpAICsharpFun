# Extraction Pipeline Improvements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Make the LLM extraction pipeline robust for large, table-heavy pages via semantic chunking, deterministic JSON merging, partial JSON recovery, and few-shot examples.

**Architecture:** A split-and-merge layer around the existing per-candidate LLM call. `SemanticChunker` splits oversized candidate text at structural boundaries; each chunk gets its own LLM call; `EntityFieldMerger` merges partial JSON outputs deterministically. `PartialJsonRecoverer` rescues truncated JSON inside `OllamaEntityExtractionClient`. `ExtractionPromptBuilder` gains optional few-shot examples loaded from `Schemas/examples/`.

**Tech Stack:** C# / .NET 10, `System.Text.Json`, xUnit + FluentAssertions, existing `OllamaEntityExtractionClient`, `ExtractionPromptBuilder`, `EntityExtractionOrchestrator`.

---

## File Map

| Action | Path |
| --- | --- |
| Create | `Features/Ingestion/EntityExtraction/SemanticChunker.cs` |
| Create | `Features/Ingestion/EntityExtraction/EntityFieldMerger.cs` |
| Create | `Features/Ingestion/EntityExtraction/PartialJsonRecoverer.cs` |
| Create | `Schemas/examples/Trap.json`, `Spell.json`, `Monster.json`, `Item.json`, `MagicItem.json`, `Rule.json`, `God.json`, `Lore.json` |
| Modify | `Features/Ingestion/EntityExtraction/EntityExtractionOptions.cs` |
| Modify | `Features/Ingestion/EntityExtraction/ExtractionPromptBuilder.cs` |
| Modify | `Features/Ingestion/EntityExtraction/OllamaEntityExtractionClient.cs` |
| Modify | `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs` |
| Modify | `Extensions/ServiceCollectionExtensions.cs` (DI wiring) |
| Modify | `Config/appsettings.json` |
| Test | `DndMcpAICsharpFun.Tests/Entities/Extraction/SemanticChunkerTests.cs` |
| Test | `DndMcpAICsharpFun.Tests/Entities/Extraction/EntityFieldMergerTests.cs` |
| Test | `DndMcpAICsharpFun.Tests/Entities/Extraction/PartialJsonRecovererTests.cs` |
| Test | `DndMcpAICsharpFun.Tests/Entities/Extraction/OllamaEntityExtractionClientTests.cs` |
| Test | `DndMcpAICsharpFun.Tests/Entities/Extraction/ExtractionPromptBuilderTests.cs` (extend) |

Conventions used throughout (match existing code):

- Namespace: `DndMcpAICsharpFun.Features.Ingestion.EntityExtraction`
- Test namespace: `DndMcpAICsharpFun.Tests.Entities.Extraction`
- Tests use xUnit `[Fact]` + FluentAssertions (`.Should()`)
- Run tests with: `dotnet test DndMcpAICsharpFun.Tests --nologo -v q --filter "FullyQualifiedName~<ClassName>"`

---

### Task 1: SemanticChunker

**Files:**
- Create: `Features/Ingestion/EntityExtraction/SemanticChunker.cs`
- Test: `DndMcpAICsharpFun.Tests/Entities/Extraction/SemanticChunkerTests.cs`

- [x] **Step 1.1: Write the failing tests**

```csharp
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public sealed class SemanticChunkerTests
{
    private readonly SemanticChunker _chunker = new();

    [Fact]
    public void Split_TextUnderLimit_ReturnsSingleChunkUnchanged()
    {
        var text = "A short paragraph.";

        var chunks = _chunker.Split(text, maxTokensPerChunk: 2000);

        chunks.Should().ContainSingle().Which.Should().Be(text);
    }

    [Fact]
    public void Split_PrefersSubHeadingBoundaries()
    {
        // Each section ~400 chars => ~100 tokens; limit 150 tokens forces a split,
        // and the split must land on the "## " boundary.
        var sectionA = "## Section A\n" + new string('a', 400);
        var sectionB = "## Section B\n" + new string('b', 400);
        var text = sectionA + "\n" + sectionB;

        var chunks = _chunker.Split(text, maxTokensPerChunk: 150);

        chunks.Should().HaveCount(2);
        chunks[0].Should().StartWith("## Section A");
        chunks[1].Should().StartWith("## Section B");
    }

    [Fact]
    public void Split_FallsBackToParagraphBreaks()
    {
        var para1 = new string('x', 400);
        var para2 = new string('y', 400);
        var text = para1 + "\n\n" + para2;

        var chunks = _chunker.Split(text, maxTokensPerChunk: 150);

        chunks.Should().HaveCount(2);
        chunks[0].Should().Contain(para1);
        chunks[1].Should().Contain(para2);
    }

    [Fact]
    public void Split_SplitsBeforeTableSeparator()
    {
        var intro = new string('i', 400);
        var table = "| Name | CR |\n|------|----|\n| Goblin | 1/4 |";
        var text = intro + "\n" + table;

        var chunks = _chunker.Split(text, maxTokensPerChunk: 110);

        chunks.Should().HaveCount(2);
        chunks[1].Should().Contain("| Name | CR |");
    }

    [Fact]
    public void Split_NeverEmitsEmptyChunks()
    {
        var text = "\n\n" + new string('z', 900) + "\n\n\n\n" + new string('w', 900) + "\n\n";

        var chunks = _chunker.Split(text, maxTokensPerChunk: 150);

        chunks.Should().NotContain(c => string.IsNullOrWhiteSpace(c));
    }

    [Fact]
    public void Split_NoBoundaries_SplitsAtLineGranularity()
    {
        // 10 lines of 200 chars, no headings/tables/paragraph breaks.
        var lines = Enumerable.Range(0, 10).Select(i => new string((char)('a' + i), 200));
        var text = string.Join("\n", lines);

        var chunks = _chunker.Split(text, maxTokensPerChunk: 150);

        chunks.Should().HaveCountGreaterThan(1);
        string.Concat(chunks.Select(c => c.Replace("\n", "")))
            .Should().Be(text.Replace("\n", ""));
    }
}
```

- [x] **Step 1.2: Run tests to verify they fail**

Run: `dotnet test DndMcpAICsharpFun.Tests --nologo -v q --filter "FullyQualifiedName~SemanticChunkerTests"`
Expected: build FAILURE — `SemanticChunker` does not exist.

- [x] **Step 1.3: Implement SemanticChunker**

```csharp
using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Splits candidate text into chunks at structural boundaries
/// (sub-headings > tables > paragraph breaks > single lines) so each chunk
/// stays under a token budget. Stateless; chars/4 approximates tokens.
/// </summary>
public sealed partial class SemanticChunker
{
    [GeneratedRegex(@"^\|[-| ]+\|\s*$")]
    private static partial Regex TableSeparator();

    public IList<string> Split(string text, int maxTokensPerChunk)
    {
        if (EstimateTokens(text) <= maxTokensPerChunk)
            return [text];

        var lines = text.Split('\n');
        var blocks = SplitIntoBlocks(lines);

        var chunks = new List<string>();
        var current = new List<string>();
        var currentTokens = 0;

        foreach (var block in blocks)
        {
            var blockTokens = EstimateTokens(block);
            if (current.Count > 0 && currentTokens + blockTokens > maxTokensPerChunk)
            {
                EmitChunk(chunks, current);
                current = [];
                currentTokens = 0;
            }
            current.Add(block);
            currentTokens += blockTokens;
        }
        EmitChunk(chunks, current);
        return chunks;
    }

    private static int EstimateTokens(string s) => s.Length / 4;

    /// <summary>
    /// Groups lines into blocks: a new block starts at a sub-heading (## / ###),
    /// before a table separator line, or after a blank line. A block never splits
    /// a table separator from its header row.
    /// </summary>
    private static List<string> SplitIntoBlocks(string[] lines)
    {
        var blocks = new List<string>();
        var current = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            bool isHeading = line.StartsWith("## ", StringComparison.Ordinal)
                          || line.StartsWith("### ", StringComparison.Ordinal);
            // Split BEFORE the table: boundary sits at the table header row,
            // i.e. when the NEXT line is the separator.
            bool nextIsTableSeparator = i + 1 < lines.Length && TableSeparator().IsMatch(lines[i + 1]);
            bool isBlank = string.IsNullOrWhiteSpace(line);

            if ((isHeading || nextIsTableSeparator) && current.Count > 0)
            {
                blocks.Add(string.Join('\n', current));
                current = [];
            }

            current.Add(line);

            if (isBlank && current.Count > 0)
            {
                blocks.Add(string.Join('\n', current));
                current = [];
            }
        }

        if (current.Count > 0)
            blocks.Add(string.Join('\n', current));

        // A "block" that is itself oversized (single huge line run with no
        // boundaries) is split per line so the greedy accumulator can work.
        return blocks
            .SelectMany(b => b.Contains('\n') || b.Length > 0 ? new[] { b } : Array.Empty<string>())
            .ToList();
    }

    private static void EmitChunk(List<string> chunks, List<string> blocks)
    {
        if (blocks.Count == 0) return;
        var chunk = string.Join('\n', blocks);
        if (!string.IsNullOrWhiteSpace(chunk))
            chunks.Add(chunk);
    }
}
```

Note for implementer: the `Split_NoBoundaries_SplitsAtLineGranularity` test feeds 10 long lines with no blank lines or headings. `SplitIntoBlocks` as written produces one block per *boundary*, so a boundary-free text yields a single oversized block. Handle that by treating each line of an oversized block as its own block: after building `blocks`, post-process with:

```csharp
        var sized = new List<string>();
        foreach (var b in blocks)
        {
            if (EstimateTokens(b) > maxTokensPerChunk)
                sized.AddRange(b.Split('\n'));
            else
                sized.Add(b);
        }
```

…which requires passing `maxTokensPerChunk` into `SplitIntoBlocks` (change its signature to `SplitIntoBlocks(string[] lines, int maxTokensPerChunk)` and replace the final `return blocks…` with the post-processing above returning `sized`).

- [x] **Step 1.4: Run tests to verify they pass**

Run: `dotnet test DndMcpAICsharpFun.Tests --nologo -v q --filter "FullyQualifiedName~SemanticChunkerTests"`
Expected: 6 passed.

- [x] **Step 1.5: Commit**

```bash
git add Features/Ingestion/EntityExtraction/SemanticChunker.cs DndMcpAICsharpFun.Tests/Entities/Extraction/SemanticChunkerTests.cs
git commit -m "feat(extraction): add SemanticChunker for structural text splitting"
```

---

### Task 2: EntityFieldMerger

**Files:**
- Create: `Features/Ingestion/EntityExtraction/EntityFieldMerger.cs`
- Test: `DndMcpAICsharpFun.Tests/Entities/Extraction/EntityFieldMergerTests.cs`

- [x] **Step 2.1: Write the failing tests**

```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public sealed class EntityFieldMergerTests
{
    private readonly EntityFieldMerger _merger = new();

    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Merge_SingleElement_ReturnsItUnchanged()
    {
        var one = Parse("""{"name":"Fireball","level":3}""");

        var merged = _merger.Merge([one]);

        merged.GetRawText().Should().Be(one.GetRawText());
    }

    [Fact]
    public void Merge_Scalars_FirstNonNullWins()
    {
        var a = Parse("""{"name":"Pit Trap","threat":null}""");
        var b = Parse("""{"name":"SHOULD NOT WIN","threat":"setback"}""");

        var merged = _merger.Merge([a, b]);

        merged.GetProperty("name").GetString().Should().Be("Pit Trap");
        merged.GetProperty("threat").GetString().Should().Be("setback");
    }

    [Fact]
    public void Merge_Arrays_ConcatenatesInChunkOrder()
    {
        var a = Parse("""{"entries":["first"]}""");
        var b = Parse("""{"entries":["second","third"]}""");

        var merged = _merger.Merge([a, b]);

        merged.GetProperty("entries").EnumerateArray()
            .Select(e => e.GetString())
            .Should().ContainInOrder("first", "second", "third");
    }

    [Fact]
    public void Merge_ArraysWithNamedItems_DropsLaterDuplicates()
    {
        var a = Parse("""{"variants":[{"name":"Spiked Pit","entries":["a"]}]}""");
        var b = Parse("""{"variants":[{"name":"Spiked Pit","entries":["b"]},{"name":"Hidden Pit","entries":["c"]}]}""");

        var merged = _merger.Merge([a, b]);

        var names = merged.GetProperty("variants").EnumerateArray()
            .Select(v => v.GetProperty("name").GetString()).ToList();
        names.Should().Equal("Spiked Pit", "Hidden Pit");
        // first occurrence kept
        merged.GetProperty("variants")[0].GetProperty("entries")[0].GetString().Should().Be("a");
    }

    [Fact]
    public void Merge_Objects_FirstNonNullWins()
    {
        var a = Parse("""{"hp":{"average":11,"formula":"2d8+2"}}""");
        var b = Parse("""{"hp":{"average":99,"formula":"9d8"}}""");

        var merged = _merger.Merge([a, b]);

        merged.GetProperty("hp").GetProperty("average").GetInt32().Should().Be(11);
    }

    [Fact]
    public void Merge_AllEmpty_ReturnsEmptyObject()
    {
        var merged = _merger.Merge([Parse("{}"), Parse("{}")]);

        merged.ValueKind.Should().Be(JsonValueKind.Object);
        merged.EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public void Merge_FieldsOnlyInLaterChunks_AreIncluded()
    {
        var a = Parse("""{"name":"Gas Trap"}""");
        var b = Parse("""{"trapHazType":"MECH"}""");

        var merged = _merger.Merge([a, b]);

        merged.GetProperty("name").GetString().Should().Be("Gas Trap");
        merged.GetProperty("trapHazType").GetString().Should().Be("MECH");
    }
}
```

- [x] **Step 2.2: Run tests to verify they fail**

Run: `dotnet test DndMcpAICsharpFun.Tests --nologo -v q --filter "FullyQualifiedName~EntityFieldMergerTests"`
Expected: build FAILURE — `EntityFieldMerger` does not exist.

- [x] **Step 2.3: Implement EntityFieldMerger**

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Merges ordered partial JSON field objects from chunked extraction into one object.
/// Scalars/objects: first non-null wins. Arrays: concatenated in chunk order,
/// deduplicated by "name" property when present. No entity-type awareness.
/// </summary>
public sealed class EntityFieldMerger
{
    public JsonElement Merge(IList<JsonElement> partials)
    {
        if (partials.Count == 1)
            return partials[0];

        var result = new JsonObject();

        foreach (var partial in partials)
        {
            if (partial.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var prop in partial.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Null)
                    continue;

                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    var target = result[prop.Name] as JsonArray;
                    if (target is null)
                    {
                        target = [];
                        result[prop.Name] = target;
                    }
                    foreach (var item in prop.Value.EnumerateArray())
                        AppendIfNotDuplicate(target, item);
                }
                else if (!result.ContainsKey(prop.Name))
                {
                    // Scalar or object: first non-null wins.
                    result[prop.Name] = JsonNode.Parse(prop.Value.GetRawText());
                }
            }
        }

        return JsonDocument.Parse(result.ToJsonString()).RootElement.Clone();
    }

    private static void AppendIfNotDuplicate(JsonArray target, JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.Object &&
            item.TryGetProperty("name", out var nameProp) &&
            nameProp.ValueKind == JsonValueKind.String)
        {
            var name = nameProp.GetString();
            foreach (var existing in target)
            {
                if (existing is JsonObject obj &&
                    obj.TryGetPropertyValue("name", out var existingName) &&
                    existingName?.GetValue<string>() == name)
                {
                    return; // duplicate by name — keep the first occurrence
                }
            }
        }
        target.Add(JsonNode.Parse(item.GetRawText()));
    }
}
```

- [x] **Step 2.4: Run tests to verify they pass**

Run: `dotnet test DndMcpAICsharpFun.Tests --nologo -v q --filter "FullyQualifiedName~EntityFieldMergerTests"`
Expected: 7 passed.

- [x] **Step 2.5: Commit**

```bash
git add Features/Ingestion/EntityExtraction/EntityFieldMerger.cs DndMcpAICsharpFun.Tests/Entities/Extraction/EntityFieldMergerTests.cs
git commit -m "feat(extraction): add EntityFieldMerger for deterministic partial-JSON merging"
```

---

### Task 3: PartialJsonRecoverer

**Files:**
- Create: `Features/Ingestion/EntityExtraction/PartialJsonRecoverer.cs`
- Test: `DndMcpAICsharpFun.Tests/Entities/Extraction/PartialJsonRecovererTests.cs`

- [x] **Step 3.1: Write the failing tests**

```csharp
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public sealed class PartialJsonRecovererTests
{
    private readonly PartialJsonRecoverer _recoverer = new();

    [Fact]
    public void TryRecover_CompleteJson_ReturnsItWhole()
    {
        var raw = """{"name":"Fireball","level":3}""";

        _recoverer.TryRecover(raw, out var recovered).Should().BeTrue();
        recovered.Should().Be(raw);
    }

    [Fact]
    public void TryRecover_TruncatedMidObject_ReturnsLastBalancedPrefix()
    {
        // Truncated after the closing } of the complete inner object — unrecoverable
        // at top level, because the top-level { never closes.
        var raw = """{"name":"Trap","variants":[{"name":"Pit","entries":["a"]}""";

        _recoverer.TryRecover(raw, out _).Should().BeFalse();
    }

    [Fact]
    public void TryRecover_TopLevelArrayTruncated_RecoversNothing()
    {
        var raw = """[{"name":"a"},{"name":"b"}""";

        _recoverer.TryRecover(raw, out _).Should().BeFalse();
    }

    [Fact]
    public void TryRecover_TrailingGarbageAfterValidJson_RecoversTheJson()
    {
        var raw = """{"name":"Fireball","level":3} and then the model rambled""";

        _recoverer.TryRecover(raw, out var recovered).Should().BeTrue();
        recovered.Should().Be("""{"name":"Fireball","level":3}""");
    }

    [Fact]
    public void TryRecover_NoJsonAtAll_ReturnsFalse()
    {
        _recoverer.TryRecover("the model returned prose", out _).Should().BeFalse();
    }

    [Fact]
    public void TryRecover_BracesInsideStrings_AreIgnored()
    {
        var raw = """{"entries":["use {@dc 15} here"],"name":"x"} trailing""";

        _recoverer.TryRecover(raw, out var recovered).Should().BeTrue();
        recovered.Should().Be("""{"entries":["use {@dc 15} here"],"name":"x"}""");
    }

    [Fact]
    public void TryRecover_EmptyString_ReturnsFalse()
    {
        _recoverer.TryRecover("", out _).Should().BeFalse();
    }
}
```

- [x] **Step 3.2: Run tests to verify they fail**

Run: `dotnet test DndMcpAICsharpFun.Tests --nologo -v q --filter "FullyQualifiedName~PartialJsonRecovererTests"`
Expected: build FAILURE — `PartialJsonRecoverer` does not exist.

- [x] **Step 3.3: Implement PartialJsonRecoverer**

```csharp
using System.Text.Json;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Recovers the longest valid JSON prefix from a truncated/garbage-suffixed
/// LLM response by tracking brace/bracket depth (string-aware) and validating
/// the prefix at the last point depth returned to zero.
/// </summary>
public sealed class PartialJsonRecoverer
{
    public bool TryRecover(string raw, out string recovered)
    {
        recovered = string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        int depth = 0;
        int lastValidClose = -1;
        bool inString = false;
        bool escaped = false;
        bool seenOpen = false;

        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];

            if (inString)
            {
                if (escaped) { escaped = false; }
                else if (c == '\\') { escaped = true; }
                else if (c == '"') { inString = false; }
                continue;
            }

            switch (c)
            {
                case '"': inString = true; break;
                case '{' or '[': depth++; seenOpen = true; break;
                case '}' or ']':
                    depth--;
                    if (depth == 0 && seenOpen) lastValidClose = i;
                    break;
            }
        }

        if (lastValidClose < 0) return false;

        var candidate = raw[..(lastValidClose + 1)].TrimStart();
        try
        {
            using var _ = JsonDocument.Parse(candidate);
            recovered = candidate;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
```

- [x] **Step 3.4: Run tests to verify they pass**

Run: `dotnet test DndMcpAICsharpFun.Tests --nologo -v q --filter "FullyQualifiedName~PartialJsonRecovererTests"`
Expected: 7 passed.

- [x] **Step 3.5: Commit**

```bash
git add Features/Ingestion/EntityExtraction/PartialJsonRecoverer.cs DndMcpAICsharpFun.Tests/Entities/Extraction/PartialJsonRecovererTests.cs
git commit -m "feat(extraction): add PartialJsonRecoverer for truncated LLM output"
```

---

### Task 4: Wire PartialJsonRecoverer into OllamaEntityExtractionClient

**Files:**
- Modify: `Features/Ingestion/EntityExtraction/OllamaEntityExtractionClient.cs`
- Modify: `Extensions/ServiceCollectionExtensions.cs` (register `PartialJsonRecoverer` singleton)
- Test: `DndMcpAICsharpFun.Tests/Entities/Extraction/OllamaEntityExtractionClientTests.cs`

- [x] **Step 4.1: Write the failing tests**

The client takes `IChatClient` (Microsoft.Extensions.AI). Stub it:

```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public sealed class OllamaEntityExtractionClientTests
{
    private sealed class StubChatClient(string reply) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private static ExtractionRequest Request() => new(
        SystemPrompt: "sys", UserPrompt: "user",
        ToolName: "emit_spell_fields", ToolDescription: "d",
        ToolInputSchema: JsonDocument.Parse("{}").RootElement.Clone(),
        ModelId: "qwen3:8b", MaxOutputTokens: 100);

    [Fact]
    public async Task ExtractAsync_ValidJson_Succeeds()
    {
        var client = new OllamaEntityExtractionClient(
            new StubChatClient("""{"name":"Fireball"}"""),
            new PartialJsonRecoverer(),
            NullLogger<OllamaEntityExtractionClient>.Instance);

        var resp = await client.ExtractAsync(Request(), CancellationToken.None);

        resp.Success.Should().BeTrue();
        resp.ToolInput!.Value.GetProperty("name").GetString().Should().Be("Fireball");
    }

    [Fact]
    public async Task ExtractAsync_TruncatedJsonWithRecoverablePrefix_RecoversPartial()
    {
        var raw = """{"name":"Fireball","level":3} model kept talking""";
        var client = new OllamaEntityExtractionClient(
            new StubChatClient(raw),
            new PartialJsonRecoverer(),
            NullLogger<OllamaEntityExtractionClient>.Instance);

        var resp = await client.ExtractAsync(Request(), CancellationToken.None);

        resp.Success.Should().BeTrue();
        resp.ToolInput!.Value.GetProperty("level").GetInt32().Should().Be(3);
        resp.RawJson.Should().Be("""{"name":"Fireball","level":3}""");
    }

    [Fact]
    public async Task ExtractAsync_UnrecoverableGarbage_FailsAsBefore()
    {
        var client = new OllamaEntityExtractionClient(
            new StubChatClient("not json at all"),
            new PartialJsonRecoverer(),
            NullLogger<OllamaEntityExtractionClient>.Instance);

        var resp = await client.ExtractAsync(Request(), CancellationToken.None);

        resp.Success.Should().BeFalse();
        resp.ErrorMessage.Should().Contain("not valid JSON");
    }
}
```

- [x] **Step 4.2: Run tests to verify they fail**

Run: `dotnet test DndMcpAICsharpFun.Tests --nologo -v q --filter "FullyQualifiedName~OllamaEntityExtractionClientTests"`
Expected: build FAILURE — client constructor has no `PartialJsonRecoverer` parameter.

- [x] **Step 4.3: Modify the client**

Change the primary constructor and the `JsonException` catch block:

```csharp
public sealed class OllamaEntityExtractionClient(
    IChatClient chat,
    PartialJsonRecoverer recoverer,
    ILogger<OllamaEntityExtractionClient> logger) : IEntityExtractionLlmClient
```

In the `catch (JsonException ex)` block, before the existing failure return:

```csharp
            catch (JsonException ex)
            {
                if (recoverer.TryRecover(rawText, out var recovered))
                {
                    logger.LogWarning(
                        "Recovered partial JSON for model {Model}: {Recovered}/{Total} chars",
                        req.ModelId, recovered.Length, rawText.Length);
                    using var doc = JsonDocument.Parse(recovered);
                    return new ExtractionResponse(
                        Success: true, ToolInput: doc.RootElement.Clone(), StopReason: stopReason,
                        InputTokens: inputTokens, OutputTokens: outputTokens,
                        ErrorMessage: null, RawJson: recovered);
                }

                logger.LogWarning(
                    "Ollama returned non-JSON for model {Model}: {Err} — raw: {Raw}",
                    req.ModelId, ex.Message, rawText[..Math.Min(300, rawText.Length)]);
                return new ExtractionResponse(
                    Success: false, ToolInput: null, StopReason: stopReason,
                    InputTokens: inputTokens, OutputTokens: outputTokens,
                    ErrorMessage: $"Response was not valid JSON: {ex.Message}",
                    RawJson: rawText);
            }
```

Note: the happy-path `JsonDocument.Parse(rawText)` in the `try` block is NOT disposed in current code; leave it as is (cloned root). Only the new recovery parse uses `using`.

- [x] **Step 4.4: Register `PartialJsonRecoverer` in DI**

In `Extensions/ServiceCollectionExtensions.cs`, next to the existing line `services.AddSingleton<ExtractionPromptBuilder>();` (line ~144), add:

```csharp
        services.AddSingleton<PartialJsonRecoverer>();
        services.AddSingleton<SemanticChunker>();
        services.AddSingleton<EntityFieldMerger>();
```

(`SemanticChunker` / `EntityFieldMerger` registrations are used by Task 6.)

- [x] **Step 4.5: Run tests + full build**

Run: `dotnet test DndMcpAICsharpFun.Tests --nologo -v q --filter "FullyQualifiedName~OllamaEntityExtractionClientTests"`
Expected: 3 passed.
Run: `dotnet build --nologo -v q`
Expected: build OK (any other call sites of the client constructor fixed if the compiler complains).

- [x] **Step 4.6: Commit**

```bash
git add Features/Ingestion/EntityExtraction/OllamaEntityExtractionClient.cs Extensions/ServiceCollectionExtensions.cs DndMcpAICsharpFun.Tests/Entities/Extraction/OllamaEntityExtractionClientTests.cs
git commit -m "feat(extraction): recover truncated JSON in Ollama extraction client"
```

---

### Task 5: Few-shot examples in ExtractionPromptBuilder

**Files:**
- Create: `Schemas/examples/Spell.json`, `Monster.json`, `Trap.json`, `Item.json`, `MagicItem.json`, `Rule.json`, `God.json`, `Lore.json`
- Modify: `Features/Ingestion/EntityExtraction/ExtractionPromptBuilder.cs`
- Modify: `Features/Ingestion/EntityExtraction/EntityExtractionOptions.cs`
- Modify: `Extensions/ServiceCollectionExtensions.cs`
- Modify: `Config/appsettings.json`
- Test: extend `DndMcpAICsharpFun.Tests/Entities/Extraction/ExtractionPromptBuilderTests.cs`

- [x] **Step 5.1: Write the failing tests** (append to `ExtractionPromptBuilderTests.cs`)

```csharp
    [Fact]
    public void BuildSystemPrompt_WithExampleForType_IncludesExampleOutput()
    {
        var examples = new Dictionary<EntityType, string>
        {
            [EntityType.Spell] = """{"name":"Fireball","level":3,"school":"V"}""",
        };
        var b = new ExtractionPromptBuilder(examples);

        var prompt = b.BuildSystemPrompt("PHB", "5e", EntityType.Spell);

        prompt.Should().Contain("Example output:");
        prompt.Should().Contain("\"name\":\"Fireball\"");
    }

    [Fact]
    public void BuildSystemPrompt_WithoutExampleForType_OmitsExampleSection()
    {
        var b = new ExtractionPromptBuilder();

        var prompt = b.BuildSystemPrompt("PHB", "5e", EntityType.Spell);

        prompt.Should().NotContain("Example output:");
    }
```

- [x] **Step 5.2: Run tests to verify they fail**

Run: `dotnet test DndMcpAICsharpFun.Tests --nologo -v q --filter "FullyQualifiedName~ExtractionPromptBuilderTests"`
Expected: build FAILURE — no constructor taking examples.

- [x] **Step 5.3: Modify ExtractionPromptBuilder**

Add an optional constructor parameter and a static loader; existing `new ExtractionPromptBuilder()` call sites stay valid.

```csharp
public sealed class ExtractionPromptBuilder(
    IReadOnlyDictionary<EntityType, string>? examples = null)
{
    private readonly IReadOnlyDictionary<EntityType, string> _examples =
        examples ?? new Dictionary<EntityType, string>();
```

At the END of `BuildSystemPrompt`, just before `return sb.ToString();`:

```csharp
        if (_examples.TryGetValue(type, out var example))
        {
            sb.AppendLine();
            sb.AppendLine("Example output:");
            sb.AppendLine(example);
        }

        return sb.ToString();
```

Add the static loader method to the class:

```csharp
    /// <summary>
    /// Loads one example JSON file per entity type from <paramref name="directory"/>
    /// (file name = exact EntityType name, e.g. "Spell.json"). Missing files are
    /// skipped silently. A missing directory yields an empty dictionary.
    /// </summary>
    public static IReadOnlyDictionary<EntityType, string> LoadExamples(string directory)
    {
        var result = new Dictionary<EntityType, string>();
        if (!Directory.Exists(directory)) return result;

        foreach (var type in Enum.GetValues<EntityType>())
        {
            var path = Path.Combine(directory, type + ".json");
            if (File.Exists(path))
                result[type] = File.ReadAllText(path).Trim();
        }
        return result;
    }
```

- [x] **Step 5.4: Add option + config + DI factory**

`EntityExtractionOptions.cs` — add property:

```csharp
    public string ExamplesDirectory { get; set; } = "Schemas/examples";
```

`Config/appsettings.json` — inside the existing `"EntityExtraction"` section add:

```json
    "ExamplesDirectory": "Schemas/examples",
```

`Extensions/ServiceCollectionExtensions.cs` — replace `services.AddSingleton<ExtractionPromptBuilder>();` with:

```csharp
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<EntityExtractionOptions>>().Value;
            return new ExtractionPromptBuilder(ExtractionPromptBuilder.LoadExamples(opts.ExamplesDirectory));
        });
```

- [x] **Step 5.5: Create the example files** (5etools-format, one compact entity each)

`Schemas/examples/Spell.json`:

```json
{"name":"Fireball","level":3,"school":"V","time":[{"number":1,"unit":"action"}],"range":{"type":"point","distance":{"type":"feet","amount":150}},"components":{"v":true,"s":true,"m":"a tiny ball of bat guano and sulfur"},"duration":[{"type":"instant"}],"entries":["A bright streak flashes from your pointing finger to a point you choose within range and then blossoms with a low roar into an explosion of flame. Each creature in a 20-foot-radius sphere centered on that point must make a Dexterity saving throw, taking {@damage 8d6} fire damage on a failed save, or half as much on a successful one."]}
```

`Schemas/examples/Monster.json`:

```json
{"name":"Goblin","size":["S"],"type":{"type":"humanoid","tags":["goblinoid"]},"alignment":["N","E"],"ac":[{"ac":15,"from":["leather armor","shield"]}],"hp":{"average":7,"formula":"2d6"},"speed":{"walk":30},"str":8,"dex":14,"con":10,"int":10,"wis":8,"cha":8,"skill":{"stealth":"+6"},"senses":["darkvision 60 ft."],"cr":"1/4","trait":[{"name":"Nimble Escape","entries":["The goblin can take the Disengage or Hide action as a bonus action on each of its turns."]}],"action":[{"name":"Scimitar","entries":["{@atk mw} +4 to hit, reach 5 ft., one target. Hit: {@damage 1d6+2} slashing damage."]}],"keywords":["Nimble Escape"]}
```

`Schemas/examples/Trap.json`:

```json
{"name":"Pit Trap","trapHazType":"MECH","threat":"setback","entries":["A simple pit dug in the ground, covered by a large cloth anchored on the pit's edge and camouflaged with dirt and debris."],"variants":[{"name":"Hidden Pit","entries":["The pit's cover is hidden. A {@dc 15} Wisdom (Perception) check spots it."]},{"name":"Spiked Pit","entries":["Sharpened spikes line the bottom: {@damage 2d10} piercing damage on a fall."]}],"detectDc":15,"disarmDc":10}
```

`Schemas/examples/Item.json`:

```json
{"name":"Rope, Hempen (50 feet)","type":"G","weight":10,"value":100,"entries":["Rope, whether made of hemp or silk, has 2 hit points and can be burst with a {@dc 17} Strength check."]}
```

`Schemas/examples/MagicItem.json`:

```json
{"name":"Sword of Sharpness","baseItem":"longsword|PHB","type":"M","rarity":"very rare","reqAttune":true,"entries":["When you attack an object with this magic sword and hit, maximize your weapon damage dice against the target.","When you attack a creature with this weapon and roll a 20 on the attack roll, that target takes an extra {@damage 4d6} slashing damage."],"variants":[{"name":"+1 Sword of Sharpness","entries":["You gain a +1 bonus to attack and damage rolls made with this weapon."]}]}
```

`Schemas/examples/Rule.json`:

```json
{"name":"Flanking","ruleType":"O","entries":["If you regularly use miniatures, flanking gives combatants a simple way to gain advantage on attack rolls against a common enemy.","A creature can't flank an enemy that it can't see.","When a creature and at least one of its allies are adjacent to an enemy and on opposite sides of the enemy's space, they flank that enemy, and each of them has advantage on melee attack rolls against that enemy."]}
```

`Schemas/examples/God.json`:

```json
{"name":"Bahamut","alignment":["L","G"],"domains":["Life","War"],"symbol":"Dragon's head in profile","pantheon":"Draconic","entries":["Bahamut, the Platinum Dragon, is the god of justice and nobility. He is worshipped by metallic dragons and good-aligned dragonborn."]}
```

`Schemas/examples/Lore.json`:

```json
{"name":"The Weave","loreType":"cosmology","entries":["The Weave is the interface between raw magic and the world. Casters tap the Weave to shape spells, and its keeper is the goddess Mystra. When the Weave was damaged in past ages, magic itself became wild and unreliable."]}
```

- [x] **Step 5.6: Run tests + build**

Run: `dotnet test DndMcpAICsharpFun.Tests --nologo -v q --filter "FullyQualifiedName~ExtractionPromptBuilderTests"`
Expected: all pass (existing + 2 new).
Run: `dotnet build --nologo -v q`
Expected: OK.

Also verify the example files are valid JSON:

```bash
for f in Schemas/examples/*.json; do python3 -m json.tool "$f" > /dev/null || echo "INVALID: $f"; done
```

Expected: no output.

- [x] **Step 5.7: Verify example files are copied to output** (they're read at runtime relative to CWD like `Schemas/canonical`; check how `SchemasDirectory` files are handled in the `.csproj` and mirror it)

Check: `grep -n "Schemas" DndMcpAICsharpFun.csproj` — if `Schemas/canonical` files have a `CopyToOutputDirectory` entry, add the same for `Schemas/examples/*.json`; if not (runtime reads from project root), do nothing.

- [x] **Step 5.8: Commit**

```bash
git add Schemas/examples/ Features/Ingestion/EntityExtraction/ExtractionPromptBuilder.cs Features/Ingestion/EntityExtraction/EntityExtractionOptions.cs Extensions/ServiceCollectionExtensions.cs Config/appsettings.json DndMcpAICsharpFun.Tests/Entities/Extraction/ExtractionPromptBuilderTests.cs DndMcpAICsharpFun.csproj
git commit -m "feat(extraction): inject few-shot examples into extraction system prompt"
```

---

### Task 6: Chunked extraction loop in EntityExtractionOrchestrator

**Files:**
- Modify: `Features/Ingestion/EntityExtraction/EntityExtractionOptions.cs` (add `MaxTokensPerChunk`)
- Modify: `Config/appsettings.json` (add `MaxTokensPerChunk`)
- Modify: `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs`
- Test: extend `DndMcpAICsharpFun.Tests/Entities/Extraction/EntityExtractionOrchestratorTests.cs`

- [x] **Step 6.1: Read the existing orchestrator tests first**

Read `DndMcpAICsharpFun.Tests/Entities/Extraction/EntityExtractionOrchestratorTests.cs` fully — it shows how the orchestrator is constructed in tests (fake `IEntityExtractionLlmClient`, etc.). The constructor gains two parameters (`SemanticChunker chunker`, `EntityFieldMerger merger`); every test construction site must pass `new SemanticChunker()` / `new EntityFieldMerger()`.

- [x] **Step 6.2: Write the failing test** (append to `EntityExtractionOrchestratorTests.cs`, following the file's existing fake/builder patterns)

```csharp
    [Fact]
    public async Task ExtractAsync_OversizedCandidate_SplitsIntoChunksAndMergesResults()
    {
        // Arrange a candidate whose text is far over the chunk budget, with two
        // paragraph-separated sections, and a fake LLM that returns a different
        // partial object per call. Use the file's existing test harness pattern
        // (fake IEntityExtractionLlmClient capturing requests, temp canonical dir).
        // Set options.MaxTokensPerChunk = 150 so the ~800-char text splits in two.
        //
        // Assert:
        //   1. the fake LLM received 2 calls for that candidate (chunking happened)
        //   2. the canonical JSON written contains ONE entity whose fields include
        //      data from both partial responses (merging happened)
    }
```

(The implementer writes this concretely against the existing harness in the file — the harness types are internal to the test file; reuse its fakes verbatim. The two partial responses should be `{"name":"Pit Trap","entries":["from chunk one"]}` and `{"entries":["from chunk two"],"trapHazType":"MECH"}`; the merged assertion checks the entity's fields contain both `"from chunk one"`, `"from chunk two"`, and `"trapHazType":"MECH"`.)

- [x] **Step 6.3: Run test to verify it fails**

Run: `dotnet test DndMcpAICsharpFun.Tests --nologo -v q --filter "FullyQualifiedName~EntityExtractionOrchestratorTests"`
Expected: build FAILURE (constructor params) or test FAILURE (single LLM call, no merge).

- [x] **Step 6.4: Add the option**

`EntityExtractionOptions.cs`:

```csharp
    public int MaxTokensPerChunk { get; set; } = 2000;
```

`Config/appsettings.json` — inside `"EntityExtraction"`:

```json
    "MaxTokensPerChunk": 2000,
```

- [x] **Step 6.5: Modify the orchestrator**

Add constructor parameters after `ExtractionRetryPolicy retry`:

```csharp
    SemanticChunker chunker,
    EntityFieldMerger merger,
```

Add a private helper that both extraction paths call (place it near `StripConfidence`):

```csharp
    /// <summary>
    /// Runs the LLM over the candidate, chunking oversized text and merging
    /// partial results. Returns null when every chunk failed; ErrorMessage
    /// carries the last failure. Mirrors the single-call behaviour for small
    /// candidates (single chunk → single call, no merge).
    /// </summary>
    private async Task<(JsonElement? Fields, string? ErrorMessage)> ExtractCandidateFieldsAsync(
        DndMcpAICsharpFun.Infrastructure.Sqlite.IngestionRecord record,
        EntityCandidate candidate,
        JsonElement schema,
        CancellationToken ct)
    {
        var chunks = chunker.Split(candidate.Text, _opts.MaxTokensPerChunk);
        var partials = new List<JsonElement>();
        var chunkFailures = new List<int>();
        string? lastError = null;

        for (int c = 0; c < chunks.Count; c++)
        {
            var chunkCandidate = chunks.Count == 1 ? candidate : candidate with { Text = chunks[c] };
            var request = new ExtractionRequest(
                SystemPrompt:    promptBuilder.BuildSystemPrompt(record.DisplayName, record.Version, candidate.Type),
                UserPrompt:      promptBuilder.BuildUserPrompt(chunkCandidate),
                ToolName:        promptBuilder.ToolName(candidate.Type),
                ToolDescription: promptBuilder.ToolDescription(candidate.Type),
                ToolInputSchema: schema,
                ModelId:         _ollama.ChatModel,
                MaxOutputTokens: _opts.MaxOutputTokensPerEntity);

            var response = await retry.ExecuteAsync(
                operation: (_, c2) => llm.ExtractAsync(request, c2),
                isSuccess: r => r.Success,
                ct);

            if (response.Success && response.ToolInput is not null)
            {
                partials.Add(response.ToolInput.Value);
            }
            else
            {
                chunkFailures.Add(c);
                lastError = response.ErrorMessage;
            }
        }

        if (partials.Count == 0)
            return (null, lastError ?? "all chunks failed");

        if (chunkFailures.Count > 0)
            logger.LogWarning(
                "Partial extraction for {Type} '{Name}': chunks [{Failed}] failed, {Ok}/{Total} ok",
                candidate.Type, candidate.DisplayName,
                string.Join(',', chunkFailures), partials.Count, chunks.Count);

        var merged = partials.Count == 1 ? partials[0] : merger.Merge(partials);
        return (merged, null);
    }
```

In `RunFullExtractionAsync`, replace the `var request = new ExtractionRequest(...)` + `var response = await retry.ExecuteAsync(...)` pair and adjust the failure/success blocks:

```csharp
            var (fieldsResult, extractError) = await ExtractCandidateFieldsAsync(record, candidate, schema, ct);

            if (fieldsResult is null)
            {
                logger.LogWarning(
                    "Extraction failed for {Type} '{Name}' (page {Page}): {Error}",
                    candidate.Type, candidate.DisplayName, candidate.Page, extractError);
                extractionErrors.Add(new ExtractionErrorEntry(
                    SourceEntityId: id,
                    FieldPath: "(extraction)",
                    MissingTargetId: string.Empty,
                    ErrorKind: "extraction_failure",
                    Detail: extractError));
                failed++;
                processed++;
                doneIds.Add(id);

                if (processed % _opts.CheckpointIntervalCandidates == 0)
                    await WriteCheckpointAsync(checkpointPath, checkpointErrorsPath, extracted, extractionErrors);

                continue;
            }

            var rawInput = fieldsResult.Value;
```

(the lines from `string? confidence = ...` onward stay exactly as they are).

In `RunErrorsOnlyAsync`, make the identical replacement: swap the `var request = ...` + `var response = ...` pair for `ExtractCandidateFieldsAsync`, replace `response.ToolInput!.Value` with `fieldsResult.Value` and `response.ErrorMessage` with `extractError` in the corresponding failure/success blocks.

- [x] **Step 6.6: Fix test construction sites**

Every `new EntityExtractionOrchestrator(...)`-style construction in `EntityExtractionOrchestratorTests.cs` gains `chunker: new SemanticChunker(), merger: new EntityFieldMerger(),` (match the file's argument style).

- [x] **Step 6.7: Run the extraction test suite**

Run: `dotnet test DndMcpAICsharpFun.Tests --nologo -v q --filter "FullyQualifiedName~Extraction"`
Expected: all pass, including the new chunking test.

- [x] **Step 6.8: Commit**

```bash
git add Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs Features/Ingestion/EntityExtraction/EntityExtractionOptions.cs Config/appsettings.json DndMcpAICsharpFun.Tests/Entities/Extraction/EntityExtractionOrchestratorTests.cs
git commit -m "feat(extraction): chunked split-and-merge extraction loop in orchestrator"
```

---

### Task 7: Full verification

- [x] **Step 7.1: Run the complete test suite**

Run: `dotnet test --nologo -v q`
Expected: 0 failed across both test projects (370+ existing tests plus the ~20 new ones).

- [x] **Step 7.2: Verify no API contract changes**

This change adds no endpoints, so `DndMcpAICsharpFun.http` and `dnd-mcp-api.insomnia.json` need no updates. Confirm: `git diff --stat | grep -E '\.http|insomnia'` → no output.

- [x] **Step 7.3: Check off tasks + update change docs**

Mark all checkboxes in this plan complete.

- [x] **Step 7.4: Final commit (if any stragglers)**

```bash
git status --short   # should be clean except plan checkbox updates
git add openspec/changes/extraction-pipeline-improvements/plan.md
git commit -m "docs(openspec): complete extraction-pipeline-improvements plan"
```

---

## Self-Review Notes

- **Spec coverage:** SemanticChunker (Task 1), EntityFieldMerger (Task 2), PartialJsonRecoverer (Tasks 3–4), few-shot examples (Task 5), options + wiring + orchestrator loop (Tasks 5–6) — every design section is covered. "What Does Not Change" is honoured: scanner, checkpoints, retry policy semantics (per-chunk), writer, endpoints untouched.
- **Type consistency:** `ExtractionRequest`/`ExtractionResponse` field names match the existing records; `EntityCandidate` is a record (so `candidate with { Text = ... }` works); orchestrator helper returns `(JsonElement?, string?)`.
- **Known judgement point (Task 6):** the design's snippet logs per-candidate partial failures; the helper keeps the same retry policy per chunk call as today per candidate. This multiplies worst-case LLM calls by chunk count — acceptable per design ("Retry policy — applied per chunk call").
