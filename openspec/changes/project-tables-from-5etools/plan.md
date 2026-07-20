# Project Tables From 5etools — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace column-collapsed MinerU tables in official-book canonicals with clean, correctly-id'd tables projected deterministically from 5etools structured JSON.

**Architecture:** Two pure projectors (captioned embedded `{type:table}` blocks; synthesized class-progression tables) plus an orchestrator, all in `Features/Ingestion/FivetoolsIngestion/`, driven by a new `Tools/ProjectTables` console that loads each canonical, resolves its 5etools source key from `Book.SourceBook`, replaces `tables[]` wholesale for official books, and reloads to prove the file is ingestable. Homebrew books (no source key) are skipped. Downstream (`StructuredFactProjector` → Postgres → `CharacterResolutionService`) is unchanged.

**Tech Stack:** C# / .NET 10, `System.Text.Json` (`JsonDocument`/`JsonElement`), xunit + FluentAssertions. Serena MCP for all `.cs` reads/edits.

## Global Constraints

- **Serena only** for all `.cs` reads/edits (`find_symbol`/`replace_symbol_body`/`insert_after_symbol`); built-in Read/Edit on code files is forbidden. Grep-verify after each Serena edit.
- **Work on `main`** — no feature branch. Commit each task after its reviewer passes.
- `net10.0`, nullable enabled, **warnings-as-errors** (`Directory.Build.props`) — every task must `dotnet build` 0/0.
- Central Package Management — no versions in csproj.
- No new HTTP endpoint / MCP tool → `DndMcpAICsharpFun.http` and `dnd-mcp-api.insomnia.json` are NOT touched.
- Canonical files are **root-owned** (container writes them); the console runs on the host and writes host-owned canonical copies — running `--all` for the corpus run happens on the host tree, not in the container.
- Reuse `EntityIdSlug` for the book-slug prefix so table ids match entity ids (a re-extract would produce the same prefix).
- Tag every projected table `dataSource` provenance via `ProvenanceRef.SourceBook = <sourceKey>`.

**Key existing types (verbatim):**
```csharp
public sealed record ProvenanceRef(string BlockId, string SourceBook, int? Page);
public sealed record CanonicalCell(string Value, ProvenanceRef? Provenance);
public sealed record CanonicalTableRow(IReadOnlyList<CanonicalCell> Cells);
public sealed record CanonicalTable(string Id, string Name, IReadOnlyList<string> Columns, IReadOnlyList<CanonicalTableRow> Rows);
public sealed record CanonicalBookMetadata(string SourceBook, string Edition, string FileHash, string DisplayName);
public sealed record CanonicalJsonFile(string SchemaVersion, CanonicalBookMetadata Book, IReadOnlyList<EntityEnvelope> Entities, IReadOnlyList<CanonicalTable> Tables = null!, IReadOnlyList<CanonicalChoiceSet> ChoiceSets = null!);
// EntityIdSlug.BookSlug("PHB") => "phb14";  SlugifyName is private.
// CanonicalJsonLoader.LoadAsync(path, ct) -> CanonicalJsonFile (THROWS on duplicate table id).
// CanonicalJsonWriter.WriteAsync(path, CanonicalJsonFile, ct) -> atomic temp+move, CanonicalJson.WriteOptions.
```

**5etools data shapes (verbatim from `5etools/`):**
- Embedded table: `{"type":"table","caption":"Draconic Ancestry","colLabels":["Dragon","Damage Type","Breath Weapon"],"rows":[["Black","Acid","5 by 30 ft. line (Dex. save)"], ...]}` (caption optional; some blocks have none).
- `classFeatures` entry: either the string `"Fighting Style|Fighter||1"` (`name|class|source|level`, source empty ⇒ base) or the object `{"classFeature":"Martial Archetype|Fighter||3","gainSubclassFeature":true}`.
- `hd`: `{"number":1,"faces":10}`.
- `classTableGroups[]`: each has `colLabels` (may contain `{@filter 1st|spells|level=1|class=Wizard}` markup) plus EITHER `rows` (`[[3],[3],...]`, one value per level 1..20) OR `rowsSpellProgression` (`[[2,0,...],[3,0,...],...]`, a 9-slot array per level).

---

## File Structure

