# Structured Entity Extraction — Plan 2: LLM Extraction Pipeline

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Series tracking:** This is **Plan 2 of 3** of the `structured-entity-extraction` change. Plan 1 shipped (commit `5426634`); see `series.md`. Plan 3 (backfill / rollout) follows.

**Goal:** Replace hand-authored canonical JSON with an LLM-driven extraction pipeline (local Ollama, `qwen3:8b`) that consumes Docling output, emits canonical JSON conforming to per-type JSON Schemas, and exposes a corpus-wide validate endpoint.

**Architecture:** A new `Features/Ingestion/EntityExtraction/` area hosts the extraction orchestrator, an `IEntityExtractionLlmClient` abstraction with an Ollama-backed implementation (via `Microsoft.Extensions.AI` `IChatClient`), an extraction prompt builder, and an atomic-write canonical JSON producer. NJsonSchema generates per-type JSON Schemas at startup from the `<Type>Fields` records (Plan 1) and persists them to disk; those schemas are passed to Ollama as a **`format` constraint** (structured output) so the model is constrained to emit conformant JSON. The reference resolver gains intra-book/inter-book classification per Plan 2's design D15. A new admin endpoint runs corpus-wide validation on demand.

**Tech Stack:** .NET 10, ASP.NET Core minimal API, System.Text.Json, **NJsonSchema 11.x** (schema generation), **Microsoft.Extensions.AI.Ollama** (`IChatClient` against `http://ollama:11434`), xUnit, NSubstitute, Serena for all code edits.

**Spec coverage in this plan:** `entity-extraction-pipeline` spec (full, including the new corpus-validate requirement), the remaining parts of `ingestion-pipeline` delta (`ExtractEntities` work-item type, status transitions for extraction phases).

---

## Architectural decisions made up-front

These are locked from `design.md` Plan 2 decisions D13–D17 — do not relitigate them in implementation.

- **D13 — LLM (amended 2026-05-06):** Local Ollama at `http://ollama:11434`, model `qwen3:8b`. SDK abstraction: `Microsoft.Extensions.AI` `IChatClient` via `Microsoft.Extensions.AI.Ollama` NuGet. Schema constraint: Ollama `format` field (JSON schema object). Escalation model removed — all retries use the same `qwen3:8b`. Implementation behind `IEntityExtractionLlmClient` (interface unchanged). See `design-amendment-meai-ollama.md`.
- **D14 — Schemas:** NJsonSchema generates `Schemas/canonical/<TypeName>Fields.schema.json` for each `<Type>Fields` record. Generation runs at build time (MSBuild target) into the source tree (committed). Hand-overrides allowed for any single type that comes out ugly — when overridden, suppress regeneration for that file via a `// SCHEMA-OVERRIDE` marker.
- **D15 — Reference integrity:** intra-book dangles fail (errors.json + entity excluded); inter-book dangles warn (warnings.json + entity intact). New `POST /admin/canonical/validate` endpoint scans whole corpus.
- **D16 — Errata:** no special tooling — `?force=true` re-extracts and `git diff` is the review tool.
- **D17 — One coherent slice:** this plan is the whole Plan 2 scope.

---

## File structure

### Schema generation (new)
- `Build/GenerateCanonicalSchemas.targets` — MSBuild target that runs the schema generator on build
- `Tools/SchemaGenerator/SchemaGenerator.csproj` — small console tool that uses NJsonSchema to emit `Schemas/canonical/<Type>Fields.schema.json`
- `Tools/SchemaGenerator/Program.cs` — generator entry point
- `Schemas/canonical/.gitignore` — exclude nothing (committed)
- `Schemas/canonical/*.schema.json` — generated artifacts (committed; 20 files for the 20 entity types)

### LLM client abstraction (new)
- `Features/Ingestion/EntityExtraction/IEntityExtractionLlmClient.cs` — interface
- `Features/Ingestion/EntityExtraction/OllamaEntityExtractionClient.cs` — `IEntityExtractionLlmClient` impl; depends on `IChatClient` from MEAI
- `Features/Ingestion/EntityExtraction/ExtractionRequest.cs` — typed request record
- `Features/Ingestion/EntityExtraction/ExtractionResponse.cs` — typed response record (raw JSON + parsed fields + token counts)
- `Infrastructure/Ollama/OllamaOptions.cs` — **modify**: add `ChatModel` property (alongside existing `BaseUrl` and `EmbeddingModel`)

### Extraction pipeline (new)
- `Features/Ingestion/EntityExtraction/IEntityExtractionOrchestrator.cs` — interface
- `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs` — main orchestrator
- `Features/Ingestion/EntityExtraction/ExtractionPromptBuilder.cs` — per-type prompt construction
- `Features/Ingestion/EntityExtraction/EntityCandidateScanner.cs` — scans Docling output to find candidate entity-typed text chunks
- `Features/Ingestion/EntityExtraction/EntityCandidate.cs` — record `(EntityType, string DisplayName, string Text, int? Page)`
- `Features/Ingestion/EntityExtraction/ExtractionRetryPolicy.cs` — bounded-retry logic for schema-violating output
- `Features/Ingestion/EntityExtraction/CanonicalJsonWriter.cs` — temp-file + atomic-rename writer
- `Features/Ingestion/EntityExtraction/ExtractionErrorsFile.cs` — sibling errors.json writer
- `Features/Ingestion/EntityExtraction/ExtractionWarningsFile.cs` — sibling warnings.json writer
- `Features/Ingestion/EntityExtraction/IntraBookReferenceClassifier.cs` — given current book's slug prefix, classify a dangling ref as intra/inter-book

### Reference resolver upgrade (modify)
- `Features/Entities/EntityReferenceResolver.cs` — add overload that accepts a `currentBookSlug` and partitions warnings into intra-book FAIL vs inter-book WARN

### Docling cache reuse (modify)
- `Features/Ingestion/Pdf/IDoclingPdfConverter.cs` — review and confirm caching surface; if no cache exists, add a per-book in-memory cache with the file hash as key
- (Possibly new) `Features/Ingestion/Pdf/DoclingOutputCache.cs` — singleton cache mapping `fileHash → DoclingDocument`

### Status / queue (modify)
- `Features/Ingestion/IIngestionQueue.cs` — add `ExtractEntities` enum value
- `Features/Ingestion/IngestionQueueWorker.cs` — dispatch the new work item type
- `Infrastructure/Sqlite/IngestionStatus.cs` — confirm `EntitiesExtracting`, `EntitiesExtracted`, `EntitiesFailed` exist; add if missing
- `Features/Ingestion/Tracking/IIngestionTracker.cs` — add `MarkEntitiesExtractingAsync`, `MarkEntitiesExtractedAsync`, `MarkEntitiesFailedAsync`
- `Features/Ingestion/Tracking/SqliteIngestionTracker.cs` — implementations

### Admin endpoints (modify + new)
- `Features/Admin/BooksAdminEndpoints.cs` — add `POST /admin/books/{id}/extract-entities` with `?force=true`
- `Features/Admin/CanonicalValidationEndpoints.cs` — new file; `POST /admin/canonical/validate`
- `Features/Admin/CanonicalValidationService.cs` — implementation: load all canonical JSON, run resolver, return structured report
- `Features/Admin/CanonicalValidationReport.cs` — DTO

### Config + DI (modify)
- `Config/appsettings.json` — add `ChatModel` to existing `Ollama` section; add `EntityExtraction` section; no `Anthropic` section
- `Extensions/ServiceCollectionExtensions.cs` — register the new services; wire `IChatClient` via MEAI Ollama
- `Program.cs` — wire endpoint mappings
- `docker-compose.yml` — `ollama-pull` entrypoint pulls both `mxbai-embed-large` and `qwen3:8b`
- `DndMcpAICsharpFun.csproj` — add `Microsoft.Extensions.AI.Ollama` NuGet package

### HTTP reference (modify)
- `DndMcpAICsharpFun.http` — add example requests for `extract-entities` and `canonical/validate`

### Tests (new)
- `DndMcpAICsharpFun.Tests/Entities/Extraction/ExtractionPromptBuilderTests.cs`
- `DndMcpAICsharpFun.Tests/Entities/Extraction/IntraBookReferenceClassifierTests.cs`
- `DndMcpAICsharpFun.Tests/Entities/Extraction/EntityCandidateScannerTests.cs`
- `DndMcpAICsharpFun.Tests/Entities/Extraction/CanonicalJsonWriterTests.cs`
- `DndMcpAICsharpFun.Tests/Entities/Extraction/EntityExtractionOrchestratorTests.cs` — uses a fake `IEntityExtractionLlmClient`
- `DndMcpAICsharpFun.Tests/Entities/Admin/ExtractEntitiesEndpointTests.cs`
- `DndMcpAICsharpFun.Tests/Entities/Admin/CanonicalValidationEndpointTests.cs`
- `DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs` — confirms a generated schema validates a known-good fixture

### Fixtures (new)
- `DndMcpAICsharpFun.Tests/Fixtures/extraction/sample-monster-stat-block.txt` — raw Docling-shaped input
- `DndMcpAICsharpFun.Tests/Fixtures/extraction/expected-monster.json` — expected canonical output

### CLAUDE.md (modify) — note that LLM extraction has landed and the operator workflow.

---

## Conventions (carried over from Plan 1)

- **All file edits via Serena.** Built-in Read/Edit forbidden on project files. Bash grep on project files forbidden.
- **Run tests after each task that touches code.** `dotnet test` from repo root.
- **One commit per task.** Stage only the files the task touches; never `git add -A`.
- **Worktree:** create one before Task 1 if not already inside one (`.worktrees/sxe2`).

---

## Task 1: NJsonSchema package + SchemaGenerator console tool

**Files:**
- Create: `Tools/SchemaGenerator/SchemaGenerator.csproj`
- Create: `Tools/SchemaGenerator/Program.cs`
- Modify: `DndMcpAICsharpFun.sln` (add the new project)

