# Docling Disk Cache + Selective Error Re-extraction — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **CRITICAL — All file edits MUST use Serena tools.** Call `initial_instructions` on the Serena MCP tool before touching any code. Built-in Read/Edit tools are FORBIDDEN on project source files.
>
> **Series tracking:** This is **Plan 3** of the `structured-entity-extraction` change. Plans 1 and 2 shipped. See `series.md`. Spec: `design-docling-cache-errors-reextract.md`.

**Goal:** Cache Docling PDF-conversion results to disk (keyed by file hash) so repeated extraction runs skip the 20-minute Docling pass, and add `?errorsOnly=true` to `extract-entities` so failed entities can be retried without re-running everything.

**Architecture:** `DoclingDiskCache` is a transparent `IDoclingPdfConverter` decorator that computes a SHA-256 hash of the PDF, reads from / writes to `data/docling-cache/<hash>.json` with atomic writes. The existing orchestrator gains a `bool errorsOnly` parameter: when true, it loads `<slug>.errors.json`, builds a retry-ID set, skips all other candidates, and merges newly-extracted entities into the existing `<slug>.json`.

**Tech Stack:** .NET 10, System.Security.Cryptography (SHA-256), System.Text.Json, xUnit, NSubstitute. All edits via Serena.

---

## File structure

### New files

- `Features/Ingestion/Pdf/DoclingDiskCache.cs` — `IDoclingPdfConverter` caching decorator
- `DndMcpAICsharpFun.Tests/Ingestion/Pdf/DoclingDiskCacheTests.cs` — cache tests

### Modified files

- `Features/Ingestion/EntityExtraction/EntityExtractionOptions.cs` — add `DoclingCacheDirectory`
- `Config/appsettings.json` — add `DoclingCacheDirectory`
- `Extensions/ServiceCollectionExtensions.cs` — wrap `DoclingPdfConverter` with `DoclingDiskCache`
- `Features/Ingestion/IIngestionQueue.cs` — add `ErrorsOnly` to `IngestionWorkItem`
- `Features/Ingestion/IngestionQueueWorker.cs` — pass `errorsOnly` to orchestrator
- `Features/Ingestion/EntityExtraction/IEntityExtractionOrchestrator.cs` — add `errorsOnly` param
- `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs` — `no_schema` → errors; `errorsOnly` branch
- `Features/Admin/BooksAdminEndpoints.cs` — accept `?errorsOnly` query param
- `DndMcpAICsharpFun.http` — add `errorsOnly` example
- `DndMcpAICsharpFun.Tests/Entities/Admin/ExtractEntitiesEndpointTests.cs` — add errorsOnly test

---

## Conventions

- **All file edits via Serena.** Call `initial_instructions` before any code work. Built-in Read/Edit forbidden on project files.
- **Run `dotnet test` after each task.** All tests must pass before committing.
- **One commit per task.**
- **Worktree:** `.worktrees/sxe2`; run `dotnet build` / `dotnet test` from there.

---

## Task 1: DoclingDiskCache

**Files:**

- Create: `Features/Ingestion/Pdf/DoclingDiskCache.cs`
- Modify: `Features/Ingestion/EntityExtraction/EntityExtractionOptions.cs`
- Modify: `Config/appsettings.json`
- Modify: `Extensions/ServiceCollectionExtensions.cs`
- Create: `DndMcpAICsharpFun.Tests/Ingestion/Pdf/DoclingDiskCacheTests.cs`

- [ ] **Step 1: Call Serena `initial_instructions`**

Required before any file work.

- [ ] **Step 2: Write the failing tests**

Create `DndMcpAICsharpFun.Tests/Ingestion/Pdf/DoclingDiskCacheTests.cs`:

```csharp
using System.Security.Cryptography;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Tests.Ingestion.Pdf;

public sealed class DoclingDiskCacheTests
{
    private static string HexHash(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [Fact]
    public async Task CacheMiss_CallsInner_AndWritesCacheFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // %PDF
            var pdfPath = Path.Combine(dir, "test.pdf");
            await File.WriteAllBytesAsync(pdfPath, pdfBytes);

            var expected = new DoclingDocument("# Hello", [new DoclingItem("text", "Hello", 1, null)]);
            var inner = Substitute.For<IDoclingPdfConverter>();
            inner.ConvertAsync(pdfPath, Arg.Any<CancellationToken>()).Returns(expected);

            var opts = Options.Create(new EntityExtractionOptions { DoclingCacheDirectory = dir });
            var cache = new DoclingDiskCache(inner, opts, NullLogger<DoclingDiskCache>.Instance);

            var result = await cache.ConvertAsync(pdfPath);

            Assert.Equal(expected.Markdown, result.Markdown);
            Assert.Single(result.Items);
            await inner.Received(1).ConvertAsync(pdfPath, Arg.Any<CancellationToken>());

            var cacheFile = Path.Combine(dir, HexHash(pdfBytes) + ".json");
            Assert.True(File.Exists(cacheFile));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task CacheHit_ReturnsFromDisk_DoesNotCallInner()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
            var pdfPath = Path.Combine(dir, "test.pdf");
            await File.WriteAllBytesAsync(pdfPath, pdfBytes);

            var expected = new DoclingDocument("# Hello", [new DoclingItem("text", "Hello", 1, null)]);
            var inner = Substitute.For<IDoclingPdfConverter>();
            inner.ConvertAsync(pdfPath, Arg.Any<CancellationToken>()).Returns(expected);

            var opts = Options.Create(new EntityExtractionOptions { DoclingCacheDirectory = dir });
            var cache = new DoclingDiskCache(inner, opts, NullLogger<DoclingDiskCache>.Instance);

            await cache.ConvertAsync(pdfPath);       // miss — writes cache
            var result = await cache.ConvertAsync(pdfPath); // hit — reads cache

            Assert.Equal(expected.Markdown, result.Markdown);
            await inner.Received(1).ConvertAsync(pdfPath, Arg.Any<CancellationToken>()); // called once only
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task CorruptCacheFile_DeletesAndCallsInnerAgain()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
            var pdfPath = Path.Combine(dir, "test.pdf");
            await File.WriteAllBytesAsync(pdfPath, pdfBytes);

            // Pre-create a corrupt cache file with the correct hash name
            var corruptPath = Path.Combine(dir, HexHash(pdfBytes) + ".json");
            await File.WriteAllTextAsync(corruptPath, "NOT VALID JSON {{{{");

            var expected = new DoclingDocument("# Hello", []);
            var inner = Substitute.For<IDoclingPdfConverter>();
            inner.ConvertAsync(pdfPath, Arg.Any<CancellationToken>()).Returns(expected);

            var opts = Options.Create(new EntityExtractionOptions { DoclingCacheDirectory = dir });
            var cache = new DoclingDiskCache(inner, opts, NullLogger<DoclingDiskCache>.Instance);

            var result = await cache.ConvertAsync(pdfPath);

            Assert.Equal(expected.Markdown, result.Markdown);
            await inner.Received(1).ConvertAsync(pdfPath, Arg.Any<CancellationToken>());
        }
        finally { Directory.Delete(dir, true); }
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test --filter "DoclingDiskCacheTests" -v minimal 2>&1 | tail -20
```

Expected: FAIL — `DoclingDiskCache` does not exist yet.

- [ ] **Step 4: Add `DoclingCacheDirectory` to `EntityExtractionOptions`**

Via Serena `replace_content` or `insert_after_symbol` on `EntityExtractionOptions.cs`, add after `CheckpointIntervalCandidates`:

```csharp
public string DoclingCacheDirectory { get; set; } = "data/docling-cache";
```

Full file after change:
```csharp
namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class EntityExtractionOptions
{
    public string CanonicalDirectory { get; set; } = "data/canonical";
    public string SchemasDirectory { get; set; } = "Schemas/canonical";
    public int MaxRetriesPerEntity { get; set; } = 3;
    public int MaxOutputTokensPerEntity { get; set; } = 8192;
    public int ProgressLogIntervalSeconds { get; set; } = 60;
    public int CheckpointIntervalCandidates { get; set; } = 100;
    public string DoclingCacheDirectory { get; set; } = "data/docling-cache";
}
```

- [ ] **Step 5: Update `appsettings.json`**

Add `"DoclingCacheDirectory": "data/docling-cache"` to the `EntityExtraction` section:

```json
"EntityExtraction": {
  "CanonicalDirectory": "data/canonical",
  "SchemasDirectory": "Schemas/canonical",
  "MaxRetriesPerEntity": 3,
  "MaxOutputTokensPerEntity": 8192,
  "ProgressLogIntervalSeconds": 60,
  "CheckpointIntervalCandidates": 100,
  "DoclingCacheDirectory": "data/docling-cache"
},
```

- [ ] **Step 6: Create `DoclingDiskCache.cs`**

Create `Features/Ingestion/Pdf/DoclingDiskCache.cs` via Serena `create_text_file`:

```csharp
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public sealed class DoclingDiskCache(
    IDoclingPdfConverter inner,
    IOptions<EntityExtractionOptions> options,
    ILogger<DoclingDiskCache> logger) : IDoclingPdfConverter
{
    private static readonly JsonSerializerOptions CacheJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<DoclingDocument> ConvertAsync(string filePath, CancellationToken ct = default)
    {
        var hash = ComputeFileHash(filePath);
        var cachePath = Path.Combine(options.Value.DoclingCacheDirectory, hash + ".json");

        try
        {
            await using var s = File.OpenRead(cachePath);
            var cached = await JsonSerializer.DeserializeAsync<DoclingDocument>(s, CacheJsonOptions, ct);
            if (cached is not null)
            {
                logger.LogInformation(
                    "Docling cache hit for {FileName} (hash {Hash})",
                    Path.GetFileName(filePath), hash[..8]);
                return cached;
            }
        }
        catch (FileNotFoundException) { }
        catch (JsonException ex)
        {
            logger.LogWarning(
                "Corrupt Docling cache file {CachePath}; deleting and re-converting: {Error}",
                cachePath, ex.Message);
            File.Delete(cachePath);
        }

        var doc = await inner.ConvertAsync(filePath, ct);
        await TryCacheAsync(cachePath, doc, ct);
        return doc;
    }

    private async Task TryCacheAsync(string cachePath, DoclingDocument doc, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(cachePath) ?? ".";
        Directory.CreateDirectory(dir);
        var tmp = cachePath + ".tmp";
        try
        {
            await using (var s = File.Create(tmp))
                await JsonSerializer.SerializeAsync(s, doc, CacheJsonOptions, ct);
            File.Move(tmp, cachePath, overwrite: true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex, "Failed to write Docling cache to {CachePath}; result returned uncached", cachePath);
            try { File.Delete(tmp); } catch { /* swallow cleanup */ }
        }
    }

    private static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
```

