# Roslyn Warnings — Fix & Build Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable `EnforceCodeStyleInBuild=true` and `TreatWarningsAsErrors=true`, then fix every resulting violation so `dotnet build` exits with 0 errors and 0 warnings.

**Architecture:** Three phases: (1) enable the build gate and exclude generated Migrations code; (2) convert all classes with traditional constructors to primary constructors and replace every direct `ILogger` call with a `[LoggerMessage]`-generated method to fix CA1873 and `readonly` field violations; (3) clean up any remaining style violations flagged by the gate.

**Tech Stack:** .NET 10 / ASP.NET Core, C# 13, `Microsoft.Extensions.Logging` (`[LoggerMessage]` source generator is built into the BCL — no extra package needed)

> **`[LoggerMessage]` requirement discovered during implementation:** The `[LoggerMessage]` source generator emits a sibling `partial` declaration for the *outer* containing type. Any class that hosts a nested `private static partial class Log` must itself be declared `partial` — i.e. `public sealed partial class MyClass(...)`. This applies to Tasks 2–7.

---

## File Map

| File | Change |
|------|--------|
| `DndMcpAICsharpFun.csproj` | Add `EnforceCodeStyleInBuild` + `TreatWarningsAsErrors` |
| `.editorconfig` | Add `[Migrations/**]` exclusion block |
| `Features/Ingestion/IngestionOrchestrator.cs` | Primary constructor + `[LoggerMessage]` |
| `Features/Ingestion/IngestionBackgroundService.cs` | Primary constructor + `[LoggerMessage]` |
| `Features/Embedding/EmbeddingIngestor.cs` | Primary constructor + `[LoggerMessage]` |
| `Infrastructure/Qdrant/QdrantCollectionInitializer.cs` | Primary constructor + `[LoggerMessage]` + collection expressions |
| `Features/Retrieval/RagRetrievalService.cs` | Primary constructor + `static` lambdas |
| `Features/Ingestion/Pdf/PdfPigTextExtractor.cs` | `[LoggerMessage]` only (already has primary constructor) |
| `Features/Admin/BooksAdminEndpoints.cs` | `[LoggerMessage]` only (static class, no constructor) |
| `Program.cs` | `static` keyword on non-capturing lambdas (if build flags them) |

---

### Task 1: Enable build gate and exclude Migrations

**Files:**
- Modify: `DndMcpAICsharpFun.csproj`
- Modify: `.editorconfig`

- [ ] **Step 1: Add build properties to the project file**

In `DndMcpAICsharpFun.csproj`, replace the `<PropertyGroup>` block:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>

  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.7" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.7">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.7" />
    <PackageReference Include="OllamaSharp" Version="5.4.25" />
    <PackageReference Include="Qdrant.Client" Version="1.17.0" />
    <PackageReference Include="UglyToad.PdfPig" Version="1.7.0-custom-5" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Add Migrations exclusion to `.editorconfig`**

Append at the very end of `.editorconfig`:

```ini
# Generated EF Core migrations — suppress all diagnostics
[Migrations/**]
generated_code = true
dotnet_analyzer_diagnostic.severity = none
```

- [ ] **Step 3: Run build to discover all violations**

```bash
dotnet build 2>&1
```

Expected: FAIL. This is intentional — the output is your work list for Tasks 2–9.

- [ ] **Step 4: Commit**

```bash
git add DndMcpAICsharpFun.csproj .editorconfig
git commit -m "chore: enable EnforceCodeStyleInBuild and TreatWarningsAsErrors"
```

---

### Task 2: IngestionOrchestrator — primary constructor + LoggerMessage

**Files:**
- Modify: `Features/Ingestion/IngestionOrchestrator.cs`

Eliminates: 5 non-`readonly` fields (IDE0044 / `dotnet_style_readonly_field`) and 5 CA1873 violations.

- [ ] **Step 1: Replace file content**

