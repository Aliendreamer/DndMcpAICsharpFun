# Infrastructure Test Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add test coverage for `SqliteIngestionTracker`, `OllamaLlmClassifier`, and `BooksAdminEndpoints` across 3 phases.

**Architecture:** Phase 1 uses in-memory SQLite via a shared `SqliteConnection` to give real EF Core roundtrips. Phase 2 is pure unit tests with a mocked `IOllamaApiClient`. Phase 3 follows the existing `WebApplication.CreateBuilder()` + `UseTestServer()` pattern with all external services mocked and middleware bypassed.

**Tech Stack:** xUnit, NSubstitute, EF Core SQLite in-memory, `Microsoft.AspNetCore.TestHost`

---

## File Map

**Modified (production):**
- `Features/Ingestion/Extraction/OllamaLlmClassifier.cs` — change constructor param `OllamaApiClient` → `IOllamaApiClient`

**Created (tests):**
- `DndMcpAICsharpFun.Tests/Infrastructure/Tracking/TrackerFixture.cs` — shared SQLite connection + DbContext factory
- `DndMcpAICsharpFun.Tests/Infrastructure/Tracking/SqliteIngestionTrackerTests.cs` — 10 tests
- `DndMcpAICsharpFun.Tests/Ingestion/Extraction/OllamaLlmClassifierTests.cs` — 5 tests
- `DndMcpAICsharpFun.Tests/Admin/BooksAdminEndpointsTests.cs` — ~19 tests

---

## Task 1: Make OllamaLlmClassifier testable

The classifier currently takes `OllamaApiClient` (concrete). Change it to `IOllamaApiClient` so tests can inject a substitute. The DI container already registers `IOllamaApiClient` as an alias for `OllamaApiClient` in `ServiceCollectionExtensions.cs`.

**Files:**
- Modify: `Features/Ingestion/Extraction/OllamaLlmClassifier.cs`

- [ ] **Step 1: Change the constructor parameter type**

In `OllamaLlmClassifier.cs`, the class declaration currently reads:

```csharp
public sealed partial class OllamaLlmClassifier(
    OllamaApiClient ollama,
    IOptions<OllamaOptions> options,
    ILogger<OllamaLlmClassifier> logger) : ILlmClassifier
```

Change it to:

```csharp
public sealed partial class OllamaLlmClassifier(
    IOllamaApiClient ollama,
    IOptions<OllamaOptions> options,
    ILogger<OllamaLlmClassifier> logger) : ILlmClassifier
```

The field `_model` and all usages of `ollama` inside the class body remain unchanged — `IOllamaApiClient` exposes the same `ChatAsync` method.

- [ ] **Step 2: Build to verify no errors**

```bash
dotnet build DndMcpAICsharpFun.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Features/Ingestion/Extraction/OllamaLlmClassifier.cs
git commit -m "refactor: use IOllamaApiClient in OllamaLlmClassifier for testability"
```

---

## Task 2: TrackerFixture — in-memory SQLite helper

Create a test fixture that opens a `SqliteConnection("DataSource=:memory:")`, keeps it alive for the test class lifetime, applies EF migrations, and provides a factory for fresh tracker instances per operation. Fresh tracker = fresh `DbContext` = no change-tracker cache pollution after bulk `ExecuteUpdateAsync` calls.

**Files:**
- Create: `DndMcpAICsharpFun.Tests/Infrastructure/Tracking/TrackerFixture.cs`

- [ ] **Step 1: Verify `SqliteConnection` is available without adding a package**

```bash
dotnet build DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj
```

`Microsoft.Data.Sqlite` comes in transitively via `Microsoft.EntityFrameworkCore.Sqlite` which is a dependency of the main project. If `SqliteConnection` cannot be resolved in a test file after adding it, run:

```bash
dotnet add DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj package Microsoft.Data.Sqlite
```

- [ ] **Step 2: Create `TrackerFixture.cs`**

