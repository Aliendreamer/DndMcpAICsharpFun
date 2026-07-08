# Corpus-wide Entity Dedup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Collapse same-entity-different-ID duplicates in `dnd_entities` so retrieval returns each entity once per edition, with a read-only duplicate report and an opt-in destructive Qdrant compact.

**Architecture:** A pure `DuplicateResolver` picks a group's winner by authority-first precedence. `FusedRetrievalService` collapses entity candidates by dedup key before rerank (durable correctness). Two admin endpoints (`/admin/retrieval/entities/duplicates`, `/admin/retrieval/entities/compact`) scan the full corpus via a new `IEntityVectorStore` scroll-all + delete-by-ids path. Dedup stays out of the extraction/ingestion write path; canonical JSON is never rewritten.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, Qdrant (gRPC), xUnit + FluentAssertions, Testcontainers (`postgres:18-alpine` for persistence; Qdrant Testcontainer for entity-store tests).

## Global Constraints

- Target framework `net10.0`; nullable enabled; **warnings-as-errors** for every project (`Directory.Build.props`).
- Central Package Management — csproj `PackageReference`s are version-less; versions live in `Directory.Packages.props`.
- Use **Serena** symbolic tools for all code reads/edits (project rule); do not hand-edit with plain Read/Edit on code files.
- Dedup key = `(EntityNameIndex.Normalize(name), Type, Edition)`. Editions never merge.
- `DuplicateResolver` precedence, evaluated in order until decided: (1) `BookType` `Core > Supplement > Adventure > Setting > Unknown`; (2) `DataSource` authoritative (`5etools-backfill`, hand-authored) > raw LLM-parsed; (3) not-`NeedsReview` > `NeedsReview`; (4) longer `CanonicalText`; (5) lexicographically smallest `Id`.
- `DuplicateResolver` is PURE (no I/O); `BookType` is supplied by the caller via a `SourceBook → BookType` map. Unmapped source book → `BookType.Unknown`.
- New endpoints live under `/admin` (admin-key guarded) and MUST be reflected in `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json` in the same change.
- Canonical JSON under `books/canonical/` MUST NOT be created/modified/deleted by any dedup code.
- Run `dotnet` via `dangerouslyDisableSandbox: true` (git-crypt config restore needs it).

---

### Task 1: Dedup key

**Files:**
- Create: `Features/Retrieval/Entities/Dedup/EntityDedupKey.cs`
- Test: `DndMcpAICsharpFun.Tests/Retrieval/Entities/Dedup/EntityDedupKeyTests.cs`

**Interfaces:**
- Consumes: `EntityEnvelope` (`Domain/Entities/EntityEnvelope.cs`), `EntityNameIndex.Normalize` (`Features/Ingestion/EntityExtraction/EntityNameIndex.cs`), `EntityType`.
- Produces: `readonly record struct EntityDedupKey(string NormalizedName, EntityType Type, string Edition)` with `static EntityDedupKey From(EntityEnvelope e)`.

- [ ] **Step 1: Write the failing test**

```csharp
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Retrieval.Entities.Dedup;

public sealed class EntityDedupKeyTests
{
    private static EntityEnvelope Env(string id, string name, EntityType type, string edition) =>
        TestEnvelopes.Make(id: id, name: name, type: type, edition: edition);

    [Fact]
    public void Same_name_type_edition_different_id_share_key()
    {
        var a = Env("phb14.spell.fireball", "Fireball", EntityType.Spell, "Edition2014");
        var b = Env("dmg14.spell.fireball", "FIRE BALL", EntityType.Spell, "Edition2014");
        EntityDedupKey.From(a).Should().Be(EntityDedupKey.From(b));
    }

    [Fact]
    public void Different_edition_does_not_share_key()
    {
        var a = Env("phb14.spell.fireball", "Fireball", EntityType.Spell, "Edition2014");
        var b = Env("phb24.spell.fireball", "Fireball", EntityType.Spell, "Edition2024");
        EntityDedupKey.From(a).Should().NotBe(EntityDedupKey.From(b));
    }

    [Fact]
    public void Different_type_does_not_share_key()
    {
        var a = Env("x.race.dwarf", "Dwarf", EntityType.Race, "Edition2014");
        var b = Env("x.monster.dwarf", "Dwarf", EntityType.Monster, "Edition2014");
        EntityDedupKey.From(a).Should().NotBe(EntityDedupKey.From(b));
    }
}
```

- [ ] **Step 2: Create the shared test-envelope factory (needed by every dedup test)**

