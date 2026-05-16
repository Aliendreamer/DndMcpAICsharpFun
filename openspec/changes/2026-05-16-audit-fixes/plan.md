# Audit Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **CRITICAL:** Use Serena MCP tools for ALL code reads and edits. Call `mcp__serena__initial_instructions` first. Built-in Read/Edit on .cs files is FORBIDDEN. Use `mcp__serena__find_symbol` with `include_body=true` to read, and `mcp__serena__replace_symbol_body` or `mcp__serena__replace_content` to edit. Line numbers from Serena are 0-based.

**Goal:** Fix all 23 bugs found in the full code audit — data integrity, null safety, security, concurrency, and operational correctness issues.

**Architecture:** Fixes span six layers — Admin endpoints, Ingestion orchestrators, VectorStore adapters, Infrastructure clients, Retrieval services, and the Blazor companion app. Most fixes are localised to a single method. The three data-integrity fixes (Tasks 1–3) are the most impactful.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, Qdrant gRPC, OllamaSharp, Entity Framework Core (SQLite), ONNX Runtime, Blazor Server

**Commit cadence:** Commit after every 5 completed tasks (Batches 1–4) and after the final batch.

---

## File Map

| File | Tasks |
| --- | --- |
| `Features/Admin/BooksAdminEndpoints.cs` | 1, 13 |
| `Features/Ingestion/BookDeletionService.cs` | 1 |
| `Features/Ingestion/Entities/EntityIngestionOrchestrator.cs` | 2 |
| `Features/VectorStore/QdrantVectorStoreService.cs` | 3 |
| `Features/VectorStore/IVectorStoreService.cs` | 3 |
| `Features/VectorStore/Entities/QdrantEntityVectorStore.cs` | 4 |
| `Features/Embedding/OllamaEmbeddingService.cs` | 5 |
| `Features/Mcp/DndMcpTools.cs` | 6 |
| `Features/Admin/CanonicalTypeFixerService.cs` | 7 |
| `Features/Admin/CanonicalTypeFixerEndpoints.cs` | 7 |
| `Features/Admin/AdminApiKeyMiddleware.cs` | 8 |
| `Features/Mcp/McpAuthMiddleware.cs` | 8 |
| `Features/Retrieval/CrossEncoderReranker.cs` | 9 |
| `DndMcpAICompanion/Components/Pages/Chat.razor` | 10 |
| `DndMcpAICompanion/Extensions/AppExtensions.cs` | 11 |
| `Infrastructure/Qdrant/QdrantCollectionInitializer.cs` | 12 |
| `Infrastructure/Qdrant/QdrantSparseState.cs` | 12, 19 |
| `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs` | 14, 15 |
| `Extensions/WebApplicationExtensions.cs` | 16 |
| `Features/Ingestion/Tracking/SqliteIngestionTracker.cs` | 17 |
| `Features/Admin/BooksAdminEndpoints.cs` | 18 |
| `Features/Ingestion/Pdf/DoclingBlockExtractor.cs` | 19 |
| `Infrastructure/Sqlite/IngestionOptions.cs` | 20 |
| `Extensions/ServiceCollectionExtensions.cs` | 20 |
| `Features/Admin/CanonicalValidationEndpoints.cs` | 21 |
| `DndMcpAICompanion/Extensions/RateLimitExtensions.cs` | 22 |
| `DndMcpAICompanion/Components/Pages/Register.razor` | 22 |

---

## BATCH 1 — HIGH severity data integrity (commit after Task 5)

---

### Task 1: Fix bookSlug derivation consistency

**Bug:** `BooksAdminEndpoints.ExtractEntities` and `BookDeletionService.DeleteBookAsync` always derive the canonical file slug from `record.DisplayName`. But `EntityIngestionOrchestrator.IngestEntitiesAsync` derives it from `record.FivetoolsSourceKey` when present. For any WotC book (PHB, MM, etc.), this means extract writes `players-handbook.json` but ingest looks for `phb.json` — extraction succeeds, ingestion throws `FileNotFoundException`. Deletion also leaves the canonical file on disk forever.

**Files:**

- Modify: `Features/Admin/BooksAdminEndpoints.cs` — `ExtractEntities` method (lines 67–113)
- Modify: `Features/Ingestion/BookDeletionService.cs` — `DeleteBookAsync` method (lines 17–46)

- [ ] **Step 1: Read both methods via Serena**

```
mcp__serena__find_symbol("ExtractEntities", "Features/Admin/BooksAdminEndpoints.cs", include_body=true)
mcp__serena__find_symbol("DeleteBookAsync", "Features/Ingestion/BookDeletionService.cs", include_body=true)
```

- [ ] **Step 2: Add a helper that computes the canonical slug the same way as `EntityIngestionOrchestrator`**

Add a `static string CanonicalSlugFor(IngestionRecord r)` method to `BooksAdminEndpoints` (or a shared static class). It must mirror the logic in `EntityIngestionOrchestrator.IngestEntitiesAsync` lines 32–34:

```csharp
private static string CanonicalSlugFor(IngestionRecord record) =>
    record.FivetoolsSourceKey is { } key
        ? EntityIdSlug.For(key, EntityType.Class, "x").Split('.')[0]
        : EntityIdSlug.For(record.DisplayName, EntityType.Class, "x").Split('.')[0];
```

Use `mcp__serena__insert_before_symbol` or `mcp__serena__insert_after_symbol` to add this to `BooksAdminEndpoints`.

- [ ] **Step 3: Update `ExtractEntities` to use `CanonicalSlugFor`**

The two places that compute `bookSlug` inside `ExtractEntities` (lines 94 and 103) currently use `EntityIdSlug.For(record.DisplayName, ...)`. Replace both with `CanonicalSlugFor(record)`.

Use `mcp__serena__replace_content` with targeted regex on the two occurrences inside `ExtractEntities`.

- [ ] **Step 4: Update `BookDeletionService.DeleteBookAsync` to use the same slug**

Line 31 currently:
```csharp
var canonicalSlug = EntityIdSlug.For(record.DisplayName, EntityType.Class, "x").Split('.')[0];
```