- [ ] **Step 1: Create the SchemaGenerator csproj (via Serena `create_text_file`)**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>DndMcpAICsharpFun.Tools.SchemaGenerator</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NJsonSchema" Version="11.0.2" />
    <ProjectReference Include="..\..\DndMcpAICsharpFun.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create Program.cs (via Serena)**

```csharp
using System.Reflection;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using NJsonSchema;
using NJsonSchema.Generation;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: SchemaGenerator <output-directory>");
    return 1;
}

var outputDir = args[0];
Directory.CreateDirectory(outputDir);

// All <Type>Fields records live in DndMcpAICsharpFun.Domain.Entities.Fields
var fieldsAssembly = typeof(ClassFields).Assembly;
var fieldsTypes = fieldsAssembly.GetTypes()
    .Where(t => t.Namespace == "DndMcpAICsharpFun.Domain.Entities.Fields"
                && t.IsClass && !t.IsAbstract
                && t.Name.EndsWith("Fields", StringComparison.Ordinal))
    .OrderBy(t => t.Name)
    .ToList();

var settings = new SystemTextJsonSchemaGeneratorSettings
{
    SerializerOptions = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web),
    GenerateAbstractProperties = false,
    AlwaysAllowAdditionalObjectProperties = false,
};

var generator = new JsonSchemaGenerator(settings);

foreach (var t in fieldsTypes)
{
    var schema = generator.Generate(t);
    var path = Path.Combine(outputDir, $"{t.Name}.schema.json");

    if (File.Exists(path) && File.ReadAllText(path).Contains("// SCHEMA-OVERRIDE", StringComparison.Ordinal))
    {
        Console.WriteLine($"SKIP (override): {t.Name}");
        continue;
    }

    var json = schema.ToJson();
    File.WriteAllText(path, json);
    Console.WriteLine($"WROTE: {t.Name}");
}

return 0;
```

- [ ] **Step 3: Add to solution**

Run: `dotnet sln add Tools/SchemaGenerator/SchemaGenerator.csproj`

- [ ] **Step 4: Build to verify**

Run: `dotnet build`
Expected: PASS.

- [ ] **Step 5: Run the generator manually to seed the schemas**

```bash
mkdir -p Schemas/canonical
dotnet run --project Tools/SchemaGenerator -- Schemas/canonical
ls Schemas/canonical/
```

Expected: 20 `.schema.json` files (one per `<Type>Fields` record).

- [ ] **Step 6: Smoke-test one of them**

Open `Schemas/canonical/MonsterFields.schema.json` via Serena `read_file` and verify it has `"type": "object"` at root and `"properties"` containing `size`, `type`, `challengeRating`, etc.

- [ ] **Step 7: Commit**

```bash
git add Tools/SchemaGenerator/ Schemas/canonical/ DndMcpAICsharpFun.sln
git commit -m "feat(schemas): NJsonSchema generator + 20 per-type entity schemas"
```

---

## Task 2: MSBuild target to keep schemas regenerated

**Files:**
- Create: `Build/GenerateCanonicalSchemas.targets`
- Modify: `DndMcpAICsharpFun.csproj`

- [ ] **Step 1: Create the MSBuild targets file (via Serena)**

```xml
<Project>
  <Target Name="GenerateCanonicalSchemas"
          BeforeTargets="BeforeBuild"
          Condition="'$(SkipCanonicalSchemaGen)' != 'true'">
    <Exec Command="dotnet run --project &quot;$(MSBuildThisFileDirectory)..\Tools\SchemaGenerator\SchemaGenerator.csproj&quot; -- &quot;$(MSBuildThisFileDirectory)..\Schemas\canonical&quot;"
          IgnoreExitCode="false"
          WorkingDirectory="$(MSBuildThisFileDirectory).." />
  </Target>
</Project>
```

- [ ] **Step 2: Import the targets in the main csproj (via Serena `replace_content`)**

Find `</Project>` in `DndMcpAICsharpFun.csproj` and insert:

```xml
  <Import Project="Build/GenerateCanonicalSchemas.targets" />
```

right before it.

- [ ] **Step 3: Test by deleting one schema file and rebuilding**

```bash
rm Schemas/canonical/SpellFields.schema.json
dotnet build /p:SkipCanonicalSchemaGen=false
ls Schemas/canonical/SpellFields.schema.json
```

Expected: file is regenerated by the build.

- [ ] **Step 4: Add a test that the generated schemas validate the test fixture**

Create `DndMcpAICsharpFun.Tests/Entities/Schemas/SchemaGenerationTests.cs`:

```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using FluentAssertions;
using NJsonSchema;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Schemas;

public class SchemaGenerationTests
{
    [Theory]
    [InlineData("test-book.class.fighter", "ClassFields")]
    [InlineData("test-book.monster.bullywug", "MonsterFields")]
    [InlineData("test-book.spell.fireball", "SpellFields")]
    public async Task Generated_schema_validates_fixture_entity(string entityId, string schemaName)
    {
        // Arrange — load fixture and locate the entity
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "canonical", "test-book.json");
        var loader = new CanonicalJsonLoader();
        var file = await loader.LoadAsync(fixturePath, CancellationToken.None);
        var entity = file.Entities.Single(e => e.Id == entityId);

        // Locate schema (relative to repo root in dev; copied next to test bin via csproj item below)
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "Schemas", "canonical", $"{schemaName}.schema.json");
        File.Exists(schemaPath).Should().BeTrue($"expected schema at {schemaPath}");

        var schema = await JsonSchema.FromFileAsync(schemaPath);
        var fieldsJson = entity.Fields.GetRawText();
        var errors = schema.Validate(fieldsJson);
        errors.Should().BeEmpty(string.Join(", ", errors.Select(e => e.ToString())));
    }
}
```

Add a copy item to `DndMcpAICsharpFun.Tests.csproj` (via Serena `replace_content`):

```xml
<ItemGroup>
  <None Include="..\Schemas\canonical\*.schema.json"
        Link="Schemas\canonical\%(FileName)%(Extension)"
        CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

(NJsonSchema package is also needed in the test project: `dotnet add DndMcpAICsharpFun.Tests package NJsonSchema`.)

- [ ] **Step 5: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~SchemaGenerationTests"`
Expected: 3 PASS.

Run full suite: `dotnet test` — expect 160/160.

- [ ] **Step 6: Commit**

```bash
git add Build/ DndMcpAICsharpFun.csproj DndMcpAICsharpFun.Tests/Entities/Schemas/ DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj
git commit -m "feat(schemas): MSBuild target to regenerate schemas + fixture validation tests"
```

---

## Task 3: OllamaEntityExtractionClient + IEntityExtractionLlmClient (MEAI+Ollama)

**Files:**
- Create: `Features/Ingestion/EntityExtraction/IEntityExtractionLlmClient.cs`
- Create: `Features/Ingestion/EntityExtraction/OllamaEntityExtractionClient.cs`
- Create: `Features/Ingestion/EntityExtraction/ExtractionRequest.cs`
- Create: `Features/Ingestion/EntityExtraction/ExtractionResponse.cs`
- Modify: `Infrastructure/Ollama/OllamaOptions.cs` — add `ChatModel` property
- Modify: `Config/appsettings.json` — add `ChatModel` to `Ollama` section
- Modify: `Extensions/ServiceCollectionExtensions.cs` — register `IChatClient` + `IEntityExtractionLlmClient`
- Modify: `DndMcpAICsharpFun.csproj` — add `Microsoft.Extensions.AI.Ollama` NuGet
- Modify: `docker-compose.yml` — `ollama-pull` pulls `qwen3:8b` alongside `mxbai-embed-large`
- Modify: `Program.cs` — wire `AddEntityExtraction`

- [ ] **Step 1: Add the NuGet package**

```bash
dotnet add DndMcpAICsharpFun.csproj package Microsoft.Extensions.AI.Ollama
```

Expected: package added, `dotnet build` still passes.

- [ ] **Step 2: Extend OllamaOptions.cs (via Serena `insert_after_symbol`)**

Read `Infrastructure/Ollama/OllamaOptions.cs` first. It currently has `BaseUrl` and `EmbeddingModel`. Add `ChatModel`:

```csharp
public string ChatModel { get; set; } = "qwen3:8b";
```

The complete file after edit:

```csharp
namespace DndMcpAICsharpFun.Infrastructure.Ollama;

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string EmbeddingModel { get; set; } = "mxbai-embed-large";
    public string ChatModel { get; set; } = "qwen3:8b";
}
```

- [ ] **Step 3: Update appsettings.json (via Serena `replace_content`)**

Find the existing `"Ollama"` section in `Config/appsettings.json` and add `ChatModel`:

```json
"Ollama": {
  "BaseUrl": "http://localhost:11434",
  "EmbeddingModel": "mxbai-embed-large",
  "ChatModel": "qwen3:8b"
}
```

There is **no** `"Anthropic"` section — all LLM inference is local.

- [ ] **Step 4: Create ExtractionRequest.cs (via Serena)**

```csharp
using System.Text.Json;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed record ExtractionRequest(
    string SystemPrompt,
    string UserPrompt,
    string ToolName,             // e.g. "emit_class_fields" — describes the JSON object expected
    string ToolDescription,      // human-readable description passed in the prompt
    JsonElement ToolInputSchema, // the per-type JSON schema; passed as Ollama format constraint
    string ModelId,
    int MaxOutputTokens);
```

- [ ] **Step 5: Create ExtractionResponse.cs (via Serena)**

```csharp
using System.Text.Json;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed record ExtractionResponse(
    bool Success,
    JsonElement? ToolInput,      // parsed JSON fields — present when Success
    string? StopReason,          // e.g. "stop", "length"
    int InputTokens,
    int OutputTokens,
    string? ErrorMessage,
    string? RawJson);            // raw model output for debugging
```

- [ ] **Step 6: Create IEntityExtractionLlmClient.cs (via Serena)**

