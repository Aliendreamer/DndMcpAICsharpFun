# Delete Book Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `DELETE /admin/books/{id}` that fully removes a book's SQLite record, disk file, and Qdrant vectors, plus a hash-first refactor of `IngestBookAsync` that adds duplicate detection.

**Architecture:** `IIngestionOrchestrator` gains `DeleteBookAsync` returning a `DeleteBookResult` enum; the endpoint maps that to 204/404/409. `IngestBookAsync` is restructured to compute the hash once at entry, store it immediately via `MarkHashAsync`, then check for duplicates before doing any extraction. `IVectorStoreService` gains `DeleteByHashAsync` which reconstructs deterministic point IDs and deletes them in one batch.

**Tech Stack:** ASP.NET Core Minimal APIs, Entity Framework Core (SQLite), Qdrant.Client 1.17.0 (grpc), xUnit, NSubstitute

---

## File map

| Action | Path |
|--------|------|
| Modify | `Features/Ingestion/Tracking/IIngestionTracker.cs` |
| Modify | `Features/Ingestion/Tracking/SqliteIngestionTracker.cs` |
| Modify | `Features/VectorStore/IVectorStoreService.cs` |
| Modify | `Features/VectorStore/QdrantVectorStoreService.cs` |
| Create | `Features/Ingestion/DeleteBookResult.cs` |
| Modify | `Features/Ingestion/IIngestionOrchestrator.cs` |
| Modify | `Features/Ingestion/IngestionOrchestrator.cs` |
| Modify | `Features/Admin/BooksAdminEndpoints.cs` |
| Modify | `DndMcpAICsharpFun.http` |
| Modify | `DndMcpAICsharpFun.Tests/Ingestion/IngestionOrchestratorTests.cs` |

---

## Task 1: Tracker — MarkHashAsync

Add `MarkHashAsync` to the tracker: sets `FileHash` and `Status = Processing` in one DB call, replacing the two-step pattern of "mark processing then store hash".

**Files:**
- Modify: `Features/Ingestion/Tracking/IIngestionTracker.cs`
- Modify: `Features/Ingestion/Tracking/SqliteIngestionTracker.cs`

- [ ] **Step 1: Add to `IIngestionTracker`**

```csharp
// In IIngestionTracker.cs — add alongside existing Mark* methods
Task MarkHashAsync(int id, string fileHash, CancellationToken ct = default);
```

- [ ] **Step 2: Implement in `SqliteIngestionTracker`**

```csharp
public async Task MarkHashAsync(int id, string fileHash, CancellationToken ct = default)
{
    await db.IngestionRecords
        .Where(r => r.Id == id)
        .ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, IngestionStatus.Processing)
            .SetProperty(r => r.FileHash, fileHash), ct);
}
```

- [ ] **Step 3: Build**

```bash
dotnet build
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add Features/Ingestion/Tracking/IIngestionTracker.cs Features/Ingestion/Tracking/SqliteIngestionTracker.cs
git commit -m "feat: add MarkHashAsync to IIngestionTracker"
```

---

## Task 2: Tracker — duplicate detection and delete

Add `GetCompletedByHashAsync`, `MarkDuplicateAsync`, and `DeleteAsync` to the tracker.

**Files:**
- Modify: `Features/Ingestion/Tracking/IIngestionTracker.cs`
- Modify: `Features/Ingestion/Tracking/SqliteIngestionTracker.cs`

- [ ] **Step 1: Add three methods to `IIngestionTracker`**

```csharp
// Add all three alongside existing methods
Task<IngestionRecord?> GetCompletedByHashAsync(string hash, int excludeId, CancellationToken ct = default);
Task MarkDuplicateAsync(int id, CancellationToken ct = default);
Task<bool> DeleteAsync(int id, CancellationToken ct = default);
```

- [ ] **Step 2: Implement all three in `SqliteIngestionTracker`**

```csharp
public Task<IngestionRecord?> GetCompletedByHashAsync(string hash, int excludeId, CancellationToken ct = default) =>
    db.IngestionRecords.FirstOrDefaultAsync(
        r => r.FileHash == hash && r.Status == IngestionStatus.Completed && r.Id != excludeId, ct);

public async Task MarkDuplicateAsync(int id, CancellationToken ct = default)
{
    await db.IngestionRecords
        .Where(r => r.Id == id)
        .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, IngestionStatus.Duplicate), ct);
}

public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
{
    var deleted = await db.IngestionRecords
        .Where(r => r.Id == id && r.Status != IngestionStatus.Processing)
        .ExecuteDeleteAsync(ct);
    return deleted > 0;
}
```