```csharp
using System.Security.Cryptography;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Ingestion.Chunking;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Sqlite;

namespace DndMcpAICsharpFun.Features.Ingestion;

public sealed class IngestionOrchestrator(
    IIngestionTracker tracker,
    IPdfTextExtractor extractor,
    DndChunker chunker,
    IEmbeddingIngestor embeddingIngestor,
    ILogger<IngestionOrchestrator> logger) : IIngestionOrchestrator
{
    public async Task IngestBookAsync(int recordId, CancellationToken cancellationToken = default)
    {
        var record = await tracker.GetByIdAsync(recordId, cancellationToken);
        if (record is null)
        {
            Log.RecordNotFound(logger, recordId);
            return;
        }

        Log.StartingIngestion(logger, record.DisplayName, recordId);
        await tracker.MarkProcessingAsync(recordId, cancellationToken);

        try
        {
            var currentHash = await ComputeHashAsync(record.FilePath, cancellationToken);

            if (record.Status == IngestionStatus.Completed && record.FileHash == currentHash)
            {
                Log.SkippingUnchanged(logger, record.DisplayName);
                return;
            }

            var pages = extractor.ExtractPages(record.FilePath);
            var version = Enum.Parse<DndVersion>(record.Version);
            var chunks = chunker.Chunk(pages, record.SourceName, version).ToList();

            await embeddingIngestor.IngestAsync(chunks, currentHash, cancellationToken);
            await tracker.MarkCompletedAsync(recordId, chunks.Count, cancellationToken);

            Log.CompletedIngestion(logger, record.DisplayName, chunks.Count);
        }
        catch (Exception ex)
        {
            Log.IngestionFailed(logger, ex, record.DisplayName, recordId);
            await tracker.MarkFailedAsync(recordId, ex.Message, cancellationToken);
        }
    }

    private static async Task<string> ComputeHashAsync(string filePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Ingestion record {Id} not found")]
        public static partial void RecordNotFound(ILogger logger, int id);

        [LoggerMessage(Level = LogLevel.Information, Message = "Starting ingestion for {DisplayName} (id={Id})")]
        public static partial void StartingIngestion(ILogger logger, string displayName, int id);

        [LoggerMessage(Level = LogLevel.Information, Message = "Skipping {DisplayName} — already ingested with same hash")]
        public static partial void SkippingUnchanged(ILogger logger, string displayName);

        [LoggerMessage(Level = LogLevel.Information, Message = "Completed {DisplayName}: {Count} chunks")]
        public static partial void CompletedIngestion(ILogger logger, string displayName, int count);

        [LoggerMessage(Level = LogLevel.Error, Message = "Ingestion failed for {DisplayName} (id={Id})")]
        public static partial void IngestionFailed(ILogger logger, Exception ex, string displayName, int id);
    }
}
```

- [ ] **Step 2: Verify build error count dropped**

```bash
dotnet build 2>&1 | grep -c " error "
```

Expected: fewer errors than after Task 1.

- [ ] **Step 3: Commit**

```bash
git add Features/Ingestion/IngestionOrchestrator.cs
git commit -m "refactor: IngestionOrchestrator — primary constructor + LoggerMessage"
```

---

### Task 3: IngestionBackgroundService — primary constructor + LoggerMessage

**Files:**
- Modify: `Features/Ingestion/IngestionBackgroundService.cs`

Eliminates: 2 non-`readonly` fields and 3 CA1873 violations.

- [ ] **Step 1: Replace file content**

