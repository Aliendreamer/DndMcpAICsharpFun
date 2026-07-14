# Stable Book-Key Retrieval + DMG Ingest — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Make block retrieval scope by a stable 5etools `source_key` instead of the fragile `source_book` display name, ingest the DMG, and add a guard so a scoped-but-absent book fails loudly instead of returning empty.

**Architecture:** Additive `source_key` payload on `dnd_blocks` written at ingest from `IngestionRecord.FivetoolsSourceKey`; a `BookCatalog` key↔display-name single source of truth; a one-time payload backfill of existing blocks; the retrieval filter (one spot) + three scope constants move to keys; a startup scope-health warning; a metadata-only registry reconcile. `dnd_entities` is unchanged (already keyed).

**Tech Stack:** .NET 10, C#, Qdrant.Client, xunit + FluentAssertions, Testcontainers (Qdrant + Postgres via Respawn).

## Global Constraints

- `net10.0`, nullable, implicit usings, **warnings-as-errors**.
- **Counts use `client.CountAsync` per known `BookCatalog` entry — NOT the Qdrant facet API** (BookCatalog is a finite set; this avoids a client-version dependency).
- **Sequencing is load-bearing:** Task 2 (ingest writes `source_key`) + Task 4 (backfill endpoint) MUST land before Task 5 (switch the retrieval filter). Until Task 5, scoping still uses `source_book`, so nothing breaks; the live backfill (Task 8) runs before Task 5's filter is relied on in the deployed system.
- Blocks are keyed in Qdrant by point id `DerivePointId(fileHash, chunkIndex)`; `source_book` stays for citations.
- Any HTTP route added/changed → update BOTH `DndMcpAICsharpFun.http` and `dnd-mcp-api.insomnia.json` in the same commit.
- Build/test require `dangerouslyDisableSandbox: true` (git-crypted Config); Docker up for Testcontainers.
- Use Serena for all code reads/edits.

---

### Task 1: BookCatalog + consistency test

**Files:**
- Create: `Features/Retrieval/BookCatalog.cs`
- Test: `DndMcpAICsharpFun.Tests/Retrieval/BookCatalogTests.cs`

**Interfaces — Produces:**
- `record BookInfo(string Key, string DisplayName, DndVersion Version, string FivetoolsSourceKey)`
- `static class BookCatalog` with `IReadOnlyList<BookInfo> All`, `IReadOnlySet<string> Keys`, `IReadOnlySet<string> DisplayNames`, `IReadOnlyDictionary<string,string> DisplayNameToKey`, `IReadOnlyDictionary<string,string> KeyToDisplayName`.

- [ ] **Step 1: failing test** `BookCatalogTests` (namespace `DndMcpAICsharpFun.Tests.Retrieval`):

```csharp
using DndMcpAICsharpFun.Features.Retrieval;
using FluentAssertions;
using Xunit;

public class BookCatalogTests
{
    [Fact]
    public void Keys_and_display_names_round_trip()
    {
        BookCatalog.KeyToDisplayName["DMG"].Should().Be("Dungeon Master's Guide 2014");
        BookCatalog.DisplayNameToKey["Dungeon Master's Guide 2014"].Should().Be("DMG");
        BookCatalog.DisplayNameToKey["PlayerHandbook 2014"].Should().Be("PHB");
    }

    [Fact]
    public void Keys_are_unique_and_nonempty()
    {
        BookCatalog.All.Should().OnlyContain(b =>
            !string.IsNullOrWhiteSpace(b.Key) && !string.IsNullOrWhiteSpace(b.DisplayName));
        BookCatalog.Keys.Count.Should().Be(BookCatalog.All.Count);
    }
}
```

- [ ] **Step 2: run → fail** (`dotnet test --filter FullyQualifiedName~BookCatalogTests`, dangerouslyDisableSandbox). Expected: compile/type error.
- [ ] **Step 3: implement** `Features/Retrieval/BookCatalog.cs`:

```csharp
using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Retrieval;

/// <summary>Single source of truth for the known source books: stable 5etools Key ↔ exact
/// dnd_blocks.source_book DisplayName. Display names are copied VERBATIM from the live corpus.</summary>
public sealed record BookInfo(string Key, string DisplayName, DndVersion Version, string FivetoolsSourceKey);

public static class BookCatalog
{
    public static IReadOnlyList<BookInfo> All { get; } =
    [
        new("PHB",  "PlayerHandbook 2014",                DndVersion.Edition2014, "PHB"),
        new("MM",   "Monster Manual 2014",                DndVersion.Edition2014, "MM"),
        new("DMG",  "Dungeon Master's Guide 2014",        DndVersion.Edition2014, "DMG"),
        new("XGE",  "Xanathar's Guide to Everything",     DndVersion.Edition2014, "XGE"),
        new("ERLW", "Eberron: Rising from the Last War",  DndVersion.Edition2014, "ERLW"),
    ];

    public static IReadOnlySet<string> Keys { get; } = All.Select(b => b.Key).ToHashSet(StringComparer.Ordinal);
    public static IReadOnlySet<string> DisplayNames { get; } = All.Select(b => b.DisplayName).ToHashSet(StringComparer.Ordinal);
    public static IReadOnlyDictionary<string,string> DisplayNameToKey { get; } =
        All.ToDictionary(b => b.DisplayName, b => b.Key, StringComparer.Ordinal);
    public static IReadOnlyDictionary<string,string> KeyToDisplayName { get; } =
        All.ToDictionary(b => b.Key, b => b.DisplayName, StringComparer.Ordinal);
}
```

- [ ] **Step 4: run → pass.** Full suite green. **Step 5: commit** `feat(retrieval): BookCatalog key↔display-name registry`.

---

### Task 2: `source_key` written at ingest (no filter change)

**Files:**
- Modify: `Infrastructure/Qdrant/QdrantPayloadFields.cs` (add `SourceKey`)
- Modify: `Features/VectorStore/BlockChunk.cs` (`BlockMetadata` gains `SourceKey`)
- Modify: `Features/Ingestion/BlockIngestionOrchestrator.cs` (set `SourceKey` from `record.FivetoolsSourceKey`)
- Modify: `Features/VectorStore/QdrantVectorStoreService.cs` (`BuildBlockPoint` writes `source_key`)
- Modify: `Infrastructure/Qdrant/QdrantCollectionInitializer.cs` (`CreatePayloadIndexes` → index `source_key`)
- Test: extend the block-point/payload test (find it: `grep -rl "BuildBlockPoint\|source_book" DndMcpAICsharpFun.Tests`)

**Interfaces — Produces:** `QdrantPayloadFields.SourceKey = "source_key"`; `BlockMetadata.SourceKey (string?)`.
**Consumes:** `IngestionRecord.FivetoolsSourceKey` (existing, nullable string).

- [ ] **Step 1:** add `public const string SourceKey = "source_key";` to `QdrantPayloadFields`.
- [ ] **Step 2:** add `string? SourceKey = null` as the LAST parameter of `BlockMetadata` (keep existing order; it defaults so existing call sites compile).
- [ ] **Step 3:** in `BlockIngestionOrchestrator.IngestBlocksAsync`, in the `new BlockMetadata(...)` construction (around line 84), add `SourceKey: record.FivetoolsSourceKey`.
- [ ] **Step 4:** write a failing test asserting a built block point payload contains `source_key` equal to the metadata's `SourceKey` when set, and omits/nulls it when `SourceKey` is null. (Follow the existing `BuildBlockPoint`/payload test pattern; if none exists, add `QdrantBlockPayloadTests` constructing a `BlockChunk` with `SourceKey:"DMG"` and asserting via the public upsert-mapping path or a small exposed mapper.)
- [ ] **Step 5:** run → fail. **Step 6:** in `QdrantVectorStoreService.BuildBlockPoint`, write `source_key` into the payload from `chunk.Metadata.SourceKey` (only when non-null, mirroring how nullable fields are handled there).
- [ ] **Step 7:** in `QdrantCollectionInitializer.CreatePayloadIndexes`, add a keyword payload index on `QdrantPayloadFields.SourceKey` for the blocks collection (copy the `source_book` index call).
- [ ] **Step 8:** run → pass. Full suite green. **Commit** `feat(ingest): write stable source_key on blocks + payload index`.