```csharp
namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public interface IEntityExtractionLlmClient
{
    Task<ExtractionResponse> ExtractAsync(ExtractionRequest request, CancellationToken ct);
}
```

- [ ] **Step 7: Create OllamaEntityExtractionClient.cs (via Serena)**

```csharp
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class OllamaEntityExtractionClient(
    IChatClient chat,
    ILogger<OllamaEntityExtractionClient> logger) : IEntityExtractionLlmClient
{
    public async Task<ExtractionResponse> ExtractAsync(ExtractionRequest req, CancellationToken ct)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, req.SystemPrompt),
            new(ChatRole.User, req.UserPrompt),
        };

        var chatOptions = new ChatOptions
        {
            ModelId = req.ModelId,
            MaxOutputTokens = req.MaxOutputTokens,
            ResponseFormat = ChatResponseFormat.ForJsonSchema(req.ToolInputSchema),
        };

        try
        {
            var response = await chat.CompleteAsync(messages, chatOptions, ct);
            var rawText = response.Message.Text ?? string.Empty;
            var inputTokens = response.Usage?.InputTokenCount ?? 0;
            var outputTokens = response.Usage?.OutputTokenCount ?? 0;
            var stopReason = response.FinishReason?.Value;

            if (string.IsNullOrWhiteSpace(rawText))
            {
                return new ExtractionResponse(
                    Success: false, ToolInput: null, StopReason: stopReason,
                    InputTokens: inputTokens, OutputTokens: outputTokens,
                    ErrorMessage: "Empty response from Ollama",
                    RawJson: null);
            }

            try
            {
                var doc = JsonDocument.Parse(rawText);
                return new ExtractionResponse(
                    Success: true, ToolInput: doc.RootElement.Clone(), StopReason: stopReason,
                    InputTokens: inputTokens, OutputTokens: outputTokens,
                    ErrorMessage: null, RawJson: rawText);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(
                    "Ollama returned non-JSON for model {Model}: {Err} — raw: {Raw}",
                    req.ModelId, ex.Message, rawText[..Math.Min(300, rawText.Length)]);
                return new ExtractionResponse(
                    Success: false, ToolInput: null, StopReason: stopReason,
                    InputTokens: inputTokens, OutputTokens: outputTokens,
                    ErrorMessage: $"Response was not valid JSON: {ex.Message}",
                    RawJson: rawText);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Ollama chat request failed for model {Model}", req.ModelId);
            return new ExtractionResponse(
                Success: false, ToolInput: null, StopReason: null,
                InputTokens: 0, OutputTokens: 0,
                ErrorMessage: ex.Message,
                RawJson: null);
        }
    }
}
```

**Note on `ChatResponseFormat.ForJsonSchema`:** This method is in `Microsoft.Extensions.AI` and accepts a `JsonElement`. The MEAI Ollama adapter translates it to the Ollama `format` field (structured output). If the MEAI version in the project uses a different overload shape, use `new ChatResponseFormatJson(req.ToolInputSchema)` instead and verify compilation.

- [ ] **Step 8: Register in DI (via Serena)**

Modify `Extensions/ServiceCollectionExtensions.cs`. Add a new `AddEntityExtraction(IConfiguration)` extension method (or add to the existing one if present):

```csharp
using Microsoft.Extensions.AI;

public static IServiceCollection AddEntityExtraction(this IServiceCollection services, IConfiguration configuration)
{
    // IChatClient for Ollama (MEAI) — used by OllamaEntityExtractionClient
    services.AddSingleton<IChatClient>(sp =>
    {
        var opts = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
        return new OllamaChatClient(new Uri(opts.BaseUrl), opts.ChatModel);
    });
    services.AddSingleton<IEntityExtractionLlmClient, OllamaEntityExtractionClient>();
    return services;
}
```

`OllamaOptions` is already bound in the existing Ollama DI registration — confirm via Serena `get_symbols_overview` on `ServiceCollectionExtensions.cs`; if it is bound there, skip re-binding. If it isn't, add `services.Configure<OllamaOptions>(configuration.GetSection("Ollama"));` to the top of `AddEntityExtraction`.

Wire from `Program.cs`:

```csharp
builder.Services.AddEntityExtraction(builder.Configuration);
```

- [ ] **Step 9: Update docker-compose.yml `ollama-pull` (via Serena)**

Find the `ollama-pull` service entrypoint in `docker-compose.yml` and change the pull command from:

```yaml
        "ollama pull mxbai-embed-large",
```

to:

```yaml
        "ollama pull mxbai-embed-large && ollama pull qwen3:8b",
```

The full entrypoint block becomes:

```yaml
    entrypoint:
      [
        "/bin/sh",
        "-c",
        "ollama pull mxbai-embed-large && ollama pull qwen3:8b",
      ]
```

- [ ] **Step 10: Build**

```bash
dotnet build
```

Expected: PASS. No references to `AnthropicMessagesClient`, `AnthropicOptions`, or `HttpClient<AnthropicMessagesClient>` anywhere.

- [ ] **Step 11: Commit**

```bash
git add Features/Ingestion/EntityExtraction/IEntityExtractionLlmClient.cs \
        Features/Ingestion/EntityExtraction/OllamaEntityExtractionClient.cs \
        Features/Ingestion/EntityExtraction/ExtractionRequest.cs \
        Features/Ingestion/EntityExtraction/ExtractionResponse.cs \
        Infrastructure/Ollama/OllamaOptions.cs \
        Config/appsettings.json \
        Extensions/ServiceCollectionExtensions.cs \
        DndMcpAICsharpFun.csproj \
        docker-compose.yml \
        Program.cs
git commit -m "feat(extraction): IEntityExtractionLlmClient + OllamaEntityExtractionClient via MEAI IChatClient"
```

---

## Task 4: ExtractionPromptBuilder

**Files:**
- Create: `Features/Ingestion/EntityExtraction/ExtractionPromptBuilder.cs`
- Create: `DndMcpAICsharpFun.Tests/Entities/Extraction/ExtractionPromptBuilderTests.cs`

- [ ] **Step 1: Write tests**

```csharp
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public class ExtractionPromptBuilderTests
{
    [Fact]
    public void Builds_system_prompt_naming_book_and_type()
    {
        var b = new ExtractionPromptBuilder();
        var prompt = b.BuildSystemPrompt("Player's Handbook 2014", "Edition2014", EntityType.Class);
        prompt.Should().Contain("Player's Handbook 2014").And.Contain("Edition2014").And.Contain("Class");
        prompt.Should().Contain("emit_class_fields");
    }

    [Fact]
    public void User_prompt_includes_candidate_text_verbatim()
    {
        var b = new ExtractionPromptBuilder();
        var candidate = new EntityCandidate(EntityType.Monster, "Bullywug", "Bullywug stat block text...", Page: 35);
        var prompt = b.BuildUserPrompt(candidate);
        prompt.Should().Contain("Bullywug").And.Contain("Bullywug stat block text...");
    }

    [Fact]
    public void Tool_name_is_lowercase_snake_case()
    {
        new ExtractionPromptBuilder().ToolName(EntityType.MagicItem).Should().Be("emit_magic_item_fields");
        new ExtractionPromptBuilder().ToolName(EntityType.DiseasePoison).Should().Be("emit_disease_poison_fields");
    }
}
```

- [ ] **Step 2: Run failing**

Run: `dotnet test --filter "FullyQualifiedName~ExtractionPromptBuilderTests"`
Expected: compile error (builder + EntityCandidate missing).

- [ ] **Step 3: Create EntityCandidate.cs (via Serena)**

```csharp
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed record EntityCandidate(
    EntityType Type,
    string DisplayName,
    string Text,
    int? Page);
```

- [ ] **Step 4: Create ExtractionPromptBuilder.cs (via Serena)**

```csharp
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
        sb.AppendLine("Cross-entity references must use existing slug-style IDs of form `<book-slug>.<type-slug>.<entity-slug>`.");
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
        // CamelCase enum name -> snake_case tool name, prefixed `emit_` and suffixed `_fields`.
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

- [ ] **Step 5: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~ExtractionPromptBuilderTests"`
Expected: 3 PASS.

- [ ] **Step 6: Commit**

```bash
git add Features/Ingestion/EntityExtraction/EntityCandidate.cs \
        Features/Ingestion/EntityExtraction/ExtractionPromptBuilder.cs \
        DndMcpAICsharpFun.Tests/Entities/Extraction/ExtractionPromptBuilderTests.cs
git commit -m "feat(extraction): prompt builder + EntityCandidate record"
```

---

## Task 5: IntraBookReferenceClassifier

**Files:**
- Create: `Features/Ingestion/EntityExtraction/IntraBookReferenceClassifier.cs`
- Create: `DndMcpAICsharpFun.Tests/Entities/Extraction/IntraBookReferenceClassifierTests.cs`

- [ ] **Step 1: Write tests**

```csharp
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public class IntraBookReferenceClassifierTests
{
    [Fact]
    public void Same_book_prefix_is_intra_book()
    {
        var c = new IntraBookReferenceClassifier("phb14");
        c.IsIntraBook("phb14.subclass.battle-master").Should().BeTrue();
    }

    [Fact]
    public void Different_book_prefix_is_inter_book()
    {
        var c = new IntraBookReferenceClassifier("phb14");
        c.IsIntraBook("tasha.subclass.swashbuckler").Should().BeFalse();
    }

    [Fact]
    public void Partition_returns_intra_and_inter_lists()
    {
        var c = new IntraBookReferenceClassifier("phb14");
        var refs = new[]
        {
            new EntityReferenceWarning("phb14.class.fighter", "fields.subclasses[0]", "phb14.subclass.battle-master"),
            new EntityReferenceWarning("phb14.class.fighter", "fields.subclasses[1]", "tasha.subclass.psi-warrior"),
        };
        var (intra, inter) = c.Partition(refs);
        intra.Should().ContainSingle(r => r.MissingTargetId == "phb14.subclass.battle-master");
        inter.Should().ContainSingle(r => r.MissingTargetId == "tasha.subclass.psi-warrior");
    }
}
```