Replace with:
```csharp
var canonicalSlug = record.FivetoolsSourceKey is { } key
    ? EntityIdSlug.For(key, EntityType.Class, "x").Split('.')[0]
    : EntityIdSlug.For(record.DisplayName, EntityType.Class, "x").Split('.')[0];
```

Use `mcp__serena__replace_content` for the single-line replacement.

- [ ] **Step 5: Build and verify**

```bash
dotnet build DndMcpAICsharpFun.csproj --no-restore 2>&1 | tail -5
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

---

### Task 2: Fix EntityIngestionOrchestrator — upsert-before-delete + empty FileHash guard

**Bug A:** `IngestEntitiesAsync` calls `DeleteByFileHashAsync` before `UpsertAsync`. If anything between delete and upsert fails, all entity vectors are permanently gone with no recovery path.

**Bug B:** When a book was registered but never block-ingested, `record.FileHash` is `""`. Calling `DeleteByFileHashAsync("")` issues a Qdrant delete matching ALL points whose `file_hash` payload is empty — potentially wiping unrelated data.

**Files:**

- Modify: `Features/Ingestion/Entities/EntityIngestionOrchestrator.cs` — `IngestEntitiesAsync` method (lines 27–91)

- [ ] **Step 1: Read `IngestEntitiesAsync` via Serena**

```
mcp__serena__find_symbol("EntityIngestionOrchestrator/IngestEntitiesAsync", include_body=true)
```

- [ ] **Step 2: Guard the delete on empty FileHash and flip order to upsert-first**

Replace the body of `IngestEntitiesAsync` using `mcp__serena__replace_symbol_body`. The key changes are:

1. Move `await store.DeleteByFileHashAsync(...)` to AFTER `await store.UpsertAsync(points, ct)`.
2. Wrap the delete in `if (!string.IsNullOrEmpty(record.FileHash))`.

The new ordering inside the method (after embedding is done):
```csharp
// Upsert first — if this fails, old data is still intact.
await store.UpsertAsync(points, ct);

// Now safe to delete old vectors (skip if hash is empty — never ingested before).
if (!string.IsNullOrEmpty(record.FileHash))
    await store.DeleteByFileHashAsync(record.FileHash, ct);

await tracker.MarkEntitiesIngestedAsync(bookId, points.Count, ct);
```

- [ ] **Step 3: Build**

```bash
dotnet build DndMcpAICsharpFun.csproj --no-restore 2>&1 | tail -5
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

---

### Task 3: Fix QdrantVectorStoreService — switch block delete to payload filter

**Bug:** `DeleteBlocksByHashAsync` derives Qdrant point IDs as `DerivePointId(fileHash, 0..ChunkCount-1)`. If `ChunkCount` in SQLite diverges from what was actually written to Qdrant (partial ingest failure), the wrong set of IDs is deleted, leaving orphaned vectors. Fix: delete by payload filter on `file_hash` field instead of derived IDs.

**Files:**

- Modify: `Features/VectorStore/QdrantVectorStoreService.cs` — `DeleteBlocksByHashAsync` (lines 34–40)
- Modify: `Features/VectorStore/IVectorStoreService.cs` — remove `blockCount` parameter
- Modify: `Features/Ingestion/BlockIngestionOrchestrator.cs` — call site

- [ ] **Step 1: Read the interface and implementation**

```
mcp__serena__find_symbol("IVectorStoreService", "Features/VectorStore/IVectorStoreService.cs", include_body=true)
mcp__serena__find_symbol("DeleteBlocksByHashAsync", "Features/VectorStore/QdrantVectorStoreService.cs", include_body=true)
```

- [ ] **Step 2: Update interface — remove `blockCount` parameter**

```csharp
Task DeleteBlocksByHashAsync(string fileHash, CancellationToken ct = default);
```

Use `mcp__serena__replace_symbol_body` or `mcp__serena__replace_content`.

- [ ] **Step 3: Update implementation to use payload filter delete**

```csharp
public async Task DeleteBlocksByHashAsync(string fileHash, CancellationToken ct = default)
{
    await client.DeleteAsync(
        _blocksCollectionName,
        new Filter
        {
            Must = { Condition.MatchKeyword(QdrantPayloadFields.FileHash, fileHash) }
        },
        cancellationToken: ct);
}
```

Use `mcp__serena__replace_symbol_body` with the method body above.

- [ ] **Step 4: Fix the call site in `BlockIngestionOrchestrator`**

Find the call via:
```
mcp__serena__find_referencing_symbols("DeleteBlocksByHashAsync", "Features/VectorStore/IVectorStoreService.cs")
```

Remove the `record.ChunkCount.Value` argument from the call.

- [ ] **Step 5: Build**