- `Domain/Entities/EntityIdSlug.cs` — **add** `public static string Table(string bookKey, string name)`.
- `Features/Ingestion/FivetoolsIngestion/FivetoolsJson.cs` — **create** small helpers: markup strip + JSON string-array read.
- `Features/Ingestion/FivetoolsIngestion/CaptionedTableProjector.cs` — **create** (recurse an entity `JsonElement` → captioned `CanonicalTable`s).
- `Features/Ingestion/FivetoolsIngestion/ClassProgressionTableProjector.cs` — **create** (synthesize one wide table per class).
- `Features/Ingestion/FivetoolsIngestion/FivetoolsTableProjection.cs` — **create** (orchestrator: scan the book's 5etools files, combine, de-dupe ids).
- `Tools/ProjectTables/Program.cs` + `Tools/ProjectTables/ProjectTables.csproj` — **create** console.
- Tests under `DndMcpAICsharpFun.Tests/Ingestion/Fivetools/`.

---

## Task 1: `EntityIdSlug.Table` + JSON helpers

**Files:**
- Modify: `Domain/Entities/EntityIdSlug.cs`
- Create: `Features/Ingestion/FivetoolsIngestion/FivetoolsJson.cs`
- Test: `DndMcpAICsharpFun.Tests/Ingestion/Fivetools/FivetoolsJsonTests.cs`

**Interfaces:**
- Produces:
  - `EntityIdSlug.Table(string bookKey, string name) : string` → `"<bookslug>.table.<name-slug>"`
  - `static class FivetoolsJson { string StripMarkup(string label); IReadOnlyList<string> StringList(JsonElement arr); }`
  - `StripMarkup("{@filter 1st|spells|level=1|class=Wizard}") == "1st"`; `StripMarkup("Dragon") == "Dragon"`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using FluentAssertions;
using Xunit;

public class FivetoolsJsonTests
{
    [Fact]
    public void Table_id_uses_book_slug_and_name_slug()
    {
        EntityIdSlug.Table("PHB", "Draconic Ancestry").Should().Be("phb14.table.draconic-ancestry");
        EntityIdSlug.Table("XGE", "Sample Table").Should().Be("xge.table.sample-table");
    }

    [Theory]
    [InlineData("{@filter 1st|spells|level=1|class=Wizard}", "1st")]
    [InlineData("{@filter Cantrips Known|spells|level=0|class=Wizard}", "Cantrips Known")]
    [InlineData("{@dice 1d6}", "1d6")]
    [InlineData("Dragon", "Dragon")]
    public void StripMarkup_returns_plain_label(string input, string expected)
        => FivetoolsJson.StripMarkup(input).Should().Be(expected);

    [Fact]
    public void StringList_reads_string_array()
    {
        using var doc = JsonDocument.Parse("[\"a\",\"b\"]");
        FivetoolsJson.StringList(doc.RootElement).Should().Equal("a", "b");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DndMcpAICsharpFun.Tests --filter FullyQualifiedName~FivetoolsJsonTests`
Expected: FAIL — `EntityIdSlug.Table` / `FivetoolsJson` do not exist (compile error).

- [ ] **Step 3: Add `EntityIdSlug.Table`** (Serena `insert_after_symbol` after the `BookSlug(IngestionRecord)` method)

```csharp
    /// <summary>The canonical id for a projected table: "&lt;book-slug&gt;.table.&lt;name-slug&gt;".</summary>
    public static string Table(string bookKey, string name)
    {
        var bookSlug = BookOverrides.TryGetValue(bookKey, out var s) ? s : SlugifyBook(bookKey);
        return $"{bookSlug}.table.{SlugifyName(name)}";
    }
```

- [ ] **Step 4: Create `FivetoolsJson.cs`**

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>Small helpers for reading 5etools JSON: strip {@tag ...} markup, read string arrays.</summary>
public static partial class FivetoolsJson
{
    // {@tagname display text|arg|arg} -> "display text"  (display is everything up to the first | or }).
    [GeneratedRegex(@"\{@\w+\s+([^|}]+)[^}]*\}")]
    private static partial Regex Tag();

    public static string StripMarkup(string label) => Tag().Replace(label, m => m.Groups[1].Value).Trim();

    public static IReadOnlyList<string> StringList(JsonElement arr)
    {
        if (arr.ValueKind != JsonValueKind.Array) return [];
        var list = new List<string>();
        foreach (var e in arr.EnumerateArray())
            list.Add(e.ValueKind == JsonValueKind.String ? e.GetString()! : e.ToString());
        return list;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test DndMcpAICsharpFun.Tests --filter FullyQualifiedName~FivetoolsJsonTests`
Expected: PASS (4 tests). Then `dotnet build` → 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add Domain/Entities/EntityIdSlug.cs Features/Ingestion/FivetoolsIngestion/FivetoolsJson.cs DndMcpAICsharpFun.Tests/Ingestion/Fivetools/FivetoolsJsonTests.cs
git commit -m "feat(tables): EntityIdSlug.Table + 5etools JSON helpers"
```

---

## Task 2: `CaptionedTableProjector`

**Files:**
- Create: `Features/Ingestion/FivetoolsIngestion/CaptionedTableProjector.cs`
- Test: `DndMcpAICsharpFun.Tests/Ingestion/Fivetools/CaptionedTableProjectorTests.cs`

**Interfaces:**
- Consumes: `EntityIdSlug.Table`, `FivetoolsJson.StringList`.
- Produces:
  - `static class CaptionedTableProjector { IReadOnlyList<CanonicalTable> Project(JsonElement entity, string bookKey, int? page) }`
  - Recurses `entity` for objects with `"type":"table"` that have a non-empty `caption`; each → `CanonicalTable(id=EntityIdSlug.Table(bookKey, caption), name=caption, columns=StringList(colLabels), rows=each rows[] entry → CanonicalTableRow of CanonicalCell(value, new ProvenanceRef($"{bookslug}.5etools", bookKey, page)))`. Uncaptioned table blocks are skipped. Row cell values: string entries verbatim; non-string entries (e.g. `{"roll":{...}}`) rendered via `.ToString()` — acceptable for v1.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using FluentAssertions;
using Xunit;

public class CaptionedTableProjectorTests
{
    private const string Dragonborn = """
    {"name":"Dragonborn","source":"PHB","entries":[
      {"type":"table","caption":"Draconic Ancestry",
       "colLabels":["Dragon","Damage Type","Breath Weapon"],
       "rows":[["Black","Acid","5 by 30 ft. line (Dex. save)"],["Blue","Lightning","5 by 30 ft. line (Dex. save)"]]}]}
    """;

    [Fact]
    public void Projects_captioned_table_with_sluggable_id()
    {
        using var doc = JsonDocument.Parse(Dragonborn);
        var tables = CaptionedTableProjector.Project(doc.RootElement, "PHB", page: 34);
        tables.Should().ContainSingle();
        var t = tables[0];
        t.Id.Should().Be("phb14.table.draconic-ancestry");
        t.Name.Should().Be("Draconic Ancestry");
        t.Columns.Should().Equal("Dragon", "Damage Type", "Breath Weapon");
        t.Rows.Should().HaveCount(2);
        t.Rows[0].Cells.Select(c => c.Value).Should().Equal("Black", "Acid", "5 by 30 ft. line (Dex. save)");
        t.Rows[0].Cells[0].Provenance!.SourceBook.Should().Be("PHB");
    }

    [Fact]
    public void Skips_uncaptioned_table_blocks()
    {
        using var doc = JsonDocument.Parse("""{"source":"PHB","entries":[{"type":"table","colLabels":["a","b"],"rows":[["1","2"]]}]}""");
        CaptionedTableProjector.Project(doc.RootElement, "PHB", null).Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DndMcpAICsharpFun.Tests --filter FullyQualifiedName~CaptionedTableProjectorTests`
Expected: FAIL — `CaptionedTableProjector` does not exist.

- [ ] **Step 3: Create `CaptionedTableProjector.cs`**

```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>Projects captioned 5etools {type:table} blocks embedded in an entity into CanonicalTables.</summary>
public static class CaptionedTableProjector
{
    public static IReadOnlyList<CanonicalTable> Project(JsonElement entity, string bookKey, int? page)
    {
        var provenance = new ProvenanceRef($"{EntityIdSlug.BookSlug(bookKey)}.5etools", bookKey, page);
        var tables = new List<CanonicalTable>();
        Walk(entity, bookKey, provenance, tables);
        return tables;
    }

    private static void Walk(JsonElement node, string bookKey, ProvenanceRef prov, List<CanonicalTable> acc)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                if (node.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                    && t.GetString() == "table"
                    && node.TryGetProperty("caption", out var capEl) && capEl.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(capEl.GetString()))
                {
                    acc.Add(ToTable(node, capEl.GetString()!, bookKey, prov));
                }
                foreach (var p in node.EnumerateObject()) Walk(p.Value, bookKey, prov, acc);
                break;
            case JsonValueKind.Array:
                foreach (var e in node.EnumerateArray()) Walk(e, bookKey, prov, acc);
                break;
        }
    }

    private static CanonicalTable ToTable(JsonElement tbl, string caption, string bookKey, ProvenanceRef prov)
    {
        var columns = tbl.TryGetProperty("colLabels", out var cl)
            ? FivetoolsJson.StringList(cl).Select(FivetoolsJson.StripMarkup).ToList()
            : new List<string>();
        var rows = new List<CanonicalTableRow>();
        if (tbl.TryGetProperty("rows", out var rs) && rs.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in rs.EnumerateArray())
            {
                var cells = new List<CanonicalCell>();
                if (r.ValueKind == JsonValueKind.Array)
                    foreach (var c in r.EnumerateArray())
                        cells.Add(new CanonicalCell(
                            c.ValueKind == JsonValueKind.String ? FivetoolsJson.StripMarkup(c.GetString()!) : c.ToString(),
                            prov));
                rows.Add(new CanonicalTableRow(cells));
            }
        }
        return new CanonicalTable(EntityIdSlug.Table(bookKey, caption), caption, columns, rows);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DndMcpAICsharpFun.Tests --filter FullyQualifiedName~CaptionedTableProjectorTests`
Expected: PASS (2 tests). `dotnet build` 0/0.

- [ ] **Step 5: Commit**

```bash
git add Features/Ingestion/FivetoolsIngestion/CaptionedTableProjector.cs DndMcpAICsharpFun.Tests/Ingestion/Fivetools/CaptionedTableProjectorTests.cs
git commit -m "feat(tables): project captioned 5etools embedded tables"
```

---

## Task 3: `ClassProgressionTableProjector` — base columns (Level / Proficiency Bonus / Features)

**Files:**
- Create: `Features/Ingestion/FivetoolsIngestion/ClassProgressionTableProjector.cs`
- Test: `DndMcpAICsharpFun.Tests/Ingestion/Fivetools/ClassProgressionTableProjectorTests.cs`

**Interfaces:**
- Consumes: `EntityIdSlug.Table`, `FivetoolsJson`.
- Produces:
  - `static class ClassProgressionTableProjector { CanonicalTable Project(JsonElement classEntry, string bookKey) }`
  - Base columns always `["Level","Proficiency Bonus","Features"]`, 20 rows (levels 1..20).
  - Proficiency bonus per level = `2 + (level-1)/4` rendered `"+N"`.
  - Features per level from `classFeatures` (parse both string and `{classFeature:...}` forms; `name = part[0]`, `level = int part[^1]`); multiple features at a level joined `", "`; empty string when none.
  - id `EntityIdSlug.Table(bookKey, className)`, name `"<ClassName>"`.
  - (classTableGroups columns are appended in Task 4 — this task emits base columns only.)

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using FluentAssertions;
using Xunit;

public class ClassProgressionTableProjectorTests
{
    private const string Fighter = """
    {"name":"Fighter","source":"PHB","hd":{"number":1,"faces":10},
     "classFeatures":["Fighting Style|Fighter||1","Second Wind|Fighter||1","Action Surge|Fighter||2",
                      {"classFeature":"Martial Archetype|Fighter||3","gainSubclassFeature":true},
                      "Ability Score Improvement|Fighter||4","Extra Attack|Fighter||5"]}
    """;

    [Fact]
    public void Martial_class_base_columns_and_prof_bonus()
    {
        using var doc = JsonDocument.Parse(Fighter);
        var t = ClassProgressionTableProjector.Project(doc.RootElement, "PHB");
        t.Id.Should().Be("phb14.table.fighter");
        t.Name.Should().Be("Fighter");
        t.Columns.Take(3).Should().Equal("Level", "Proficiency Bonus", "Features");
        t.Rows.Should().HaveCount(20);
        t.Rows[0].Cells[0].Value.Should().Be("1");
        t.Rows[0].Cells[1].Value.Should().Be("+2");
        t.Rows[0].Cells[2].Value.Should().Be("Fighting Style, Second Wind");
        t.Rows[4].Cells[1].Value.Should().Be("+3"); // level 5
        t.Rows[4].Cells[2].Value.Should().Be("Extra Attack");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DndMcpAICsharpFun.Tests --filter FullyQualifiedName~ClassProgressionTableProjectorTests`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Create `ClassProgressionTableProjector.cs`** (base-columns only for now)

```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>Synthesizes one wide progression CanonicalTable per class (levels 1-20).</summary>
public static class ClassProgressionTableProjector
{
    public static CanonicalTable Project(JsonElement classEntry, string bookKey)
    {
        var name = classEntry.GetProperty("name").GetString()!;
        var prov = new ProvenanceRef($"{EntityIdSlug.BookSlug(bookKey)}.5etools", bookKey, null);
        var featuresByLevel = FeaturesByLevel(classEntry);

        var columns = new List<string> { "Level", "Proficiency Bonus", "Features" };
        var rows = new List<CanonicalTableRow>();
        for (var level = 1; level <= 20; level++)
        {
            var cells = new List<CanonicalCell>
            {
                new(level.ToString(), prov),
                new($"+{2 + (level - 1) / 4}", prov),
                new(featuresByLevel.TryGetValue(level, out var f) ? string.Join(", ", f) : "", prov),
            };
            rows.Add(new CanonicalTableRow(cells));
        }
        return new CanonicalTable(EntityIdSlug.Table(bookKey, name), name, columns, rows);
    }

    private static Dictionary<int, List<string>> FeaturesByLevel(JsonElement classEntry)
    {
        var map = new Dictionary<int, List<string>>();
        if (!classEntry.TryGetProperty("classFeatures", out var cf) || cf.ValueKind != JsonValueKind.Array)
            return map;
        foreach (var e in cf.EnumerateArray())
        {
            var spec = e.ValueKind == JsonValueKind.String ? e.GetString()
                     : e.TryGetProperty("classFeature", out var s) ? s.GetString() : null;
            if (spec is null) continue;
            var parts = spec.Split('|');
            if (parts.Length < 2 || !int.TryParse(parts[^1], out var level)) continue;
            (map.TryGetValue(level, out var list) ? list : map[level] = new List<string>()).Add(parts[0]);
        }
        return map;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DndMcpAICsharpFun.Tests --filter FullyQualifiedName~ClassProgressionTableProjectorTests`
Expected: PASS. `dotnet build` 0/0.

- [ ] **Step 5: Commit**

```bash
git add Features/Ingestion/FivetoolsIngestion/ClassProgressionTableProjector.cs DndMcpAICsharpFun.Tests/Ingestion/Fivetools/ClassProgressionTableProjectorTests.cs
git commit -m "feat(tables): synthesize class progression base columns"
```

---

## Task 4: `ClassProgressionTableProjector` — append classTableGroups columns

**Files:**
- Modify: `Features/Ingestion/FivetoolsIngestion/ClassProgressionTableProjector.cs`
- Modify (add test): `DndMcpAICsharpFun.Tests/Ingestion/Fivetools/ClassProgressionTableProjectorTests.cs`

**Interfaces:**
- Produces (extends Task 3, same `Project` signature): after the base 3 columns, append one column per `classTableGroups[].colLabels` entry (markup-stripped). Fill values per level from either `rows` (`rows[level-1][colIndex]`) or `rowsSpellProgression` (`rowsSpellProgression[level-1][colIndex]`, where `0` renders as `""`). Groups with neither array are skipped-and the run continues.

- [ ] **Step 1: Write the failing test** (append to the existing test class)

```csharp
    private const string Wizard = """
    {"name":"Wizard","source":"PHB","hd":{"number":1,"faces":6},
     "classFeatures":["Spellcasting|Wizard||1","Arcane Recovery|Wizard||1"],
     "classTableGroups":[
       {"colLabels":["{@filter Cantrips Known|spells|level=0|class=Wizard}"],
        "rows":[[3],[3],[3],[4],[4]]},
       {"title":"Spell Slots per Spell Level",
        "colLabels":["{@filter 1st|spells|level=1|class=Wizard}","{@filter 2nd|spells|level=2|class=Wizard}"],
        "rowsSpellProgression":[[2,0],[3,0],[4,2],[4,3],[4,3]]}]}
    """;

    [Fact]
    public void Caster_appends_group_columns_stripped_and_expanded()
    {
        using var doc = JsonDocument.Parse(Wizard);
        var t = ClassProgressionTableProjector.Project(doc.RootElement, "PHB");
        t.Columns.Should().Equal("Level", "Proficiency Bonus", "Features", "Cantrips Known", "1st", "2nd");
        // level 1: cantrips 3, 1st-slot 2, 2nd-slot blank
        t.Rows[0].Cells[3].Value.Should().Be("3");
        t.Rows[0].Cells[4].Value.Should().Be("2");
        t.Rows[0].Cells[5].Value.Should().Be("");
        // level 3: 2nd-level slots appear (2)
        t.Rows[2].Cells[5].Value.Should().Be("2");
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DndMcpAICsharpFun.Tests --filter FullyQualifiedName~ClassProgressionTableProjectorTests`
Expected: FAIL — `Caster_appends_group_columns...` fails (only 3 columns produced).

- [ ] **Step 3: Extend `Project`** — Serena `replace_symbol_body` on `ClassProgressionTableProjector/Project`. Build a per-column value provider from the groups, append labels to `columns`, and append cells per level.

```csharp
    public static CanonicalTable Project(JsonElement classEntry, string bookKey)
    {
        var name = classEntry.GetProperty("name").GetString()!;
        var prov = new ProvenanceRef($"{EntityIdSlug.BookSlug(bookKey)}.5etools", bookKey, null);
        var featuresByLevel = FeaturesByLevel(classEntry);
        var groupCols = GroupColumns(classEntry); // (label, valueForLevel) in order

        var columns = new List<string> { "Level", "Proficiency Bonus", "Features" };
        columns.AddRange(groupCols.Select(g => g.Label));

        var rows = new List<CanonicalTableRow>();
        for (var level = 1; level <= 20; level++)
        {
            var cells = new List<CanonicalCell>
            {
                new(level.ToString(), prov),
                new($"+{2 + (level - 1) / 4}", prov),
                new(featuresByLevel.TryGetValue(level, out var f) ? string.Join(", ", f) : "", prov),
            };
            foreach (var g in groupCols) cells.Add(new CanonicalCell(g.Value(level), prov));
            rows.Add(new CanonicalTableRow(cells));
        }
        return new CanonicalTable(EntityIdSlug.Table(bookKey, name), name, columns, rows);
    }

    private readonly record struct GroupCol(string Label, Func<int, string> Value);

    private static List<GroupCol> GroupColumns(JsonElement classEntry)
    {
        var cols = new List<GroupCol>();
        if (!classEntry.TryGetProperty("classTableGroups", out var groups) || groups.ValueKind != JsonValueKind.Array)
            return cols;
        foreach (var g in groups.EnumerateArray())
        {
            var labels = g.TryGetProperty("colLabels", out var cl)
                ? FivetoolsJson.StringList(cl).Select(FivetoolsJson.StripMarkup).ToList()
                : new List<string>();
            var hasRows = g.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array;
            var hasProg = g.TryGetProperty("rowsSpellProgression", out var prog) && prog.ValueKind == JsonValueKind.Array;
            if (!hasRows && !hasProg) continue; // skip malformed group, continue
            var data = hasRows ? rows : prog;
            for (var col = 0; col < labels.Count; col++)
            {
                var c = col; var d = data; // capture
                cols.Add(new GroupCol(labels[c], level =>
                {
                    var idx = level - 1;
                    if (idx < 0 || idx >= d.GetArrayLength()) return "";
                    var row = d[idx];
                    if (row.ValueKind != JsonValueKind.Array || c >= row.GetArrayLength()) return "";
                    var v = row[c];
                    if (v.ValueKind == JsonValueKind.Number) { var n = v.GetInt32(); return n == 0 ? "" : n.ToString(); }
                    return v.ValueKind == JsonValueKind.String ? FivetoolsJson.StripMarkup(v.GetString()!) : v.ToString();
                }));
            }
        }
        return cols;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DndMcpAICsharpFun.Tests --filter FullyQualifiedName~ClassProgressionTableProjectorTests`
Expected: PASS (both tests). `dotnet build` 0/0.

- [ ] **Step 5: Commit**

```bash
git add Features/Ingestion/FivetoolsIngestion/ClassProgressionTableProjector.cs DndMcpAICsharpFun.Tests/Ingestion/Fivetools/ClassProgressionTableProjectorTests.cs
git commit -m "feat(tables): append classTableGroups columns (markup-strip + slot expand)"
```

---

## Task 5: `FivetoolsTableProjection` orchestrator

**Files:**
- Create: `Features/Ingestion/FivetoolsIngestion/FivetoolsTableProjection.cs`
- Test: `DndMcpAICsharpFun.Tests/Ingestion/Fivetools/FivetoolsTableProjectionTests.cs`

**Interfaces:**
- Consumes: both projectors.
- Produces:
  - `sealed class FivetoolsTableProjection { IReadOnlyList<CanonicalTable> BuildForBook(string fivetoolsDir, string sourceKey) }`
  - Scans, for entries whose `"source" == sourceKey`:
    - captioned embedded tables — files: `races.json` (key `race`), `feats.json` (`feat`), `backgrounds.json` (`background`), `optionalfeatures.json` (`optionalfeature`), `charcreationoptions.json` (`charcreationoption`), and every `class/class-*.json` (arrays `class` and `subclass`).
    - class progression tables — every `class/class-*.json` entry in the `class` array with matching source.
  - **Unique ids:** on a duplicate id, append `-2`, `-3`, … so all returned ids are unique (progression tables win their bare id since they run first; captioned collisions get suffixes).
  - Missing files are skipped (some books lack some categories). Page: pull `page` (int) from the entity when present, else null.

- [ ] **Step 1: Write the failing test** (uses a temp 5etools dir fixture)

```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using FluentAssertions;
using Xunit;

public class FivetoolsTableProjectionTests
{
    private static string WriteFixture()
    {
        var dir = Path.Combine(Path.GetTempPath(), "5et-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "class"));
        File.WriteAllText(Path.Combine(dir, "races.json"), """
        {"race":[{"name":"Dragonborn","source":"PHB","page":34,"entries":[
          {"type":"table","caption":"Draconic Ancestry","colLabels":["Dragon","Damage Type"],"rows":[["Black","Acid"]]}]},
          {"name":"Elf","source":"XGE","entries":[{"type":"table","caption":"Ignore Me","colLabels":["x"],"rows":[["y"]]}]}]}
        """);
        File.WriteAllText(Path.Combine(dir, "class", "class-fighter.json"), """
        {"class":[{"name":"Fighter","source":"PHB","classFeatures":["Second Wind|Fighter||1"]}]}
        """);
        return dir;
    }

    [Fact]
    public void Builds_captioned_and_progression_for_the_source_key_only()
    {
        var dir = WriteFixture();
        try
        {
            var tables = new FivetoolsTableProjection().BuildForBook(dir, "PHB");
            var ids = tables.Select(t => t.Id).ToList();
            ids.Should().Contain("phb14.table.draconic-ancestry");
            ids.Should().Contain("phb14.table.fighter");
            ids.Should().NotContain("xge.table.ignore-me");   // wrong source key filtered out
            ids.Should().OnlyHaveUniqueItems();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DndMcpAICsharpFun.Tests --filter FullyQualifiedName~FivetoolsTableProjectionTests`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Create `FivetoolsTableProjection.cs`**

```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>Builds a book's full CanonicalTable set from the local 5etools data for a given source key.</summary>
public sealed class FivetoolsTableProjection
{
    private static readonly (string File, string Key)[] EmbeddedSources =
    [
        ("races.json", "race"), ("feats.json", "feat"), ("backgrounds.json", "background"),
        ("optionalfeatures.json", "optionalfeature"), ("charcreationoptions.json", "charcreationoption"),
    ];

    public IReadOnlyList<CanonicalTable> BuildForBook(string fivetoolsDir, string sourceKey)
    {
        var tables = new List<CanonicalTable>();

        // Class progression tables (run first so they keep the bare id on any collision).
        foreach (var classFile in SafeGlob(Path.Combine(fivetoolsDir, "class"), "class-*.json"))
            foreach (var cls in Entries(classFile, "class", sourceKey))
                tables.Add(ClassProgressionTableProjector.Project(cls, sourceKey));

        // Captioned embedded tables from class + subclass entries.
        foreach (var classFile in SafeGlob(Path.Combine(fivetoolsDir, "class"), "class-*.json"))
            foreach (var arrayKey in new[] { "class", "subclass" })
                foreach (var e in Entries(classFile, arrayKey, sourceKey))
                    tables.AddRange(CaptionedTableProjector.Project(e, sourceKey, PageOf(e)));

        // Captioned embedded tables from the top-level category files.
        foreach (var (file, key) in EmbeddedSources)
            foreach (var e in Entries(Path.Combine(fivetoolsDir, file), key, sourceKey))
                tables.AddRange(CaptionedTableProjector.Project(e, sourceKey, PageOf(e)));

        return Deduplicate(tables);
    }

    private static IEnumerable<string> SafeGlob(string dir, string pattern) =>
        Directory.Exists(dir) ? Directory.EnumerateFiles(dir, pattern) : [];

    private static IEnumerable<JsonElement> Entries(string path, string arrayKey, string sourceKey)
    {
        if (!File.Exists(path)) yield break;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty(arrayKey, out var arr) || arr.ValueKind != JsonValueKind.Array) yield break;
        foreach (var e in arr.EnumerateArray())
            if (e.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String && s.GetString() == sourceKey)
                yield return e.Clone(); // Clone so it outlives the JsonDocument
    }

    private static int? PageOf(JsonElement e) =>
        e.TryGetProperty("page", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;

    private static IReadOnlyList<CanonicalTable> Deduplicate(List<CanonicalTable> tables)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<CanonicalTable>(tables.Count);
        foreach (var t in tables)
        {
            var id = t.Id;
            for (var n = 2; !seen.Add(id); n++) id = $"{t.Id}-{n}";
            result.Add(id == t.Id ? t : t with { Id = id });
        }
        return result;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DndMcpAICsharpFun.Tests --filter FullyQualifiedName~FivetoolsTableProjectionTests`
Expected: PASS. `dotnet build` 0/0.

- [ ] **Step 5: Commit**

```bash
git add Features/Ingestion/FivetoolsIngestion/FivetoolsTableProjection.cs DndMcpAICsharpFun.Tests/Ingestion/Fivetools/FivetoolsTableProjectionTests.cs
git commit -m "feat(tables): 5etools table projection orchestrator (per source key)"
```

---

## Task 6: `Tools/ProjectTables` console

**Files:**
- Create: `Tools/ProjectTables/Program.cs`
- Create: `Tools/ProjectTables/ProjectTables.csproj`
- Test: `DndMcpAICsharpFun.Tests/Ingestion/Fivetools/ProjectTablesConsoleTests.cs` (test the orchestration helper, not `Main`)

**Interfaces:**
- Consumes: `FivetoolsTableProjection`, `CanonicalJsonLoader`, `CanonicalJsonWriter`.
- Produces:
  - A testable helper `static class ProjectTablesRunner { Task<ProjectResult> RunOneAsync(string canonicalPath, string fivetoolsDir, CanonicalJsonLoader loader, CanonicalJsonWriter writer, CancellationToken ct) }` where `record ProjectResult(bool Skipped, string? SkipReason, int TableCount)`.
  - For a book whose `Book.SourceBook` is null/empty ⇒ `Skipped=true` (homebrew), canonical untouched.
  - Otherwise: build tables via projection, write `file with { Tables = newTables }`, then **reload via loader** (throws on duplicate id) as the round-trip proof.
  - `Program.cs` parses args (`<slug>` or `--all`; optional `[canonicalDir] [fivetoolsDir]`), calls the runner per book, prints a summary, returns non-zero on any failure.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Tools.ProjectTables; // ProjectTablesRunner lives in this ns
using FluentAssertions;
using Xunit;

public class ProjectTablesConsoleTests
{
    [Fact]
    public async Task Official_book_replaces_tables_and_round_trips()
    {
        var (dir, fiveDir, canon) = Fixtures.OfficialBook("PHB"); // writes a minimal canonical + 5etools
        try
        {
            var res = await ProjectTablesRunner.RunOneAsync(canon, fiveDir, new CanonicalJsonLoader(), new CanonicalJsonWriter(), default);
            res.Skipped.Should().BeFalse();
            var reloaded = await new CanonicalJsonLoader().LoadAsync(canon, default);
            reloaded.Tables.Select(t => t.Id).Should().Contain("phb14.table.draconic-ancestry").And.OnlyHaveUniqueItems();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Homebrew_book_is_skipped_and_untouched()
    {
        var (dir, fiveDir, canon) = Fixtures.HomebrewBook(); // Book.SourceBook = ""
        try
        {
            var before = await File.ReadAllTextAsync(canon);
            var res = await ProjectTablesRunner.RunOneAsync(canon, fiveDir, new CanonicalJsonLoader(), new CanonicalJsonWriter(), default);
            res.Skipped.Should().BeTrue();
            (await File.ReadAllTextAsync(canon)).Should().Be(before);
        }
        finally { Directory.Delete(dir, true); }
    }
}
```

(Write a small `Fixtures` helper in the test file that emits a schemaVersion-`1` canonical JSON with one entity + the 5etools `races.json`/`class/` fixture from Task 5.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DndMcpAICsharpFun.Tests --filter FullyQualifiedName~ProjectTablesConsoleTests`
Expected: FAIL — `ProjectTablesRunner` does not exist.

- [ ] **Step 3: Create `ProjectTables.csproj`** (copy `Tools/CanonicalNameCleanup`'s csproj; reference the main project)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>DndMcpAICsharpFun.Tools.ProjectTables</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\DndMcpAICsharpFun.csproj" />
  </ItemGroup>
</Project>
```

(Verify against `Tools/CanonicalNameCleanup/*.csproj` — match its exact property set. Add the project to the solution if the repo tracks a `.sln`: `dotnet sln add Tools/ProjectTables/ProjectTables.csproj`.)

- [ ] **Step 4: Create `Program.cs`** with the runner + arg parsing

```csharp
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

namespace DndMcpAICsharpFun.Tools.ProjectTables;

public sealed record ProjectResult(bool Skipped, string? SkipReason, int TableCount);

public static class ProjectTablesRunner
{
    public static async Task<ProjectResult> RunOneAsync(
        string canonicalPath, string fivetoolsDir,
        CanonicalJsonLoader loader, CanonicalJsonWriter writer, CancellationToken ct)
    {
        var file = await loader.LoadAsync(canonicalPath, ct);
        var key = file.Book.SourceBook;
        if (string.IsNullOrWhiteSpace(key))
            return new ProjectResult(Skipped: true, SkipReason: "no fivetoolsSourceKey (homebrew)", TableCount: 0);

        var tables = new FivetoolsTableProjection().BuildForBook(fivetoolsDir, key);
        await writer.WriteAsync(canonicalPath, file with { Tables = tables }, ct);
        // Round-trip proof: reload validates unique ids + schema; throws on any violation.
        await loader.LoadAsync(canonicalPath, ct);
        return new ProjectResult(Skipped: false, SkipReason: null, TableCount: tables.Count);
    }
}

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: ProjectTables <slug|--all> [canonicalDir] [fivetoolsDir]");
            return 1;
        }
        var canonicalDir = args.Length > 1 ? args[1] : Path.Combine("books", "canonical");
        var fivetoolsDir = args.Length > 2 ? args[2] : "5etools";
        var loader = new CanonicalJsonLoader();
        var writer = new CanonicalJsonWriter();

        var slugs = args[0] == "--all"
            ? Directory.EnumerateFiles(canonicalDir, "*.json")
                .Where(p => !p.Contains(".errors.") && !p.Contains(".declined.") && !p.Contains(".warnings.") && !p.Contains(".progress"))
                .Select(Path.GetFileNameWithoutExtension)!.ToList()
            : new List<string> { args[0] };

        var failed = 0;
        foreach (var slug in slugs)
        {
            var path = Path.Combine(canonicalDir, slug + ".json");
            try
            {
                var r = await ProjectTablesRunner.RunOneAsync(path, fivetoolsDir, loader, writer, CancellationToken.None);
                Console.WriteLine(r.Skipped ? $"{slug}: SKIP ({r.SkipReason})" : $"{slug}: {r.TableCount} tables");
            }
            catch (Exception ex) { failed++; Console.Error.WriteLine($"{slug}: FAIL {ex.Message}"); }
        }
        return failed == 0 ? 0 : 1;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test DndMcpAICsharpFun.Tests --filter FullyQualifiedName~ProjectTablesConsoleTests`
Expected: PASS (2 tests). `dotnet build` 0/0 (whole solution, including the new console).

- [ ] **Step 6: Commit**

```bash
git add Tools/ProjectTables DndMcpAICsharpFun.Tests/Ingestion/Fivetools/ProjectTablesConsoleTests.cs *.sln
git commit -m "feat(tables): ProjectTables console (replace official-book tables, round-trip)"
```

---

## Task 7: Live validation on PHB + engine wiring

**Files:** none (validation only; canonical data changes are committed in Task 8).

- [ ] **Step 1: Run the console on PHB** (host tree)

Run: `dotnet run --project Tools/ProjectTables -- phb14`
Expected: `phb14: <N> tables` (N in the low hundreds), exit 0.

- [ ] **Step 2: Inspect the projected canonical**

Run: `python3 -c "import json;d=json.load(open('books/canonical/phb14.json'));ts=d['tables'];ids=[t['id'] for t in ts];print('tables',len(ts));print('draconic', [t for t in ts if t['id']=='phb14.table.draconic-ancestry'][0]['columns']);print('fighter?', 'phb14.table.fighter' in ids, 'wizard?', 'phb14.table.wizard' in ids);print('unique', len(ids)==len(set(ids)))"`
Expected: `draconic ['Dragon','Damage Type','Breath Weapon']`, `fighter? True wizard? True`, `unique True`.

- [ ] **Step 3: Ingest + exercise the resolution engine** (requires the stack up per `mem:operations/running_the_stack`)

Rebuild + restart the app image so it has the new canonical (`docker compose up -d --build app`), then:
Run: `POST /admin/books/{phbId}/ingest-entities`, then hit the breath-weapon resolution path (`resolve_character_feature` for a Dragonborn hero, or `GET /retrieval/entities/phb14.table.draconic-ancestry`) and confirm it resolves against the projected table.
Expected: breath-weapon resolves (Black→Acid, 5×30 line) via `phb14.table.draconic-ancestry` — the id the resolver expects.

- [ ] **Step 4: Record the result** in the change (no code commit) — note table count + that resolution wired up.

---

## Task 8: Corpus run + review the canonical diff

**Files:**
- Modify (data): `books/canonical/*.json` `tables[]` for all official books.

- [ ] **Step 1: Run `--all`**

Run: `dotnet run --project Tools/ProjectTables -- --all`
Expected: one line per book; official books show a table count, any homebrew shows `SKIP`. Exit 0.

- [ ] **Step 2: Verify no MinerU-origin tables remain + each reloads**

Run: `python3 -c "import json,glob;
for f in glob.glob('books/canonical/*.json'):
  n=f.split('/')[-1]
  if any(x in n for x in ['.errors.','.declined.','.warnings.','.progress']): continue
  d=json.load(open(f)); ts=d.get('tables',[]); ids=[t['id'] for t in ts]
  print(n, len(ts), 'unique' if len(ids)==len(set(ids)) else 'DUP!')"`
Expected: every official book lists tables, all `unique`; no `DUP!`.

- [ ] **Step 3: Review the git diff** of `books/canonical/*.json` `tables[]` per book — spot-check a class table and a captioned table in DMG/XGE/MPMM as well as PHB.

- [ ] **Step 4: Commit the canonical data**

```bash
git add books/canonical/*.json
git commit -m "data(tables): project 5etools tables into official-book canonicals (replaces MinerU tables)"
```

---

## Task 9: Gates

- [ ] **Step 1: Full build** — `dotnet build` → 0 warnings/errors.
- [ ] **Step 2: Full test suite** — `dotnet test` (Docker up for persistence tests) → all green; note the count.
- [ ] **Step 3: Format** — `dotnet format --verify-no-changes` → clean.
- [ ] **Step 4: Confirm no endpoint/security surface changed** — `git diff --stat` shows only `Tools/`, `Features/Ingestion/FivetoolsIngestion/`, `Domain/Entities/EntityIdSlug.cs`, tests, and `books/canonical/*.json`; `DndMcpAICsharpFun.http` / `dnd-mcp-api.insomnia.json` untouched.
- [ ] **Step 5: Commit** any format fixes; the change is ready for whole-branch review.

---

## Self-Review notes (coverage)

- Spec "captioned projection" → Tasks 2, 5, 7/8. "class progression synthesis" (martial + caster) → Tasks 3, 4. "official-only wholesale replace + skip homebrew" → Task 6. "unique-id + ingestable round-trip" → Tasks 5 (dedupe), 6 (reload proof), 8 (corpus reload check).
- No new HTTP surface → `.http`/insomnia untouched (Global Constraints, Task 9 Step 4).
- Type consistency: `ClassProgressionTableProjector.Project(JsonElement, string)` and `CaptionedTableProjector.Project(JsonElement, string, int?)` and `FivetoolsTableProjection.BuildForBook(string, string)` and `ProjectTablesRunner.RunOneAsync(...)` used identically across tasks.
- Risk (martial synthesis / caster parsing correctness) → covered by Fighter + Wizard unit tests (Tasks 3/4) and the PHB live inspection (Task 7).