```csharp
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DndMcpAICsharpFun.Tests.Infrastructure.Tracking;

public sealed class TrackerFixture : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<IngestionDbContext> _options;

    public TrackerFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<IngestionDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = new IngestionDbContext(_options);
        db.Database.Migrate();
    }

    public SqliteIngestionTracker CreateTracker()
    {
        var db = new IngestionDbContext(_options);
        return new SqliteIngestionTracker(db);
    }

    public static IngestionRecord SampleRecord() => new()
    {
        FilePath = "/tmp/test.pdf",
        FileName = "test.pdf",
        FileHash = string.Empty,
        SourceName = "PHB",
        Version = "5e",
        DisplayName = "Player's Handbook",
        Status = IngestionStatus.Pending,
    };

    public void Dispose() => _connection.Dispose();
}
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add DndMcpAICsharpFun.Tests/Infrastructure/Tracking/TrackerFixture.cs
git commit -m "test: add TrackerFixture for in-memory SQLite tracker tests"
```

---

## Task 3: SqliteIngestionTrackerTests — 10 tests

Each test creates a fresh `TrackerFixture` (one connection per test class, fresh tracker per operation) to ensure complete isolation.

**Files:**
- Create: `DndMcpAICsharpFun.Tests/Infrastructure/Tracking/SqliteIngestionTrackerTests.cs`

- [ ] **Step 1: Create the test class skeleton**

```csharp
using DndMcpAICsharpFun.Infrastructure.Sqlite;

namespace DndMcpAICsharpFun.Tests.Infrastructure.Tracking;

public sealed class SqliteIngestionTrackerTests : IDisposable
{
    private readonly TrackerFixture _fixture = new();
    public void Dispose() => _fixture.Dispose();
}
```