```bash
dotnet build DndMcpAICsharpFun.csproj --no-restore 2>&1 | tail -5
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

---

### Task 4: Fix QdrantEntityVectorStore — unguarded MapField indexer + Enum.Parse

**Bug:** `ToEnvelope` accesses mandatory payload fields like `p[EntityPayloadFields.Id].StringValue` with no `TryGetValue` guard. If any field is absent (e.g., a point written by an older schema version), it throws `KeyNotFoundException`. `Enum.Parse<EntityType>` on the `Type` field throws `ArgumentException` if the string value no longer matches an enum name.

**Files:**

- Modify: `Features/VectorStore/Entities/QdrantEntityVectorStore.cs` — `ToEnvelope` (lines 184–213)

- [ ] **Step 1: Read `ToEnvelope` via Serena**

```
mcp__serena__find_symbol("ToEnvelope", "Features/VectorStore/Entities/QdrantEntityVectorStore.cs", include_body=true)
```

- [ ] **Step 2: Replace the method body with guarded access**

Use `mcp__serena__replace_symbol_body`. New body:

```csharp
private static EntityEnvelope? ToEnvelope(Google.Protobuf.Collections.MapField<string, Value> p)
{
    if (!p.TryGetValue(EntityPayloadFields.Id, out var idV) ||
        !p.TryGetValue(EntityPayloadFields.Type, out var typeV) ||
        !p.TryGetValue(EntityPayloadFields.Name, out var nameV) ||
        !p.TryGetValue(EntityPayloadFields.SourceBook, out var sbV) ||
        !p.TryGetValue(EntityPayloadFields.Edition, out var edV) ||
        !p.TryGetValue(EntityPayloadFields.CanonicalText, out var ctV))
        return null;

    if (!Enum.TryParse<EntityType>(typeV.StringValue, out var entityType))
        return null;

    var fieldsJson = p.TryGetValue(EntityPayloadFields.FieldsJson, out var fv) ? fv.StringValue : "{}";
    var fields = JsonDocument.Parse(fieldsJson).RootElement.Clone();

    var firstBook = p.TryGetValue(EntityPayloadFields.FirstBook, out var fb) ? fb.StringValue : string.Empty;
    var firstEdition = p.TryGetValue(EntityPayloadFields.FirstEdition, out var fe) ? fe.StringValue : string.Empty;

    return new EntityEnvelope(
        Id: idV.StringValue,
        Type: entityType,
        Name: nameV.StringValue,
        SourceBook: sbV.StringValue,
        Edition: edV.StringValue,
        Page: p.TryGetValue(EntityPayloadFields.Page, out var pp) ? (int?)pp.IntegerValue : null,
        FirstAppearedIn: new FirstAppearance(firstBook, firstEdition),
        RevisedIn: Array.Empty<Revision>(),
        SettingTags: p.TryGetValue(EntityPayloadFields.SettingTags, out var st)
            ? st.ListValue.Values.Select(v => v.StringValue).ToList()
            : Array.Empty<string>(),
        CanonicalText: ctV.StringValue,
        Fields: fields,
        DataSource: p.TryGetValue(EntityPayloadFields.DataSource, out var ds) ? ds.StringValue : "",
        Srd:            p.TryGetValue(EntityPayloadFields.Srd,            out var srdV)   && srdV.StringValue   == "true",
        Srd52:          p.TryGetValue(EntityPayloadFields.Srd52,          out var srd52V) && srd52V.StringValue  == "true",
        BasicRules2024: p.TryGetValue(EntityPayloadFields.BasicRules2024, out var brV)    && brV.StringValue     == "true",
        NeedsReview:    p.TryGetValue(EntityPayloadFields.NeedsReview,    out var nrV)    && nrV.StringValue     == "true",
        Keywords: p.TryGetValue(EntityPayloadFields.Keywords, out var kw)
            ? kw.ListValue.Values.Select(v => v.StringValue).ToList()
            : Array.Empty<string>());
}
```

- [ ] **Step 3: Update all callers of `ToEnvelope` to handle the nullable return**

Find callers:
```
mcp__serena__find_referencing_symbols("ToEnvelope", "Features/VectorStore/Entities/QdrantEntityVectorStore.cs")
```

For each call site, add a null filter. Example:
```csharp
// Before
.Select(hit => ToEnvelope(hit.Payload))
// After
.Select(hit => ToEnvelope(hit.Payload))
.Where(e => e is not null)
.Select(e => e!)
```

- [ ] **Step 4: Build**

```bash
dotnet build DndMcpAICsharpFun.csproj --no-restore 2>&1 | tail -5
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

---

### Task 5: Fix OllamaEmbeddingService — null Embeddings guard

**Bug:** `response.Embeddings` is typed `IList<float[]>?`. A model mismatch or malformed Ollama response returns null. All callers immediately index `[0]` — `NullReferenceException` at runtime.

**Files:**

- Modify: `Features/Embedding/OllamaEmbeddingService.cs` — `EmbedAsync` method

- [ ] **Step 1: Read `EmbedAsync` via Serena**

```
mcp__serena__find_symbol("OllamaEmbeddingService/EmbedAsync", include_body=true)
```

- [ ] **Step 2: Add null guard after the Ollama call**

Use `mcp__serena__replace_content` to change:
```csharp
return response.Embeddings;
```
to:
```csharp
return response.Embeddings
    ?? throw new InvalidOperationException(
        $"Ollama returned null embeddings for model '{_model}'. Check that the model is loaded and supports embedding.");
```

- [ ] **Step 3: Build and run tests**

```bash
dotnet build DndMcpAICsharpFun.csproj --no-restore 2>&1 | tail -5
dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj --no-restore --no-build 2>&1 | tail -10
```

---

### Task 5-COMMIT: Commit Batch 1

- [ ] **Commit all Batch 1 changes**

```bash
git add Features/Admin/BooksAdminEndpoints.cs \
        Features/Ingestion/BookDeletionService.cs \
        Features/Ingestion/Entities/EntityIngestionOrchestrator.cs \
        Features/VectorStore/QdrantVectorStoreService.cs \
        Features/VectorStore/IVectorStoreService.cs \
        Features/Ingestion/BlockIngestionOrchestrator.cs \
        Features/VectorStore/Entities/QdrantEntityVectorStore.cs \
        Features/Embedding/OllamaEmbeddingService.cs

git commit -m "fix: data integrity — slug consistency, upsert-before-delete, payload-filter block delete, null-safe ToEnvelope, Ollama null guard"
```

---

## BATCH 2 — HIGH/MEDIUM security + crashes (commit after Task 10)

---

### Task 6: Fix MCP tool input validation

**Bug:** `search_lore`, `search_entities`, and `get_entity` accept `string query`/`string id` but pass them to Ollama/Qdrant with no null/empty guard. Empty-string embedding returns zero-vector (silent bad results); `get_entity("")` may crash with `RpcException`.

**Files:**

- Modify: `Features/Mcp/DndMcpTools.cs` — all three tool methods

- [ ] **Step 1: Read `DndMcpTools` via Serena**

```
mcp__serena__find_symbol("DndMcpTools", "Features/Mcp/DndMcpTools.cs", depth=1, include_body=true)
```

- [ ] **Step 2: Add guard at the top of `search_lore`**

Use `mcp__serena__replace_content` to insert after the method opening brace:
```csharp
if (string.IsNullOrWhiteSpace(query))
    return "Error: query must not be empty.";
```

- [ ] **Step 3: Add guard at the top of `search_entities`**

Same pattern:
```csharp
if (string.IsNullOrWhiteSpace(query))
    return "Error: query must not be empty.";
```