- [ ] **Step 2: Run failing**

Run: `dotnet test --filter "FullyQualifiedName~IntraBookReferenceClassifierTests"`
Expected: compile errors.

- [ ] **Step 3: Create IntraBookReferenceClassifier.cs (via Serena)**

```csharp
using DndMcpAICsharpFun.Features.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class IntraBookReferenceClassifier(string currentBookSlug)
{
    private readonly string _prefix = currentBookSlug + ".";

    public bool IsIntraBook(string targetId) =>
        targetId.StartsWith(_prefix, StringComparison.Ordinal);

    public (IList<EntityReferenceWarning> Intra, IList<EntityReferenceWarning> Inter) Partition(
        IEnumerable<EntityReferenceWarning> warnings)
    {
        var intra = new List<EntityReferenceWarning>();
        var inter = new List<EntityReferenceWarning>();
        foreach (var w in warnings)
        {
            if (IsIntraBook(w.MissingTargetId)) intra.Add(w);
            else inter.Add(w);
        }
        return (intra, inter);
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~IntraBookReferenceClassifierTests"`
Expected: 3 PASS.

- [ ] **Step 5: Commit**

```bash
git add Features/Ingestion/EntityExtraction/IntraBookReferenceClassifier.cs \
        DndMcpAICsharpFun.Tests/Entities/Extraction/IntraBookReferenceClassifierTests.cs
git commit -m "feat(extraction): intra/inter-book reference classifier"
```

---

## Task 6: EntityCandidateScanner — Docling output → candidate entities

**Files:**
- Create: `Features/Ingestion/EntityExtraction/EntityCandidateScanner.cs`
- Create: `DndMcpAICsharpFun.Tests/Entities/Extraction/EntityCandidateScannerTests.cs`

- [ ] **Step 1: Read existing Docling output types via Serena**

Use Serena `find_symbol` to locate `DoclingDocument` (Plan 1's existing type at `Features/Ingestion/Pdf/DoclingDocument.cs`) and read it. The scanner consumes that type plus the Plan 1 `TocCategoryMap` (built from bookmarks) to identify candidate entities.

- [ ] **Step 2: Write tests**

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Extraction; // TocCategoryMap
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public class EntityCandidateScannerTests
{
    [Fact]
    public void Scanner_groups_by_section_and_maps_category_to_entity_type()
    {
        // Build a minimal mock: 3 sections — one categorised Monster, one Spell, one Lore (skipped).
        var toc = new TocCategoryMapBuilder()
            .Add("Bullywug", page: 35, ContentCategory.Monster)
            .Add("Fireball", page: 241, ContentCategory.Spell)
            .Add("History of the realms", page: 12, ContentCategory.Lore)
            .Build();

        var blocks = new List<DummyBlock>
        {
            new("Bullywug", page: 35, "Bullywug stat block text"),
            new("Fireball", page: 241, "Fireball spell text"),
            new("History of the realms", page: 12, "Long lore prose"),
        };

        var scanner = new EntityCandidateScanner();
        var candidates = scanner.Scan(blocks.Select(b => new ScannerInput(b.Section, b.Page, b.Text)).ToList(), toc).ToList();

        candidates.Should().HaveCount(2);
        candidates.Should().Contain(c => c.Type == EntityType.Monster && c.DisplayName == "Bullywug");
        candidates.Should().Contain(c => c.Type == EntityType.Spell && c.DisplayName == "Fireball");
    }

    private sealed record DummyBlock(string Section, int Page, string Text);

    private sealed class TocCategoryMapBuilder
    {
        // Build a TocCategoryMap with the specified (section, page, category) entries.
        // Implementation: use the project's existing TocCategoryMap API.
        // Reader: read TocCategoryMap.cs to find its constructor / Add method shape.
        // ...
        public TocCategoryMapBuilder Add(string section, int page, ContentCategory category) { /* TODO */ return this; }
        public TocCategoryMap Build() { /* TODO */ throw new NotImplementedException(); }
    }
}
```

**Heads-up:** the test fixture builder above is sketched — read `Features/Ingestion/Extraction/TocCategoryMap.cs` (Plan 1) via Serena to see its public surface and adapt the builder. If `TocCategoryMap` has a constructor taking `IList<TocSectionEntry>` directly, just construct it inline.

- [ ] **Step 3: Run failing**

Run: `dotnet test --filter "FullyQualifiedName~EntityCandidateScannerTests"`
Expected: compile errors (scanner missing).

- [ ] **Step 4: Create ScannerInput.cs (via Serena, in the same EntityExtraction folder)**

```csharp
namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed record ScannerInput(string SectionTitle, int Page, string Text);
```

- [ ] **Step 5: Create EntityCandidateScanner.cs (via Serena)**

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class EntityCandidateScanner
{
    public IEnumerable<EntityCandidate> Scan(IList<ScannerInput> blocks, TocCategoryMap toc)
    {
        // Group blocks by section, concatenate text per section, map ContentCategory → EntityType,
        // emit one candidate per section that is entity-typed. Lore / Rule / Combat / Adventuring / Encounter sections are skipped.

        var bySection = blocks
            .GroupBy(b => b.SectionTitle)
            .Select(g => new
            {
                Section = g.Key,
                Page = g.Min(b => b.Page),
                Text = string.Join("\n\n", g.Select(b => b.Text)),
            });

        foreach (var s in bySection)
        {
            var category = toc.GetCategoryForSection(s.Section);
            var type = MapCategoryToEntityType(category);
            if (type is null) continue;

            yield return new EntityCandidate(type.Value, s.Section, s.Text, s.Page);
        }
    }

    private static EntityType? MapCategoryToEntityType(ContentCategory category) => category switch
    {
        ContentCategory.Spell      => EntityType.Spell,
        ContentCategory.Monster    => EntityType.Monster,
        ContentCategory.Class      => EntityType.Class,
        ContentCategory.Race       => EntityType.Race,
        ContentCategory.Background => EntityType.Background,
        ContentCategory.Item       => EntityType.Item,
        ContentCategory.Condition  => EntityType.Condition,
        ContentCategory.God        => EntityType.God,
        ContentCategory.Plane      => EntityType.Plane,
        ContentCategory.Treasure   => EntityType.MagicItem,
        ContentCategory.Trap       => EntityType.Trap,
        // Rule, Combat, Adventuring, Encounter, Trait, Lore, Unknown → not entities (skipped).
        _ => null,
    };
}
```

**Adapt:** `TocCategoryMap.GetCategoryForSection(string)` — the actual method name may differ. Read `TocCategoryMap.cs` and use the right method. If no such method exists, use whatever lookup is canonical (likely a property accessor or a `Lookup` method).

- [ ] **Step 6: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~EntityCandidateScannerTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add Features/Ingestion/EntityExtraction/EntityCandidateScanner.cs \
        Features/Ingestion/EntityExtraction/ScannerInput.cs \
        DndMcpAICsharpFun.Tests/Entities/Extraction/EntityCandidateScannerTests.cs
git commit -m "feat(extraction): EntityCandidateScanner maps Docling sections to typed candidates"
```

---

## Task 7: CanonicalJsonWriter — atomic temp + rename

**Files:**
- Create: `Features/Ingestion/EntityExtraction/CanonicalJsonWriter.cs`
- Create: `DndMcpAICsharpFun.Tests/Entities/Extraction/CanonicalJsonWriterTests.cs`

- [ ] **Step 1: Write tests**

```csharp
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public class CanonicalJsonWriterTests
{
    [Fact]
    public async Task Write_succeeds_atomically()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "test.json");
        try
        {
            var writer = new CanonicalJsonWriter();
            var file = new CanonicalJsonFile(
                SchemaVersion: "1",
                Book: new CanonicalBookMetadata("Test Book", "Edition2014", "deadbeef", "Test Book"),
                Entities: Array.Empty<EntityEnvelope>());

            await writer.WriteAsync(path, file, CancellationToken.None);

            File.Exists(path).Should().BeTrue();
            var content = await File.ReadAllTextAsync(path);
            content.Should().Contain("\"schemaVersion\": \"1\"");
            Directory.GetFiles(dir, "*.tmp").Should().BeEmpty();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Failed_write_leaves_no_partial_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "test.json");
        try
        {
            var writer = new CanonicalJsonWriter();
            // Create a directory at the target path so the rename fails
            Directory.CreateDirectory(path);

            var file = new CanonicalJsonFile("1",
                new CanonicalBookMetadata("X", "Edition2014", "h", "X"),
                Array.Empty<EntityEnvelope>());

            var act = () => writer.WriteAsync(path, file, CancellationToken.None).AsTask();
            await act.Should().ThrowAsync<Exception>();

            // Final file remains a directory; no .tmp leftover.
            Directory.GetFiles(dir, "*.tmp").Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            Directory.Delete(dir, true);
        }
    }
}
```

- [ ] **Step 2: Run failing**

Run: `dotnet test --filter "FullyQualifiedName~CanonicalJsonWriterTests"`
Expected: compile errors.

- [ ] **Step 3: Implement CanonicalJsonWriter.cs (via Serena)**

```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class CanonicalJsonWriter
{
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public async ValueTask WriteAsync(string path, CanonicalJsonFile file, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path) ?? ".";
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, file, WriteOptions, ct);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* swallow cleanup */ }
            throw;
        }
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~CanonicalJsonWriterTests"`
Expected: 2 PASS.

- [ ] **Step 5: Commit**

```bash
git add Features/Ingestion/EntityExtraction/CanonicalJsonWriter.cs \
        DndMcpAICsharpFun.Tests/Entities/Extraction/CanonicalJsonWriterTests.cs
git commit -m "feat(extraction): atomic canonical JSON writer (temp + rename)"
```

---

## Task 8: ExtractionErrorsFile + ExtractionWarningsFile — sibling artifacts