```csharp
using DndMcpAICsharpFun.Features.Ingestion.Tracking;

namespace DndMcpAICsharpFun.Features.Ingestion;

public sealed class IngestionBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<IngestionBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan CycleInterval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunCycleAsync(stoppingToken);
            await Task.Delay(CycleInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        Log.CycleStarting(logger);
        int processed = 0, failed = 0;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var tracker = scope.ServiceProvider.GetRequiredService<IIngestionTracker>();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IIngestionOrchestrator>();

            var eligible = await tracker.GetPendingAndFailedAsync(ct);

            foreach (var record in eligible)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    await orchestrator.IngestBookAsync(record.Id, ct);
                    processed++;
                }
                catch (Exception ex)
                {
                    Log.RecordError(logger, ex, record.Id);
                    failed++;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.CycleFailed(logger, ex);
        }

        Log.CycleComplete(logger, processed, failed);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Ingestion cycle starting")]
        public static partial void CycleStarting(ILogger logger);

        [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled error ingesting record {Id}")]
        public static partial void RecordError(ILogger logger, Exception ex, int id);

        [LoggerMessage(Level = LogLevel.Error, Message = "Ingestion cycle failed")]
        public static partial void CycleFailed(ILogger logger, Exception ex);

        [LoggerMessage(Level = LogLevel.Information, Message = "Ingestion cycle complete. Processed={Processed} Failed={Failed}")]
        public static partial void CycleComplete(ILogger logger, int processed, int failed);
    }
}
```

- [ ] **Step 2: Verify build error count dropped**

```bash
dotnet build 2>&1 | grep -c " error "
```

- [ ] **Step 3: Commit**

```bash
git add Features/Ingestion/IngestionBackgroundService.cs
git commit -m "refactor: IngestionBackgroundService — primary constructor + LoggerMessage"
```

---

### Task 4: EmbeddingIngestor — primary constructor + LoggerMessage

**Files:**
- Modify: `Features/Embedding/EmbeddingIngestor.cs`

Eliminates: 4 non-`readonly` fields and 2 CA1873 violations. The derived `_batchSize` field stays as an explicit `readonly` field initialised from the primary constructor parameter.

- [ ] **Step 1: Replace file content**

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Embedding;

public sealed class EmbeddingIngestor(
    IEmbeddingService embeddingService,
    IVectorStoreService vectorStore,
    IOptions<IngestionOptions> options,
    ILogger<EmbeddingIngestor> logger) : IEmbeddingIngestor
{
    private readonly int _batchSize = options.Value.EmbeddingBatchSize;

    public async Task IngestAsync(IList<ContentChunk> chunks, string fileHash, CancellationToken ct = default)
    {
        int total = chunks.Count;
        int upserted = 0;

        for (int offset = 0; offset < total; offset += _batchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = chunks.Skip(offset).Take(_batchSize).ToList();
            var texts = batch.Select(static c => c.Text).ToList();
            var vectors = await embeddingService.EmbedAsync(texts, ct);

            var points = batch
                .Zip(vectors, (chunk, vector) => (chunk, vector, fileHash))
                .ToList();

            await vectorStore.UpsertAsync(points, ct);
            upserted += batch.Count;

            Log.UpsertedChunks(logger, upserted, total);
        }

        Log.IngestedChunks(logger, total);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Upserted {Upserted}/{Total} chunks")]
        public static partial void UpsertedChunks(ILogger logger, int upserted, int total);

        [LoggerMessage(Level = LogLevel.Information, Message = "Ingested {Total} chunks into vector store")]
        public static partial void IngestedChunks(ILogger logger, int total);
    }
}
```

Note: `.Select(static c => c.Text)` uses only the lambda parameter — eligible for `static`. The `.Zip` lambda captures `fileHash` from the method scope and cannot be `static`.

- [ ] **Step 2: Verify build error count dropped**

```bash
dotnet build 2>&1 | grep -c " error "
```

- [ ] **Step 3: Commit**

```bash
git add Features/Embedding/EmbeddingIngestor.cs
git commit -m "refactor: EmbeddingIngestor — primary constructor + LoggerMessage"
```

---

### Task 5: QdrantCollectionInitializer — primary constructor + LoggerMessage + collection expressions

**Files:**
- Modify: `Infrastructure/Qdrant/QdrantCollectionInitializer.cs`

Eliminates: 3 non-`readonly` fields, 4 CA1873 violations, 2 array-creation style violations (`dotnet_style_prefer_collection_expression`).

- [ ] **Step 1: Replace file content**

```csharp
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace DndMcpAICsharpFun.Infrastructure.Qdrant;