- [ ] **Step 4: Add guard at the top of `get_entity`**

```csharp
if (string.IsNullOrWhiteSpace(id))
    return "Error: id must not be empty.";
```

- [ ] **Step 5: Build**

```bash
dotnet build DndMcpAICsharpFun.csproj --no-restore 2>&1 | tail -5
```

---

### Task 7: Fix path traversal + null safety in CanonicalTypeFixerService

**Bug A (HIGH — Security):** `FixTypesAsync` receives `bookSlug` from the query string and constructs `Path.Combine(_canonicalDirectory, bookSlug + ".json")` with no sanitization. A value of `../../../etc/shadow` traverses outside the canonical directory.

**Bug B (MEDIUM):** `doc["entities"]!.AsArray()` and `entity!["name"]!.GetValue<string>()` use `!` null-forgiving but have no runtime guard. A malformed canonical file causes `NullReferenceException`.

**Files:**

- Modify: `Features/Admin/CanonicalTypeFixerService.cs` — `FixTypesAsync`
- Modify: `Features/Admin/CanonicalTypeFixerEndpoints.cs` — endpoint that receives the `book` query param

- [ ] **Step 1: Read both files via Serena**

```
mcp__serena__find_symbol("CanonicalTypeFixerService/FixTypesAsync", include_body=true)
mcp__serena__find_symbol("CanonicalTypeFixerEndpoints", "Features/Admin/CanonicalTypeFixerEndpoints.cs", depth=1, include_body=true)
```

- [ ] **Step 2: Sanitize the bookSlug in the endpoint before passing to the service**

In the endpoint, add validation that `bookSlug` contains only safe characters before calling the service:
```csharp
// Reject any slug that isn't a safe alphanumeric/hyphen slug
if (string.IsNullOrWhiteSpace(bookSlug) ||
    !System.Text.RegularExpressions.Regex.IsMatch(bookSlug, @"^[a-zA-Z0-9_\-]+$"))
    return Results.BadRequest("Invalid book slug.");
```

Use `mcp__serena__replace_content` to insert this before the service call.

- [ ] **Step 3: Replace null-forgiving `!` accesses with null checks in `FixTypesAsync`**

Use `mcp__serena__replace_symbol_body` to rewrite `FixTypesAsync`. Key changes:

```csharp
var doc = JsonNode.Parse(json)
    ?? throw new InvalidOperationException($"Canonical file at {path} parsed as JSON null.");
var entitiesNode = doc["entities"]
    ?? throw new InvalidOperationException($"Canonical file at {path} has no 'entities' array.");
var entities = entitiesNode.AsArray();

foreach (var entity in entities)
{
    if (entity is null) continue;
    var name   = entity["name"]?.GetValue<string>();
    var source = entity["sourceBook"]?.GetValue<string>();
    if (name is null || source is null) continue;
    var key = (name.ToLowerInvariant(), source.ToUpperInvariant());
    // ... rest of loop unchanged
    var currentType = entity["type"]?.GetValue<string>();
    var oldId       = entity["id"]?.GetValue<string>();
    if (currentType is null || oldId is null) continue;
    // ... rest unchanged
}
```

- [ ] **Step 4: Build**

```bash
dotnet build DndMcpAICsharpFun.csproj --no-restore 2>&1 | tail -5
```

---

### Task 8: Harden API key middleware — empty key guard + constant-time comparison

**Bug A:** `AdminApiKeyMiddleware` does not check if `_apiKey` is empty/null before comparing. An unconfigured key leaves the auth posture undefined. `McpAuthMiddleware` already has this guard.

**Bug B:** Both middlewares use `string !=` (early-exit equality) — not constant-time. Should use `CryptographicOperations.FixedTimeEquals`.

**Files:**

- Modify: `Features/Admin/AdminApiKeyMiddleware.cs` — `InvokeAsync`
- Modify: `Features/Mcp/McpAuthMiddleware.cs` — `InvokeAsync`

- [ ] **Step 1: Read both middleware files**

```
mcp__serena__find_symbol("AdminApiKeyMiddleware/InvokeAsync", include_body=true)
mcp__serena__find_symbol("McpAuthMiddleware/InvokeAsync", include_body=true)
```

- [ ] **Step 2: Replace `AdminApiKeyMiddleware.InvokeAsync`**

```csharp
public async Task InvokeAsync(HttpContext context)
{
    if (string.IsNullOrEmpty(_apiKey) ||
        !context.Request.Headers.TryGetValue(HeaderName, out var provided) ||
        !CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(provided.ToString()),
            System.Text.Encoding.UTF8.GetBytes(_apiKey)))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    await next(context);
}
```

Add `using System.Security.Cryptography;` to the file if not present.

- [ ] **Step 3: Apply same fix to `McpAuthMiddleware.InvokeAsync`**

Replace the key comparison with `CryptographicOperations.FixedTimeEquals` (same pattern as above, adapted to how McpAuthMiddleware reads its header).

- [ ] **Step 4: Build**

```bash
dotnet build DndMcpAICsharpFun.csproj --no-restore 2>&1 | tail -5
```

---

### Task 9: Fix CrossEncoderReranker — guard `.First()` on ONNX results

**Bug:** `ScorePair` ends with `results.First().AsTensor<float>().First()`. If the ONNX model output is empty or the tensor has zero elements, `InvalidOperationException` is thrown, crashing the entire reranker for the request.

**Files:**

- Modify: `Features/Retrieval/CrossEncoderReranker.cs` — `ScorePair` (lines 75–115)

- [ ] **Step 1: Read `ScorePair` via Serena**

```
mcp__serena__find_symbol("CrossEncoderReranker/ScorePair", include_body=true)
```

- [ ] **Step 2: Replace the final two lines of `ScorePair`**

Use `mcp__serena__replace_content` to change:
```csharp
using var results = _session!.Run(inputs);
return results.First().AsTensor<float>().First();
```
to:
```csharp
using var results = _session!.Run(inputs);
var outputList = results.ToList();
if (outputList.Count == 0)
    throw new InvalidOperationException("ONNX reranker returned no output tensors.");
var tensor = outputList[0].AsTensor<float>();
var scores = tensor.ToArray();
if (scores.Length == 0)
    throw new InvalidOperationException("ONNX reranker output tensor is empty.");
return scores[0];
```