---

### Task 3: CountAsync-based counts + set-payload-by-book on the vector store

**Files:**
- Modify: `Features/VectorStore/IVectorStoreService.cs`
- Modify: `Features/VectorStore/QdrantVectorStoreService.cs`
- Test: `DndMcpAICsharpFun.Tests/VectorStore/QdrantVectorStoreSourceKeyTests.cs` (Testcontainers Qdrant — find the existing Qdrant integration-test base/fixture and reuse it: `grep -rl "Testcontainers\|QdrantContainer\|IAsyncLifetime" DndMcpAICsharpFun.Tests | head`)

**Interfaces — Produces (add to `IVectorStoreService`):**
- `Task<IReadOnlyDictionary<string,long>> GetSourceKeyCountsAsync(CancellationToken ct = default)` — for each `BookCatalog` key, `CountAsync(blocks, source_key==key)`; include only keys with count > 0? NO — include ALL BookCatalog keys (0 included) so the guard can see zeros.
- `Task<IReadOnlyDictionary<string,long>> GetSourceBookCountsAsync(CancellationToken ct = default)` — same, per `BookCatalog` display name via `source_book`.
- `Task<long> SetSourceKeyForBookAsync(string displayName, string key, CancellationToken ct = default)` — `client.SetPayloadAsync(blocks, payload:{source_key:key}, filter: source_book==displayName)`; return `CountAsync(source_book==displayName)`.

- [ ] **Step 1:** add the three signatures to `IVectorStoreService`.
- [ ] **Step 2:** failing integration test: seed 3 points with `source_book="PlayerHandbook 2014"` and 2 with `"Monster Manual 2014"` (no `source_key`); assert `GetSourceBookCountsAsync()["PlayerHandbook 2014"]==3`; call `SetSourceKeyForBookAsync("PlayerHandbook 2014","PHB")` → returns 3; assert `GetSourceKeyCountsAsync()["PHB"]==3` and `["MM"]==0`.
- [ ] **Step 3:** run → fail. **Step 4:** implement in `QdrantVectorStoreService`:
  - counts: `foreach (var b in BookCatalog.All) result[b.Key] = (long)(await client.CountAsync(_blocksCollectionName, filter: MatchKeyword(QdrantPayloadFields.SourceKey, b.Key), cancellationToken: ct));` (build the filter with a single `Condition`/`Match`; reuse the helper the class already uses for delete filters).
  - `SetSourceKeyForBookAsync`: `await client.SetPayloadAsync(_blocksCollectionName, new Dictionary<string,Value>{["source_key"]=key}, filter: MatchKeyword(QdrantPayloadFields.SourceBook, displayName), cancellationToken: ct);` then return the count.
- [ ] **Step 5:** run → pass. Full suite green. **Commit** `feat(vectorstore): source_key counts + set-payload-by-book`.

---

### Task 4: Backfill endpoint (idempotent, no re-embed)

**Files:**
- Create: `Features/Admin/RetrievalBackfillService.cs`
- Modify: `Features/Admin/RetrievalAdminEndpoints.cs` (add the route) + DI registration
- Modify: `DndMcpAICsharpFun.http`, `dnd-mcp-api.insomnia.json`
- Test: `DndMcpAICsharpFun.Tests/.../RetrievalBackfillTests.cs` (Testcontainers Qdrant)

**Interfaces — Produces:** `RetrievalBackfillService.BackfillAsync(ct) : Task<IReadOnlyDictionary<string,long>>` — for each `BookCatalog` entry, `SetSourceKeyForBookAsync(DisplayName, Key)`, collect per-key counts.
**Consumes:** `IVectorStoreService` (Task 3).