Create `DndMcpAICsharpFun.Tests/Retrieval/Entities/Dedup/TestEnvelopes.cs`:

```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Tests.Retrieval.Entities.Dedup;

internal static class TestEnvelopes
{
    private static readonly JsonElement EmptyFields = JsonDocument.Parse("{}").RootElement.Clone();

    public static EntityEnvelope Make(
        string id, string name, EntityType type, string edition,
        string sourceBook = "PHB", string dataSource = "", bool needsReview = false,
        string canonicalText = "text") =>
        new(
            Id: id, Type: type, Name: name, SourceBook: sourceBook, Edition: edition,
            Page: null, FirstAppearedIn: new FirstAppearance(sourceBook, edition),
            RevisedIn: [], SettingTags: [], CanonicalText: canonicalText, Fields: EmptyFields,
            DataSource: dataSource, NeedsReview: needsReview);
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~EntityDedupKeyTests` (with `dangerouslyDisableSandbox: true`)
Expected: FAIL — `EntityDedupKey` does not exist.

- [ ] **Step 4: Implement `EntityDedupKey`**

```csharp
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup;

/// <summary>Identity of a real-world entity for corpus dedup: normalized name + type + edition.</summary>
public readonly record struct EntityDedupKey(string NormalizedName, EntityType Type, string Edition)
{
    public static EntityDedupKey From(EntityEnvelope e) =>
        new(EntityNameIndex.Normalize(e.Name), e.Type, e.Edition);
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~EntityDedupKeyTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Features/Retrieval/Entities/Dedup/EntityDedupKey.cs DndMcpAICsharpFun.Tests/Retrieval/Entities/Dedup/
git commit -m "feat(dedup): entity dedup key (normalized name + type + edition)"
```

---

### Task 2: DuplicateResolver (authority-first winner)

**Files:**
- Create: `Features/Retrieval/Entities/Dedup/DuplicateResolver.cs`
- Test: `DndMcpAICsharpFun.Tests/Retrieval/Entities/Dedup/DuplicateResolverTests.cs`

**Interfaces:**
- Consumes: `EntityEnvelope`, `BookType` (`Domain/BookType.cs`), `TestEnvelopes` (test only).
- Produces:
  - `static class DuplicateResolver`
  - `EntityEnvelope Winner(IReadOnlyList<EntityEnvelope> group, IReadOnlyDictionary<string, BookType> bookTypeBySourceBook)` — returns the single winner; throws `ArgumentException` on an empty group.
  - Authoritative `DataSource` values recognised: `"5etools-backfill"`, `"hand-authored"` (case-insensitive). Everything else is treated as non-authoritative.

- [ ] **Step 1: Write the failing tests**

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Retrieval.Entities.Dedup;

public sealed class DuplicateResolverTests
{
    private static readonly Dictionary<string, BookType> Books = new()
    {
        ["PHB"] = BookType.Core,
        ["XGE"] = BookType.Supplement,
        ["ADV"] = BookType.Adventure,
    };

    private static EntityEnvelope E(string id, string source = "PHB", string ds = "",
        bool needsReview = false, string text = "text") =>
        TestEnvelopes.Make(id, "Fireball", EntityType.Spell, "Edition2014",
            sourceBook: source, dataSource: ds, needsReview: needsReview, canonicalText: text);

    [Fact]
    public void Core_book_beats_supplement()
    {
        var core = E("a", "PHB");
        var supp = E("b", "XGE");
        DuplicateResolver.Winner([supp, core], Books).Should().Be(core);
    }

    [Fact]
    public void Authority_outranks_needs_review()
    {
        var coreFlagged = E("a", "PHB", needsReview: true);
        var suppClean   = E("b", "XGE", needsReview: false);
        DuplicateResolver.Winner([suppClean, coreFlagged], Books).Should().Be(coreFlagged);
    }

    [Fact]
    public void Authoritative_datasource_beats_parsed_within_same_book()
    {
        var backfill = E("a", "PHB", ds: "5etools-backfill");
        var parsed   = E("b", "PHB", ds: "");
        DuplicateResolver.Winner([parsed, backfill], Books).Should().Be(backfill);
    }

    [Fact]
    public void Not_needs_review_beats_needs_review_when_authority_ties()
    {
        var clean   = E("a", "PHB", needsReview: false);
        var flagged = E("b", "PHB", needsReview: true);
        DuplicateResolver.Winner([flagged, clean], Books).Should().Be(clean);
    }