- [ ] **Step 3: Build**

```bash
dotnet build DndMcpAICsharpFun.csproj --no-restore 2>&1 | tail -5
```

---

### Task 10: Fix Chat.razor — cancel Ollama call on component dispose

**Bug:** `Chat.razor` passes `CancellationToken.None` to `ChatService.SendAsync`. When a user navigates away mid-generation, the Ollama call (with `Timeout.InfiniteTimeSpan`) runs to completion. Under concurrent users this exhausts Ollama's concurrency capacity.

**Files:**

- Modify: `DndMcpAICompanion/Components/Pages/Chat.razor`

- [ ] **Step 1: Read `Chat.razor` via Serena**

```
mcp__serena__find_symbol("Chat", "DndMcpAICompanion/Components/Pages/Chat.razor", depth=1)
```

- [ ] **Step 2: Implement `IAsyncDisposable`, add a `CancellationTokenSource` field**

Add to the `@code` block:
```csharp
private CancellationTokenSource _cts = new();

public async ValueTask DisposeAsync()
{
    await _cts.CancelAsync();
    _cts.Dispose();
}
```

Change the class declaration line to:
```
@implements IAsyncDisposable
```

- [ ] **Step 3: Replace `CancellationToken.None` with `_cts.Token`**

```csharp
await ChatService.SendAsync(msg, _webSearchEnabled, _cts.Token);
```

- [ ] **Step 4: Build companion project**

```bash
dotnet build DndMcpAICompanion/DndMcpAICompanion.csproj --no-restore 2>&1 | tail -5
```

---

### Task 10-COMMIT: Commit Batch 2

- [ ] **Commit all Batch 2 changes**

```bash
git add Features/Mcp/DndMcpTools.cs \
        Features/Admin/CanonicalTypeFixerService.cs \
        Features/Admin/CanonicalTypeFixerEndpoints.cs \
        Features/Admin/AdminApiKeyMiddleware.cs \
        Features/Mcp/McpAuthMiddleware.cs \
        Features/Retrieval/CrossEncoderReranker.cs \
        DndMcpAICompanion/Components/Pages/Chat.razor

git commit -m "fix: security + crash fixes — MCP input validation, path traversal, constant-time key compare, ONNX bounds, chat cancellation"
```

---

## BATCH 3 — MEDIUM operational correctness (commit after Task 15)

---

### Task 11: Fix AppExtensions — blocking dispose at shutdown

**Bug:** `app.Lifetime.ApplicationStopping.Register(() => mcpClient.DisposeAsync().AsTask().GetAwaiter().GetResult())` blocks the host shutdown thread with no timeout, potentially hanging shutdown indefinitely if the MCP client dispose stalls.

**Files:**

- Modify: `DndMcpAICompanion/Extensions/AppExtensions.cs`

- [ ] **Step 1: Read `AppExtensions` via Serena**

```
mcp__serena__find_symbol("AppExtensions", "DndMcpAICompanion/Extensions/AppExtensions.cs", depth=1, include_body=true)
```

- [ ] **Step 2: Replace the blocking register with a fire-and-forget dispose with timeout**

```csharp
app.Lifetime.ApplicationStopping.Register(() =>
{
    try
    {
        mcpClient.DisposeAsync().AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5))
            .GetAwaiter().GetResult();
    }
    catch (TimeoutException)
    {
        // MCP client did not dispose within 5 s — proceeding with shutdown.
    }
    catch
    {
        // Dispose errors are non-fatal during shutdown.
    }
});
```

- [ ] **Step 3: Build companion**

```bash
dotnet build DndMcpAICompanion/DndMcpAICompanion.csproj --no-restore 2>&1 | tail -5
```

---

### Task 12: Fix QdrantCollectionInitializer — throw on final failure + volatile SparseState

**Bug A:** After all retries are exhausted `StartAsync` returns normally (no exception, no flag set). The app starts accepting requests even though Qdrant collections may not exist.

**Bug B:** `QdrantSparseState.SparseSupported` is a plain `bool` with no `volatile` annotation — written by the hosted service thread, read by concurrent request handlers.

**Files:**

- Modify: `Infrastructure/Qdrant/QdrantCollectionInitializer.cs` — `StartAsync`
- Modify: `Infrastructure/Qdrant/QdrantSparseState.cs`

- [ ] **Step 1: Read both files**

```
mcp__serena__find_symbol("QdrantCollectionInitializer/StartAsync", include_body=true)
mcp__serena__find_symbol("QdrantSparseState", include_body=true)
```

- [ ] **Step 2: Throw on final failure in `StartAsync`**

Replace the final catch block (which currently just logs and returns) to rethrow:
```csharp
catch (Exception ex)
{
    LogCollectionInitFailed(logger, ex, _options.BlocksCollectionName);
    throw; // crash the host — Qdrant is required
}
```

- [ ] **Step 3: Mark `SparseSupported` as `volatile`**

```csharp
public sealed class QdrantSparseState
{
    public volatile bool SparseSupported;
}
```

Change from auto-property to a public field with `volatile` (Serena replace_content or replace_symbol_body).

- [ ] **Step 4: Build**

```bash
dotnet build DndMcpAICsharpFun.csproj --no-restore 2>&1 | tail -5
```

---

### Task 13: Fix BooksAdminEndpoints — missing ingestion status guards

**Bug:** `IngestBlocks` only guards `Processing`. `IngestEntities` only guards `Processing`. Neither guards `EntitiesExtracting` or `EntitiesIngesting`. Two rapid calls can both pass the guard before the worker sets the status, causing double ingestion.

**Files:**

- Modify: `Features/Admin/BooksAdminEndpoints.cs` — `IngestBlocks` and `IngestEntities` methods

- [ ] **Step 1: Read both methods**

```
mcp__serena__find_symbol("IngestBlocks", "Features/Admin/BooksAdminEndpoints.cs", include_body=true)
mcp__serena__find_symbol("IngestEntities", "Features/Admin/BooksAdminEndpoints.cs", include_body=true)
```