- [ ] **Step 1:** failing integration test: seed PHB+MM blocks without `source_key` → `BackfillAsync()` → every block has the right `source_key`, returned dict `{PHB:n, MM:m}` equals the facet counts; a second `BackfillAsync()` returns the same (idempotent).
- [ ] **Step 2:** run → fail. **Step 3:** implement `RetrievalBackfillService`; register in DI; add `POST /admin/retrieval/backfill-source-keys` (admin-key guarded — mirror an existing `/admin/retrieval/*` route) returning `{ perBook, total }`.
- [ ] **Step 4:** update `.http` + `.insomnia.json` with the endpoint (incl. `X-Admin-Api-Key`).
- [ ] **Step 5:** run → pass. Full suite green. **Commit** `feat(admin): source_key backfill endpoint`.

---

### Task 5: Switch retrieval scoping to source_key (after 2+4)

**Files:**
- Modify: `Features/Retrieval/RetrievalQuery.cs` (`SourceBooks` → `SourceKeys`)
- Modify: `Features/Retrieval/RagRetrievalService.cs` (filter on `SourceKey`)
- Modify: `Features/Rules/RuleSources.cs`, `Features/Downtime/DowntimeSources.cs`, `Features/Lore/SettingCatalog.cs` (→ keys)
- Modify: `Features/Rules/RulesAdjudicationService.cs`, `Features/Downtime/DowntimeService.cs`, `Features/Lore/SettingLoreService.cs` (pass keys)
- Test: update the scope tests to seed `source_key` and assert key-filtering (must fail on old display-name filter, pass on new)

**Interfaces — Produces:** `RetrievalQuery.SourceKeys`; `RuleSources.Keys = ["PHB","DMG"]`; `DowntimeSources.Keys = ["XGE","DMG"]`; `SettingCatalog` returns keys.

- [ ] **Step 1:** rename `RetrievalQuery.SourceBooks` → `SourceKeys` (`IReadOnlyCollection<string>?`).
- [ ] **Step 2:** in `RagRetrievalService` (lines 112–124) change the `should`-OR loop to `KeywordCondition(QdrantPayloadFields.SourceKey, key)` over `query.SourceKeys`.
- [ ] **Step 3:** `RuleSources.Books` → `public static readonly IReadOnlyCollection<string> Keys = ["PHB","DMG"];`; `DowntimeSources.Books` → `["XGE","DMG"];`; `SettingCatalog.Core` → `["PHB","DMG","MM"]` and `SettingBooks["Eberron"]` → `["ERLW"]`, resolving to keys. Update the three services to pass `.Keys` / resolved keys into `SourceKeys`.
- [ ] **Step 4:** update the existing scope tests (setting-lore non-vacuity, rules/downtime scope) so their seeded blocks carry `source_key` and the assertions check the filter/results by key. Ensure each test FAILS against the old display-name filter and PASSES with the key filter (behavior-change discrimination).
- [ ] **Step 5:** run → pass. Full suite green. **Commit** `feat(retrieval): scope blocks by stable source_key`.

---

### Task 6: Scope-health startup guard

**Files:**
- Create: `Features/Retrieval/ScopeHealthCheck.cs` (an `IHostedService`)
- Modify: `Program.cs` (register) + `DndMcpAICsharpFun.Tests/Di/FullContainerScopeValidationTests.cs` replica
- Test: `DndMcpAICsharpFun.Tests/Retrieval/ScopeHealthCheckTests.cs`

**Interfaces — Consumes:** `IVectorStoreService.GetSourceKeyCountsAsync`; the scope key sets. **Produces:** a WARNING log per zero-count scope key.