- [ ] **Step 2: Run to verify zero tests compile and pass**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "SqliteIngestionTrackerTests" --no-build
```

Expected: 0 tests run, 0 failures.

- [ ] **Step 3: Write `CreateAsync_AssignsId_AndReturnsRecord`**

```csharp
[Fact]
public async Task CreateAsync_AssignsId_AndReturnsRecord()
{
    var tracker = _fixture.CreateTracker();
    var record = TrackerFixture.SampleRecord();

    var created = await tracker.CreateAsync(record);

    Assert.True(created.Id > 0);
    Assert.Equal("Player's Handbook", created.DisplayName);
    Assert.Equal(IngestionStatus.Pending, created.Status);
}
```

- [ ] **Step 4: Write `GetByIdAsync_ExistingId_ReturnsRecord`**

```csharp
[Fact]
public async Task GetByIdAsync_ExistingId_ReturnsRecord()
{
    var writer = _fixture.CreateTracker();
    var created = await writer.CreateAsync(TrackerFixture.SampleRecord());

    var reader = _fixture.CreateTracker();
    var found = await reader.GetByIdAsync(created.Id);

    Assert.NotNull(found);
    Assert.Equal(created.Id, found.Id);
    Assert.Equal("Player's Handbook", found.DisplayName);
}
```

- [ ] **Step 5: Write `GetByIdAsync_MissingId_ReturnsNull`**

```csharp
[Fact]
public async Task GetByIdAsync_MissingId_ReturnsNull()
{
    var tracker = _fixture.CreateTracker();

    var result = await tracker.GetByIdAsync(99999);

    Assert.Null(result);
}
```

- [ ] **Step 6: Write `GetAllAsync_ReturnsAllRecords`**

```csharp
[Fact]
public async Task GetAllAsync_ReturnsAllRecords()
{
    var writer = _fixture.CreateTracker();
    await writer.CreateAsync(TrackerFixture.SampleRecord());
    await writer.CreateAsync(TrackerFixture.SampleRecord() with { DisplayName = "DMG" });

    var reader = _fixture.CreateTracker();
    var all = await reader.GetAllAsync();

    Assert.Equal(2, all.Count);
}
```

- [ ] **Step 7: Write `MarkHashAsync_SetsProcessingAndHash`**

```csharp
[Fact]
public async Task MarkHashAsync_SetsProcessingAndHash()
{
    var writer = _fixture.CreateTracker();
    var created = await writer.CreateAsync(TrackerFixture.SampleRecord());

    var updater = _fixture.CreateTracker();
    await updater.MarkHashAsync(created.Id, "abc123def456");

    var reader = _fixture.CreateTracker();
    var updated = await reader.GetByIdAsync(created.Id);
    Assert.NotNull(updated);
    Assert.Equal("abc123def456", updated.FileHash);
    Assert.Equal(IngestionStatus.Processing, updated.Status);
}
```

- [ ] **Step 8: Write `MarkExtractedAsync_SetsStatusExtracted`**

```csharp
[Fact]
public async Task MarkExtractedAsync_SetsStatusExtracted()
{
    var writer = _fixture.CreateTracker();
    var created = await writer.CreateAsync(TrackerFixture.SampleRecord());

    var updater = _fixture.CreateTracker();
    await updater.MarkExtractedAsync(created.Id);

    var reader = _fixture.CreateTracker();
    var updated = await reader.GetByIdAsync(created.Id);
    Assert.NotNull(updated);
    Assert.Equal(IngestionStatus.Extracted, updated.Status);
}
```

- [ ] **Step 9: Write `MarkJsonIngestedAsync_SetsStatusAndChunkCount`**

```csharp
[Fact]
public async Task MarkJsonIngestedAsync_SetsStatusAndChunkCount()
{
    var writer = _fixture.CreateTracker();
    var created = await writer.CreateAsync(TrackerFixture.SampleRecord());

    var updater = _fixture.CreateTracker();
    await updater.MarkJsonIngestedAsync(created.Id, chunkCount: 42);

    var reader = _fixture.CreateTracker();
    var updated = await reader.GetByIdAsync(created.Id);
    Assert.NotNull(updated);
    Assert.Equal(IngestionStatus.JsonIngested, updated.Status);
    Assert.Equal(42, updated.ChunkCount);
}
```

- [ ] **Step 10: Write `MarkFailedAsync_SetsStatusAndError`**

```csharp
[Fact]
public async Task MarkFailedAsync_SetsStatusAndError()
{
    var writer = _fixture.CreateTracker();
    var created = await writer.CreateAsync(TrackerFixture.SampleRecord());

    var updater = _fixture.CreateTracker();
    await updater.MarkFailedAsync(created.Id, "LLM timeout");

    var reader = _fixture.CreateTracker();
    var updated = await reader.GetByIdAsync(created.Id);
    Assert.NotNull(updated);
    Assert.Equal(IngestionStatus.Failed, updated.Status);
    Assert.Equal("LLM timeout", updated.Error);
}
```

- [ ] **Step 11: Write `ResetForReingestionAsync_ResetsStatusToPending`**

```csharp
[Fact]
public async Task ResetForReingestionAsync_ResetsStatusToPending()
{
    var writer = _fixture.CreateTracker();
    var created = await writer.CreateAsync(TrackerFixture.SampleRecord());
    var failer = _fixture.CreateTracker();
    await failer.MarkFailedAsync(created.Id, "some error");

    var resetter = _fixture.CreateTracker();
    await resetter.ResetForReingestionAsync(created.Id);

    var reader = _fixture.CreateTracker();
    var updated = await reader.GetByIdAsync(created.Id);
    Assert.NotNull(updated);
    Assert.Equal(IngestionStatus.Pending, updated.Status);
    Assert.Null(updated.Error);
    Assert.Null(updated.ChunkCount);
}
```

- [ ] **Step 12: Write `DeleteAsync_RemovesRecord`**

```csharp
[Fact]
public async Task DeleteAsync_RemovesRecord()
{
    var writer = _fixture.CreateTracker();
    var created = await writer.CreateAsync(TrackerFixture.SampleRecord());

    var deleter = _fixture.CreateTracker();
    var deleted = await deleter.DeleteAsync(created.Id);

    var reader = _fixture.CreateTracker();
    var found = await reader.GetByIdAsync(created.Id);
    Assert.True(deleted);
    Assert.Null(found);
}
```

- [ ] **Step 13: Run all tracker tests**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "SqliteIngestionTrackerTests"
```

Expected: 10 tests pass, 0 failures.

- [ ] **Step 14: Commit**

```bash
git add DndMcpAICsharpFun.Tests/Infrastructure/Tracking/SqliteIngestionTrackerTests.cs
git commit -m "test: add SqliteIngestionTracker tests with in-memory SQLite"
```

---

## Task 4: OllamaLlmClassifierTests — 5 tests

