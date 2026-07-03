# MM Canonical Monster-Name Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rewrite the stat-line-garbled monster names in the committed `books/canonical/mm14.json` to their clean 5etools canonical form and remove the resulting duplicate backfills — in place, deterministically, without a re-extract.

**Architecture:** A pure static transform `MonsterNameCleanup.Clean(entities, matcher, bookKey)` reuses the extractor's exact `EntityNameMatcher` (stat-line stripping) + `EntityIdSlug` to rewrite names/ids and de-dupe grounded-vs-backfill collisions. A one-time `Tools/CanonicalNameCleanup` console wraps it with file I/O (`CanonicalJsonLoader`/`CanonicalJsonWriter`). No HTTP endpoint.

**Tech Stack:** .NET 10, C#, xUnit + FluentAssertions, System.Text.Json.

## Global Constraints

- `net10.0`, nullable enabled, implicit usings, **warnings-as-errors** (all projects; `Directory.Build.props`).
- Central Package Management — csproj `PackageReference`s are version-less (`Directory.Packages.props`).
- Serena symbolic tools for all code reads/edits; built-in Read/Edit forbidden on code files.
- The transform MUST reuse `EntityNameMatcher.MatchOfType` + `EntityIdSlug.For` — NO new stripping or slug logic.
- Never delete a grounded (dataSource ≠ `"5etools-backfill"`) entity.
- Run tests with the sandbox disabled (git-crypt) and Docker up for persistence tests: the non-persistence tests here need no DB.

---

### Task 1: Pure cleanup transform + counts type

**Files:**
- Create: `Features/Ingestion/FivetoolsIngestion/MonsterNameCleanup.cs`
- Test: `DndMcpAICsharpFun.Tests/Ingestion/FivetoolsIngestion/MonsterNameCleanupTests.cs`

**Interfaces:**
- Consumes: `EntityNameMatcher.MatchOfType(string, EntityType) -> (string Canonical, EntityType Type)?`; `EntityIdSlug.For(string book, EntityType, string name) -> string`; `EntityNameIndex.Normalize(string) -> string`; `EntityEnvelope` (record, positional ctor: `Id, Type, Name, SourceBook, Edition, Page, FirstAppearedIn, RevisedIn, SettingTags, CanonicalText, Fields, DataSource="", Srd, Srd52, BasicRules2024, NeedsReview, Keywords, Disposition`).
- Produces:
  - `readonly record struct MonsterNameCleanupCounts(int Cleaned, int Deduped, int GroundedCollisionsFlagged, int Grounded, int Backfilled)`
  - `static (IReadOnlyList<EntityEnvelope> Entities, MonsterNameCleanupCounts Counts) MonsterNameCleanup.Clean(IReadOnlyList<EntityEnvelope> entities, EntityNameMatcher matcher, string bookKey)`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Tests;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Ingestion.FivetoolsIngestion;

public sealed class MonsterNameCleanupTests
{
    private static readonly EntityNameMatcher Matcher =
        new(new EntityNameIndex(TestPaths.RepoFile("5etools")));

    private const string BookKey = "mm14";
    private const string Backfill = "5etools-backfill";

    // Minimal Monster envelope; grounded unless dataSource == Backfill.
    private static EntityEnvelope Monster(string name, string dataSource = "extraction", bool needsReview = false) =>
        new(
            Id: EntityIdSlug.For(BookKey, EntityType.Monster, name),
            Type: EntityType.Monster,
            Name: name,
            SourceBook: "MM",
            Edition: "Edition2014",
            Page: null,
            FirstAppearedIn: new FirstAppearance("MM", "Edition2014", null),
            RevisedIn: Array.Empty<Revision>(),
            SettingTags: Array.Empty<string>(),
            CanonicalText: "",
            Fields: JsonDocument.Parse("{\"hp\":367}").RootElement.Clone(),
            DataSource: dataSource,
            NeedsReview: needsReview);

    private static EntityEnvelope NonMonster(string name) =>
        Monster(name) with { Type = EntityType.Spell, Fields = JsonDocument.Parse("{}").RootElement.Clone() };

    [Fact]
    public void Garbled_dragon_name_is_rewritten_and_id_recomputed()
    {
        var garbled = Monster("ANCIENT BLACK DRAGON Gargantuan dragon, chaotic evil");
        var (entities, counts) = MonsterNameCleanup.Clean(new[] { garbled }, Matcher, BookKey);

        counts.Cleaned.Should().Be(1);
        var e = entities.Single();
        e.Name.Should().Be("Ancient Black Dragon");
        e.Id.Should().Be(EntityIdSlug.For(BookKey, EntityType.Monster, "Ancient Black Dragon"));
        e.DataSource.Should().Be("extraction");            // preserved
        e.Fields.GetProperty("hp").GetInt32().Should().Be(367); // preserved
    }