public sealed class QdrantCollectionInitializer(
    QdrantClient client,
    IOptions<QdrantOptions> options,
    ILogger<QdrantCollectionInitializer> logger) : IHostedService
{
    private readonly QdrantOptions _options = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var exists = await client.CollectionExistsAsync(_options.CollectionName, cancellationToken);
            if (exists)
            {
                Log.CollectionExists(logger, _options.CollectionName);
                return;
            }

            await client.CreateCollectionAsync(
                _options.CollectionName,
                new VectorParams { Size = (ulong)_options.VectorSize, Distance = Distance.Cosine },
                cancellationToken: cancellationToken);

            Log.CollectionCreated(logger, _options.CollectionName, _options.VectorSize);

            await CreatePayloadIndexesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Log.CollectionInitFailed(logger, ex, _options.CollectionName);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task CreatePayloadIndexesAsync(CancellationToken ct)
    {
        string[] keywordFields = ["source_book", "version", "category", "entity_name"];
        foreach (var field in keywordFields)
            await client.CreatePayloadIndexAsync(_options.CollectionName, field, PayloadSchemaType.Keyword, cancellationToken: ct);

        string[] intFields = ["page_number", "chunk_index"];
        foreach (var field in intFields)
            await client.CreatePayloadIndexAsync(_options.CollectionName, field, PayloadSchemaType.Integer, cancellationToken: ct);

        Log.PayloadIndexesCreated(logger, _options.CollectionName);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Qdrant collection '{Collection}' already exists")]
        public static partial void CollectionExists(ILogger logger, string collection);

        [LoggerMessage(Level = LogLevel.Information, Message = "Created Qdrant collection '{Collection}' (size={Size}, distance=Cosine)")]
        public static partial void CollectionCreated(ILogger logger, string collection, int size);

        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to initialise Qdrant collection '{Collection}'")]
        public static partial void CollectionInitFailed(ILogger logger, Exception ex, string collection);

        [LoggerMessage(Level = LogLevel.Information, Message = "Created payload indexes on collection '{Collection}'")]
        public static partial void PayloadIndexesCreated(ILogger logger, string collection);
    }
}
```

- [ ] **Step 2: Verify build error count dropped**

```bash
dotnet build 2>&1 | grep -c " error "
```

- [ ] **Step 3: Commit**

```bash
git add Infrastructure/Qdrant/QdrantCollectionInitializer.cs
git commit -m "refactor: QdrantCollectionInitializer — primary constructor + LoggerMessage"
```

---

### Task 6: RagRetrievalService — primary constructor + static lambdas

**Files:**
- Modify: `Features/Retrieval/RagRetrievalService.cs`

Eliminates: 4 non-`readonly` fields (IDE0044) and static-lambda violations on two `.Select(...)` calls.

- [ ] **Step 1: Replace file content**

```csharp
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace DndMcpAICsharpFun.Features.Retrieval;