Pure unit tests. The classifier uses `IOllamaApiClient` (after Task 1 change). The LLM response format is `{"types": ["Spell", "Monster"]}` — a JSON object with a `types` key. The classifier also accepts bare arrays `["Spell"]` as fallback.

**Files:**
- Create: `DndMcpAICsharpFun.Tests/Ingestion/Extraction/OllamaLlmClassifierTests.cs`

- [ ] **Step 1: Create the test file with helpers**

```csharp
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using Microsoft.Extensions.Logging.Abstractions;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace DndMcpAICsharpFun.Tests.Ingestion.Extraction;

public sealed class OllamaLlmClassifierTests
{
    private static OllamaLlmClassifier BuildSut(IOllamaApiClient ollama) =>
        new(ollama,
            Options.Create(new OllamaOptions { ExtractionModel = "llama3.2" }),
            NullLogger<OllamaLlmClassifier>.Instance);

    private static IAsyncEnumerable<ChatResponseStream?> StreamResponse(string content) =>
        YieldItems(new ChatResponseStream { Message = new Message { Content = content } });

    private static async IAsyncEnumerable<T> YieldItems<T>(params T[] items)
    {
        foreach (var item in items) yield return item;
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<ChatResponseStream?> ThrowingStream()
    {
        await Task.CompletedTask;
        throw new OperationCanceledException();
        yield break;
    }
}
```

- [ ] **Step 2: Write `ClassifyPageAsync_ValidJsonObject_ReturnsCategories`**

```csharp
[Fact]
public async Task ClassifyPageAsync_ValidJsonObject_ReturnsCategories()
{
    var ollama = Substitute.For<IOllamaApiClient>();
    ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
        .Returns(StreamResponse("""{"types":["Spell","Monster"]}"""));
    var sut = BuildSut(ollama);

    var result = await sut.ClassifyPageAsync("some page text");

    Assert.Equal(2, result.Count);
    Assert.Contains("Spell", result);
    Assert.Contains("Monster", result);
}
```

- [ ] **Step 3: Write `ClassifyPageAsync_EmptyTypesArray_ReturnsEmptyList`**

```csharp
[Fact]
public async Task ClassifyPageAsync_EmptyTypesArray_ReturnsEmptyList()
{
    var ollama = Substitute.For<IOllamaApiClient>();
    ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
        .Returns(StreamResponse("""{"types":[]}"""));
    var sut = BuildSut(ollama);

    var result = await sut.ClassifyPageAsync("some page text");

    Assert.Empty(result);
}
```

- [ ] **Step 4: Write `ClassifyPageAsync_InvalidJson_ReturnsEmptyList`**

```csharp
[Fact]
public async Task ClassifyPageAsync_InvalidJson_ReturnsEmptyList()
{
    var ollama = Substitute.For<IOllamaApiClient>();
    ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
        .Returns(StreamResponse("not json at all"));
    var sut = BuildSut(ollama);

    var result = await sut.ClassifyPageAsync("some page text");

    Assert.Empty(result);
}
```

- [ ] **Step 5: Write `ClassifyPageAsync_EmptyStringResponse_ReturnsEmptyList`**

```csharp
[Fact]
public async Task ClassifyPageAsync_EmptyStringResponse_ReturnsEmptyList()
{
    var ollama = Substitute.For<IOllamaApiClient>();
    ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
        .Returns(StreamResponse(string.Empty));
    var sut = BuildSut(ollama);

    var result = await sut.ClassifyPageAsync("some page text");

    Assert.Empty(result);
}
```

- [ ] **Step 6: Write `ClassifyPageAsync_CancelledToken_ThrowsOperationCancelled`**

```csharp
[Fact]
public async Task ClassifyPageAsync_CancelledToken_ThrowsOperationCancelled()
{
    var ollama = Substitute.For<IOllamaApiClient>();
    ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
        .Returns(ThrowingStream());
    var sut = BuildSut(ollama);

    await Assert.ThrowsAsync<OperationCanceledException>(
        () => sut.ClassifyPageAsync("some page text", CancellationToken.None));
}
```