**Files:**
- Create: `Features/Ingestion/EntityExtraction/ExtractionErrorsFile.cs`
- Create: `Features/Ingestion/EntityExtraction/ExtractionWarningsFile.cs`

- [ ] **Step 1: Implement (via Serena)**

```csharp
// ExtractionErrorsFile.cs
using System.Text.Json;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed record ExtractionErrorEntry(
    string SourceEntityId,
    string FieldPath,
    string MissingTargetId,
    string ErrorKind,           // "intra_book_dangling_ref" | "schema_validation_failure"
    string? Detail);

public sealed class ExtractionErrorsFile
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public async Task WriteAsync(string path, IList<ExtractionErrorEntry> errors, CancellationToken ct)
    {
        if (errors.Count == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        var dir = Path.GetDirectoryName(path) ?? ".";
        Directory.CreateDirectory(dir);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, errors, JsonOptions, ct);
    }
}
```

```csharp
// ExtractionWarningsFile.cs
using System.Text.Json;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed record ExtractionWarningEntry(
    string SourceEntityId,
    string FieldPath,
    string MissingTargetId,
    string WarningKind);        // "inter_book_dangling_ref"

public sealed class ExtractionWarningsFile
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public async Task WriteAsync(string path, IList<ExtractionWarningEntry> warnings, CancellationToken ct)
    {
        if (warnings.Count == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        var dir = Path.GetDirectoryName(path) ?? ".";
        Directory.CreateDirectory(dir);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, warnings, JsonOptions, ct);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build` — expect PASS.

- [ ] **Step 3: Commit**

```bash
git add Features/Ingestion/EntityExtraction/ExtractionErrorsFile.cs \
        Features/Ingestion/EntityExtraction/ExtractionWarningsFile.cs
git commit -m "feat(extraction): sibling errors.json + warnings.json writers"
```

---

## Task 9: ExtractionRetryPolicy

**Files:**
- Create: `Features/Ingestion/EntityExtraction/ExtractionRetryPolicy.cs`

- [ ] **Step 1: Implement (via Serena)**

```csharp
namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class ExtractionRetryPolicy
{
    public int MaxAttempts { get; init; } = 3;
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(2);

    public async Task<T> ExecuteAsync<T>(
        Func<int, CancellationToken, Task<T>> operation,
        Func<T, bool> isSuccess,
        CancellationToken ct)
    {
        var lastResult = default(T)!;
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            lastResult = await operation(attempt, ct);
            if (isSuccess(lastResult)) return lastResult;
            if (attempt < MaxAttempts)
                await Task.Delay(BaseDelay * Math.Pow(2, attempt - 1), ct);
        }
        return lastResult;
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build` — expect PASS.

- [ ] **Step 3: Commit**

```bash
git add Features/Ingestion/EntityExtraction/ExtractionRetryPolicy.cs
git commit -m "feat(extraction): bounded retry policy with exponential backoff"
```

---

## Task 10: EntityExtractionOrchestrator — main extraction flow

**Files:**
- Create: `Features/Ingestion/EntityExtraction/IEntityExtractionOrchestrator.cs`
- Create: `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs`
- Create: `Features/Ingestion/EntityExtraction/EntityExtractionOptions.cs`
- Create: `DndMcpAICsharpFun.Tests/Entities/Extraction/EntityExtractionOrchestratorTests.cs`

This is the load-bearing task. Reads Docling output, drives the LLM client, validates output, classifies dangling refs, atomically writes the canonical JSON.

- [ ] **Step 1: Create EntityExtractionOptions.cs (via Serena)**

```csharp
namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class EntityExtractionOptions
{
    public string CanonicalDirectory { get; set; } = "data/canonical";
    public string SchemasDirectory { get; set; } = "Schemas/canonical";
    public int MaxRetriesPerEntity { get; set; } = 3;
    public int ProgressLogIntervalSeconds { get; set; } = 60;
}
```

- [ ] **Step 2: Create IEntityExtractionOrchestrator.cs (via Serena)**

```csharp
namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public interface IEntityExtractionOrchestrator
{
    Task ExtractAsync(int bookId, bool force, CancellationToken ct);
}
```

- [ ] **Step 3: Create EntityExtractionOrchestrator.cs (via Serena)**

This is a substantial file. Subagent: read Plan 1's `EntityIngestionOrchestrator` first to learn project conventions (logger usage, IIngestionTracker access, FileHash handling, slug derivation). Then implement:

```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.Ingestion.Extraction; // TocCategoryMap
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class EntityExtractionOrchestrator(
    IIngestionTracker tracker,
    IDoclingPdfConverter docling,
    IPdfBookmarkReader bookmarks,
    IEntityExtractionLlmClient llm,
    ExtractionPromptBuilder promptBuilder,
    EntityCandidateScanner scanner,
    CanonicalJsonWriter writer,
    ExtractionErrorsFile errorsFile,
    ExtractionWarningsFile warningsFile,
    EntityReferenceResolver refResolver,
    ExtractionRetryPolicy retry,
    IOptions<EntityExtractionOptions> options,
    IOptions<OllamaOptions> ollamaOpts,
    ILogger<EntityExtractionOrchestrator> logger) : IEntityExtractionOrchestrator
{
    private readonly EntityExtractionOptions _opts = options.Value;
    private readonly OllamaOptions _ollama = ollamaOpts.Value;

    public async Task ExtractAsync(int bookId, bool force, CancellationToken ct)
    {
        var record = await tracker.GetByIdAsync(bookId, ct)
                     ?? throw new InvalidOperationException($"No ingestion record {bookId}");

        var bookSlug = EntityIdSlug.For(record.DisplayName, EntityType.Class, "x").Split('.')[0];
        var canonicalPath = Path.Combine(_opts.CanonicalDirectory, $"{bookSlug}.json");
        var errorsPath = Path.Combine(_opts.CanonicalDirectory, $"{bookSlug}.errors.json");
        var warningsPath = Path.Combine(_opts.CanonicalDirectory, $"{bookSlug}.warnings.json");

        if (File.Exists(canonicalPath) && !force)
            throw new InvalidOperationException(
                $"Canonical JSON already exists at {canonicalPath}; pass force=true to overwrite");

        await tracker.MarkEntitiesExtractingAsync(bookId, ct);

        try
        {
            // 1. Layout: reuse Docling output by file hash if available; else convert.
            var doclingDoc = await docling.ConvertAsync(record.FilePath, ct);

            // 2. Read bookmarks → TOC → ContentCategory map.
            var bookmarkTree = await bookmarks.ReadBookmarksAsync(record.FilePath, ct);
            var toc = TocCategoryMap.FromBookmarks(bookmarkTree);
                // Adapt to actual TocCategoryMap construction shape after reading the file.

            // 3. Scan candidates from Docling blocks + TOC.
            var scannerInputs = doclingDoc.Items
                .Select(item => new ScannerInput(item.SectionTitle, item.PageNumber, item.Text))
                .ToList();
            var candidates = scanner.Scan(scannerInputs, toc).ToList();

            logger.LogInformation("Extraction candidates: {Count} for book {BookId}", candidates.Count, bookId);

            // 4. Load schemas keyed by EntityType.
            var schemaCache = LoadSchemas();

            var extracted = new List<EntityEnvelope>();
            var failedEntities = new List<ExtractionErrorEntry>();

            var startedAt = DateTimeOffset.UtcNow;
            var lastProgressLog = startedAt;
            int attempted = 0;

            foreach (var candidate in candidates)
            {
                ct.ThrowIfCancellationRequested();
                attempted++;

                if ((DateTimeOffset.UtcNow - lastProgressLog).TotalSeconds >= _opts.ProgressLogIntervalSeconds)
                {
                    logger.LogInformation(
                        "Extraction progress: {Done}/{Total} ({Pct}%) — book {BookId}",
                        attempted, candidates.Count, attempted * 100 / candidates.Count, bookId);
                    lastProgressLog = DateTimeOffset.UtcNow;
                }

                if (!schemaCache.TryGetValue(candidate.Type, out var schema))
                {
                    logger.LogWarning("No schema for {Type}; skipping {Name}", candidate.Type, candidate.DisplayName);
                    continue;
                }

                var envelope = await TryExtractEntityAsync(record, candidate, schema, bookSlug, ct);
                if (envelope is not null)
                    extracted.Add(envelope);
                else
                    failedEntities.Add(new ExtractionErrorEntry(
                        SourceEntityId: EntityIdSlug.For(record.DisplayName, candidate.Type, candidate.DisplayName),
                        FieldPath: "",
                        MissingTargetId: "",
                        ErrorKind: "schema_validation_failure",
                        Detail: $"Failed all retries on {candidate.Type} '{candidate.DisplayName}'"));
            }

            // 5. Reference resolution: classify intra/inter-book.
            var allWarnings = refResolver.Resolve(extracted).ToList();
            var classifier = new IntraBookReferenceClassifier(bookSlug);
            var (intra, inter) = classifier.Partition(allWarnings);

            // Drop intra-book offenders from extracted list.
            var droppedIds = intra.Select(w => w.SourceEntityId).ToHashSet(StringComparer.Ordinal);
            var clean = extracted.Where(e => !droppedIds.Contains(e.Id)).ToList();
            failedEntities.AddRange(intra.Select(w => new ExtractionErrorEntry(
                w.SourceEntityId, w.FieldPath, w.MissingTargetId,
                "intra_book_dangling_ref", null)));

            // 6. Write canonical JSON atomically.
            var canonical = new CanonicalJsonFile(
                SchemaVersion: CanonicalJsonSchema.CurrentVersion,
                Book: new CanonicalBookMetadata(record.DisplayName, record.Version, record.FileHash, record.DisplayName),
                Entities: clean);

            await writer.WriteAsync(canonicalPath, canonical, ct);
            await errorsFile.WriteAsync(errorsPath, failedEntities, ct);
            await warningsFile.WriteAsync(warningsPath, inter.Select(w => new ExtractionWarningEntry(
                w.SourceEntityId, w.FieldPath, w.MissingTargetId, "inter_book_dangling_ref")).ToList(), ct);

            // 7. Final summary.
            var elapsed = DateTimeOffset.UtcNow - startedAt;
            logger.LogInformation(
                "Extraction summary: book {BookId}, candidates {Cands}, extracted {Extracted}, intra-book failures {Intra}, inter-book warnings {Inter}, elapsed {Elapsed}",
                bookId, candidates.Count, clean.Count, intra.Count, inter.Count, elapsed);

            await tracker.MarkEntitiesExtractedAsync(bookId, clean.Count, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Extraction failed for book {BookId}", bookId);
            await tracker.MarkEntitiesFailedAsync(bookId, ex.Message, ct);
            throw;
        }
    }

    private async Task<EntityEnvelope?> TryExtractEntityAsync(
        Infrastructure.Sqlite.IngestionRecord record,
        EntityCandidate candidate,
        JsonElement schema,
        string bookSlug,
        CancellationToken ct)
    {
        var system = promptBuilder.BuildSystemPrompt(record.DisplayName, record.Version, candidate.Type);
        var user = promptBuilder.BuildUserPrompt(candidate);
        var toolName = promptBuilder.ToolName(candidate.Type);
        var toolDescription = promptBuilder.ToolDescription(candidate.Type);

        var request = new ExtractionRequest(
            SystemPrompt: system,
            UserPrompt: user,
            ToolName: toolName,
            ToolDescription: toolDescription,
            ToolInputSchema: schema,
            ModelId: _ollama.ChatModel,
            MaxOutputTokens: 4096);

        var response = await retry.ExecuteAsync(
            (attempt, c) => llm.ExtractAsync(request, c),
            r => r.Success,
            ct);

        if (!response.Success || response.ToolInput is not { } toolInput)
        {
            logger.LogWarning(
                "LLM extraction failed for {Type} '{Name}': {Err}",
                candidate.Type, candidate.DisplayName, response.ErrorMessage);
            return null;
        }

        // Build envelope around the extracted fields.
        var entityId = EntityIdSlug.For(record.DisplayName, candidate.Type, candidate.DisplayName);
        var envelope = new EntityEnvelope(
            Id: entityId,
            Type: candidate.Type,
            Name: candidate.DisplayName,
            SourceBook: record.DisplayName,
            Edition: record.Version,
            Page: candidate.Page,
            FirstAppearedIn: new FirstAppearance(record.DisplayName, record.Version),
            RevisedIn: Array.Empty<Revision>(),
            SettingTags: new[] { "core" },        // default; later refinement may set this differently
            CanonicalText: candidate.Text,         // overwritten downstream by canonicalText renderer at ingest time
            Fields: toolInput);

        return envelope;
    }

    private Dictionary<EntityType, JsonElement> LoadSchemas()
    {
        var dict = new Dictionary<EntityType, JsonElement>();
        foreach (var type in Enum.GetValues<EntityType>())
        {
            var path = Path.Combine(_opts.SchemasDirectory, $"{type}Fields.schema.json");
            if (!File.Exists(path)) continue;
            var json = File.ReadAllText(path);
            dict[type] = JsonDocument.Parse(json).RootElement.Clone();
        }
        return dict;
    }
}
```