- [ ] **Step 7: Wire `DoclingDiskCache` in DI**

In `Extensions/ServiceCollectionExtensions.cs`, inside `AddIngestionPipeline`, replace:

```csharp
services.AddSingleton<IDoclingPdfConverter, DoclingPdfConverter>();
```

with:

```csharp
services.AddSingleton<DoclingPdfConverter>();
services.AddSingleton<IDoclingPdfConverter>(sp => new DoclingDiskCache(
    sp.GetRequiredService<DoclingPdfConverter>(),
    sp.GetRequiredService<IOptions<EntityExtractionOptions>>(),
    sp.GetRequiredService<ILogger<DoclingDiskCache>>()));
```

Also add `using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;` if not already present (it is — check the existing usings).

- [ ] **Step 8: Run tests**

```bash
dotnet test --filter "DoclingDiskCacheTests" -v minimal 2>&1 | tail -20
```

Expected: all 3 tests PASS.

- [ ] **Step 9: Run full test suite**

```bash
dotnet test -v minimal 2>&1 | tail -10
```

Expected: all tests pass, 0 failures.

- [ ] **Step 10: Commit**

```bash
git add Features/Ingestion/Pdf/DoclingDiskCache.cs \
        Features/Ingestion/EntityExtraction/EntityExtractionOptions.cs \
        Config/appsettings.json \
        Extensions/ServiceCollectionExtensions.cs \
        DndMcpAICsharpFun.Tests/Ingestion/Pdf/DoclingDiskCacheTests.cs
git commit -m "feat(ingestion): add DoclingDiskCache — SHA-256-keyed disk cache for Docling output"
```

---

## Task 2: Record `no_schema` skips in `errors.json`

**Files:**

- Modify: `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs`

- [ ] **Step 1: Call Serena `initial_instructions`** (if starting fresh session)

- [ ] **Step 2: Write the failing test**

Add to `DndMcpAICsharpFun.Tests/Entities/Extraction/EntityExtractionOrchestratorTests.cs` (file already exists — find it via Serena):

```csharp
[Fact]
public async Task ExtractAsync_NoSchemaForType_WritesNoSchemaError()
{
    // Arrange: orchestrator with no schemas loaded (empty SchemasDirectory)
    // so every candidate hits the no-schema branch
    // ... see Task 2 Step 3 note — use the existing test harness pattern in that file
}
```

**Before writing the test, read the existing `EntityExtractionOrchestratorTests.cs` via Serena to understand the test harness (fake LLM client, temp directories, etc.).** Use the same helper methods already there. The test verifies that when no schema exists for a type, an `ExtractionErrorEntry` with `ErrorKind == "no_schema"` is written to the errors file.

- [ ] **Step 3: Run the test to verify it fails**

```bash
dotnet test --filter "NoSchema" -v minimal 2>&1 | tail -20
```