- [ ] **Step 3: Build**

```bash
dotnet build
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add Features/Ingestion/Tracking/IIngestionTracker.cs Features/Ingestion/Tracking/SqliteIngestionTracker.cs
git commit -m "feat: add duplicate detection and delete methods to IIngestionTracker"
```

---

## Task 3: Vector store — DeleteByHashAsync

Add `DeleteByHashAsync` to `IVectorStoreService` and implement it in `QdrantVectorStoreService` using the existing `DerivePointId` helper and Qdrant's `IReadOnlyList<Guid>` delete overload.

**Files:**
- Modify: `Features/VectorStore/IVectorStoreService.cs`
- Modify: `Features/VectorStore/QdrantVectorStoreService.cs`

- [ ] **Step 1: Add to `IVectorStoreService`**

```csharp
// IVectorStoreService.cs
Task DeleteByHashAsync(string fileHash, int chunkCount, CancellationToken ct = default);
```

- [ ] **Step 2: Implement in `QdrantVectorStoreService`**

Add below the `UpsertAsync` method, before `BuildPoint`:

```csharp
public async Task DeleteByHashAsync(string fileHash, int chunkCount, CancellationToken ct = default)
{
    var ids = Enumerable.Range(0, chunkCount)
        .Select(i => DerivePointId(fileHash, i))
        .ToList();
    await _client.DeleteAsync(_collectionName, ids, cancellationToken: ct);
}
```

- [ ] **Step 3: Build**

```bash
dotnet build
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add Features/VectorStore/IVectorStoreService.cs Features/VectorStore/QdrantVectorStoreService.cs
git commit -m "feat: add DeleteByHashAsync to IVectorStoreService"
```

---

## Task 4: DeleteBookResult enum

Create the result type used by `DeleteBookAsync`.

**Files:**
- Create: `Features/Ingestion/DeleteBookResult.cs`

- [ ] **Step 1: Create the file**

```csharp
namespace DndMcpAICsharpFun.Features.Ingestion;

public enum DeleteBookResult
{
    Deleted,
    NotFound,
    Conflict,
}
```

- [ ] **Step 2: Build**

```bash
dotnet build
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add Features/Ingestion/DeleteBookResult.cs
git commit -m "feat: add DeleteBookResult enum"
```

---

## Task 5: Orchestrator — DeleteBookAsync interface

Extend `IIngestionOrchestrator` and inject `IVectorStoreService` into `IngestionOrchestrator`.

**Files:**
- Modify: `Features/Ingestion/IIngestionOrchestrator.cs`
- Modify: `Features/Ingestion/IngestionOrchestrator.cs`

- [ ] **Step 1: Add method to `IIngestionOrchestrator`**

```csharp
// IIngestionOrchestrator.cs
namespace DndMcpAICsharpFun.Features.Ingestion;

public interface IIngestionOrchestrator
{
    Task IngestBookAsync(int recordId, CancellationToken cancellationToken = default);
    Task<DeleteBookResult> DeleteBookAsync(int id, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Add `IVectorStoreService` to the `IngestionOrchestrator` constructor**

Change the constructor primary-parameters list (top of class):

```csharp
public sealed partial class IngestionOrchestrator(
    IIngestionTracker tracker,
    IPdfTextExtractor extractor,
    DndChunker chunker,
    IEmbeddingIngestor embeddingIngestor,
    IVectorStoreService vectorStore,
    ILogger<IngestionOrchestrator> logger) : IIngestionOrchestrator
```

Also add the missing using at the top of the file:

```csharp
using DndMcpAICsharpFun.Features.VectorStore;
```

- [ ] **Step 3: Build (will fail — `DeleteBookAsync` not yet implemented)**

```bash
dotnet build
```
Expected: error `CS0535` — `IngestionOrchestrator` does not implement `DeleteBookAsync`. That's fine — proceed to the next step.

---

## Task 6: Orchestrator — implement DeleteBookAsync

Add the `DeleteBookAsync` implementation and its log message.

**Files:**
- Modify: `Features/Ingestion/IngestionOrchestrator.cs`

- [ ] **Step 1: Write the failing test first**

In `DndMcpAICsharpFun.Tests/Ingestion/IngestionOrchestratorTests.cs`, add the following tests inside the `IngestionOrchestratorTests` class. Also add `IVectorStoreService` mock field and update `BuildSut()`:

```csharp
// Add new field alongside existing mocks
private readonly IVectorStoreService _vectorStore = Substitute.For<IVectorStoreService>();