    [Fact]
    public void Clean_names_and_nonmonsters_untouched_and_idempotent()
    {
        var input = new[] { Monster("Dragon Turtle"), NonMonster("Fireball") };
        var (once, c1) = MonsterNameCleanup.Clean(input, Matcher, BookKey);

        c1.Should().Be(new MonsterNameCleanupCounts(0, 0, 0, Grounded: 1, Backfilled: 0));
        once.Select(e => (e.Name, e.Id)).Should().Equal(input.Select(e => (e.Name, e.Id)));

        var (twice, c2) = MonsterNameCleanup.Clean(once, Matcher, BookKey);
        c2.Cleaned.Should().Be(0);
        c2.Deduped.Should().Be(0);
        twice.Select(e => (e.Name, e.Id)).Should().Equal(once.Select(e => (e.Name, e.Id)));
    }

    [Fact]
    public void Cleaned_grounded_dragon_drops_its_backfill_duplicate()
    {
        var garbled = Monster("ANCIENT BLACK DRAGON Gargantuan dragon, chaotic evil");   // grounded
        var backfillDupe = Monster("Ancient Black Dragon", dataSource: Backfill);        // clean backfill
        var (entities, counts) = MonsterNameCleanup.Clean(new[] { garbled, backfillDupe }, Matcher, BookKey);

        counts.Cleaned.Should().Be(1);
        counts.Deduped.Should().Be(1);
        entities.Should().ContainSingle(e => e.Name == "Ancient Black Dragon");
        entities.Single(e => e.Name == "Ancient Black Dragon").DataSource.Should().Be("extraction"); // grounded kept
        counts.Grounded.Should().Be(1);
        counts.Backfilled.Should().Be(0);
    }