Expected: FAIL (or compile error if the feature isn't there yet — either is fine).

- [ ] **Step 4: Update the `no_schema` branch in the orchestrator**

In `EntityExtractionOrchestrator.cs`, find the block:

```csharp
if (!schemas.TryGetValue(candidate.Type, out var schema))
{
    logger.LogWarning(
        "No schema for entity type {Type}; skipping candidate {Name}",
        candidate.Type, candidate.DisplayName);
    failed++;
    processed++;
    continue;
}
```

Replace with:

```csharp
if (!schemas.TryGetValue(candidate.Type, out var schema))
{
    logger.LogWarning(
        "No schema for entity type {Type}; skipping candidate {Name}",
        candidate.Type, candidate.DisplayName);
    extractionErrors.Add(new ExtractionErrorEntry(
        SourceEntityId: id,
        FieldPath: "(type)",
        MissingTargetId: string.Empty,
        ErrorKind: "no_schema",
        Detail: $"No JSON schema found for entity type {candidate.Type}"));
    failed++;
    processed++;
    doneIds.Add(id);
    continue;
}
```

- [ ] **Step 5: Run tests**

```bash
dotnet test -v minimal 2>&1 | tail -10
```

Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs \
        DndMcpAICsharpFun.Tests/Entities/Extraction/EntityExtractionOrchestratorTests.cs
git commit -m "feat(extraction): record no_schema skips in errors.json so they are retryable"
```

---

## Task 3: `errorsOnly` plumbing — interface, queue, endpoint, HTTP file

**Files:**

- Modify: `Features/Ingestion/IIngestionQueue.cs`
- Modify: `Features/Ingestion/IngestionQueueWorker.cs`
- Modify: `Features/Ingestion/EntityExtraction/IEntityExtractionOrchestrator.cs`
- Modify: `Features/Admin/BooksAdminEndpoints.cs`
- Modify: `DndMcpAICsharpFun.http`
- Modify: `DndMcpAICsharpFun.Tests/Entities/Admin/ExtractEntitiesEndpointTests.cs`

- [ ] **Step 1: Call Serena `initial_instructions`** (if starting fresh session)

- [ ] **Step 2: Write the failing endpoint test**

Add to `DndMcpAICsharpFun.Tests/Entities/Admin/ExtractEntitiesEndpointTests.cs`:

```csharp
[Fact]
public async Task ExtractEntities_ErrorsOnlyTrue_Returns202_AndEnqueuesWithErrorsOnly()
{
    var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(tempDir);
    try
    {
        // canonical must NOT exist for errorsOnly (endpoint does not check — orchestrator does)
        var (client, tracker, queue) = await BuildClientAsync(tempDir);
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(MakeRecord(1));
        queue.TryEnqueue(Arg.Any<IngestionWorkItem>()).Returns(true);

        var response = await client.PostAsync("/admin/books/1/extract-entities?errorsOnly=true", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        queue.Received(1).TryEnqueue(Arg.Is<IngestionWorkItem>(i =>
            i.Type == IngestionWorkType.ExtractEntities
            && i.BookId == 1
            && i.ErrorsOnly));
    }
    finally { Directory.Delete(tempDir, true); }
}
```

- [ ] **Step 3: Run the test to verify it fails**

```bash
dotnet test --filter "ErrorsOnly" -v minimal 2>&1 | tail -20
```

Expected: compile error — `ErrorsOnly` does not exist on `IngestionWorkItem` yet.

- [ ] **Step 4: Add `ErrorsOnly` to `IngestionWorkItem`**

In `Features/Ingestion/IIngestionQueue.cs`, the `IngestionWorkItem` record currently reads:

```csharp
public record IngestionWorkItem(IngestionWorkType Type, int BookId, bool Force = false);
```

Change to:

```csharp
public record IngestionWorkItem(IngestionWorkType Type, int BookId, bool Force = false, bool ErrorsOnly = false);
```

- [ ] **Step 5: Add `errorsOnly` to `IEntityExtractionOrchestrator`**

In `Features/Ingestion/EntityExtraction/IEntityExtractionOrchestrator.cs`, change:

```csharp
public interface IEntityExtractionOrchestrator
{
    Task ExtractAsync(int bookId, bool force, CancellationToken ct);
}
```

to:

```csharp
public interface IEntityExtractionOrchestrator
{
    Task ExtractAsync(int bookId, bool force, bool errorsOnly, CancellationToken ct);
}
```

- [ ] **Step 6: Update `IngestionQueueWorker` to pass `errorsOnly`**

In `Features/Ingestion/IngestionQueueWorker.cs`, find:

```csharp
case IngestionWorkType.ExtractEntities:
    var extractor = scope.ServiceProvider.GetRequiredService<EntityExtraction.IEntityExtractionOrchestrator>();
    await extractor.ExtractAsync(item.BookId, item.Force, stoppingToken);
    break;
```

Change to:

```csharp
case IngestionWorkType.ExtractEntities:
    var extractor = scope.ServiceProvider.GetRequiredService<EntityExtraction.IEntityExtractionOrchestrator>();
    await extractor.ExtractAsync(item.BookId, item.Force, item.ErrorsOnly, stoppingToken);
    break;
```

- [ ] **Step 7: Update `EntityExtractionOrchestrator` signature**

In `EntityExtractionOrchestrator.cs`, change the method signature from:

```csharp
public async Task ExtractAsync(int bookId, bool force, CancellationToken ct)
```

to:

```csharp
public async Task ExtractAsync(int bookId, bool force, bool errorsOnly, CancellationToken ct)
```

At this point the `errorsOnly` parameter is accepted but not yet used. The full logic comes in Task 4.

- [ ] **Step 8: Update `BooksAdminEndpoints.cs`**

In the `ExtractEntities` handler, add `bool? errorsOnly` parameter and pass it to the enqueue call. The `?errorsOnly=true` flag bypasses the canonical-file-exists check (that check only applies to full extractions). Replace the full `ExtractEntities` handler:

```csharp
private static async Task<IResult> ExtractEntities(
    int id,
    bool? force,
    bool? errorsOnly,
    IIngestionTracker tracker,
    IIngestionQueue queue,
    IOptions<EntityExtractionOptions> opts,
    CancellationToken ct)
{
    var record = await tracker.GetByIdAsync(id, ct);
    if (record is null)
        return Results.NotFound($"Book with id {id} not found");

    if (record.Status == IngestionStatus.Processing
        || record.Status == IngestionStatus.EntitiesExtracting
        || record.Status == IngestionStatus.EntitiesIngesting)
        return Results.Conflict("Book is currently processing.");

    var forceFlag = force ?? false;
    var errorsOnlyFlag = errorsOnly ?? false;

    if (!errorsOnlyFlag)
    {
        var bookSlug = EntityIdSlug.For(record.DisplayName, EntityType.Class, "x").Split('.')[0];
        var canonicalPath = Path.Combine(opts.Value.CanonicalDirectory, $"{bookSlug}.json");
        if (File.Exists(canonicalPath) && !forceFlag)
            return Results.Conflict($"Canonical file already exists at {canonicalPath}. Use ?force=true to overwrite.");
    }

    queue.TryEnqueue(new IngestionWorkItem(
        IngestionWorkType.ExtractEntities, id,
        Force: forceFlag, ErrorsOnly: errorsOnlyFlag));
    return Results.Accepted($"/admin/books/{id}");
}
```

- [ ] **Step 9: Update `DndMcpAICsharpFun.http`**

Add an example request after the existing `extract-entities` example:

```http
### Re-extract only failed entities (requires existing canonical JSON)
POST http://localhost:5101/admin/books/{{bookId}}/extract-entities?errorsOnly=true
X-Api-Key: {{apiKey}}
```

- [ ] **Step 10: Run tests**

```bash
dotnet test -v minimal 2>&1 | tail -10
```

Expected: all pass including the new `ErrorsOnly` test.

- [ ] **Step 11: Commit**

```bash
git add Features/Ingestion/IIngestionQueue.cs \
        Features/Ingestion/IngestionQueueWorker.cs \
        Features/Ingestion/EntityExtraction/IEntityExtractionOrchestrator.cs \
        Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs \
        Features/Admin/BooksAdminEndpoints.cs \
        DndMcpAICsharpFun.http \
        DndMcpAICsharpFun.Tests/Entities/Admin/ExtractEntitiesEndpointTests.cs
git commit -m "feat(extraction): add errorsOnly plumbing — interface, queue, endpoint, HTTP file"
```

---

## Task 4: `errorsOnly` orchestrator logic

**Files:**

- Modify: `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs`
- Modify: `DndMcpAICsharpFun.Tests/Entities/Extraction/EntityExtractionOrchestratorTests.cs`

- [ ] **Step 1: Call Serena `initial_instructions`** (if starting fresh session)

- [ ] **Step 2: Read `EntityExtractionOrchestratorTests.cs` via Serena to understand the test harness**

Look for the existing fake `IEntityExtractionLlmClient` setup and temp-directory helpers. You will extend this file.

- [ ] **Step 3: Write the failing orchestrator tests for `errorsOnly`**

Add to `EntityExtractionOrchestratorTests.cs` (use the same test harness already present):

```csharp
[Fact]
public async Task ExtractAsync_ErrorsOnly_OnlyCallsLlmForCandidatesInRetrySet()
{
    // Arrange: two candidates. errors.json lists only one. Verify LLM called once.
    // Use fake IEntityExtractionLlmClient that records which candidates it received.
    // Steps: set up temp dirs, write a minimal canonical JSON and errors.json,
    // create a real (or stub) IDoclingPdfConverter that returns two DoclingItems,
    // call ExtractAsync(bookId, force: false, errorsOnly: true, ct).
    // Assert: LLM fake called exactly once (for the candidate in retrySet).
}

[Fact]
public async Task ExtractAsync_ErrorsOnly_MergesNewEntityIntoExistingCanonical()
{
    // Arrange: existing canonical has 1 entity. errors.json has 1 candidate.
    // LLM returns success for the retried candidate.
    // Assert: canonical JSON after run has 2 entities (old + new).
}

[Fact]
public async Task ExtractAsync_ErrorsOnly_NoErrorsFile_ReturnsEarlyWithoutLlmCall()
{
    // Arrange: no errors.json exists for the book.
    // Assert: LLM never called; canonical JSON unchanged; no exception.
}

[Fact]
public async Task ExtractAsync_ErrorsOnly_NoCanonicalJson_Throws()
{
    // Arrange: no canonical JSON exists.
    // Assert: throws InvalidOperationException containing "run full extraction first".
}
```

**Before writing, read the existing tests in the file to copy the exact setup pattern.** Do NOT invent a new harness — extend the one already there.

- [ ] **Step 4: Run tests to verify they fail**

```bash
dotnet test --filter "ErrorsOnly" -v minimal 2>&1 | tail -30
```

Expected: FAIL (logic not implemented yet).

- [ ] **Step 5: Implement `errorsOnly` branch in `EntityExtractionOrchestrator.ExtractAsync`**

Read the current full `ExtractAsync` method via Serena. After the pre-condition block and tracker call, add the `errorsOnly` branch. The full modified `ExtractAsync` body:

```csharp
public async Task ExtractAsync(int bookId, bool force, bool errorsOnly, CancellationToken ct)
{
    var record = await tracker.GetByIdAsync(bookId, ct)
                 ?? throw new InvalidOperationException($"No ingestion record {bookId}");

    var bookSlug = EntityIdSlug
        .For(record.DisplayName, EntityType.Class, "x")
        .Split('.')[0];

    var canonicalPath = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".json");
    var errorsPath    = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".errors.json");
    var warningsPath  = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".warnings.json");

    if (!errorsOnly && File.Exists(canonicalPath) && !force)
        throw new InvalidOperationException(
            $"Canonical JSON already exists at {canonicalPath}; pass force=true to overwrite.");

    await tracker.MarkEntitiesExtractingAsync(bookId, ct);

    try
    {
        logger.LogInformation(
            "Entity extraction starting: book {BookId} ({DisplayName}), file {FilePath}, errorsOnly={ErrorsOnly}",
            bookId, record.DisplayName, record.FilePath, errorsOnly);

        // 1. Convert PDF via Docling (cache hit if DoclingDiskCache is wired).
        var doc = await docling.ConvertAsync(record.FilePath, ct);

        // 2. Read bookmarks → TocCategoryMap.
        var pdfBookmarks = bookmarks.ReadBookmarks(record.FilePath);
        var tocEntries   = BookmarkTocMapper.Map(pdfBookmarks);
        var tocMap       = new TocCategoryMap(tocEntries);

        // 3. Project Docling items into ScannerInputs.
        var scannerInputs = BuildScannerInputs(doc.Items);
        var candidates    = scanner.Scan(scannerInputs, tocMap).ToList();

        logger.LogInformation(
            "Entity extraction: {CandidateCount} candidates from {ItemCount} Docling items",
            candidates.Count, doc.Items.Count);

        // 4. Load schemas keyed by EntityType.
        var schemas = LoadSchemas();

        if (errorsOnly)
        {
            await RunErrorsOnlyAsync(
                bookId, record, bookSlug, candidates, schemas,
                canonicalPath, errorsPath, warningsPath, ct);
        }
        else
        {
            await RunFullExtractionAsync(
                bookId, record, bookSlug, candidates, schemas,
                canonicalPath, errorsPath, warningsPath, ct);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Entity extraction failed for book {BookId}", bookId);
        try { await tracker.MarkEntitiesFailedAsync(bookId, ex.Message, CancellationToken.None); }
        catch (Exception trackerEx) { logger.LogError(trackerEx, "Failed to mark book {BookId} as EntitiesFailed", bookId); }
        throw;
    }
}
```

- [ ] **Step 6: Extract the existing full-extraction loop into `RunFullExtractionAsync`**

Move the existing loop body (checkpoint load, candidate loop, reference resolution, write, checkpoint delete) into a private method:

```csharp
private async Task RunFullExtractionAsync(
    int bookId,
    IngestionRecord record,
    string bookSlug,
    List<EntityCandidate> candidates,
    Dictionary<EntityType, JsonElement> schemas,
    string canonicalPath,
    string errorsPath,
    string warningsPath,
    CancellationToken ct)
{
    var checkpointPath       = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".progress.json");
    var checkpointErrorsPath = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".progress.errors.json");

    var (extracted, extractionErrors, doneIds) =
        await LoadCheckpointAsync(checkpointPath, checkpointErrorsPath);

    if (doneIds.Count > 0)
        logger.LogInformation(
            "Resuming from checkpoint: {Done} candidates already processed ({Extracted} ok, {Errors} failed)",
            doneIds.Count, extracted.Count, extractionErrors.Count);

    int success   = extracted.Count;
    int failed    = extractionErrors.Count;
    int processed = 0;

    var sw      = Stopwatch.StartNew();
    var lastLog = TimeSpan.Zero;

    for (int i = 0; i < candidates.Count; i++)
    {
        ct.ThrowIfCancellationRequested();
        var candidate = candidates[i];
        var id = EntityIdSlug.For(record.DisplayName, candidate.Type, candidate.DisplayName);

        if (doneIds.Contains(id)) continue;

        if (!schemas.TryGetValue(candidate.Type, out var schema))
        {
            logger.LogWarning(
                "No schema for entity type {Type}; skipping candidate {Name}",
                candidate.Type, candidate.DisplayName);
            extractionErrors.Add(new ExtractionErrorEntry(
                SourceEntityId: id,
                FieldPath: "(type)",
                MissingTargetId: string.Empty,
                ErrorKind: "no_schema",
                Detail: $"No JSON schema found for entity type {candidate.Type}"));
            failed++;
            processed++;
            doneIds.Add(id);
            continue;
        }

        var request = new ExtractionRequest(
            SystemPrompt:    promptBuilder.BuildSystemPrompt(record.DisplayName, record.Version, candidate.Type),
            UserPrompt:      promptBuilder.BuildUserPrompt(candidate),
            ToolName:        promptBuilder.ToolName(candidate.Type),
            ToolDescription: promptBuilder.ToolDescription(candidate.Type),
            ToolInputSchema: schema,
            ModelId:         _ollama.ChatModel,
            MaxOutputTokens: _opts.MaxOutputTokensPerEntity);

        var response = await retry.ExecuteAsync(
            operation: (_, c) => llm.ExtractAsync(request, c),
            isSuccess: r => r.Success,
            ct);

        if (!response.Success || response.ToolInput is null)
        {
            logger.LogWarning(
                "Extraction failed for {Type} '{Name}' (page {Page}): {Error}",
                candidate.Type, candidate.DisplayName, candidate.Page, response.ErrorMessage);
            extractionErrors.Add(new ExtractionErrorEntry(
                SourceEntityId: id,
                FieldPath: "(extraction)",
                MissingTargetId: string.Empty,
                ErrorKind: "extraction_failure",
                Detail: response.ErrorMessage));
            failed++;
            processed++;
            doneIds.Add(id);

            if (processed % _opts.CheckpointIntervalCandidates == 0)
                await WriteCheckpointAsync(checkpointPath, checkpointErrorsPath, extracted, extractionErrors);

            continue;
        }

        extracted.Add(new EntityEnvelope(
            Id:             id,
            Type:           candidate.Type,
            Name:           candidate.DisplayName,
            SourceBook:     record.DisplayName,
            Edition:        record.Version,
            Page:           candidate.Page,
            FirstAppearedIn: new FirstAppearance(record.DisplayName, record.Version, candidate.Page),
            RevisedIn:      Array.Empty<Revision>(),
            SettingTags:    Array.Empty<string>(),
            CanonicalText:  string.Empty,
            Fields:         response.ToolInput.Value));
        success++;
        processed++;
        doneIds.Add(id);

        if (processed % _opts.CheckpointIntervalCandidates == 0)
            await WriteCheckpointAsync(checkpointPath, checkpointErrorsPath, extracted, extractionErrors);

        if (sw.Elapsed - lastLog >= TimeSpan.FromSeconds(_opts.ProgressLogIntervalSeconds))
        {
            logger.LogInformation(
                "Extraction progress: {Done}/{Total} ({Success} ok, {Failed} failed)",
                success + failed, candidates.Count, success, failed);
            lastLog = sw.Elapsed;
        }
    }

    var refWarnings = refResolver.Resolve(extracted).ToList();
    var classifier  = new IntraBookReferenceClassifier(bookSlug);
    var (intra, inter) = classifier.Partition(refWarnings);

    if (intra.Count > 0)
    {
        var offenders = intra.Select(w => w.SourceEntityId).ToHashSet(StringComparer.Ordinal);
        extracted.RemoveAll(e => offenders.Contains(e.Id));
        foreach (var w in intra)
            extractionErrors.Add(new ExtractionErrorEntry(
                SourceEntityId: w.SourceEntityId,
                FieldPath:      w.FieldPath,
                MissingTargetId: w.MissingTargetId,
                ErrorKind:      "intra_book_dangling_ref",
                Detail:         null));
    }

    var interWarnings = inter
        .Select(w => new ExtractionWarningEntry(
            SourceEntityId:  w.SourceEntityId,
            FieldPath:       w.FieldPath,
            MissingTargetId: w.MissingTargetId,
            WarningKind:     "inter_book_dangling_ref"))
        .ToList();

    var canonicalFile = new CanonicalJsonFile(
        SchemaVersion: CanonicalJsonSchema.CurrentVersion,
        Book: new CanonicalBookMetadata(
            SourceBook:  record.DisplayName,
            Edition:     record.Version,
            FileHash:    record.FileHash,
            DisplayName: record.DisplayName),
        Entities: extracted);

    await writer.WriteAsync(canonicalPath, canonicalFile, ct);
    await errorsFile.WriteAsync(errorsPath, extractionErrors, ct);
    await warningsFile.WriteAsync(warningsPath, interWarnings, ct);

    File.Delete(checkpointPath);
    File.Delete(checkpointErrorsPath);

    logger.LogInformation(
        "Entity extraction complete: book {BookId}, {Clean} clean / {Errors} errors / {Warnings} warnings",
        bookId, extracted.Count, extractionErrors.Count, interWarnings.Count);

    await tracker.MarkEntitiesExtractedAsync(bookId, extracted.Count, ct);
}
```

- [ ] **Step 7: Implement `RunErrorsOnlyAsync`**

Add the following private method to `EntityExtractionOrchestrator`:

```csharp
private async Task RunErrorsOnlyAsync(
    int bookId,
    IngestionRecord record,
    string bookSlug,
    List<EntityCandidate> candidates,
    Dictionary<EntityType, JsonElement> schemas,
    string canonicalPath,
    string errorsPath,
    string warningsPath,
    CancellationToken ct)
{
    // Pre-condition: canonical must exist.
    CanonicalJsonFile existingFile;
    try
    {
        await using var cs = File.OpenRead(canonicalPath);
        existingFile = await JsonSerializer.DeserializeAsync<CanonicalJsonFile>(cs, CheckpointOptions, ct)
            ?? throw new InvalidOperationException($"Canonical JSON at {canonicalPath} deserialised to null");
    }
    catch (FileNotFoundException)
    {
        throw new InvalidOperationException(
            $"No canonical JSON found for {bookSlug}; run full extraction first.");
    }

    // Load errors file → retrySet.
    List<ExtractionErrorEntry> previousErrors;
    try
    {
        await using var es = File.OpenRead(errorsPath);
        previousErrors = await JsonSerializer.DeserializeAsync<List<ExtractionErrorEntry>>(es, CheckpointOptions, ct) ?? [];
    }
    catch (FileNotFoundException)
    {
        logger.LogInformation("No errors file found for {BookSlug}; nothing to retry", bookSlug);
        await tracker.MarkEntitiesExtractedAsync(bookId, existingFile.Entities.Count, ct);
        return;
    }

    if (previousErrors.Count == 0)
    {
        logger.LogInformation("Errors file for {BookSlug} is empty; nothing to retry", bookSlug);
        await tracker.MarkEntitiesExtractedAsync(bookId, existingFile.Entities.Count, ct);
        return;
    }

    var retrySet = previousErrors.Select(e => e.SourceEntityId).ToHashSet(StringComparer.Ordinal);
    logger.LogInformation(
        "Re-extracting {Count} failed entities for book {BookId}", retrySet.Count, bookId);

    var newlyExtracted  = new List<EntityEnvelope>();
    var newErrors       = new List<ExtractionErrorEntry>();

    for (int i = 0; i < candidates.Count; i++)
    {
        ct.ThrowIfCancellationRequested();
        var candidate = candidates[i];
        var id = EntityIdSlug.For(record.DisplayName, candidate.Type, candidate.DisplayName);

        if (!retrySet.Contains(id)) continue;

        if (!schemas.TryGetValue(candidate.Type, out var schema))
        {
            logger.LogWarning(
                "No schema for entity type {Type}; skipping candidate {Name}",
                candidate.Type, candidate.DisplayName);
            newErrors.Add(new ExtractionErrorEntry(
                SourceEntityId: id,
                FieldPath: "(type)",
                MissingTargetId: string.Empty,
                ErrorKind: "no_schema",
                Detail: $"No JSON schema found for entity type {candidate.Type}"));
            continue;
        }

        var request = new ExtractionRequest(
            SystemPrompt:    promptBuilder.BuildSystemPrompt(record.DisplayName, record.Version, candidate.Type),
            UserPrompt:      promptBuilder.BuildUserPrompt(candidate),
            ToolName:        promptBuilder.ToolName(candidate.Type),
            ToolDescription: promptBuilder.ToolDescription(candidate.Type),
            ToolInputSchema: schema,
            ModelId:         _ollama.ChatModel,
            MaxOutputTokens: _opts.MaxOutputTokensPerEntity);

        var response = await retry.ExecuteAsync(
            operation: (_, c) => llm.ExtractAsync(request, c),
            isSuccess: r => r.Success,
            ct);

        if (!response.Success || response.ToolInput is null)
        {
            logger.LogWarning(
                "Re-extraction failed for {Type} '{Name}': {Error}",
                candidate.Type, candidate.DisplayName, response.ErrorMessage);
            newErrors.Add(new ExtractionErrorEntry(
                SourceEntityId: id,
                FieldPath: "(extraction)",
                MissingTargetId: string.Empty,
                ErrorKind: "extraction_failure",
                Detail: response.ErrorMessage));
            continue;
        }

        newlyExtracted.Add(new EntityEnvelope(
            Id:             id,
            Type:           candidate.Type,
            Name:           candidate.DisplayName,
            SourceBook:     record.DisplayName,
            Edition:        record.Version,
            Page:           candidate.Page,
            FirstAppearedIn: new FirstAppearance(record.DisplayName, record.Version, candidate.Page),
            RevisedIn:      Array.Empty<Revision>(),
            SettingTags:    Array.Empty<string>(),
            CanonicalText:  string.Empty,
            Fields:         response.ToolInput.Value));
    }

    // Reference resolution on newly extracted only.
    var refWarnings = refResolver.Resolve(newlyExtracted).ToList();
    var classifier  = new IntraBookReferenceClassifier(bookSlug);
    var (intra, inter) = classifier.Partition(refWarnings);

    if (intra.Count > 0)
    {
        var offenders = intra.Select(w => w.SourceEntityId).ToHashSet(StringComparer.Ordinal);
        newlyExtracted.RemoveAll(e => offenders.Contains(e.Id));
        foreach (var w in intra)
            newErrors.Add(new ExtractionErrorEntry(
                SourceEntityId: w.SourceEntityId,
                FieldPath:      w.FieldPath,
                MissingTargetId: w.MissingTargetId,
                ErrorKind:      "intra_book_dangling_ref",
                Detail:         null));
    }

    var interWarnings = inter
        .Select(w => new ExtractionWarningEntry(
            SourceEntityId:  w.SourceEntityId,
            FieldPath:       w.FieldPath,
            MissingTargetId: w.MissingTargetId,
            WarningKind:     "inter_book_dangling_ref"))
        .ToList();

    // Merge newly extracted into existing canonical.
    var mergedEntities = existingFile.Entities.Concat(newlyExtracted).ToList();
    var mergedFile     = existingFile with { Entities = mergedEntities };

    await writer.WriteAsync(canonicalPath, mergedFile, ct);
    await errorsFile.WriteAsync(errorsPath, newErrors, ct);
    await warningsFile.WriteAsync(warningsPath, interWarnings, ct);

    logger.LogInformation(
        "Error re-extraction complete: book {BookId}, {New} newly extracted, {StillFailing} still failing, {Warnings} warnings",
        bookId, newlyExtracted.Count, newErrors.Count, interWarnings.Count);

    await tracker.MarkEntitiesExtractedAsync(bookId, mergedEntities.Count, ct);
}
```

- [ ] **Step 8: Run the orchestrator errorsOnly tests**

```bash
dotnet test --filter "ErrorsOnly" -v minimal 2>&1 | tail -30
```

Expected: all 4 errorsOnly tests PASS.

- [ ] **Step 9: Run the full test suite**

```bash
dotnet test -v minimal 2>&1 | tail -10
```

Expected: all tests pass, 0 failures.

- [ ] **Step 10: Commit**

```bash
git add Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs \
        DndMcpAICsharpFun.Tests/Entities/Extraction/EntityExtractionOrchestratorTests.cs