public sealed class RagRetrievalService(
    QdrantClient qdrant,
    IEmbeddingService embedding,
    IOptions<QdrantOptions> qdrantOptions,
    IOptions<RetrievalOptions> retrievalOptions) : IRagRetrievalService
{
    private readonly string _collectionName = qdrantOptions.Value.CollectionName;
    private readonly RetrievalOptions _options = retrievalOptions.Value;

    public async Task<IList<RetrievalResult>> SearchAsync(RetrievalQuery query, CancellationToken ct = default)
    {
        var points = await ExecuteSearchAsync(query, ct);
        return points
            .Select(static p => new RetrievalResult(
                QdrantPayloadMapper.GetText(p.Payload),
                QdrantPayloadMapper.ToChunkMetadata(p.Payload),
                p.Score))
            .ToList();
    }

    public async Task<IList<RetrievalDiagnosticResult>> SearchDiagnosticAsync(RetrievalQuery query, CancellationToken ct = default)
    {
        var points = await ExecuteSearchAsync(query, ct);
        return points
            .Select(static p => new RetrievalDiagnosticResult(
                QdrantPayloadMapper.GetText(p.Payload),
                QdrantPayloadMapper.ToChunkMetadata(p.Payload),
                p.Score,
                p.Id.Uuid))
            .ToList();
    }

    private async Task<IReadOnlyList<ScoredPoint>> ExecuteSearchAsync(RetrievalQuery query, CancellationToken ct)
    {
        var vectors = await embedding.EmbedAsync([query.QueryText], ct);
        var vector = vectors[0].AsMemory();
        var filter = BuildFilter(query);
        var limit = (ulong)Math.Min(query.TopK, _options.MaxTopK);

        return await qdrant.SearchAsync(
            _collectionName,
            vector,
            filter: filter,
            limit: limit,
            scoreThreshold: _options.ScoreThreshold,
            cancellationToken: ct);
    }

    private static Filter? BuildFilter(RetrievalQuery query)
    {
        var conditions = new List<Condition>();

        if (query.Version.HasValue)
            conditions.Add(KeywordCondition("version", query.Version.Value.ToString()));

        if (query.Category.HasValue)
            conditions.Add(KeywordCondition("category", query.Category.Value.ToString()));

        if (!string.IsNullOrWhiteSpace(query.SourceBook))
            conditions.Add(KeywordCondition("source_book", query.SourceBook));

        if (!string.IsNullOrWhiteSpace(query.EntityName))
            conditions.Add(KeywordCondition("entity_name", query.EntityName));

        if (conditions.Count == 0)
            return null;

        var filter = new Filter();
        foreach (var c in conditions)
            filter.Must.Add(c);
        return filter;
    }

    private static Condition KeywordCondition(string key, string value) =>
        new()
        {
            Field = new FieldCondition
            {
                Key = key,
                Match = new Match { Keyword = value }
            }
        };
}
```

- [ ] **Step 2: Verify build error count dropped**

```bash
dotnet build 2>&1 | grep -c " error "
```

- [ ] **Step 3: Commit**

```bash
git add Features/Retrieval/RagRetrievalService.cs
git commit -m "refactor: RagRetrievalService — primary constructor + static lambdas"
```

---

### Task 7: PdfPigTextExtractor — LoggerMessage

**Files:**
- Modify: `Features/Ingestion/Pdf/PdfPigTextExtractor.cs`

Eliminates: 1 CA1873 violation (`Path.GetFileName(filePath)` is a method call evaluated even when `LogWarning` is disabled).

- [ ] **Step 1: Replace file content**

```csharp
using DndMcpAICsharpFun.Infrastructure.Sqlite;

using Microsoft.Extensions.Options;