    [Fact]
    public void Longer_text_wins_when_higher_tiers_tie()
    {
        var shortT = E("a", "PHB", text: "x");
        var longT  = E("b", "PHB", text: "xxxxxxxxxx");
        DuplicateResolver.Winner([shortT, longT], Books).Should().Be(longT);
    }

    [Fact]
    public void Lexicographic_id_is_the_final_deterministic_tiebreak()
    {
        var z = E("zzz", "PHB");
        var a = E("aaa", "PHB");
        DuplicateResolver.Winner([z, a], Books).Should().Be(a);
        DuplicateResolver.Winner([a, z], Books).Should().Be(a); // order-independent
    }

    [Fact]
    public void Unmapped_source_book_is_unknown_authority()
    {
        var known   = E("a", "PHB");            // Core
        var unmapped = E("b", "MYSTERY");       // not in map → Unknown (lowest)
        DuplicateResolver.Winner([unmapped, known], Books).Should().Be(known);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~DuplicateResolverTests`
Expected: FAIL — `DuplicateResolver` does not exist.

- [ ] **Step 3: Implement `DuplicateResolver`**

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup;

/// <summary>Picks the single winner of a duplicate group by authority-first precedence.</summary>
public static class DuplicateResolver
{
    private static readonly HashSet<string> AuthoritativeSources =
        new(StringComparer.OrdinalIgnoreCase) { "5etools-backfill", "hand-authored" };

    public static EntityEnvelope Winner(
        IReadOnlyList<EntityEnvelope> group,
        IReadOnlyDictionary<string, BookType> bookTypeBySourceBook)
    {
        if (group.Count == 0) throw new ArgumentException("Duplicate group is empty", nameof(group));

        return group
            .OrderByDescending(e => BookAuthority(bookTypeBySourceBook.GetValueOrDefault(e.SourceBook, BookType.Unknown)))
            .ThenByDescending(e => AuthoritativeSources.Contains(e.DataSource) ? 1 : 0)
            .ThenByDescending(e => e.NeedsReview ? 0 : 1)
            .ThenByDescending(e => e.CanonicalText.Length)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .First();
    }

    // Higher = more authoritative. Core outranks all supplements/adventures/settings.
    private static int BookAuthority(BookType t) => t switch
    {
        BookType.Core       => 4,
        BookType.Supplement => 3,
        BookType.Adventure  => 2,
        BookType.Setting    => 1,
        _                   => 0, // Unknown
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~DuplicateResolverTests`
Expected: PASS (all 7).

- [ ] **Step 5: Commit**

```bash
git add Features/Retrieval/Entities/Dedup/DuplicateResolver.cs DndMcpAICsharpFun.Tests/Retrieval/Entities/Dedup/DuplicateResolverTests.cs
git commit -m "feat(dedup): authority-first DuplicateResolver"
```

---

### Task 3: BookType lookup provider

**Files:**
- Create: `Features/Retrieval/Entities/Dedup/BookTypeLookup.cs`
- Modify: `Extensions/ServiceCollectionExtensions.cs` (register the provider)
- Test: `DndMcpAICsharpFun.Tests/Retrieval/Entities/Dedup/BookTypeLookupTests.cs`

**Interfaces:**
- Consumes: `IngestionTracker.GetAllAsync(int limit, int offset, CancellationToken)` returning `List<IngestionRecord>`; `IngestionRecord` has `BookType`, `FivetoolsSourceKey`, and `EntityIdSlug.BookSlug(record)`; entity `SourceBook = record.FivetoolsSourceKey ?? merged.SourceBook`.
- Produces: `interface IBookTypeLookup { Task<IReadOnlyDictionary<string, BookType>> BuildAsync(CancellationToken ct = default); }` and `sealed class BookTypeLookup(IngestionTracker tracker) : IBookTypeLookup`. The map is keyed on every token an entity's `SourceBook` might hold: `FivetoolsSourceKey` (when present) AND `EntityIdSlug.BookSlug(record)`, both → `record.BookType`.

- [ ] **Step 1: Write the failing test** (uses an in-memory fake tracker seam — inject records directly)

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Retrieval.Entities.Dedup;

public sealed class BookTypeLookupTests
{
    [Fact]
    public void Maps_fivetools_key_and_slug_to_booktype()
    {
        var records = new[]
        {
            new IngestionRecord { Id = 1, DisplayName = "Player's Handbook 2014",
                FivetoolsSourceKey = "PHB", BookType = BookType.Core },
            new IngestionRecord { Id = 2, DisplayName = "Homebrew Tome",
                FivetoolsSourceKey = null, BookType = BookType.Supplement },
        };

        var map = BookTypeLookup.Build(records);

        map["PHB"].Should().Be(BookType.Core);                  // fivetools key
        map.Should().ContainKey("homebrew-tome");               // display-name slug
        map["homebrew-tome"].Should().Be(BookType.Supplement);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~BookTypeLookupTests`
Expected: FAIL — `BookTypeLookup` does not exist.

- [ ] **Step 3: Implement `BookTypeLookup`** (pure `Build` static + async wrapper that reads the tracker)

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Entities;              // EntityIdSlug
using DndMcpAICsharpFun.Features.Ingestion.Tracking;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup;

public interface IBookTypeLookup
{
    Task<IReadOnlyDictionary<string, BookType>> BuildAsync(CancellationToken ct = default);
}

public sealed class BookTypeLookup(IngestionTracker tracker) : IBookTypeLookup
{
    public async Task<IReadOnlyDictionary<string, BookType>> BuildAsync(CancellationToken ct = default)
    {
        var all = await tracker.GetAllAsync(limit: int.MaxValue, offset: 0, ct);
        return Build(all);
    }

    public static IReadOnlyDictionary<string, BookType> Build(IEnumerable<IngestionRecord> records)
    {
        var map = new Dictionary<string, BookType>(StringComparer.Ordinal);
        foreach (var r in records)
        {
            if (!string.IsNullOrEmpty(r.FivetoolsSourceKey)) map[r.FivetoolsSourceKey] = r.BookType;
            map[EntityIdSlug.BookSlug(r)] = r.BookType;
        }
        return map;
    }
}
```

> If `EntityIdSlug.BookSlug` throws for a record without enough data, wrap the single line in a try and skip that record. Verify the exact `EntityIdSlug` signature with Serena before implementing; adjust the call if needed. The slug for "Player's Handbook 2014" without a fivetools key is expected to be `player-s-handbook-2014` or similar — assert on whatever `BookSlug` actually returns (fix the test's expected string to match real output).

- [ ] **Step 4: Register in DI**

In `Extensions/ServiceCollectionExtensions.cs`, next to the entity-retrieval registrations, add:

```csharp
services.AddScoped<IBookTypeLookup, BookTypeLookup>();
```

- [ ] **Step 5: Run test to verify it passes; build**

Run: `dotnet test --filter FullyQualifiedName~BookTypeLookupTests` then `dotnet build`
Expected: PASS; build clean.

- [ ] **Step 6: Commit**

```bash
git add Features/Retrieval/Entities/Dedup/BookTypeLookup.cs Extensions/ServiceCollectionExtensions.cs DndMcpAICsharpFun.Tests/Retrieval/Entities/Dedup/BookTypeLookupTests.cs
git commit -m "feat(dedup): SourceBook->BookType lookup from ingestion records"
```

---

### Task 4: Collapse duplicates in fused retrieval (Slice 1)

**Files:**
- Create: `Features/Retrieval/Entities/Dedup/EntityHitCollapser.cs`
- Modify: `Features/Retrieval/FusedRetrievalService.cs` (inject `IBookTypeLookup`; collapse in `FetchEntitiesAsync`)
- Test: `DndMcpAICsharpFun.Tests/Retrieval/Entities/Dedup/EntityHitCollapserTests.cs`

**Interfaces:**
- Consumes: `EntitySearchHit(EntityEnvelope Envelope, float Score, string PointId)` (`Features/VectorStore/Entities/EntityPoint.cs`), `EntityDedupKey`, `DuplicateResolver`, `IBookTypeLookup`.
- Produces: `static class EntityHitCollapser` with `IReadOnlyList<EntitySearchHit> Collapse(IReadOnlyList<EntitySearchHit> hits, IReadOnlyDictionary<string, BookType> bookTypes)` — one hit per dedup key: the `DuplicateResolver` winner's envelope, with `Score = max score in group`, `PointId` = winner's point id.

- [ ] **Step 1: Write the failing collapser test**

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Retrieval.Entities.Dedup;

public sealed class EntityHitCollapserTests
{
    private static readonly Dictionary<string, BookType> Books = new() { ["PHB"] = BookType.Core, ["XGE"] = BookType.Supplement };

    private static EntitySearchHit Hit(string id, string src, float score, string edition = "Edition2014") =>
        new(TestEnvelopes.Make(id, "Fireball", EntityType.Spell, edition, sourceBook: src), score, "pt-" + id);

    [Fact]
    public void Duplicates_collapse_to_winner_with_max_score()
    {
        var core = Hit("a", "PHB", score: 0.4f);
        var supp = Hit("b", "XGE", score: 0.9f);
        var result = EntityHitCollapser.Collapse([core, supp], Books);

        result.Should().HaveCount(1);
        result[0].Envelope.Id.Should().Be("a");     // Core wins
        result[0].Score.Should().Be(0.9f);          // max score of the group
    }

    [Fact]
    public void Distinct_editions_both_survive()
    {
        var y2014 = Hit("a", "PHB", 0.5f, "Edition2014");
        var y2024 = Hit("b", "PHB", 0.6f, "Edition2024");
        var result = EntityHitCollapser.Collapse([y2014, y2024], Books);
        result.Should().HaveCount(2);
    }

    [Fact]
    public void Non_duplicate_hits_pass_through()
    {
        var fireball = Hit("a", "PHB", 0.5f);
        var iceknife = new EntitySearchHit(
            TestEnvelopes.Make("c", "Ice Knife", EntityType.Spell, "Edition2014", sourceBook: "PHB"), 0.3f, "pt-c");
        EntityHitCollapser.Collapse([fireball, iceknife], Books).Should().HaveCount(2);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~EntityHitCollapserTests`
Expected: FAIL — `EntityHitCollapser` does not exist.

- [ ] **Step 3: Implement `EntityHitCollapser`**

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.VectorStore.Entities;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup;

public static class EntityHitCollapser
{
    public static IReadOnlyList<EntitySearchHit> Collapse(
        IReadOnlyList<EntitySearchHit> hits,
        IReadOnlyDictionary<string, BookType> bookTypes)
    {
        if (hits.Count <= 1) return hits;

        return hits
            .GroupBy(h => EntityDedupKey.From(h.Envelope))
            .Select(g =>
            {
                var group = g.ToList();
                if (group.Count == 1) return group[0];
                var winner = DuplicateResolver.Winner(group.Select(h => h.Envelope).ToList(), bookTypes);
                var maxScore = group.Max(h => h.Score);
                var winnerHit = group.First(h => h.Envelope.Id == winner.Id); // Id is unique within a group
                return winnerHit with { Score = maxScore };
            })
            .ToList();
    }
}
```

- [ ] **Step 4: Run collapser test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~EntityHitCollapserTests`
Expected: PASS.

- [ ] **Step 5: Wire the collapser into `FusedRetrievalService`**

Using Serena, add `IBookTypeLookup bookTypeLookup` to the primary constructor of `FusedRetrievalService`, then change `FetchEntitiesAsync` to collapse before projecting:

```csharp
private async Task<List<FusedCandidate>> FetchEntitiesAsync(
    float[] vector, int limit, CancellationToken ct)
{
    var hits = await entityStore.SearchAsync(vector, new EntityFilters(), limit, ct);
    var bookTypes = await bookTypeLookup.BuildAsync(ct);
    var collapsed = EntityHitCollapser.Collapse(hits.ToList(), bookTypes);
    return collapsed.Select(h => new FusedCandidate(
        Source: "entity",
        Id: h.Envelope.Id,
        Title: h.Envelope.Name,
        Text: h.Envelope.CanonicalText,
        Score: h.Score)).ToList();
}
```

Add `using DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup;` to the file.

- [ ] **Step 6: Build + run all non-persistence retrieval tests**

Run: `dotnet build` then `dotnet test --filter FullyQualifiedName~Retrieval`
Expected: build clean; existing fused-retrieval tests still green (a `FusedRetrievalService` test may need an `IBookTypeLookup` stub — provide one returning an empty map, or the real lookup with a fake tracker).

- [ ] **Step 7: Commit**

```bash
git add Features/Retrieval/
git commit -m "feat(dedup): collapse duplicate entities in fused retrieval (slice 1)"
```

---

### Task 5: Full-corpus scroll + delete-by-ids on the entity store

**Files:**
- Modify: `Features/VectorStore/Entities/IEntityVectorStore.cs`
- Modify: `Features/VectorStore/Entities/QdrantEntityVectorStore.cs`
- Test: `DndMcpAICsharpFun.Tests/VectorStore/Entities/QdrantEntityVectorStoreScrollTests.cs` (Qdrant Testcontainer — Docker required)

**Interfaces:**
- Produces on `IEntityVectorStore`:
  - `Task<IReadOnlyList<EntitySearchHit>> ScrollAllAsync(CancellationToken ct = default)` — every point as `EntitySearchHit` (Score 0), paginated internally.
  - `Task DeleteByIdsAsync(IReadOnlyCollection<string> entityIds, CancellationToken ct = default)` — delete points whose `EntityPayloadFields.Id` is in the set.

- [ ] **Step 1: Write the failing Testcontainer test**

```csharp
// Follows the existing Qdrant entity-store test fixture pattern in the repo.
// Upsert 3 points (two share a dedup key), assert ScrollAllAsync returns all 3,
// then DeleteByIdsAsync([loserId]) leaves 2.
```

Model it on the existing entity-store integration tests (find them with Serena: search `QdrantEntityVectorStore` under `DndMcpAICsharpFun.Tests`). Assert: `ScrollAllAsync` count == upserted count; after `DeleteByIdsAsync([oneId])`, `GetByIdAsync(thatId)` is null and the others remain.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~QdrantEntityVectorStoreScrollTests`
Expected: FAIL — methods not defined (won't compile).

- [ ] **Step 3: Add the interface members** to `IEntityVectorStore.cs`:

```csharp
Task<IReadOnlyList<EntitySearchHit>> ScrollAllAsync(CancellationToken ct = default);
Task DeleteByIdsAsync(IReadOnlyCollection<string> entityIds, CancellationToken ct = default);
```

- [ ] **Step 4: Implement in `QdrantEntityVectorStore.cs`** (reuse the paginated `ScrollAsync` pattern already used by `GetByIdsAsync`)

```csharp
public async Task<IReadOnlyList<EntitySearchHit>> ScrollAllAsync(CancellationToken ct = default)
{
    const uint PageSize = 1000;
    var result = new List<EntitySearchHit>();
    PointId? offset = null;
    do
    {
        var page = await client.ScrollAsync(
            _collection, filter: null, offset: offset, limit: PageSize,
            payloadSelector: true, cancellationToken: ct);
        foreach (var p in page.Result)
        {
            var env = ToEnvelope(p.Payload);
            if (env is not null) result.Add(new EntitySearchHit(env, 0f, p.Id.Uuid));
        }
        offset = page.NextPageOffset;
    } while (offset is not null);
    return result;
}

public async Task DeleteByIdsAsync(IReadOnlyCollection<string> entityIds, CancellationToken ct = default)
{
    if (entityIds.Count == 0) return;
    var filter = new Filter();
    var match = new Match { Keywords = new RepeatedStrings() };
    match.Keywords.Strings.AddRange(entityIds);
    filter.Must.Add(new Condition { Field = new FieldCondition { Key = EntityPayloadFields.Id, Match = match } });
    await client.DeleteAsync(_collection, filter, cancellationToken: ct);
}
```

> Confirm the `ScrollAsync` overload signature with `filter: null` compiles against the installed Qdrant client; if a non-null filter is required, pass `new Filter()`.

- [ ] **Step 5: Run the Testcontainer test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~QdrantEntityVectorStoreScrollTests` (Docker running)
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Features/VectorStore/Entities/ DndMcpAICsharpFun.Tests/VectorStore/Entities/
git commit -m "feat(dedup): full-corpus scroll + delete-by-ids on entity store"
```

---

### Task 6: EntityDuplicateService (report + compact)

**Files:**
- Create: `Features/Retrieval/Entities/Dedup/EntityDuplicateService.cs`
- Create: `Features/Retrieval/Entities/Dedup/DuplicateGroup.cs` (DTOs)
- Modify: `Extensions/ServiceCollectionExtensions.cs`
- Test: `DndMcpAICsharpFun.Tests/Retrieval/Entities/Dedup/EntityDuplicateServiceTests.cs` (unit, with a fake `IEntityVectorStore` + injected book-type map)

**Interfaces:**
- Produces:
  - `sealed record DuplicateGroup(string Key, string WinnerId, IReadOnlyList<string> LoserIds)`
  - `sealed record DuplicateReport(int GroupCount, int LoserCount, IReadOnlyList<DuplicateGroup> Groups)`
  - `sealed class EntityDuplicateService(IEntityVectorStore store, IBookTypeLookup bookTypeLookup)` with:
    - `Task<DuplicateReport> FindDuplicatesAsync(CancellationToken ct)` — scroll all, group by key, keep groups with >1 member.
    - `Task<DuplicateReport> CompactAsync(bool apply, CancellationToken ct)` — same grouping; when `apply`, call `store.DeleteByIdsAsync(allLoserIds)`; always returns the report of groups acted on.

- [ ] **Step 1: Write the failing test** (fake store returns a fixed hit list; assert grouping, winner, and that `apply=false` deletes nothing, `apply=true` deletes only losers)

```csharp
// FakeEntityVectorStore implements IEntityVectorStore; ScrollAllAsync returns a seeded list;
// DeleteByIdsAsync records the ids it was called with.
// FakeBookTypeLookup returns { ["PHB"]=Core, ["XGE"]=Supplement }.
// Seed: two Fireball hits (phb/xge, same edition) + one unique Ice Knife.
// FindDuplicatesAsync -> 1 group, WinnerId = phb id, LoserIds = [xge id].
// CompactAsync(apply:false) -> same report, DeleteByIdsAsync NOT called.
// CompactAsync(apply:true)  -> DeleteByIdsAsync called once with exactly [xge id].
```

Implement the two fakes inline in the test file. Assert counts and the exact deleted-id set.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~EntityDuplicateServiceTests`
Expected: FAIL — service/DTOs not defined.

- [ ] **Step 3: Implement the DTOs and service**

```csharp
namespace DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup;

public sealed record DuplicateGroup(string Key, string WinnerId, IReadOnlyList<string> LoserIds);
public sealed record DuplicateReport(int GroupCount, int LoserCount, IReadOnlyList<DuplicateGroup> Groups);
```

```csharp
using DndMcpAICsharpFun.Features.VectorStore.Entities;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup;

public sealed class EntityDuplicateService(IEntityVectorStore store, IBookTypeLookup bookTypeLookup)
{
    public async Task<DuplicateReport> FindDuplicatesAsync(CancellationToken ct = default)
    {
        var (groups, _) = await BuildGroupsAsync(ct);
        return Report(groups);
    }

    public async Task<DuplicateReport> CompactAsync(bool apply, CancellationToken ct = default)
    {
        var (groups, loserIds) = await BuildGroupsAsync(ct);
        if (apply && loserIds.Count > 0)
            await store.DeleteByIdsAsync(loserIds, ct);
        return Report(groups);
    }

    private async Task<(List<DuplicateGroup> groups, List<string> loserIds)> BuildGroupsAsync(CancellationToken ct)
    {
        var hits = await store.ScrollAllAsync(ct);
        var bookTypes = await bookTypeLookup.BuildAsync(ct);
        var groups = new List<DuplicateGroup>();
        var loserIds = new List<string>();

        foreach (var g in hits.GroupBy(h => EntityDedupKey.From(h.Envelope)))
        {
            var members = g.ToList();
            if (members.Count < 2) continue;
            var winner = DuplicateResolver.Winner(members.Select(h => h.Envelope).ToList(), bookTypes);
            var losers = members.Where(h => h.Envelope.Id != winner.Id).Select(h => h.Envelope.Id).ToList();
            loserIds.AddRange(losers);
            groups.Add(new DuplicateGroup(
                $"{g.Key.NormalizedName}|{g.Key.Type}|{g.Key.Edition}", winner.Id, losers));
        }
        return (groups, loserIds);
    }

    private static DuplicateReport Report(List<DuplicateGroup> groups) =>
        new(groups.Count, groups.Sum(g => g.LoserIds.Count), groups);
}
```

- [ ] **Step 4: Register in DI** (`ServiceCollectionExtensions.cs`):

```csharp
services.AddScoped<EntityDuplicateService>();
```

- [ ] **Step 5: Run the test + build**

Run: `dotnet test --filter FullyQualifiedName~EntityDuplicateServiceTests` then `dotnet build`
Expected: PASS; build clean.

- [ ] **Step 6: Commit**

```bash
git add Features/Retrieval/Entities/Dedup/EntityDuplicateService.cs Features/Retrieval/Entities/Dedup/DuplicateGroup.cs Extensions/ServiceCollectionExtensions.cs DndMcpAICsharpFun.Tests/Retrieval/Entities/Dedup/EntityDuplicateServiceTests.cs
git commit -m "feat(dedup): EntityDuplicateService report + compact"
```

---

### Task 7: Admin endpoints

**Files:**
- Modify: `Features/Admin/RetrievalAdminEndpoints.cs` (add the two routes to the existing `/admin` retrieval group)
- Test: `DndMcpAICsharpFun.Tests/Admin/EntityDuplicatesEndpointsTests.cs` (WebApplicationFactory-style, matching existing admin endpoint tests)

**Interfaces:**
- Consumes: `EntityDuplicateService`.
- Produces routes (under `/admin`):
  - `GET /admin/retrieval/entities/duplicates` → `Results.Ok(DuplicateReport)`.
  - `POST /admin/retrieval/entities/compact` with optional `bool apply` query (default false) → `Results.Ok(DuplicateReport)`.

- [ ] **Step 1: Write failing endpoint tests** (model on existing `RetrievalAdmin` / admin endpoint tests — find with Serena). Assert: unauthenticated call → 401/403 (admin-key guard); `GET .../duplicates` → 200 with report; `POST .../compact` (no apply) → 200 and store's delete not invoked; `POST .../compact?apply=true` → 200.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~EntityDuplicatesEndpointsTests`
Expected: FAIL — routes not mapped.

- [ ] **Step 3: Add the routes** in `RetrievalAdminEndpoints.MapRetrievalAdmin` (Serena `find_symbol` to locate the method; follow its existing handler style):

```csharp
group.MapGet("/retrieval/entities/duplicates",
    async (EntityDuplicateService svc, CancellationToken ct) =>
        Results.Ok(await svc.FindDuplicatesAsync(ct)));

group.MapPost("/retrieval/entities/compact",
    async (EntityDuplicateService svc, bool apply, CancellationToken ct) =>
        Results.Ok(await svc.CompactAsync(apply, ct)));
```

> If the existing group binds optional bools differently, match that convention; `apply` defaults to false when the query param is absent (`bool?` → `?? false` if minimal-API binding requires it).

- [ ] **Step 4: Run tests + build**

Run: `dotnet test --filter FullyQualifiedName~EntityDuplicatesEndpointsTests` then `dotnet build`
Expected: PASS; build clean.

- [ ] **Step 5: Commit**

```bash
git add Features/Admin/RetrievalAdminEndpoints.cs DndMcpAICsharpFun.Tests/Admin/EntityDuplicatesEndpointsTests.cs
git commit -m "feat(dedup): admin duplicates report + compact endpoints"
```

---

### Task 8: API contract docs

**Files:**
- Modify: `DndMcpAICsharpFun.http`
- Modify: `dnd-mcp-api.insomnia.json`

- [ ] **Step 1: Add `.http` examples** near the other `/admin/retrieval` entries:

```
### Admin — Report duplicate entities (read-only; groups by normalized name+type+edition)
GET {{baseUrl}}/admin/retrieval/entities/duplicates
X-Admin-Api-Key: {{adminKey}}

### Admin — Compact duplicate entities (dry-run: reports only, deletes nothing)
POST {{baseUrl}}/admin/retrieval/entities/compact
X-Admin-Api-Key: {{adminKey}}

### Admin — Compact duplicate entities (apply: deletes loser points from dnd_entities; canonical JSON untouched)
POST {{baseUrl}}/admin/retrieval/entities/compact?apply=true
X-Admin-Api-Key: {{adminKey}}
```

- [ ] **Step 2: Mirror both endpoints into `dnd-mcp-api.insomnia.json`** (add request objects with the `X-Admin-Api-Key` header, matching the existing admin request shape). Then validate JSON:

Run: `python3 -c "import json; json.load(open('dnd-mcp-api.insomnia.json')); print('valid')"`
Expected: `valid`.

- [ ] **Step 3: Commit**

```bash
git add DndMcpAICsharpFun.http dnd-mcp-api.insomnia.json
git commit -m "docs(api): duplicates report + compact endpoints in .http/insomnia"
```

---

### Task 9: Full verification + review

- [ ] **Step 1: Full build + non-persistence suite**

Run: `dotnet build` then `dotnet test --filter FullyQualifiedName!~Persistence` (or the repo's non-persistence filter)
Expected: build clean under warnings-as-errors; all green.

- [ ] **Step 2: Persistence/Testcontainer suite** (Docker up)

Run: `dotnet test` (full)
Expected: all green, including the new scroll/delete Testcontainer test.

- [ ] **Step 3: Drive the endpoints against a running host** (per the `verify` skill)

Start the host (`dotnet run`), then: `GET /admin/retrieval/entities/duplicates` (observe groups), `POST /admin/retrieval/entities/compact` (dry-run — nothing deleted), `POST /admin/retrieval/entities/compact?apply=true` (losers deleted), re-run the report (fewer/no groups). Confirm no file under `books/canonical/` changed (`git status books/canonical/` clean).

- [ ] **Step 4: Whole-change code review** — dispatch the opus Serena-based code-reviewer over the diff; cross-check every ADDED/MODIFIED requirement in `specs/` against the implementation. Address findings.

- [ ] **Step 5: Final commit if review produced fixes**, then stop for the user's "commit"/"archive" directive.