**Adapt:** several APIs (`IDoclingPdfConverter.ConvertAsync`, `IPdfBookmarkReader.ReadBookmarksAsync`, `TocCategoryMap.FromBookmarks`, `DoclingDocument.Items`, `IngestionRecord.Version`) need to be matched against the actual signatures from Plan 1. Read each via Serena before writing the orchestrator. If a method's name or shape differs, adjust call sites — the *logic* (Docling → scanner → LLM loop → resolve → atomic write) is the spec.

- [ ] **Step 4: Write the test (uses fake LLM client)**

```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public class EntityExtractionOrchestratorTests
{
    [Fact]
    public async Task Extracts_one_monster_with_a_fake_llm_client()
    {
        // Arrange a minimal scenario: the orchestrator dependencies are mostly substituted.
        // Skipped here for brevity in the plan; subagent: build the WAF using Plan 1's
        // EntityIngestionOrchestratorTests as a template (NSubstitute + concrete services where simple).
        // Stub IEntityExtractionLlmClient to return a hand-crafted ExtractionResponse with a
        // valid MonsterFields tool input. Confirm:
        //   - The orchestrator writes data/canonical/<slug>.json with one entity.
        //   - tracker.MarkEntitiesExtractedAsync(bookId, 1, _) was called.
        //   - No errors.json file exists post-run.

        // Subagent: this test will need to construct fake DoclingDocument, IPdfBookmarkReader output,
        // and TocCategoryMap. Mirror the EntityIngestionOrchestratorTests pattern.
        Assert.True(true); // placeholder body; replace with real test
    }
}
```

The test placeholder is intentional — building this WAF is non-trivial. The subagent should look at `EntityIngestionOrchestratorTests` from Plan 1 as the closest template and produce a concrete test. The minimum verifications: orchestrator produces a non-empty canonical JSON, tracker progresses through `EntitiesExtracting → EntitiesExtracted`, no errors file is written, file is at the expected path.

If the subagent can't get a high-quality test in scope, mark as `DONE_WITH_CONCERNS` and we'll fix in a follow-up.

- [ ] **Step 5: Build + run tests**

Run: `dotnet build && dotnet test`
Expected: build clean. Tests at minimum compile and don't regress existing tests.

- [ ] **Step 6: Register in DI**

Modify `Extensions/ServiceCollectionExtensions.cs` `AddEntityExtraction` to add:

```csharp
services.Configure<EntityExtractionOptions>(configuration.GetSection("EntityExtraction"));
services.AddSingleton<ExtractionPromptBuilder>();
services.AddSingleton<EntityCandidateScanner>();
services.AddSingleton<CanonicalJsonWriter>();
services.AddSingleton<ExtractionErrorsFile>();
services.AddSingleton<ExtractionWarningsFile>();
services.AddSingleton<ExtractionRetryPolicy>();
services.AddScoped<IEntityExtractionOrchestrator, EntityExtractionOrchestrator>();
```

`OllamaOptions` is already configured in the existing Ollama DI registration; the orchestrator injects `IOptions<OllamaOptions>` — no extra `Configure<>` call needed here.

Add `EntityExtraction` section to `Config/appsettings.json`:

```json
"EntityExtraction": {
  "CanonicalDirectory": "data/canonical",
  "SchemasDirectory": "Schemas/canonical",
  "MaxRetriesPerEntity": 3,
  "ProgressLogIntervalSeconds": 60
}
```

- [ ] **Step 7: Commit**

```bash
git add Features/Ingestion/EntityExtraction/EntityExtractionOptions.cs \
        Features/Ingestion/EntityExtraction/IEntityExtractionOrchestrator.cs \
        Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs \
        DndMcpAICsharpFun.Tests/Entities/Extraction/EntityExtractionOrchestratorTests.cs \
        Extensions/ServiceCollectionExtensions.cs \
        Config/appsettings.json
git commit -m "feat(extraction): EntityExtractionOrchestrator end-to-end pipeline"
```

---

## Task 11: IngestionStatus + tracker methods for extraction phases

**Files:**
- Modify: `Infrastructure/Sqlite/IngestionStatus.cs` (verify enum has `EntitiesExtracting`, `EntitiesExtracted` — add if missing)
- Modify: `Features/Ingestion/Tracking/IIngestionTracker.cs`
- Modify: `Features/Ingestion/Tracking/SqliteIngestionTracker.cs`
- Modify: `Migrations/IngestionDbContextModelSnapshot.cs` (regenerate if needed)

- [ ] **Step 1: Read current `IngestionStatus.cs` via Serena**

Plan 1 added `EntitiesIngesting`, `EntitiesIngested`, `EntitiesFailed`. Plan 2 needs `EntitiesExtracting`, `EntitiesExtracted` for the *extraction* phase (extraction precedes ingestion in the pipeline). Add them if missing.

```csharp
public enum IngestionStatus
{
    Pending,
    Processing,
    Failed,
    Duplicate,
    JsonIngested,
    EntitiesExtracting,
    EntitiesExtracted,
    EntitiesIngesting,
    EntitiesIngested,
    EntitiesFailed,
}
```

- [ ] **Step 2: Extend `IIngestionTracker.cs`**

Add via Serena:
```csharp
Task MarkEntitiesExtractingAsync(int bookId, CancellationToken ct);
Task MarkEntitiesExtractedAsync(int bookId, int entityCount, CancellationToken ct);
Task MarkEntitiesFailedAsync(int bookId, string error, CancellationToken ct);
```

- [ ] **Step 3: Implement in `SqliteIngestionTracker.cs`**

```csharp
public async Task MarkEntitiesExtractingAsync(int bookId, CancellationToken ct)
{
    var record = await db.IngestionRecords.FindAsync([bookId], ct)
                 ?? throw new InvalidOperationException($"Record {bookId} not found");
    record.Status = IngestionStatus.EntitiesExtracting;
    await db.SaveChangesAsync(ct);
}

public async Task MarkEntitiesExtractedAsync(int bookId, int entityCount, CancellationToken ct)
{
    var record = await db.IngestionRecords.FindAsync([bookId], ct)
                 ?? throw new InvalidOperationException($"Record {bookId} not found");
    record.Status = IngestionStatus.EntitiesExtracted;
    record.EntityCount = entityCount;
    await db.SaveChangesAsync(ct);
}

public async Task MarkEntitiesFailedAsync(int bookId, string error, CancellationToken ct)
{
    var record = await db.IngestionRecords.FindAsync([bookId], ct)
                 ?? throw new InvalidOperationException($"Record {bookId} not found");
    record.Status = IngestionStatus.EntitiesFailed;
    record.Error = error;
    await db.SaveChangesAsync(ct);
}
```

- [ ] **Step 4: Build**