using UglyToad.PdfPig;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public sealed class PdfPigTextExtractor(
    IOptions<IngestionOptions> options,
    ILogger<PdfPigTextExtractor> logger) : IPdfTextExtractor
{
    private readonly int _minPageCharacters = options.Value.MinPageCharacters;

    public IEnumerable<(int PageNumber, string Text)> ExtractPages(string filePath)
    {
        using var document = PdfDocument.Open(filePath);

        foreach (var page in document.GetPages())
        {
            var text = page.Text;

            if (text.Length < _minPageCharacters)
                Log.SparsePage(logger, Path.GetFileName(filePath), page.Number, text.Length);

            yield return (page.Number, text);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Sparse page detected in {File} at page {Page} ({Chars} chars)")]
        public static partial void SparsePage(ILogger logger, string file, int page, int chars);
    }
}
```

- [ ] **Step 2: Verify build error count dropped**

```bash
dotnet build 2>&1 | grep -c " error "
```

- [ ] **Step 3: Commit**

```bash
git add Features/Ingestion/Pdf/PdfPigTextExtractor.cs
git commit -m "refactor: PdfPigTextExtractor — LoggerMessage"
```

---

### Task 8: BooksAdminEndpoints — LoggerMessage

**Files:**
- Modify: `Features/Admin/BooksAdminEndpoints.cs`

Eliminates: 1 CA1873 violation. The `await using` block form for `dest` is kept intentionally — converting it to simple `await using var dest` would change disposal semantics (the write handle must be closed before the file is re-opened for hashing). If `IDE0063` still fires on these two statements after this task, add `csharp_prefer_simple_using_statement = false:none` to `.editorconfig` under `[*.cs]`.

- [ ] **Step 1: Replace file content**

```csharp
using System.Security.Cryptography;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Admin;

public static class BooksAdminEndpoints
{
    public static RouteGroupBuilder MapBooksAdmin(this RouteGroupBuilder group)
    {
        group.MapPost("/books/register", RegisterBook);
        group.MapGet("/books", GetAllBooks);
        group.MapPost("/books/{id:int}/reingest", ReingestBook);
        return group;
    }

    private static async Task<IResult> RegisterBook(
        IFormFile file,
        string sourceName,
        string version,
        string displayName,
        IIngestionTracker tracker,
        IOptions<IngestionOptions> ingestionOptions,
        ILogger<RegisterBookRequest> logger,
        CancellationToken ct)
    {
        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return Results.Problem("Only PDF files are accepted.", statusCode: 400);

        if (!Enum.TryParse<DndVersion>(version, ignoreCase: true, out var parsedVersion))
            return Results.Problem(
                $"Invalid version '{version}'. Valid values: {string.Join(", ", Enum.GetNames<DndVersion>())}",
                statusCode: 400);

        var booksPath = ingestionOptions.Value.BooksPath;
        Directory.CreateDirectory(booksPath);

        var filePath = Path.Combine(booksPath, file.FileName);

        string hash;
        await using (var dest = File.Create(filePath))
        {
            await using var src = file.OpenReadStream();
            await src.CopyToAsync(dest, ct);
        }

        await using (var stream = File.OpenRead(filePath))
        {
            var hashBytes = await SHA256.HashDataAsync(stream, ct);
            hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        var existing = await tracker.GetByHashAsync(hash, ct);
        if (existing is not null)
            return Results.Ok(existing);

        var record = new IngestionRecord
        {
            FilePath = filePath,
            FileName = file.FileName,
            FileHash = hash,
            SourceName = sourceName,
            Version = parsedVersion.ToString(),
            DisplayName = displayName,
            Status = IngestionStatus.Pending,
        };

        var created = await tracker.CreateAsync(record, ct);
        Log.BookRegistered(logger, created.DisplayName, created.Id, file.FileName);

        return Results.Created($"/admin/books/{created.Id}", created);
    }

    private static async Task<IResult> GetAllBooks(IIngestionTracker tracker)
    {
        var records = await tracker.GetAllAsync();
        return Results.Ok(records);
    }

    private static async Task<IResult> ReingestBook(
        int id,
        IIngestionTracker tracker,
        IIngestionOrchestrator orchestrator,
        CancellationToken ct)
    {
        var record = await tracker.GetByIdAsync(id, ct);
        if (record is null)
            return Results.NotFound($"Book with id {id} not found");

        await tracker.ResetForReingestionAsync(id, ct);
        _ = Task.Run(() => orchestrator.IngestBookAsync(id, CancellationToken.None), ct);

        return Results.Accepted($"/admin/books/{id}");
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Registered book {DisplayName} (id={Id}, file={File})")]
        public static partial void BookRegistered(ILogger logger, string displayName, int id, string file);
    }
}

public sealed record RegisterBookRequest(
    string SourceName,
    string Version,
    string DisplayName);
```

- [ ] **Step 2: Verify build error count dropped**

```bash
dotnet build 2>&1 | grep -c " error "
```

- [ ] **Step 3: Commit**

```bash
git add Features/Admin/BooksAdminEndpoints.cs
git commit -m "refactor: BooksAdminEndpoints — LoggerMessage"
```

---

### Task 9: Remaining style violations

**Files:**
- Modify: `Program.cs` (likely)
- Modify: `.editorconfig` (if `IDE0063` fires on `BooksAdminEndpoints`)

- [ ] **Step 1: Collect remaining violations**

```bash
dotnet build 2>&1
```

Review every remaining error. After Tasks 1–8 the most likely survivors are:

| Error code | Rule | Typical location | Fix |
|------------|------|-----------------|-----|
| IDE0062 | `csharp_prefer_static_anonymous_function` | `Program.cs` lambdas | Add `static` to `sp =>` lambdas that only use `sp` |
| IDE0063 | `csharp_prefer_simple_using_statement` | `BooksAdminEndpoints.cs` | Demote to `none` in `.editorconfig` (see below) |
| IDE0090 | `csharp_style_implicit_object_creation_when_type_is_apparent` | Any `new Foo()` where type is explicit on both sides | Replace with `new()` |

- [ ] **Step 2: Fix static lambdas in Program.cs (if flagged)**

Lambdas in `Program.cs` that only use their parameter `sp` and call no instance members from outer scope are eligible for `static`. Update `Program.cs`:

```csharp
builder.Services.AddSingleton<QdrantClient>(static sp =>
{
    var opts = sp.GetRequiredService<IOptions<QdrantOptions>>().Value;
    return new QdrantClient(opts.Host, opts.Port, apiKey: opts.ApiKey);
});

builder.Services.AddSingleton<OllamaApiClient>(static sp =>
{
    var opts = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
    return new OllamaApiClient(new Uri(opts.BaseUrl));
});

builder.Services.AddDbContext<IngestionDbContext>(static (sp, options) =>
{
    var ingestionOpts = sp.GetRequiredService<IOptions<IngestionOptions>>().Value;
    options.UseSqlite($"Data Source={ingestionOpts.DatabasePath}");
});
```

And the middleware predicate:
```csharp
app.UseWhen(
    static ctx => ctx.Request.Path.StartsWithSegments("/admin"),
    static adminApp => adminApp.UseMiddleware<AdminApiKeyMiddleware>()
);
```

- [ ] **Step 3: Handle IDE0063 on BooksAdminEndpoints if still flagging**

If `IDE0063` fires on `await using (var dest = ...)` in `BooksAdminEndpoints.cs`, add to `.editorconfig` under the existing `[*.cs]` section (after line `csharp_using_directive_placement = outside_namespace:silent`):

```ini
csharp_prefer_simple_using_statement = false:none
```

- [ ] **Step 4: Fix any other remaining violations**

For each error still in `dotnet build 2>&1`:
- The error message includes the rule code (e.g., `IDE0090`)
- The rule code maps directly to the `.editorconfig` key shown in the table in Step 1
- Either fix the code to satisfy the rule, or add `<rule-key> = false:none` to `.editorconfig` under `[*.cs]` if the rule should not apply project-wide

Run `dotnet build 2>&1 | grep -c " error "` after each fix to confirm the count is falling.

- [ ] **Step 5: Commit**

```bash
git add -p
git commit -m "style: fix remaining Roslyn style violations"
```

---

### Task 10: Final verification

- [ ] **Step 1: Run full clean build**

```bash
dotnet build 2>&1
```

Expected output (last lines):
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

If any errors remain, return to the relevant task, apply the fix, and re-run.

- [ ] **Step 2: Commit any last-minute fixes (if needed)**

```bash
git add -p
git commit -m "style: final Roslyn cleanup — build gate green"
```