- [ ] **Step 7: Run all classifier tests**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "OllamaLlmClassifierTests"
```

Expected: 5 tests pass, 0 failures.

- [ ] **Step 8: Commit**

```bash
git add DndMcpAICsharpFun.Tests/Ingestion/Extraction/OllamaLlmClassifierTests.cs
git commit -m "test: add OllamaLlmClassifier unit tests"
```

---

## Task 5: BooksAdminEndpointsTests — helper + 19 tests

Follow the exact pattern from `CancelExtractEndpointTests.cs`: `WebApplication.CreateBuilder()` + `UseTestServer()`. The admin middleware is NOT added here — tests call endpoints directly without any API key header. All external services are NSubstitute mocks.

**Files:**
- Create: `DndMcpAICsharpFun.Tests/Admin/BooksAdminEndpointsTests.cs`

- [ ] **Step 1: Create the test file with `BuildClientAsync` helper**

```csharp
using System.Net;
using System.Net.Http.Json;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DndMcpAICsharpFun.Tests.Admin;

public sealed class BooksAdminEndpointsTests
{
    private static async Task<(
        HttpClient Client,
        IIngestionTracker Tracker,
        IIngestionQueue Queue,
        IIngestionOrchestrator Orchestrator,
        IEntityJsonStore JsonStore,
        IExtractionCancellationRegistry Registry)> BuildClientAsync()
    {
        var tracker = Substitute.For<IIngestionTracker>();
        var queue = Substitute.For<IIngestionQueue>();
        var orchestrator = Substitute.For<IIngestionOrchestrator>();
        var jsonStore = Substitute.For<IEntityJsonStore>();
        var registry = Substitute.For<IExtractionCancellationRegistry>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(tracker);
        builder.Services.AddSingleton(queue);
        builder.Services.AddSingleton(orchestrator);
        builder.Services.AddSingleton(jsonStore);
        builder.Services.AddSingleton(registry);
        builder.Services.AddSingleton<ILogger<RegisterBookRequest>>(
            NullLogger<RegisterBookRequest>.Instance);
        builder.Services.AddSingleton<ILogger<RegisterBookByPathRequest>>(
            NullLogger<RegisterBookByPathRequest>.Instance);
        builder.Services.Configure<AdminOptions>(o => o.ApiKey = "test-key");
        builder.Services.Configure<IngestionOptions>(o => o.BooksPath = Path.GetTempPath());

        var app = builder.Build();
        // No middleware — bypass auth, test endpoint logic only
        app.MapGroup("/admin").MapBooksAdmin();

        await app.StartAsync();
        return (app.GetTestClient(), tracker, queue, orchestrator, jsonStore, registry);
    }

    private static IngestionRecord MakeRecord(
        int id = 1,
        IngestionStatus status = IngestionStatus.Pending) => new()
    {
        Id = id,
        FilePath = "/tmp/test.pdf",
        FileName = "test.pdf",
        FileHash = string.Empty,
        SourceName = "PHB",
        Version = "5e",
        DisplayName = "Player's Handbook",
        Status = status,
    };
}
```

- [ ] **Step 2: Write register-book tests (file upload)**

```csharp
[Fact]
public async Task RegisterBook_ValidPdf_Returns202()
{
    var (client, tracker, _, _, _, _) = await BuildClientAsync();
    var record = MakeRecord();
    tracker.CreateAsync(Arg.Any<IngestionRecord>(), Arg.Any<CancellationToken>())
        .Returns(record);

    using var content = new MultipartFormDataContent();
    content.Add(new ByteArrayContent([0x25, 0x50, 0x44, 0x46]), "file", "test.pdf");
    content.Add(new StringContent("PHB"), "sourceName");
    content.Add(new StringContent("5e"), "version");
    content.Add(new StringContent("Player's Handbook"), "displayName");

    var response = await client.PostAsync("/admin/books/register", content);

    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
}

[Fact]
public async Task RegisterBook_NonPdfExtension_Returns400()
{
    var (client, _, _, _, _, _) = await BuildClientAsync();

    using var content = new MultipartFormDataContent();
    content.Add(new ByteArrayContent([0x00]), "file", "test.docx");
    content.Add(new StringContent("PHB"), "sourceName");
    content.Add(new StringContent("5e"), "version");
    content.Add(new StringContent("PHB"), "displayName");

    var response = await client.PostAsync("/admin/books/register", content);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}