- [ ] **Step 2: Expand the conflict check in `IngestBlocks`**

Replace:
```csharp
if (record.Status == IngestionStatus.Processing)
    return Results.Conflict("Book is currently processing.");
```
with:
```csharp
if (record.Status is IngestionStatus.Processing
    or IngestionStatus.EntitiesExtracting
    or IngestionStatus.EntitiesIngesting)
    return Results.Conflict("Book is currently processing.");
```

- [ ] **Step 3: Expand the conflict check in `IngestEntities`**

Same replacement as Step 2 — add `EntitiesExtracting` and `EntitiesIngesting` to the guard.

- [ ] **Step 4: Build**

```bash
dotnet build DndMcpAICsharpFun.csproj --no-restore 2>&1 | tail -5
```

---

### Task 14: Fix EntityExtractionOrchestrator — checkpoint delete ordering + File.Delete guard

**Bug A:** The deletion order is: write canonical JSON → delete checkpoint files → update SQLite. If `MarkEntitiesExtractedAsync` throws after checkpoints are deleted, the canonical file exists but SQLite says `EntitiesFailed`. A retry with `force=false` hits the "canonical already exists" conflict; with `force=true` overwrites a good file.

**Bug B:** `File.Delete(checkpointPath)` is unguarded. If the file is locked (antivirus, another process), it throws, triggering the outer catch which marks the book as failed even though extraction succeeded.

**Files:**

- Modify: `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs` — `RunFullExtractionAsync` (around lines 276–295)

- [ ] **Step 1: Read `RunFullExtractionAsync` via Serena**

```
mcp__serena__find_symbol("RunFullExtractionAsync", include_body=true, max_answer_chars=10000)
```

- [ ] **Step 2: Wrap File.Delete calls in try/catch**

Find the two `File.Delete(checkpointPath)` and `File.Delete(checkpointErrorsPath)` calls. Wrap each:
```csharp
try { File.Delete(checkpointPath); }
catch (Exception ex) { logger.LogWarning(ex, "Could not delete checkpoint file {Path}", checkpointPath); }

try { File.Delete(checkpointErrorsPath); }
catch (Exception ex) { logger.LogWarning(ex, "Could not delete checkpoint errors file {Path}", checkpointErrorsPath); }
```

- [ ] **Step 3: Move `MarkEntitiesExtractedAsync` BEFORE `File.Delete` calls**

Reorder the sequence to:

1. `writer.WriteAsync(...)` — write canonical JSON
2. `errorsFile.WriteAsync(...)` — write errors
3. `warningsFile.WriteAsync(...)` — write warnings
4. `await tracker.MarkEntitiesExtractedAsync(...)` — update SQLite FIRST
5. Delete checkpoint files (with try/catch from Step 2)

Use `mcp__serena__replace_content` with a targeted regex on the relevant lines.

- [ ] **Step 4: Build**

```bash
dotnet build DndMcpAICsharpFun.csproj --no-restore 2>&1 | tail -5
```

---

### Task 15: Fix EntityExtractionOrchestrator — LoadSchemas + WriteCheckpointAsync temp cleanup

**Bug A:** `LoadSchemas` only catches `FileNotFoundException`. A malformed schema JSON causes `JsonException` which propagates uncaught, killing the entire extraction run before processing a single entity.

**Bug B:** `WriteCheckpointAsync` writes to a `.tmp` file then calls `File.Move`. If `File.Move` throws, the `.tmp` file leaks on disk.

**Files:**

- Modify: `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs` — `LoadSchemas` and `WriteCheckpointAsync`

- [ ] **Step 1: Read both methods**

```
mcp__serena__find_symbol("LoadSchemas", include_body=true)
mcp__serena__find_symbol("WriteCheckpointAsync", include_body=true)
```

- [ ] **Step 2: Broaden the catch in `LoadSchemas`**

Replace:
```csharp
catch (FileNotFoundException)
{
    logger.LogDebug("Schema file not found for {Type} at {Path}; type will be skipped", type, path);
}
```
with:
```csharp
catch (FileNotFoundException)
{
    logger.LogDebug("Schema file not found for {Type} at {Path}; type will be skipped", type, path);
}
catch (JsonException ex)
{
    logger.LogWarning(ex, "Schema file for {Type} at {Path} is malformed; type will be skipped", type, path);
}
catch (IOException ex)
{
    logger.LogWarning(ex, "Could not read schema file for {Type} at {Path}; type will be skipped", type, path);
}
```

- [ ] **Step 3: Add temp file cleanup to `WriteCheckpointAsync`**

Wrap the two `File.Move` calls in try/catch that deletes the tmp file on failure:
```csharp
try
{
    File.Move(tmp1, progressPath, overwrite: true);
}
catch
{
    try { File.Delete(tmp1); } catch { /* best effort */ }
    throw;
}
// same pattern for tmp2/errorsPath
```

- [ ] **Step 4: Build and run tests**

```bash
dotnet build DndMcpAICsharpFun.csproj --no-restore 2>&1 | tail -5
dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj --no-restore --no-build 2>&1 | tail -10
```

---

### Task 15-COMMIT: Commit Batch 3

- [ ] **Commit all Batch 3 changes**

```bash
git add DndMcpAICompanion/Extensions/AppExtensions.cs \
        Infrastructure/Qdrant/QdrantCollectionInitializer.cs \
        Infrastructure/Qdrant/QdrantSparseState.cs \
        Features/Admin/BooksAdminEndpoints.cs \
        Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs

git commit -m "fix: operational correctness — shutdown timeout, Qdrant init fatal, status guards, checkpoint safety, LoadSchemas robustness"
```

---

## BATCH 4 — LOW severity (commit after Task 20)

---

### Task 16: Guard /metrics endpoint — development only

**Bug:** `/metrics` (Prometheus scraping) is exposed in all environments with no auth and no environment check. The CLAUDE.md acknowledges it is local-dev only, but nothing enforces that.

**Files:**

- Modify: `Extensions/WebApplicationExtensions.cs` — where `MapPrometheusScrapingEndpoint()` is called