// Replace BuildSut() with:
private IngestionOrchestrator BuildSut()
{
    var detectors = new IPatternDetector[] { new SpellPatternDetector() };
    var detector = new ContentCategoryDetector(detectors);
    var opts = Options.Create(new IngestionOptions { MaxChunkTokens = 512, OverlapTokens = 64 });
    var chunker = new DndChunker(detector, opts);
    return new IngestionOrchestrator(
        _tracker, _extractor, chunker, _embeddingIngestor, _vectorStore,
        NullLogger<IngestionOrchestrator>.Instance);
}
```

Add these test methods:

```csharp
[Fact]
public async Task DeleteBookAsync_RecordNotFound_ReturnsNotFound()
{
    _tracker.GetByIdAsync(99, Arg.Any<CancellationToken>()).Returns((IngestionRecord?)null);
    var sut = BuildSut();

    var result = await sut.DeleteBookAsync(99);

    Assert.Equal(DeleteBookResult.NotFound, result);
    await _vectorStore.DidNotReceive().DeleteByHashAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
}

[Fact]
public async Task DeleteBookAsync_RecordProcessing_ReturnsConflict()
{
    var record = new IngestionRecord
    {
        Id = 1, FilePath = _tempFile, FileName = "test.pdf",
        FileHash = "abc", SourceName = "PHB", Version = "Edition2014",
        DisplayName = "PHB", Status = IngestionStatus.Processing
    };
    _tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(record);
    var sut = BuildSut();

    var result = await sut.DeleteBookAsync(1);

    Assert.Equal(DeleteBookResult.Conflict, result);
    await _vectorStore.DidNotReceive().DeleteByHashAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
}

[Fact]
public async Task DeleteBookAsync_CompletedRecord_DeletesVectorsFileSqlite()
{
    var record = new IngestionRecord
    {
        Id = 2, FilePath = _tempFile, FileName = "test.pdf",
        FileHash = "abc123", SourceName = "PHB", Version = "Edition2014",
        DisplayName = "PHB", Status = IngestionStatus.Completed, ChunkCount = 10
    };
    _tracker.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(record);
    _tracker.DeleteAsync(2, Arg.Any<CancellationToken>()).Returns(true);
    var sut = BuildSut();

    var result = await sut.DeleteBookAsync(2);

    Assert.Equal(DeleteBookResult.Deleted, result);
    await _vectorStore.Received(1).DeleteByHashAsync("abc123", 10, Arg.Any<CancellationToken>());
    await _tracker.Received(1).DeleteAsync(2, Arg.Any<CancellationToken>());
}