- [ ] **Step 1:** failing unit test: a fake `IVectorStoreService` returning counts with `DMG:0`; run the check's core method with a captured `ILogger`; assert a WARNING mentioning `DMG` was logged. Plus a consistency test: `RuleSources.Keys ∪ DowntimeSources.Keys ∪ SettingCatalog(all).⊆ BookCatalog.Keys`.
- [ ] **Step 2:** run → fail. **Step 3:** implement `ScopeHealthCheck : IHostedService` — on `StartAsync` (after collection init; depend on the initializer or run lazily), gather the union of scope keys, call `GetSourceKeyCountsAsync`, `LogWarning` for any with 0 (or missing). Non-fatal — never throw. Register in `Program.cs` AND `FullContainerScopeValidationTests.BuildServiceCollection`.
- [ ] **Step 4:** run → pass. Full suite green. **Commit** `feat(retrieval): startup scope-health guard`.

---

### Task 7: Metadata-only registry reconcile

**Files:**
- Create: `Features/Ingestion/RegistryReconcileService.cs`
- Modify: `Features/Admin/BooksAdminEndpoints.cs` (add route) + DI
- Modify: `DndMcpAICsharpFun.http`, `dnd-mcp-api.insomnia.json`
- Test: `DndMcpAICsharpFun.Tests/.../RegistryReconcileTests.cs` (Testcontainers Postgres + Qdrant)

**Interfaces — Produces:** `RegistryReconcileService.ReconcileAsync(ct) : Task<IReadOnlyList<string>>` (created display names).
**Consumes:** `IVectorStoreService.GetSourceBookCountsAsync`; `IngestionTracker` (find its create/insert method) or `IDbContextFactory<AppDbContext>`; `BookCatalog`; `IEntityVectorStore`/entity counts.

- [ ] **Step 1:** failing integration test: seed MM blocks in Qdrant + a single PHB `IngestionRecord` in Postgres → `ReconcileAsync()` creates an MM record with `ChunkCount`= MM block count, `Status=EntitiesIngested`, `FivetoolsSourceKey="MM"`, and leaves PHB untouched; a second run returns empty (no-op).
- [ ] **Step 2:** run → fail. **Step 3:** implement: for each `BookCatalog` display name with `GetSourceBookCountsAsync > 0` and no existing `IngestionRecord` (match by `DisplayName`), insert a record (`ChunkCount` from the block count, `EntityCount` from the entity-store count if available else null, `Status=EntitiesIngested`, `Version`+`FivetoolsSourceKey` from `BookCatalog`; `FilePath`/`FileHash` best-effort — leave null if no on-disk PDF hashes to a match). Add `POST /admin/books/reconcile` (admin-key guarded).
- [ ] **Step 4:** update `.http` + `.insomnia.json`.
- [ ] **Step 5:** run → pass. Full suite green. **Commit** `feat(admin): metadata-only registry reconcile`.

---

### Task 8: Operational deploy + DMG ingest + live smoke (controller-run)

> Not a subagent task — the controller runs these against the live container after Tasks 1–7 merge.

- [ ] **Step 1:** `docker compose up -d --build app`; wait `/health` 200.
- [ ] **Step 2 (runbook order):** `POST /admin/retrieval/backfill-source-keys` → assert each per-book count equals that book's live block count (PHB 5243, MM 4995, XGE 2138, ERLW 4322). Then `POST /admin/books/reconcile` → assert MM/XGE/ERLW records created.
- [ ] **Step 3:** identify the DMG PDF on disk (confirm it has bookmarks; note conversion cached vs slow MinerU). Register (`fivetoolsSourceKey=DMG`, `displayName="Dungeon Master's Guide 2014"`); `POST /admin/books/{id}/ingest-blocks`; poll to completion.
- [ ] **Step 4:** assert `GetSourceKeyCountsAsync`/count shows `DMG > 0` and the DMG `source_book` equals `"Dungeon Master's Guide 2014"` exactly (fix registration + re-ingest if not).
- [ ] **Step 5:** live smoke (Playwright, `test`/`test`): a DMG-covered rules question (`ask_rules`) and a downtime question (`plan_downtime`) each retrieve + CITE the DMG; the startup log no longer WARNs about `DMG` at 0 blocks.
- [ ] **Step 6:** capture any durable lesson in `.claude/skills/dev-flow/SKILL.md`; finish ceremony (commit → archive → skill-optimizer → refresh roadmap memory).