- [ ] **Step 1: Read the file**

```
mcp__serena__find_symbol("WebApplicationExtensions", "Extensions/WebApplicationExtensions.cs", depth=1, include_body=true)
```

- [ ] **Step 2: Wrap in environment check**

```csharp
if (app.Environment.IsDevelopment())
    app.MapPrometheusScrapingEndpoint();
```

Use `mcp__serena__replace_content` to wrap the existing call.

- [ ] **Step 3: Build**

```bash
dotnet build DndMcpAICsharpFun.csproj --no-restore 2>&1 | tail -5
```

---

### Task 17: Add page limit to GET /admin/books

**Bug:** `GetAllBooks` calls `tracker.GetAllAsync()` which does a full `ToListAsync()` with no `Take(n)`. Serializes the entire table on every call.

**Files:**

- Modify: `Features/Ingestion/Tracking/SqliteIngestionTracker.cs` — `GetAllAsync`
- Modify: `Features/Admin/BooksAdminEndpoints.cs` — `GetAllBooks` handler to pass a limit

- [ ] **Step 1: Read `GetAllAsync` and `GetAllBooks`**

```
mcp__serena__find_symbol("GetAllAsync", "Features/Ingestion/Tracking/SqliteIngestionTracker.cs", include_body=true)
mcp__serena__find_symbol("GetAllBooks", "Features/Admin/BooksAdminEndpoints.cs", include_body=true)
```

- [ ] **Step 2: Add `limit` and `offset` parameters to `IIngestionTracker.GetAllAsync` and implementation**

Update the interface:
```csharp
Task<List<IngestionRecord>> GetAllAsync(int limit = 100, int offset = 0, CancellationToken ct = default);
```

Update the implementation:
```csharp
return await db.IngestionRecords
    .OrderBy(r => r.Id)
    .Skip(offset)
    .Take(limit)
    .ToListAsync(ct);
```

- [ ] **Step 3: Update the endpoint to accept query params**

```csharp
private static async Task<IResult> GetAllBooks(
    IIngestionTracker tracker,
    int limit = 100,
    int offset = 0,
    CancellationToken ct = default)
{
    var records = await tracker.GetAllAsync(limit, offset, ct);
    return Results.Ok(records);
}
```

- [ ] **Step 4: Build**

```bash
dotnet build DndMcpAICsharpFun.csproj --no-restore 2>&1 | tail -5
```

---

### Task 18: Fix POST /books/register — return 201 Created

**Bug:** `RegisterBook` returns `Results.Accepted(uri, body)` (HTTP 202). The resource is created synchronously before the response is sent — 201 Created is correct.

**Files:**

- Modify: `Features/Admin/BooksAdminEndpoints.cs` — `RegisterBook` handler

- [ ] **Step 1: Find the `RegisterBook` method**

```
mcp__serena__find_symbol("RegisterBook", "Features/Admin/BooksAdminEndpoints.cs", include_body=true)
```

- [ ] **Step 2: Replace `Results.Accepted` with `Results.Created`**

```csharp
return Results.Created($"/admin/books/{created.Id}", response);
```

Use `mcp__serena__replace_content`.

- [ ] **Step 3: Build**

```bash
dotnet build DndMcpAICsharpFun.csproj --no-restore 2>&1 | tail -5
```

---

### Task 19: Fix DoclingBlockExtractor — propagate CancellationToken

**Bug:** `DoclingBlockExtractor.ExtractBlocks` passes `CancellationToken.None` to `converter.ConvertAsync`. A host shutdown signal during PDF conversion (which can take 30+ minutes) is silently ignored.

**Files:**

- Modify: `Features/Ingestion/Pdf/DoclingBlockExtractor.cs` — `ExtractBlocks`
- Modify: `Features/Ingestion/Pdf/IPdfBlockExtractor.cs` — add CT parameter to interface

- [ ] **Step 1: Read both files**

```
mcp__serena__find_symbol("IPdfBlockExtractor", "Features/Ingestion/Pdf/IPdfBlockExtractor.cs", include_body=true)
mcp__serena__find_symbol("DoclingBlockExtractor/ExtractBlocks", include_body=true)
```

- [ ] **Step 2: Add `CancellationToken ct = default` to `IPdfBlockExtractor.ExtractBlocks`**

```csharp
IEnumerable<PdfBlock> ExtractBlocks(string filePath, CancellationToken ct = default);
```

- [ ] **Step 3: Update `DoclingBlockExtractor.ExtractBlocks` to accept and pass the token**

```csharp
public IEnumerable<PdfBlock> ExtractBlocks(string filePath, CancellationToken ct = default)
{
    var doc = converter.ConvertAsync(filePath, ct)
        .GetAwaiter().GetResult();
    // ... rest unchanged
}
```

- [ ] **Step 4: Fix all call sites**

```
mcp__serena__find_referencing_symbols("ExtractBlocks", "Features/Ingestion/Pdf/IPdfBlockExtractor.cs")
```

Pass the `cancellationToken` from the orchestrator's context.

- [ ] **Step 5: Build**

```bash
dotnet build DndMcpAICsharpFun.csproj --no-restore 2>&1 | tail -5
```

---

### Task 20-COMMIT: Commit Batch 4

- [ ] **Commit all Batch 4 changes**

```bash
git add Extensions/WebApplicationExtensions.cs \
        Features/Ingestion/Tracking/SqliteIngestionTracker.cs \
        Features/Admin/BooksAdminEndpoints.cs \
        Features/Ingestion/Pdf/IPdfBlockExtractor.cs \
        Features/Ingestion/Pdf/DoclingBlockExtractor.cs \
        Features/Ingestion/BlockIngestionOrchestrator.cs

git commit -m "fix: low-severity — metrics dev-only, admin books pagination, register 201, CT propagation through block extractor"
```

---

## BATCH 5 — LOW severity remainder (commit after Task 23)

---

### Task 21: Add Options startup validation

**Bug:** No options class implements `IValidateOptions<T>` or chains `.ValidateDataAnnotations().ValidateOnStart()`. A misconfigured `OllamaOptions.EmbeddingModel = ""` or `QdrantOptions.Port = 0` is discovered only at the first runtime failure, not at startup.