[Fact]
public async Task DeleteBookAsync_PendingRecord_SkipsVectorsDeletesSqlite()
{
    var record = new IngestionRecord
    {
        Id = 3, FilePath = _tempFile, FileName = "test.pdf",
        FileHash = string.Empty, SourceName = "PHB", Version = "Edition2014",
        DisplayName = "PHB", Status = IngestionStatus.Pending
    };
    _tracker.GetByIdAsync(3, Arg.Any<CancellationToken>()).Returns(record);
    _tracker.DeleteAsync(3, Arg.Any<CancellationToken>()).Returns(true);
    var sut = BuildSut();

    var result = await sut.DeleteBookAsync(3);

    Assert.Equal(DeleteBookResult.Deleted, result);
    await _vectorStore.DidNotReceive().DeleteByHashAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    await _tracker.Received(1).DeleteAsync(3, Arg.Any<CancellationToken>());
}
```

Also add the required using at the top of the test file:

```csharp
using DndMcpAICsharpFun.Features.VectorStore;
```

- [ ] **Step 2: Run tests — expect failures**

```bash
dotnet test --filter "ClassName=DndMcpAICsharpFun.Tests.Ingestion.IngestionOrchestratorTests"
```
Expected: compile error or test failures for the new Delete tests.

- [ ] **Step 3: Implement `DeleteBookAsync` in `IngestionOrchestrator`**

Add the method and its log message at the bottom of the class, after `IngestBookAsync`:

```csharp
public async Task<DeleteBookResult> DeleteBookAsync(int id, CancellationToken cancellationToken = default)
{
    var record = await tracker.GetByIdAsync(id, cancellationToken);
    if (record is null)
        return DeleteBookResult.NotFound;

    if (record.Status == IngestionStatus.Processing)
        return DeleteBookResult.Conflict;

    if (record.Status == IngestionStatus.Completed && record.ChunkCount.HasValue)
        await vectorStore.DeleteByHashAsync(record.FileHash, record.ChunkCount.Value, cancellationToken);

    if (File.Exists(record.FilePath))
        File.Delete(record.FilePath);

    await tracker.DeleteAsync(id, cancellationToken);
    LogBookDeleted(logger, record.DisplayName, id);
    return DeleteBookResult.Deleted;
}
```

Add the log message alongside existing ones:

```csharp
[LoggerMessage(Level = LogLevel.Information, Message = "Deleted book {DisplayName} (id={Id})")]
private static partial void LogBookDeleted(ILogger logger, string displayName, int id);
```

- [ ] **Step 4: Run tests — all pass**

```bash
dotnet test --filter "ClassName=DndMcpAICsharpFun.Tests.Ingestion.IngestionOrchestratorTests"
```
Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add Features/Ingestion/IIngestionOrchestrator.cs Features/Ingestion/IngestionOrchestrator.cs \
        Features/Ingestion/DeleteBookResult.cs \
        DndMcpAICsharpFun.Tests/Ingestion/IngestionOrchestratorTests.cs
git commit -m "feat: implement DeleteBookAsync on IngestionOrchestrator"
```

---

## Task 7: Orchestrator — hash-first refactor + duplicate detection

Restructure `IngestBookAsync`: compute hash first, call `MarkHashAsync`, check for duplicates, then proceed with extraction. Remove the second hash computation.

**Files:**
- Modify: `Features/Ingestion/IngestionOrchestrator.cs`
- Modify: `DndMcpAICsharpFun.Tests/Ingestion/IngestionOrchestratorTests.cs`

- [ ] **Step 1: Write the failing tests for duplicate detection**

Add to `IngestionOrchestratorTests`:

```csharp
[Fact]
public async Task IngestBookAsync_DuplicateHash_MarksDuplicateAndSkipsExtraction()
{
    var record = new IngestionRecord
    {
        Id = 10, FilePath = _tempFile, FileName = "test.pdf",
        FileHash = string.Empty, SourceName = "PHB", Version = "Edition2014",
        DisplayName = "PHB", Status = IngestionStatus.Pending
    };
    var existing = new IngestionRecord
    {
        Id = 5, Status = IngestionStatus.Completed, FileHash = "will-be-set"
    };
    _tracker.GetByIdAsync(10, Arg.Any<CancellationToken>()).Returns(record);
    // GetCompletedByHashAsync returns a different completed record
    _tracker.GetCompletedByHashAsync(Arg.Any<string>(), 10, Arg.Any<CancellationToken>())
        .Returns(existing);
    var sut = BuildSut();

    await sut.IngestBookAsync(10);

    await _tracker.Received(1).MarkDuplicateAsync(10, Arg.Any<CancellationToken>());
    _extractor.DidNotReceive().ExtractPages(Arg.Any<string>());
    await _embeddingIngestor.DidNotReceive()
        .IngestAsync(Arg.Any<IList<ContentChunk>>(), Arg.Any<string>());
}

[Fact]
public async Task IngestBookAsync_PendingRecord_CallsMarkHashAsync()
{
    var record = new IngestionRecord
    {
        Id = 11, FilePath = _tempFile, FileName = "test.pdf",
        FileHash = string.Empty, SourceName = "PHB", Version = "Edition2014",
        DisplayName = "PHB", Status = IngestionStatus.Pending
    };
    _tracker.GetByIdAsync(11, Arg.Any<CancellationToken>()).Returns(record);
    _tracker.GetCompletedByHashAsync(Arg.Any<string>(), 11, Arg.Any<CancellationToken>())
        .Returns((IngestionRecord?)null);
    _extractor.ExtractPages(_tempFile)
        .Returns([(1, "Casting Time: 1 action\nRange: 150 feet\nDuration: Instantaneous")]);
    var sut = BuildSut();

    await sut.IngestBookAsync(11);

    await _tracker.Received(1).MarkHashAsync(11, Arg.Any<string>(), Arg.Any<CancellationToken>());
}
```