git commit -m "feat(extraction): implement errorsOnly branch — retry failed entities, merge into existing canonical JSON"
```

---

## Self-review

**Spec coverage:**

- ✅ `DoclingDiskCache` — SHA-256 key, disk write, corrupt-cache recovery (Task 1)
- ✅ `DoclingCacheDirectory` config (Task 1)
- ✅ DI wiring — `DoclingPdfConverter` wrapped by `DoclingDiskCache` (Task 1)
- ✅ `no_schema` → `errors.json` with `ErrorKind: "no_schema"` (Task 2)
- ✅ `?errorsOnly=true` query param (Task 3)
- ✅ `ErrorsOnly` on `IngestionWorkItem` (Task 3)
- ✅ Pre-condition: no canonical → exception (Task 4)
- ✅ Pre-condition: no errors file → early return (Task 4)
- ✅ Skip candidates not in retrySet (Task 4)
- ✅ Merge into existing canonical JSON (Task 4)
- ✅ No checkpoints for errorsOnly runs (Task 4 — `RunErrorsOnlyAsync` has no checkpoint writes)
- ✅ HTTP file updated (Task 3)
- ✅ Tests for all behaviours

**Type consistency check:** `DoclingDiskCache`, `EntityExtractionOptions.DoclingCacheDirectory`, `IngestionWorkItem.ErrorsOnly`, `IEntityExtractionOrchestrator.ExtractAsync(int, bool, bool, CancellationToken)`, `RunErrorsOnlyAsync`, `RunFullExtractionAsync` — all consistent across all four tasks. ✅

**No placeholders.** All implementation steps contain complete code. ✅