**Files:**

- Modify: `Extensions/ServiceCollectionExtensions.cs` — all `Configure<T>` calls
- Modify: Key Options classes to add `[Required]` attributes: `OllamaOptions`, `QdrantOptions`, `DoclingOptions`, `EntityExtractionOptions`

- [ ] **Step 1: Read the options classes and ServiceCollectionExtensions**

```
mcp__serena__find_symbol("OllamaOptions", include_body=true)
mcp__serena__find_symbol("QdrantOptions", include_body=true)
mcp__serena__find_symbol("DoclingOptions", include_body=true)
mcp__serena__find_symbol("EntityExtractionOptions", include_body=true)
```

- [ ] **Step 2: Add `[Required]` to critical string properties**

For each options class, add `[Required]` (from `System.ComponentModel.DataAnnotations`) to non-nullable string properties that must not be empty (e.g., `EmbeddingModel`, `ChatModel`, `BaseUrl`, `CanonicalDirectory`).

- [ ] **Step 3: Chain `.ValidateDataAnnotations().ValidateOnStart()` in ServiceCollectionExtensions**

For each `services.Configure<T>(...)` call, add:
```csharp
services.AddOptions<OllamaOptions>()
    .BindConfiguration("Ollama")
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

Repeat for `QdrantOptions`, `DoclingOptions`, `EntityExtractionOptions`.

- [ ] **Step 4: Build**

```bash
dotnet build DndMcpAICsharpFun.csproj --no-restore 2>&1 | tail -5
```

---

### Task 22: Fix /admin/canonical/validate — return 422 for NeedsReview warnings

**Bug:** `POST /admin/canonical/validate` returns 200 even when `NeedsReview` warnings exist. A CI script gating on status code cannot distinguish "all clean" from "warnings present".

**Files:**

- Modify: `Features/Admin/CanonicalValidationEndpoints.cs`

- [ ] **Step 1: Read the endpoint**

```
mcp__serena__find_symbol("CanonicalValidationEndpoints", "Features/Admin/CanonicalValidationEndpoints.cs", depth=1, include_body=true)
```

- [ ] **Step 2: Return 422 also for NeedsReview warnings**

```csharp
if (report.Failures.Count > 0 || report.NeedsReview.Count > 0)
    return Results.UnprocessableEntity(report);
return Results.Ok(report);
```

- [ ] **Step 3: Build**

```bash
dotnet build DndMcpAICsharpFun.csproj --no-restore 2>&1 | tail -5
```

---

### Task 23: Add rate limiting to companion registration endpoint

**Bug:** `/register` in the companion Blazor app has no rate limiting specific to account creation. The global sliding window limiter (60 req/min) is too generous. An attacker can enumerate usernames (distinct error messages) and bulk-register accounts.

**Files:**

- Modify: `DndMcpAICompanion/Extensions/RateLimitExtensions.cs` — add a registration-specific limiter
- Modify: `DndMcpAICompanion/Components/Pages/Register.razor` — apply the limiter

- [ ] **Step 1: Read `RateLimitExtensions` and `Register.razor`**

```
mcp__serena__find_symbol("AddDndRateLimiting", include_body=true)
mcp__serena__find_symbol("Register", "DndMcpAICompanion/Components/Pages/Register.razor", depth=1)
```

- [ ] **Step 2: Add a strict registration limiter**

In `AddDndRateLimiting`, add a fixed-window limiter for registration:
```csharp
options.AddFixedWindowLimiter("registration", o =>
{
    o.PermitLimit = 5;
    o.Window = TimeSpan.FromMinutes(10);
    o.QueueLimit = 0;
    o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
});
```

- [ ] **Step 3: Apply `[EnableRateLimiting("registration")]` or route-level attribute**

In `Register.razor`, add `[EnableRateLimiting("registration")]` on the page route, or handle it via middleware. Since this is Blazor Server (not minimal API), add the attribute to the `@page` component class or handle it in the POST handler:

In the `HandleRegister` method, check if the limiter has been hit and return an error message if so. If the Blazor approach doesn't support route-level rate limiting cleanly, use a service-level counter per IP stored in `IMemoryCache` (5 attempts per IP per 10 minutes):

```csharp
@inject IMemoryCache Cache

// In HandleRegister:
var ip = HttpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
var key = $"reg:{ip}";
var count = Cache.GetOrCreate(key, e => { e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10); return 0; });
if (count >= 5) { _error = "Too many registration attempts. Try again later."; return; }
Cache.Set(key, count + 1, TimeSpan.FromMinutes(10));
```

Add `IMemoryCache` to DI if not already registered (`services.AddMemoryCache()` in `DatabaseExtensions` or `AppExtensions`).

- [ ] **Step 4: Build companion**

```bash
dotnet build DndMcpAICompanion/DndMcpAICompanion.csproj --no-restore 2>&1 | tail -5
```

---

### Task 23-COMMIT: Commit Batch 5

- [ ] **Commit all Batch 5 changes**

```bash
git add Extensions/ServiceCollectionExtensions.cs \
        Infrastructure/Sqlite/IngestionOptions.cs \
        Features/Admin/CanonicalValidationEndpoints.cs \
        DndMcpAICompanion/Extensions/RateLimitExtensions.cs \
        DndMcpAICompanion/Components/Pages/Register.razor

git commit -m "fix: low-severity — options startup validation, validate 422 for warnings, registration rate limiting"
```

---

## Final verification

- [ ] **Full build of all projects**

```bash
dotnet build DndMcpAICsharpFun.csproj --no-restore 2>&1 | tail -5
dotnet build DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj --no-restore 2>&1 | tail -5
dotnet build DndMcpAICompanion/DndMcpAICompanion.csproj --no-restore 2>&1 | tail -5
```

- [ ] **Run all tests**

```bash
dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj --no-restore 2>&1 | tail -15
dotnet test DndMcpAICompanion.Tests/DndMcpAICompanion.Tests.csproj --no-restore 2>&1 | tail -15
```

All tests must pass. If any fail, investigate and fix before considering the batch done.
