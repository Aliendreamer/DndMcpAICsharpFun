# Filter Degenerate Tables — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Stop emitting degenerate "tables" (header-only / <2-column grids, and monster stat-block ability lines MinerU mis-tags as tables) so the structured table set is clean.

**Architecture:** A single chokepoint — `HtmlTableParser.Parse` returns `null` for a degenerate grid; `MinerUTableCollector` already does `if (table is null) continue`, so no collector change is needed. Two drop rules: **D1** a real table needs ≥2 columns and ≥1 data row (a 1-row / <2-col grid is not a table); **D2** a small grid (≤2 rows) whose cells are dominated by ability-score tokens (`STR 22 (+6)` shape) is a stat-block fragment, dropped even though it technically has a data row.

**Tech Stack:** C# / .NET 10, xunit + FluentAssertions. Serena for all `.cs`.

## Global Constraints
- **Serena only** for `.cs` (and every tracked file); built-in Read/Edit/Write forbidden. If a Serena call hangs >2 min, STOP (dead MCP) and report — do not retry.
- **Work on `main`**; commit each reviewed task.
- Warnings-as-errors → `dotnet build` 0/0. `dotnet` needs `dangerouslyDisableSandbox: true` (git-crypt). Ignore LSP false CS0246/CS1061 on test files.
- **No HTTP endpoint change** (`.http`/insomnia untouched). No new package.
- Filter is **additive at collection** — it changes NOTHING for genuine tables (≥2 cols, ≥1 data row, non-stat-block). Existing `HtmlTableParserTests` non-null cases MUST stay green unchanged.
- D2 stays **narrow**: the ability-token regex is exactly the spec's `\b(STR|DEX|CON|INT|WIS|CHA)\b\s*\d` (case-sensitive — stat blocks are uppercase; OCR-lowercase is out of scope), the ≥3-match threshold + ≤2-row guard keep genuine reference tables safe.
- **STOP before Task 2 Step "live MTF re-extract"** — it is a ~multi-hour re-extraction; run ONLY on explicit user go.

**Known shapes (verified):**
```csharp
// Domain/Entities/CanonicalKnowledge.cs
public sealed record CanonicalTable(string Id, string Name, IReadOnlyList<string> Columns, IReadOnlyList<CanonicalTableRow> Rows);
// CanonicalTableRow(IReadOnlyList<CanonicalCell> Cells); CanonicalCell(string Value, ProvenanceRef Provenance).

// Features/Ingestion/Pdf/HtmlTableParser.cs — CURRENT Parse body:
//   if (string.IsNullOrWhiteSpace(html)) return null;
//   var rows = ExtractRows(html);            // List<List<string>>; each <tr> -> cells
//   if (rows.Count == 0) return null;
//   var columns = rows[0];
//   if (columns.Count == 0) return null;     // <-- REPLACED by the D1 condition below
//   var dataRows = rows.Skip(1).Select(...).ToList();
//   return new CanonicalTable(tableId, name, columns, dataRows);
// The class is `public static partial class HtmlTableParser` with [GeneratedRegex] helpers (RowRx/CellRx/TagRx/WhitespaceRx).
// dataRows.Count == 0  <=>  rows.Count < 2  (dataRows is rows.Skip(1)).

// MinerUTableCollector.Collect already: var table = HtmlTableParser.Parse(...); if (table is null) continue;  // NO change needed.

// Tests: DndMcpAICsharpFun.Tests/Ingestion/Pdf/HtmlTableParserTests.cs
//   ProvenanceRef Prov = new("phb14.block.table.0", "PHB", 34); Parse(html, id, name, Prov).
//   Existing non-null cases all have >=2 cols + >=1 data row (Draconic 3x2, th-header 2x1, normalize 2x1) -> stay green.
```

---

## Task 1: Drop degenerate + stat-block-fragment tables in `HtmlTableParser.Parse`

**Files:**
- Modify: `Features/Ingestion/Pdf/HtmlTableParser.cs` (D1 condition + `IsStatBlockFragment` helper + `AbilityTokenRx` regex)
- Test: `DndMcpAICsharpFun.Tests/Ingestion/Pdf/HtmlTableParserTests.cs` (add degenerate/stat-block/kept cases)

**Interfaces:**
- No signature change. `HtmlTableParser.Parse(string? html, string tableId, string name, ProvenanceRef cellProvenance)` still returns `CanonicalTable?`; it now returns `null` for degenerate/stat-block grids.
- Produces: a private `static bool IsStatBlockFragment(List<List<string>> rows)` and a `[GeneratedRegex] static partial Regex AbilityTokenRx()`.