    [Fact]
    public void Grounded_vs_grounded_collision_keeps_first_and_flags_other()
    {
        var a = Monster("Ancient Black Dragon");                                          // already clean grounded
        var b = Monster("ANCIENT BLACK DRAGON Gargantuan dragon, chaotic evil");          // cleans to same name
        var (entities, counts) = MonsterNameCleanup.Clean(new[] { a, b }, Matcher, BookKey);

        counts.GroundedCollisionsFlagged.Should().Be(1);
        entities.Should().HaveCount(2);                                    // neither deleted
        entities.Count(e => e.NeedsReview).Should().Be(1);                 // the second flagged
        entities.First(e => e.Name == "Ancient Black Dragon").NeedsReview.Should().BeFalse(); // first untouched
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~MonsterNameCleanupTests`
Expected: FAIL — `MonsterNameCleanup` / `MonsterNameCleanupCounts` do not exist (compile error).

- [ ] **Step 3: Write the transform**

```csharp
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

public readonly record struct MonsterNameCleanupCounts(
    int Cleaned, int Deduped, int GroundedCollisionsFlagged, int Grounded, int Backfilled);

/// <summary>
/// One-time, pure transform that rewrites stat-line-garbled canonical Monster names to their clean
/// 5etools canonical form and de-duplicates the resulting duplicate 5etools-backfill entities.
/// Reuses the extractor's <see cref="EntityNameMatcher"/> (stat-line stripping) and
/// <see cref="EntityIdSlug"/> so its output is identical to what a re-extract would produce.
/// </summary>
public static class MonsterNameCleanup
{
    private const string BackfillDataSource = "5etools-backfill";

    private static bool IsBackfill(EntityEnvelope e) =>
        string.Equals(e.DataSource, BackfillDataSource, StringComparison.Ordinal);

    public static (IReadOnlyList<EntityEnvelope> Entities, MonsterNameCleanupCounts Counts) Clean(
        IReadOnlyList<EntityEnvelope> entities, EntityNameMatcher matcher, string bookKey)
    {
        // 1. Rewrite garbled monster names + recompute ids (all other fields preserved).
        var cleaned = 0;
        var rewritten = new List<EntityEnvelope>(entities.Count);
        foreach (var e in entities)
        {
            if (e.Type != EntityType.Monster)
            {
                rewritten.Add(e);
                continue;
            }

            if (matcher.MatchOfType(e.Name, EntityType.Monster) is { } m
                && !string.Equals(m.Canonical, e.Name, StringComparison.Ordinal))
            {
                cleaned++;
                rewritten.Add(e with
                {
                    Name = m.Canonical,
                    Id = EntityIdSlug.For(bookKey, EntityType.Monster, m.Canonical),
                });
            }
            else
            {
                rewritten.Add(e);
            }
        }

        // 2. De-dupe by normalized monster name: keep grounded, drop backfill; flag grounded-vs-grounded.
        var deduped = 0;
        var groundedCollisionsFlagged = 0;
        var drop = new HashSet<int>();
        var flag = new HashSet<int>();

        var monsterGroups = rewritten
            .Select((e, i) => (e, i))
            .Where(x => x.e.Type == EntityType.Monster)
            .GroupBy(x => EntityNameIndex.Normalize(x.e.Name), StringComparer.Ordinal);

        foreach (var group in monsterGroups)
        {
            var grounded = group.Where(x => !IsBackfill(x.e)).Select(x => x.i).ToList();
            var backfill = group.Where(x => IsBackfill(x.e)).Select(x => x.i).ToList();

            if (grounded.Count >= 1 && backfill.Count >= 1)
            {
                foreach (var bi in backfill) drop.Add(bi);
                deduped += backfill.Count;
            }
            if (grounded.Count >= 2)
            {
                foreach (var gi in grounded.Skip(1)) flag.Add(gi);
                groundedCollisionsFlagged += grounded.Count - 1;
            }
        }

        var result = new List<EntityEnvelope>(rewritten.Count);
        for (var i = 0; i < rewritten.Count; i++)
        {
            if (drop.Contains(i)) continue;
            var e = rewritten[i];
            if (flag.Contains(i) && !e.NeedsReview) e = e with { NeedsReview = true };
            result.Add(e);
        }

        var groundedCount = result.Count(e => e.Type == EntityType.Monster && !IsBackfill(e));
        var backfilledCount = result.Count(e => e.Type == EntityType.Monster && IsBackfill(e));

        return (result, new MonsterNameCleanupCounts(
            cleaned, deduped, groundedCollisionsFlagged, groundedCount, backfilledCount));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~MonsterNameCleanupTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add Features/Ingestion/FivetoolsIngestion/MonsterNameCleanup.cs \
        DndMcpAICsharpFun.Tests/Ingestion/FivetoolsIngestion/MonsterNameCleanupTests.cs
git commit -m "feat(cleanup): pure MonsterNameCleanup transform (name rewrite + backfill de-dupe)"
```

---

### Task 2: One-time console `Tools/CanonicalNameCleanup`

**Files:**
- Create: `Tools/CanonicalNameCleanup/CanonicalNameCleanup.csproj`
- Create: `Tools/CanonicalNameCleanup/Program.cs`
- Modify: the solution file (add the project) — `DndMcpAICsharpFun.sln` (use `dotnet sln add`).

**Interfaces:**
- Consumes: `MonsterNameCleanup.Clean(...)` (Task 1); `CanonicalJsonLoader` (`new`, `LoadAsync(path, ct) -> CanonicalJsonFile`); `CanonicalJsonWriter` (`new`, `WriteAsync(path, CanonicalJsonFile, ct)`); `CanonicalJsonFile` (record with `Entities`, `with { Entities = ... }`); `EntityNameMatcher(new EntityNameIndex("5etools"))`.
- Produces: an executable run as `dotnet run --project Tools/CanonicalNameCleanup -- <slug>`.

- [ ] **Step 1: Create the csproj** (mirrors `Tools/SchemaGenerator`; version-less references per CPM)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>DndMcpAICsharpFun.Tools.CanonicalNameCleanup</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\DndMcpAICsharpFun.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create `Program.cs`**

```csharp
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

if (args.Length < 1)
{
    Console.Error.WriteLine(
        "Usage: CanonicalNameCleanup <canonical-slug> [canonicalDir] [fivetoolsDir]  (e.g. mm14)");
    return 1;
}

var slug = args[0];
var canonicalDir = args.Length > 1 ? args[1] : Path.Combine("books", "canonical");
var fivetoolsDir = args.Length > 2 ? args[2] : "5etools";
var canonicalPath = Path.Combine(canonicalDir, slug + ".json");

if (!File.Exists(canonicalPath))
{
    Console.Error.WriteLine($"Canonical file not found: {canonicalPath}");
    return 1;
}
if (!Directory.Exists(fivetoolsDir))
{
    Console.Error.WriteLine($"5etools directory not found: {fivetoolsDir}");
    return 1;
}

var matcher = new EntityNameMatcher(new EntityNameIndex(fivetoolsDir));
var loader = new CanonicalJsonLoader();
var writer = new CanonicalJsonWriter();

var file = await loader.LoadAsync(canonicalPath, CancellationToken.None);
var (entities, counts) = MonsterNameCleanup.Clean(file.Entities, matcher, slug);
await writer.WriteAsync(canonicalPath, file with { Entities = entities }, CancellationToken.None);

Console.WriteLine(
    $"cleaned: {counts.Cleaned}  deduped: {counts.Deduped}  " +
    $"groundedCollisionsFlagged: {counts.GroundedCollisionsFlagged}");
Console.WriteLine($"grounded: {counts.Grounded}  backfilled: {counts.Backfilled}");
return 0;
```

- [ ] **Step 3: Add the project to the solution + build**

Run:
```bash
dotnet sln add Tools/CanonicalNameCleanup/CanonicalNameCleanup.csproj
dotnet build Tools/CanonicalNameCleanup/CanonicalNameCleanup.csproj
```
Expected: `Build succeeded` with 0 warnings (warnings-as-errors).

- [ ] **Step 4: Smoke-test on a copy (no committed data change)**

Run:
```bash
cp books/canonical/mm14.json /tmp/claude-1000/mm14.copy.json
dotnet run --project Tools/CanonicalNameCleanup -- mm14 /tmp/claude-1000
```
Note: with `canonicalDir=/tmp/claude-1000` the tool reads/writes `/tmp/claude-1000/mm14.json` — copy the file there first, or just run the real path in Task 4. Expected: prints two `cleaned/deduped/...` + `grounded/backfilled` lines, exit 0. (This step only proves the wiring; the real run is Task 4.)

- [ ] **Step 5: Commit**

```bash
git add Tools/CanonicalNameCleanup/ DndMcpAICsharpFun.sln
git commit -m "feat(tools): one-time CanonicalNameCleanup console wrapping MonsterNameCleanup"
```

---

### Task 3: Verify the full suite green

**Files:** none (verification only).

- [ ] **Step 1: Build + full test run**

Run: `dotnet build && dotnet test`
Expected: `Build succeeded` (0 warnings), all tests PASS (prior count + 4 new). Docker must be up for the persistence tests.

- [ ] **Step 2: Commit** (only if a fixup was needed; otherwise skip)

---

### Task 4: MM data cleanup run — supersedes mm-monster-name-and-precision 3.2/3.4/3.5/3.6

**Files:** Modify (data): `books/canonical/mm14.json`.

> Run these on the host (canonical files are host-writable from the repo). The app container must be serving the current image for the HTTP steps; base URL `http://localhost:5101`.

- [ ] **Step 1: Run the cleanup on MM**

Run: `dotnet run --project Tools/CanonicalNameCleanup -- mm14`
Expected: two lines printed (record the numbers). Spot-check:
```bash
grep -c "Gargantuan dragon" books/canonical/mm14.json   # expect 0
grep -c "\"name\": \"Ancient Black Dragon\"" books/canonical/mm14.json  # expect 1
```

- [ ] **Step 2: Flag unknown-extra monsters** (now safe — dragons are clean)

Run: `curl -sS -X POST http://localhost:5101/admin/books/1/flag-unknown-monsters -H "<admin auth header>"`
Expected: the `extraUnknown` names (e.g. `Lord Soth`, `Roa`) returned as flagged; `extraOtherSource` untouched.

- [ ] **Step 3: Re-check monster recall**

Run: `curl -sS http://localhost:5101/admin/books/1/monster-recall -H "<admin auth header>"`
Expected: `missing == 0` (still 450/450); record the new grounded : backfilled ratio (was 337 : 152 — expect grounded up, backfilled down by the deduped count).

- [ ] **Step 4: Validate the corpus**

Run: `curl -sS -X POST http://localhost:5101/admin/canonical/validate -H "<admin auth header>"`
Expected: HTTP 200 (clean) — no duplicate-id FAIL.

- [ ] **Step 5: Commit the improved canonical + update memory**

```bash
git add books/canonical/mm14.json
git commit -m "data(mm): clean stat-line-garbled monster names + drop duplicate backfills"
```
Then update the Serena `extraction/mm_monster_status` memory with the before/after (grounded/backfilled/extra) deltas and mark the data-cleanup DONE.

---

## Notes for the executor

- If any HTTP step needs an admin auth header/key, read it from the running config the same way `DndMcpAICsharpFun.http` documents; do not invent one.
- If the recall check reports any monster newly `missing` after cleanup, STOP — that means a name was rewritten to a value not in the MM roster (unexpected). Investigate before committing; `git checkout books/canonical/mm14.json` restores the pre-run canonical.