Run: `dotnet build` — expect PASS.

- [ ] **Step 5: Generate migration if any column change**

Status is stored as TEXT, so adding enum values is a no-op migration. Run:

```bash
dotnet ef migrations add AddEntityExtractionStatuses --project DndMcpAICsharpFun --startup-project DndMcpAICsharpFun --output-dir Migrations
```

Inspect the generated file. If empty, delete it. If non-empty (unexpected), keep.

- [ ] **Step 6: Run tests**

Run: `dotnet test` — expect all green.

- [ ] **Step 7: Commit**

```bash
git add Infrastructure/Sqlite/IngestionStatus.cs \
        Features/Ingestion/Tracking/IIngestionTracker.cs \
        Features/Ingestion/Tracking/SqliteIngestionTracker.cs \
        Migrations/  # only if migration kept
git commit -m "feat(ingestion): IngestionStatus + tracker methods for entity extraction phases"
```

---

## Task 12: IngestionWorkType.ExtractEntities + queue dispatch

**Files:**
- Modify: `Features/Ingestion/IIngestionQueue.cs`
- Modify: `Features/Ingestion/IngestionQueueWorker.cs`

- [ ] **Step 1: Extend enum (via Serena)**

```csharp
public enum IngestionWorkType { IngestBlocks, IngestEntities, ExtractEntities }
```

- [ ] **Step 2: Modify `IngestionQueueWorker` dispatch (via Serena)**

Add a case for `ExtractEntities` that resolves `IEntityExtractionOrchestrator` and calls `ExtractAsync(item.BookId, force, ct)`. The work item needs a `Force` field — extend `IngestionWorkItem`:

```csharp
public record IngestionWorkItem(IngestionWorkType Type, int BookId, bool Force = false);
```

Worker switch:
```csharp
case IngestionWorkType.ExtractEntities:
    await extractor.ExtractAsync(item.BookId, item.Force, ct);
    break;
```

- [ ] **Step 3: Build + test**

Run: `dotnet build && dotnet test` — expect PASS.

- [ ] **Step 4: Commit**

```bash
git add Features/Ingestion/IIngestionQueue.cs \
        Features/Ingestion/IngestionQueueWorker.cs
git commit -m "feat(ingestion): ExtractEntities work-item type + queue dispatch"
```

---

## Task 13: POST /admin/books/{id}/extract-entities endpoint

**Files:**
- Modify: `Features/Admin/BooksAdminEndpoints.cs`
- Create: `DndMcpAICsharpFun.Tests/Entities/Admin/ExtractEntitiesEndpointTests.cs`
- Modify: `DndMcpAICsharpFun.http`

- [ ] **Step 1: Add endpoint (via Serena)**

```csharp
admin.MapPost("/books/{id:int}/extract-entities", async (
    int id,
    bool force,
    IIngestionTracker tracker,
    IIngestionQueue queue,
    IOptions<EntityExtractionOptions> opts,
    CancellationToken ct) =>
{
    var record = await tracker.GetByIdAsync(id, ct);
    if (record is null) return Results.NotFound();
    if (record.Status == IngestionStatus.Processing
        || record.Status == IngestionStatus.EntitiesExtracting
        || record.Status == IngestionStatus.EntitiesIngesting)
        return Results.Conflict();

    var bookSlug = EntityIdSlug.For(record.DisplayName, EntityType.Class, "x").Split('.')[0];
    var canonicalPath = Path.Combine(opts.Value.CanonicalDirectory, $"{bookSlug}.json");
    if (File.Exists(canonicalPath) && !force) return Results.Conflict();

    queue.TryEnqueue(new IngestionWorkItem(IngestionWorkType.ExtractEntities, id, Force: force));
    return Results.Accepted($"/admin/books/{id}");
});
```

(`force` minimal-API binds from query string named `force`, so `?force=true` works.)

- [ ] **Step 2: Test (mirror the IngestEntities endpoint test pattern)**

```csharp
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

public class ExtractEntitiesEndpointTests
{
    [Fact]
    public async Task Returns_404_for_unknown_book()
    {
        // Build a tiny test app with substituted IIngestionTracker returning null for any id.
        // POST /admin/books/9999/extract-entities → expect 404.
        // Subagent: copy-adapt from IngestEntitiesEndpointTests (Plan 1), substituting endpoint path.
        Assert.True(true); // subagent: replace with real test
    }

    [Fact]
    public async Task Returns_409_when_canonical_exists_and_force_is_false()
    {
        Assert.True(true); // subagent: real test
    }

    [Fact]
    public async Task Returns_202_with_force_true_and_existing_canonical()
    {
        Assert.True(true); // subagent: real test
    }
}
```

Concrete tests should follow the existing `IngestEntitiesEndpointTests` template exactly.

- [ ] **Step 3: Update DndMcpAICsharpFun.http (via Serena)**

```http
### Extract entities (initial)
POST {{baseUrl}}/admin/books/{{bookId}}/extract-entities
X-Admin-Api-Key: {{adminKey}}

### Re-extract entities (force overwrite)
POST {{baseUrl}}/admin/books/{{bookId}}/extract-entities?force=true
X-Admin-Api-Key: {{adminKey}}
```

- [ ] **Step 4: Build + test**

Run: `dotnet build && dotnet test` — expect PASS.

- [ ] **Step 5: Commit**

```bash
git add Features/Admin/BooksAdminEndpoints.cs \
        DndMcpAICsharpFun.Tests/Entities/Admin/ExtractEntitiesEndpointTests.cs \
        DndMcpAICsharpFun.http
git commit -m "feat(admin): POST /admin/books/{id}/extract-entities (with ?force=true)"
```

---

## Task 14: CanonicalValidationService + endpoint

**Files:**
- Create: `Features/Admin/CanonicalValidationReport.cs`
- Create: `Features/Admin/CanonicalValidationService.cs`
- Create: `Features/Admin/CanonicalValidationEndpoints.cs`
- Create: `DndMcpAICsharpFun.Tests/Entities/Admin/CanonicalValidationEndpointTests.cs`
- Modify: `Program.cs` (map endpoint)
- Modify: `Extensions/ServiceCollectionExtensions.cs`
- Modify: `DndMcpAICsharpFun.http`

- [ ] **Step 1: CanonicalValidationReport.cs (via Serena)**

```csharp
namespace DndMcpAICsharpFun.Features.Admin;

public sealed record CanonicalValidationFailure(string File, string Kind, string Detail);

public sealed record CanonicalValidationWarning(string File, string SourceEntityId, string FieldPath, string MissingTargetId);

public sealed record CanonicalValidationReport(
    int FilesScanned,
    int TotalEntities,
    IReadOnlyList<CanonicalValidationFailure> Failures,
    IReadOnlyList<CanonicalValidationWarning> Warnings);
```

- [ ] **Step 2: CanonicalValidationService.cs (via Serena)**

```csharp
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Admin;

public sealed class CanonicalValidationService(
    CanonicalJsonLoader loader,
    EntityReferenceResolver resolver,
    IOptions<EntityExtractionOptions> options,
    ILogger<CanonicalValidationService> logger)
{
    private readonly EntityExtractionOptions _opts = options.Value;

    public async Task<CanonicalValidationReport> ValidateAsync(CancellationToken ct)
    {
        var failures = new List<CanonicalValidationFailure>();
        var warnings = new List<CanonicalValidationWarning>();
        var allEntities = new List<EntityEnvelope>();
        var seenIds = new Dictionary<string, string>(StringComparer.Ordinal);  // id → file

        if (!Directory.Exists(_opts.CanonicalDirectory))
            return new CanonicalValidationReport(0, 0, failures, warnings);

        var files = Directory.GetFiles(_opts.CanonicalDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Where(f => !f.EndsWith(".errors.json", StringComparison.Ordinal)
                     && !f.EndsWith(".warnings.json", StringComparison.Ordinal))
            .OrderBy(f => f)
            .ToList();

        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var loaded = await loader.LoadAsync(path, ct);

                foreach (var entity in loaded.Entities)
                {
                    if (seenIds.TryGetValue(entity.Id, out var existingFile))
                    {
                        failures.Add(new CanonicalValidationFailure(
                            File: Path.GetFileName(path),
                            Kind: "duplicate_id",
                            Detail: $"id '{entity.Id}' also defined in {Path.GetFileName(existingFile)}"));
                    }
                    else
                    {
                        seenIds[entity.Id] = path;
                    }
                }

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
                failures.Add(new CanonicalValidationFailure(
                    File: Path.GetFileName(path),
                    Kind: "load_error",
                    Detail: ex.Message));
            }
        }

        // Resolve references across the union; classify intra/inter-book by scanning each file's slug prefix.
        var refWarnings = resolver.Resolve(allEntities).ToList();
        foreach (var w in refWarnings)
        {
            // intra-book here would be a real bug — extraction should have caught it. Mark as failure.
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

        return new CanonicalValidationReport(files.Count, allEntities.Count, failures, warnings);
    }
}
```

- [ ] **Step 3: CanonicalValidationEndpoints.cs (via Serena)**

```csharp
namespace DndMcpAICsharpFun.Features.Admin;

public static class CanonicalValidationEndpoints
{
    public static WebApplication MapCanonicalValidationEndpoints(this WebApplication app)
    {
        app.MapPost("/admin/canonical/validate", async (
            CanonicalValidationService svc, CancellationToken ct) =>
        {
            var report = await svc.ValidateAsync(ct);
            return report.Failures.Count > 0
                ? Results.UnprocessableEntity(report)
                : Results.Ok(report);
        });
        return app;
    }
}
```

- [ ] **Step 4: Wire DI + endpoint mapping**

`Extensions/ServiceCollectionExtensions.cs` — add `services.AddSingleton<CanonicalValidationService>();` to `AddEntityExtraction`.

`Program.cs` — `app.MapCanonicalValidationEndpoints();` after `app.MapEntityRetrievalEndpoints();`.