- [ ] **Step 2: Run tests — expect failures**

```bash
dotnet test --filter "ClassName=DndMcpAICsharpFun.Tests.Ingestion.IngestionOrchestratorTests"
```
Expected: new tests fail (methods not yet called in current flow).

- [ ] **Step 3: Refactor `IngestBookAsync`**

Replace the entire `IngestBookAsync` method body with:

```csharp
public async Task IngestBookAsync(int recordId, CancellationToken cancellationToken = default)
{
    var record = await tracker.GetByIdAsync(recordId, cancellationToken);
    if (record is null)
    {
        LogRecordNotFound(logger, recordId);
        return;
    }

    LogStartingIngestion(logger, record.DisplayName, recordId);

    try
    {
        var currentHash = await ComputeHashAsync(record.FilePath, cancellationToken);

        if (record.Status == IngestionStatus.Completed && record.FileHash == currentHash)
        {
            LogSkippingUnchanged(logger, record.DisplayName);
            return;
        }

        await tracker.MarkHashAsync(recordId, currentHash, cancellationToken);

        var duplicate = await tracker.GetCompletedByHashAsync(currentHash, recordId, cancellationToken);
        if (duplicate is not null)
        {
            LogDuplicateDetected(logger, record.DisplayName, duplicate.Id);
            await tracker.MarkDuplicateAsync(recordId, cancellationToken);
            return;
        }

        var pages = extractor.ExtractPages(record.FilePath);
        var version = Enum.Parse<DndVersion>(record.Version);
        var chunks = chunker.Chunk(pages, record.SourceName, version).ToList();

        await embeddingIngestor.IngestAsync(chunks, currentHash, cancellationToken);
        await tracker.MarkCompletedAsync(recordId, chunks.Count, cancellationToken);

        LogCompletedIngestion(logger, record.DisplayName, chunks.Count);
    }
    catch (Exception ex)
    {
        LogIngestionFailed(logger, ex, record.DisplayName, recordId);
        await tracker.MarkFailedAsync(recordId, ex.Message, cancellationToken);
    }
}
```

Add the new log message:

```csharp
[LoggerMessage(Level = LogLevel.Information, Message = "Book {DisplayName} is a duplicate of record {ExistingId} — marking as Duplicate")]
private static partial void LogDuplicateDetected(ILogger logger, string displayName, int existingId);
```

Also remove the now-unused `MarkProcessingAsync` call — it is replaced by `MarkHashAsync`.

- [ ] **Step 4: Update the existing unchanged-hash test**

The `IngestBookAsync_CompletedUnchangedHash_SkipsExtraction` test currently does not set up `MarkHashAsync`. Because we now skip before `MarkHashAsync` for Completed+same-hash, no setup is needed — but verify NSubstitute doesn't complain. If the test tracker mock needs `GetCompletedByHashAsync` set up for non-skip paths, add a default return of null:

The test should still pass as-is because early return happens before `MarkHashAsync`. Run the full suite to confirm.

- [ ] **Step 5: Update the `IngestBookAsync_PendingRecord_IngestsAndMarkCompleted` test**

Add `GetCompletedByHashAsync` stub returning null (so duplicate check passes):

```csharp
_tracker.GetCompletedByHashAsync(Arg.Any<string>(), 2, Arg.Any<CancellationToken>())
    .Returns((IngestionRecord?)null);
```

Add this line after the existing `_tracker.GetByIdAsync(2, ...)` setup in that test.

- [ ] **Step 6: Run all tests**

```bash
dotnet test
```
Expected: all tests pass.

- [ ] **Step 7: Commit**

```bash
git add Features/Ingestion/IngestionOrchestrator.cs \
        DndMcpAICsharpFun.Tests/Ingestion/IngestionOrchestratorTests.cs
git commit -m "feat: hash-first refactor and duplicate detection in IngestBookAsync"
```

---