[Fact]
public async Task RegisterBook_InvalidVersion_Returns400()
{
    var (client, _, _, _, _, _) = await BuildClientAsync();

    using var content = new MultipartFormDataContent();
    content.Add(new ByteArrayContent([0x25, 0x50, 0x44, 0x46]), "file", "test.pdf");
    content.Add(new StringContent("PHB"), "sourceName");
    content.Add(new StringContent("invalid_version"), "version");
    content.Add(new StringContent("PHB"), "displayName");

    var response = await client.PostAsync("/admin/books/register", content);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}
```

- [ ] **Step 3: Write register-by-path tests**

```csharp
[Fact]
public async Task RegisterBookByPath_ValidPath_Returns202()
{
    var (client, tracker, _, _, _, _) = await BuildClientAsync();
    var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
    await File.WriteAllBytesAsync(tempFile, [0x25, 0x50, 0x44, 0x46]);
    try
    {
        tracker.CreateAsync(Arg.Any<IngestionRecord>(), Arg.Any<CancellationToken>())
            .Returns(MakeRecord());

        var response = await client.PostAsJsonAsync("/admin/books/register-path", new
        {
            filePath = tempFile,
            sourceName = "PHB",
            version = "5e",
            displayName = "Player's Handbook"
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }
    finally { File.Delete(tempFile); }
}

[Fact]
public async Task RegisterBookByPath_FileNotFound_Returns400()
{
    var (client, _, _, _, _, _) = await BuildClientAsync();

    var response = await client.PostAsJsonAsync("/admin/books/register-path", new
    {
        filePath = "/nonexistent/path/file.pdf",
        sourceName = "PHB",
        version = "5e",
        displayName = "PHB"
    });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}

[Fact]
public async Task RegisterBookByPath_InvalidVersion_Returns400()
{
    var (client, _, _, _, _, _) = await BuildClientAsync();
    var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
    await File.WriteAllBytesAsync(tempFile, [0x25, 0x50, 0x44, 0x46]);
    try
    {
        var response = await client.PostAsJsonAsync("/admin/books/register-path", new
        {
            filePath = tempFile,
            sourceName = "PHB",
            version = "bad_version",
            displayName = "PHB"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    finally { File.Delete(tempFile); }
}
```

- [ ] **Step 5: Write get-all-books test**

```csharp
[Fact]
public async Task GetAllBooks_Returns200WithList()
{
    var (client, tracker, _, _, _, _) = await BuildClientAsync();
    tracker.GetAllAsync(Arg.Any<CancellationToken>())
        .Returns([MakeRecord(1), MakeRecord(2)]);

    var response = await client.GetAsync("/admin/books");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}
```

- [ ] **Step 6: Write extract tests**

```csharp
[Fact]
public async Task ExtractBook_NotFound_Returns404()
{
    var (client, tracker, _, _, _, _) = await BuildClientAsync();
    tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns((IngestionRecord?)null);

    var response = await client.PostAsync("/admin/books/1/extract", null);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
}

[Fact]
public async Task ExtractBook_AlreadyProcessing_Returns409()
{
    var (client, tracker, _, _, _, _) = await BuildClientAsync();
    tracker.GetByIdAsync(1, Arg.Any<CancellationToken>())
        .Returns(MakeRecord(1, IngestionStatus.Processing));

    var response = await client.PostAsync("/admin/books/1/extract", null);

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
}

[Fact]
public async Task ExtractBook_Success_Returns202AndEnqueues()
{
    var (client, tracker, queue, _, _, _) = await BuildClientAsync();
    tracker.GetByIdAsync(1, Arg.Any<CancellationToken>())
        .Returns(MakeRecord(1, IngestionStatus.Pending));

    var response = await client.PostAsync("/admin/books/1/extract", null);

    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    queue.Received(1).TryEnqueue(Arg.Is<IngestionWorkItem>(w =>
        w.Type == IngestionWorkType.Extract && w.BookId == 1));
}
```

- [ ] **Step 7: Write get-extracted tests**

```csharp
[Fact]
public async Task GetExtracted_NotFound_Returns404()
{
    var (client, tracker, _, _, _, _) = await BuildClientAsync();
    tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns((IngestionRecord?)null);

    var response = await client.GetAsync("/admin/books/1/extracted");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
}

[Fact]
public async Task GetExtracted_Success_Returns200()
{
    var (client, tracker, _, _, jsonStore, _) = await BuildClientAsync();
    tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(MakeRecord(1));
    jsonStore.ListPageFiles(1).Returns(["page_1.json", "page_2.json"]);

    var response = await client.GetAsync("/admin/books/1/extracted");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}
```

- [ ] **Step 8: Write ingest-json tests**

```csharp
[Fact]
public async Task IngestJson_NotFound_Returns404()
{
    var (client, tracker, _, _, _, _) = await BuildClientAsync();
    tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns((IngestionRecord?)null);

    var response = await client.PostAsync("/admin/books/1/ingest-json", null);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
}

[Fact]
public async Task IngestJson_AlreadyProcessing_Returns409()
{
    var (client, tracker, _, _, _, _) = await BuildClientAsync();
    tracker.GetByIdAsync(1, Arg.Any<CancellationToken>())
        .Returns(MakeRecord(1, IngestionStatus.Processing));

    var response = await client.PostAsync("/admin/books/1/ingest-json", null);

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
}

[Fact]
public async Task IngestJson_Success_Returns202()
{
    var (client, tracker, queue, _, _, _) = await BuildClientAsync();
    tracker.GetByIdAsync(1, Arg.Any<CancellationToken>())
        .Returns(MakeRecord(1, IngestionStatus.Extracted));

    var response = await client.PostAsync("/admin/books/1/ingest-json", null);

    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    queue.Received(1).TryEnqueue(Arg.Is<IngestionWorkItem>(w =>
        w.Type == IngestionWorkType.IngestJson && w.BookId == 1));
}
```

- [ ] **Step 9: Write delete tests**

```csharp
[Fact]
public async Task DeleteBook_NotFound_Returns404()
{
    var (client, _, _, orchestrator, _, _) = await BuildClientAsync();
    orchestrator.DeleteBookAsync(1, Arg.Any<CancellationToken>())
        .Returns(DeleteBookResult.NotFound);

    var response = await client.DeleteAsync("/admin/books/1");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
}

[Fact]
public async Task DeleteBook_Conflict_Returns409()
{
    var (client, _, _, orchestrator, _, _) = await BuildClientAsync();
    orchestrator.DeleteBookAsync(1, Arg.Any<CancellationToken>())
        .Returns(DeleteBookResult.Conflict);

    var response = await client.DeleteAsync("/admin/books/1");

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
}

[Fact]
public async Task DeleteBook_Success_Returns204()
{
    var (client, _, _, orchestrator, _, _) = await BuildClientAsync();
    orchestrator.DeleteBookAsync(1, Arg.Any<CancellationToken>())
        .Returns(DeleteBookResult.Deleted);

    var response = await client.DeleteAsync("/admin/books/1");

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
}
```

- [ ] **Step 10: Write cancel-extract tests**

```csharp
[Fact]
public async Task CancelExtract_NotFound_Returns404()
{
    var (client, _, _, _, _, registry) = await BuildClientAsync();
    registry.Cancel(1).Returns(false);

    var response = await client.PostAsync("/admin/books/1/cancel-extract", null);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
}

[Fact]
public async Task CancelExtract_Success_Returns200()
{
    var (client, _, _, _, _, registry) = await BuildClientAsync();
    registry.Cancel(1).Returns(true);

    var response = await client.PostAsync("/admin/books/1/cancel-extract", null);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}
```

- [ ] **Step 11: Run all endpoint tests**

```bash
dotnet test DndMcpAICsharpFun.Tests --filter "BooksAdminEndpointsTests"
```

Expected: 19 tests pass, 0 failures.

- [ ] **Step 12: Run the full test suite**

```bash
dotnet test DndMcpAICsharpFun.Tests
```

Expected: all tests pass, 0 failures.

- [ ] **Step 13: Commit**

```bash
git add DndMcpAICsharpFun.Tests/Admin/BooksAdminEndpointsTests.cs
git commit -m "test: add BooksAdminEndpoints HTTP integration tests"
```