- [ ] **Step 5: Test**

```csharp
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

public class CanonicalValidationEndpointTests
{
    [Fact]
    public async Task Empty_directory_returns_empty_report()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var svc = new CanonicalValidationService(
                new CanonicalJsonLoader(),
                new EntityReferenceResolver(),
                Options.Create(new EntityExtractionOptions { CanonicalDirectory = dir }),
                NullLogger<CanonicalValidationService>.Instance);

            var report = await svc.ValidateAsync(CancellationToken.None);
            report.FilesScanned.Should().Be(0);
            report.TotalEntities.Should().Be(0);
            report.Failures.Should().BeEmpty();
            report.Warnings.Should().BeEmpty();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Schema_version_mismatch_is_reported_as_failure()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(dir, "broken.json"),
                """{"schemaVersion":"99","book":{"sourceBook":"x","edition":"e","fileHash":"h","displayName":"x"},"entities":[]}""");

            var svc = new CanonicalValidationService(
                new CanonicalJsonLoader(),
                new EntityReferenceResolver(),
                Options.Create(new EntityExtractionOptions { CanonicalDirectory = dir }),
                NullLogger<CanonicalValidationService>.Instance);

            var report = await svc.ValidateAsync(CancellationToken.None);
            report.Failures.Should().ContainSingle(f => f.Kind == "schema_validation_failure" && f.File == "broken.json");
        }
        finally { Directory.Delete(dir, true); }
    }
}
```

- [ ] **Step 6: Update DndMcpAICsharpFun.http**

```http
### Validate the entire canonical corpus
POST {{baseUrl}}/admin/canonical/validate
X-Admin-Api-Key: {{adminKey}}
```

- [ ] **Step 7: Build + test + commit**

```bash
git add Features/Admin/CanonicalValidationReport.cs \
        Features/Admin/CanonicalValidationService.cs \
        Features/Admin/CanonicalValidationEndpoints.cs \
        Extensions/ServiceCollectionExtensions.cs \
        Program.cs \
        DndMcpAICsharpFun.http \
        DndMcpAICsharpFun.Tests/Entities/Admin/CanonicalValidationEndpointTests.cs
git commit -m "feat(admin): POST /admin/canonical/validate corpus-wide validation endpoint"
```

---

## Task 15: Replace seed phb14.json with extracted output (manual smoke)

The Plan 1 seed JSON was hand-authored. Plan 2 should validate end-to-end by re-extracting that book and reviewing the diff.

This task is **manual** — requires Docker stack + a real PDF + `qwen3:8b` pulled.

- [ ] **Step 1: Boot the stack**
```bash
./start.sh Development
```

The `ollama-pull` container will automatically pull `mxbai-embed-large` and `qwen3:8b` on first start. Watch it finish:
```bash
docker compose logs -f ollama-pull
```
Expected: both pulls complete, container exits 0.

- [ ] **Step 2: Verify Ollama has the chat model**
```bash
docker compose exec ollama ollama list
```
Expected output includes `qwen3:8b` and `mxbai-embed-large`. No API key required — all inference is local.

- [ ] **Step 3: Register a real PHB 2014 PDF**
```bash
curl -X POST http://localhost:5101/admin/books/register \
  -H "X-Admin-Api-Key: $ADMIN_KEY" \
  -F file=@/path/to/phb14.pdf \
  -F version=Edition2014 \
  -F displayName="Player's Handbook 2014" \
  -F bookType=Core
```

- [ ] **Step 4: Block-ingest first (to populate Docling cache)**
```bash
curl -X POST http://localhost:5101/admin/books/<bookId>/ingest-blocks \
  -H "X-Admin-Api-Key: $ADMIN_KEY"
```
Wait until status moves past `Processing`.

- [ ] **Step 5: Trigger extraction**
```bash
curl -X POST "http://localhost:5101/admin/books/<bookId>/extract-entities?force=true" \
  -H "X-Admin-Api-Key: $ADMIN_KEY"
```
Watch logs:
```bash
docker compose logs -f app | grep Extraction
```
Expected: progress logs every ~60s, summary at completion.

- [ ] **Step 6: Validate the corpus**
```bash
curl -X POST http://localhost:5101/admin/canonical/validate \
  -H "X-Admin-Api-Key: $ADMIN_KEY" | jq
```
Expected: 200 with empty `failures`, possibly some inter-book warnings.

- [ ] **Step 7: Review the diff**
```bash
git diff data/canonical/phb14.json
```
Compare to the hand-seeded version. The new extraction should have 100s of entities (every Spell, Class, Race, Background, Monster in PHB).

- [ ] **Step 8: Re-ingest entities into Qdrant**
```bash
curl -X POST http://localhost:5101/admin/books/<bookId>/ingest-entities \
  -H "X-Admin-Api-Key: $ADMIN_KEY"
```

- [ ] **Step 9: Spot-check the five canonical example queries** (from the project goal memory)
```bash
# 1. Multiclass cleric/fighter feats
curl "http://localhost:5101/retrieval/entities/search?q=feats+cleric+fighter+multiclass&type=Feat"

# 2. Amphibian monsters for level-5 party — needs MM ingested too; smoke is single-book
curl "http://localhost:5101/retrieval/entities/search?q=amphibian&type=Monster&keyword=amphibian"

# 3. Eberron gods — needs Eberron book; defer

# 4. Plan a swashbuckler rogue — Subclass under PHB or Tasha
curl "http://localhost:5101/retrieval/entities/search?q=swashbuckler&type=Subclass"

# 5. Which book introduced the artificer?
curl "http://localhost:5101/retrieval/entities/search?q=artificer&type=Class"
```

Each should return reasonable results.

- [ ] **Step 10: Commit the regenerated seed**
```bash
git add data/canonical/phb14.json data/canonical/phb14.warnings.json
git commit -m "data(canonical): regenerate phb14.json via Plan 2 extraction pipeline"
```

(`phb14.errors.json` should not exist if extraction was clean.)

---

## Task 16: Documentation update

**Files:**
- Modify: `CLAUDE.md`
- Modify: `README.md`

- [ ] **Step 1: Update CLAUDE.md (via Serena)**

In the "Structured Entity Extraction (vertical slice)" section added in Plan 1, update the LLM-extraction note from "will land in Plan 2" to a current-state description. Add the new endpoints:

```markdown
- `POST /admin/books/{id}/extract-entities` — run LLM-driven extraction (local Ollama `qwen3:8b`); produces `data/canonical/<book-slug>.json` plus optional sibling `<book-slug>.errors.json` and `<book-slug>.warnings.json`. Pass `?force=true` to overwrite an existing canonical JSON.
- `POST /admin/canonical/validate` — corpus-wide validation; 200 (clean) / 422 (FAIL-class issues).
```

Also add to the operator workflow:

```markdown
**Adding a new book (Plan 2 onward):**
1. `POST /admin/books/register` — upload PDF.
2. `POST /admin/books/{id}/ingest-blocks` — populate `dnd_blocks`.
3. `POST /admin/books/{id}/extract-entities` — produce canonical JSON (requires Ollama running with `qwen3:8b`).
4. Review the canonical JSON diff in a PR; hand-correct any LLM mistakes.
5. `POST /admin/canonical/validate` — pre-merge sanity check.
6. Merge.
7. `POST /admin/books/{id}/ingest-entities` — populate `dnd_entities`.
```

- [ ] **Step 2: Update README.md (via Serena)**

Add a brief mention under the existing companion-agent vision that LLM-driven extraction now exists, powered by local Ollama. One paragraph max — don't bloat the README.

Specifically in the configuration section, document:

```markdown
### Ollama models

Entity extraction uses `qwen3:8b` via local Ollama. The docker-compose stack pulls it automatically
(`ollama-pull` service). To pull manually:

```bash
docker compose run --rm ollama-pull
# or directly:
ollama pull qwen3:8b
```

No external API key is required. The model and base URL are set in `Config/appsettings.json` under the
`Ollama` section.
```

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md README.md
git commit -m "docs: document Plan 2 extraction pipeline + Ollama model setup"
```

---

## Plan 2 self-review

Before marking complete, verify against `entity-extraction-pipeline/spec.md`:

- ✅ `POST /admin/books/{id}/extract-entities` (Task 13)
- ✅ Canonical JSON written to `data/canonical/<book-slug>.json` (Task 7 + 10)
- ✅ Docling output reused, not re-run (Task 10 — caching is via the existing converter; if no cache exists in Plan 1, the orchestrator just calls convert once per extraction)
- ✅ Schema-constrained per-type extraction with errors-file output (Task 10)
- ✅ Progress + summary logging (Task 10)
- ✅ Cross-entity reference resolution split intra/inter-book (Tasks 5 + 10)
- ✅ Idempotent re-extraction with ?force=true (Task 13)
- ✅ Atomic writes (Task 7)
- ✅ NEW: corpus-wide `POST /admin/canonical/validate` (Task 14)

`ingestion-pipeline` delta extraction parts:
- ✅ `IngestionWorkType.ExtractEntities` work-item type (Task 12)
- ✅ Status track for `EntitiesExtracting` / `EntitiesExtracted` / `EntitiesFailed` (Task 11)

Type names consistent across tasks: `IEntityExtractionLlmClient`, `IEntityExtractionOrchestrator`, `EntityExtractionOptions`, `EntityCandidate`, `ExtractionRequest`, `ExtractionResponse`, `ExtractionPromptBuilder`, `EntityCandidateScanner`, `CanonicalJsonWriter`, `IntraBookReferenceClassifier`, `ExtractionRetryPolicy`, `CanonicalValidationService`, `CanonicalValidationReport`. ✅

No placeholders. Some tests in Tasks 10 and 13 have placeholder bodies marked as "subagent: replace with real test" — those are explicit, not silent.

---

## Execution

Per the project's persistent rule (`feedback_always_subagent_option1.md`), execute via `superpowers:subagent-driven-development` immediately on user go-ahead. Do not present a numbered choice.