## Task 8: Admin endpoint — DELETE /admin/books/{id}

Wire the endpoint in `BooksAdminEndpoints` and add the example to the `.http` file.

**Files:**
- Modify: `Features/Admin/BooksAdminEndpoints.cs`
- Modify: `DndMcpAICsharpFun.http`

- [ ] **Step 1: Register the route in `MapBooksAdmin`**

```csharp
public static RouteGroupBuilder MapBooksAdmin(this RouteGroupBuilder group)
{
    group.MapPost("/books/register", RegisterBook).DisableAntiforgery();
    group.MapGet("/books", GetAllBooks);
    group.MapPost("/books/{id:int}/reingest", ReingestBook);
    group.MapDelete("/books/{id:int}", DeleteBook);
    return group;
}
```

- [ ] **Step 2: Add the `DeleteBook` handler**

Add after the `ReingestBook` handler method:

```csharp
private static async Task<IResult> DeleteBook(
    int id,
    IIngestionOrchestrator orchestrator,
    CancellationToken ct)
{
    var result = await orchestrator.DeleteBookAsync(id, ct);
    return result switch
    {
        DeleteBookResult.Deleted   => Results.NoContent(),
        DeleteBookResult.NotFound  => Results.NotFound(),
        DeleteBookResult.Conflict  => Results.Conflict("Book is currently being ingested. Wait for ingestion to complete before deleting."),
        _                          => Results.StatusCode(500)
    };
}
```

Add the missing using at the top of `BooksAdminEndpoints.cs` if not already present:

```csharp
using DndMcpAICsharpFun.Features.Ingestion;
```

- [ ] **Step 3: Build**

```bash
dotnet build
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Add to `DndMcpAICsharpFun.http`**

Add after the `Re-ingest` example block:

```http
### Admin Books — Delete a book record and all associated data
DELETE {{baseUrl}}/admin/books/1
X-Admin-Api-Key: {{adminKey}}
```

- [ ] **Step 5: Run all tests**

```bash
dotnet test
```
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add Features/Admin/BooksAdminEndpoints.cs DndMcpAICsharpFun.http
git commit -m "feat: add DELETE /admin/books/{id} endpoint"
```

---

## Task 9: Spec archive

Update the living specs in `openspec/specs/` to reflect the changes.

**Files:**
- Modify: `openspec/specs/ingestion-pipeline/spec.md`
- Create: `openspec/specs/book-deletion/spec.md`

- [ ] **Step 1: Apply the ingestion-pipeline delta**

Open `openspec/specs/ingestion-pipeline/spec.md`. Apply the changes from `openspec/changes/delete-book-cleanup/specs/ingestion-pipeline/spec.md`:

- Replace the `### Requirement: The ingestion orchestrator processes a book end-to-end` section with the MODIFIED version (hash-first flow).
- Add the new `### Requirement: Ingestion detects duplicate books by file hash` section and its scenario.
- Update `### Requirement: A PDF book can be registered via the admin API` — remove the "Duplicate file returns existing record" scenario (that check is now done during ingestion, not at upload time).

- [ ] **Step 2: Create `openspec/specs/book-deletion/spec.md`**

Copy `openspec/changes/delete-book-cleanup/specs/book-deletion/spec.md` verbatim into `openspec/specs/book-deletion/spec.md`, adding the standard spec header:

```markdown
# book-deletion

## Purpose

Defines requirements for the admin endpoint that fully removes a book record, its PDF from disk, and all associated Qdrant vectors.

## Requirements
```

Then append all the requirements from the change spec file beneath that header.

- [ ] **Step 3: Commit**

```bash
git add openspec/specs/ingestion-pipeline/spec.md openspec/specs/book-deletion/spec.md
git commit -m "docs: update and create openspec specs for delete-book-cleanup"
```

---

## Task 10: Archive the change

Mark the change as done so `openspec status` stays clean.

- [ ] **Step 1: Archive**

```bash
openspec archive delete-book-cleanup
```

- [ ] **Step 2: Verify**

```bash
openspec status --change "delete-book-cleanup"
```
Expected: change is archived.

- [ ] **Step 3: Final test run**

```bash
dotnet test
```
Expected: all tests pass.

- [ ] **Step 4: Final commit**

```bash
git add openspec/
git commit -m "chore: archive delete-book-cleanup change"
```