- [ ] **Step 1: Write the failing tests** — via Serena (`find_symbol` `HtmlTableParserTests` for style, then `insert_after_symbol`/`replace_symbol_body` to add). Add these facts. Read the existing test file first so provenance/naming matches.

```csharp
    // filter-degenerate-tables: a header-only grid (columns but no data rows) is not a table.
    [Fact]
    public void Header_only_grid_is_dropped()
    {
        const string html = "<table><tr><td>A</td><td>B</td><td>C</td></tr></table>";
        HtmlTableParser.Parse(html, "t", "T", Prov).Should().BeNull();
    }

    // filter-degenerate-tables D1: a table needs >=2 columns; a single-column grid is a list, not a table.
    [Fact]
    public void Single_column_grid_is_dropped()
    {
        const string html = "<table><tr><td>Header</td></tr><tr><td>one</td></tr><tr><td>two</td></tr></table>";
        HtmlTableParser.Parse(html, "t", "T", Prov).Should().BeNull();
    }

    // filter-degenerate-tables D2: a single-row stat-block ability line MinerU mis-tags as a table.
    [Fact]
    public void Single_row_stat_block_line_is_dropped()
    {
        const string html = "<table><tr><td>STR 22 (+6)</td><td>DEX 19 (+4)</td><td>CON 24 (+7)</td></tr></table>";
        HtmlTableParser.Parse(html, "t", "T", Prov).Should().BeNull();
    }

    // filter-degenerate-tables D2: a stat block that survives D1 (2 rows / >=1 data row) is still a
    // stat-block fragment (ability-token cells) and is dropped.
    [Fact]
    public void Two_row_stat_block_fragment_is_dropped()
    {
        const string html = "<table>" +
            "<tr><td>STR 22 (+6)</td><td>DEX 19 (+4)</td><td>CON 24 (+7)</td></tr>" +
            "<tr><td>INT 3 (-4)</td><td>WIS 11 (+0)</td><td>CHA 16 (+3)</td></tr></table>";
        HtmlTableParser.Parse(html, "t", "T", Prov).Should().BeNull();
    }

    // A genuine table (>=2 cols, >=1 data row, not a stat block) is kept unchanged.
    [Fact]
    public void Genuine_two_column_table_is_kept()
    {
        const string html = "<table><tr><td>Level</td><td>Proficiency Bonus</td></tr>" +
            "<tr><td>1</td><td>+2</td></tr></table>";
        var t = HtmlTableParser.Parse(html, "t", "T", Prov);
        t.Should().NotBeNull();
        t!.Columns.Should().Equal("Level", "Proficiency Bonus");
        t.Rows.Should().ContainSingle();
    }
```

- [ ] **Step 2: Run the new tests → they FAIL** (the degenerate/stat-block cases currently return a table, not null).

Run: `dotnet test --filter "FullyQualifiedName~HtmlTableParserTests" 2>&1 | tail -20` (with `dangerouslyDisableSandbox: true`).
Expected: `Header_only_grid_is_dropped`, `Single_column_grid_is_dropped`, `Single_row_stat_block_line_is_dropped`, `Two_row_stat_block_fragment_is_dropped` FAIL; `Genuine_two_column_table_is_kept` and all pre-existing tests PASS.

- [ ] **Step 3: Implement the filter** — Serena `replace_symbol_body` on `HtmlTableParser.Parse`, and `insert_after_symbol` (after `Parse`) to add the helper + regex. Replace the old `if (columns.Count == 0) return null;` with the D1 condition; add the D2 guard before building `dataRows`:

```csharp
    /// <summary>Parse table HTML into a CanonicalTable; every cell carries the shared table provenance.</summary>
    public static CanonicalTable? Parse(string? html, string tableId, string name, ProvenanceRef cellProvenance)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        var rows = ExtractRows(html);
        if (rows.Count == 0) return null;

        var columns = rows[0];
        // filter-degenerate-tables D1: a real table needs >=2 columns and >=1 data row.
        // (dataRows is rows.Skip(1), so rows.Count < 2 means zero data rows — a header-only grid.)
        if (columns.Count < 2 || rows.Count < 2) return null;

        // filter-degenerate-tables D2: a monster stat-block ability line ("STR 22 (+6) …") MinerU
        // mis-tags as a table; drop it even though it technically has a data row.
        if (IsStatBlockFragment(rows)) return null;

        var dataRows = rows.Skip(1)
            .Select(r => new CanonicalTableRow(
                r.Select(cell => new CanonicalCell(cell, cellProvenance)).ToList()))
            .ToList();

        return new CanonicalTable(tableId, name, columns, dataRows);
    }

    // A small grid (<=2 rows) dominated by ability-score tokens like "STR 22" / "DEX 19 (+4)" is a
    // stat-block fragment, not a table. Narrow by design — the ability regex + >=3-match threshold +
    // <=2-row guard keep genuine multi-row reference tables (which never carry 3 such cells) safe.
    private static bool IsStatBlockFragment(List<List<string>> rows)
    {
        if (rows.Count > 2) return false;
        var abilityCells = rows.SelectMany(r => r).Count(cell => AbilityTokenRx().IsMatch(cell));
        return abilityCells >= 3;
    }
```

Add the regex alongside the other `[GeneratedRegex]` helpers:

```csharp
    [GeneratedRegex(@"\b(STR|DEX|CON|INT|WIS|CHA)\b\s*\d")]
    private static partial Regex AbilityTokenRx();
```

Grep-verify (`search_for_pattern`) that the only changes are: the D1 line replacing the `columns.Count == 0` check, the D2 `IsStatBlockFragment` call, the new helper, and the new regex — no other behavior touched.

- [ ] **Step 4: Run the tests → PASS.**

Run: `dotnet test --filter "FullyQualifiedName~HtmlTableParserTests" 2>&1 | tail -20`
Expected: all HtmlTableParser tests PASS (the 5 new + all pre-existing).

- [ ] **Step 5: Run the collector tests → still green** (the collector already skips `null`, so degenerate tables just drop out).

Run: `dotnet test --filter "FullyQualifiedName~MinerUTableCollectorTests" 2>&1 | tail -20`
Expected: PASS. If any collector test fed a degenerate grid and asserted a table came out, that assertion encoded the OLD behavior — report it (do not weaken a genuine assertion); most likely none do.

- [ ] **Step 6: Build 0/0 + format.**

Run: `dotnet build 2>&1 | tail -5` → 0 warnings / 0 errors.
Run: `dotnet format DndMcpAICsharpFun.slnx --include Features/Ingestion/Pdf/HtmlTableParser.cs DndMcpAICsharpFun.Tests/Ingestion/Pdf/HtmlTableParserTests.cs`

- [ ] **Step 7: Commit.**

```bash
git add Features/Ingestion/Pdf/HtmlTableParser.cs DndMcpAICsharpFun.Tests/Ingestion/Pdf/HtmlTableParserTests.cs
git commit -m "feat(ingestion): drop degenerate + stat-block-fragment tables at parse"
```

---

## Task 2: Verify (full suite) + optional live validation (deferred)

- [ ] **Step 1: Whole-suite gate.** `dotnet build` 0/0; FULL `dotnet test` green (needs Docker for persistence — if Docker is down, run `dotnet test --filter "FullyQualifiedName!~Persistence"` and note it). Confirm `git diff --stat` is confined to the two files; `.http`/insomnia untouched.

- [ ] **Step 2 (DEFERRED — explicit user go only): live MTF re-extract.** The live proof is re-extracting a monster book (MTF, ~106 degenerate stat-block tables) and confirming the degenerate-table share drops toward ~0 with no genuine table lost. STOP here; run only on explicit go (it is a ~multi-hour re-extraction). NOTE the conversion-cache gotcha: this is a parse-logic change, so `docker exec … rm -f /books/conversion-cache/*.mineru.json` before re-extracting or the OLD parse is reused.

---

## Self-Review notes
- Spec D1 (0 data rows dropped) → Task 1 `rows.Count < 2`. Spec "≥2 columns" (proposal "< 2 columns … degenerate") → `columns.Count < 2`. Spec D2 (stat-block ability line, "even if it has a row") → `IsStatBlockFragment` with the exact spec regex, ≤2-row guard, ≥3-match threshold, covered by both the single-row and the two-row stat-block tests. Spec "keep genuine tables unchanged" → `Genuine_two_column_table_is_kept` + all pre-existing non-null tests stay green.
- Chokepoint is the parser (design D3 allows parser OR collector; parser is the single call site and the collector already handles `null`) — so no collector change, no risk of a second divergent filter.
- Live MTF validation deferred (Task 2 Step 2), mirroring the deferred-live-validation discipline.
