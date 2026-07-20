# Table Name From Heading — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Name caption-less MinerU tables from the nearest preceding `section_header` (bounded window), instead of `Table N`.

**Architecture:** One method — `MinerUTableCollector.Collect` — already iterates `doc.Items` in order. Track the most recent `section_header` text + a distance counter; when a `table` item has an empty caption and the heading is within the window, use it as the name. Keep the positional `-<n>` id suffix (resolver id-alignment is an explicit non-goal, and moot for the books this helps — the 5etools-projected official books bypass MinerU tables entirely; this helps the SKIPPED monster/reference books (mm14/mtf/mpmm/SRD, which kept their MinerU tables) + homebrew).

**Tech Stack:** C# / .NET 10, xunit + FluentAssertions. Serena MCP for all `.cs`.

## Global Constraints

- **Serena only** for `.cs` reads/edits; grep-verify after each edit. Built-in Read/Edit on `.cs` forbidden.
- **Work on `main`**; commit after the reviewer passes.
- Warnings-as-errors → `dotnet build` 0/0. `dotnet` needs `dangerouslyDisableSandbox: true` (git-crypt).
- Ignore LSP false CS0246/CS1061 on test files; trust `dotnet build`/test.
- No HTTP endpoint change.

**Key existing shapes (verbatim):**
```csharp
public sealed record PdfStructureItem(string Type, string Text, int PageNumber, int? Level, string? Html = null);
// heading items: Type == "section_header", Text == heading; table items: Type == "table", Text == caption (empty if none), Html == table html.
// MinerUTableCollector.Collect(PdfStructureDocument doc, string bookSlug, string sourceBook) : IReadOnlyList<CanonicalTable>
//   currently: caption = IsNullOrWhiteSpace(item.Text) ? $"Table {index+1}" : item.Text; id = $"{bookSlug}.table.{Slug(caption,index)}".
// private static string Slug(string caption, int index) => "<slug>-<index>"  (keep — positional suffix stays for uniqueness).
```

---

## Task 1: Name caption-less tables from the preceding heading

**Files:**
- Modify: `Features/Ingestion/EntityExtraction/MinerUTableCollector.cs` (the `Collect` method)
- Test: `DndMcpAICsharpFun.Tests/Entities/Extraction/MinerUTableCollectorTests.cs` (exists)

**Interfaces:**
- `Collect` signature unchanged. Behavior: track last `section_header` text + distance; a `table` with empty caption within `HeadingWindow` items of a heading → named from that heading; else `Table N`; a captioned table keeps its caption.

- [ ] **Step 1: Write the failing tests** (append to `MinerUTableCollectorTests`). Use the real `PdfStructureItem`/`PdfStructureDocument` shapes. A minimal valid table Html so `HtmlTableParser.Parse` returns non-null — reuse the Html shape already used by the existing tests in this file (read them via Serena first; mirror their `<table>…</table>` fixture).

```csharp
    [Fact]
    public void Uncaptioned_table_takes_preceding_section_header_name()
    {
        var html = "<table><tr><td>Black</td><td>Acid</td></tr></table>"; // or the file's existing fixture html
        var doc = new PdfStructureDocument("md", new List<PdfStructureItem>
        {
            new("section_header", "Draconic Ancestry", 34, 2),
            new("text", "Your draconic ancestry determines...", 34, null),
            new("table", "", 34, null, html),
        });
        var tables = MinerUTableCollector.Collect(doc, "phb14", "PHB");
        tables.Should().ContainSingle();
        tables[0].Name.Should().Be("Draconic Ancestry");
        tables[0].Id.Should().StartWith("phb14.table.draconic-ancestry");
    }

    [Fact]
    public void Captioned_table_keeps_its_caption()
    {
        var html = "<table><tr><td>x</td><td>y</td></tr></table>";
        var doc = new PdfStructureDocument("md", new List<PdfStructureItem>
        {
            new("section_header", "Some Section", 1, 2),
            new("table", "Real Caption", 1, null, html),
        });
        var tables = MinerUTableCollector.Collect(doc, "phb14", "PHB");
        tables[0].Name.Should().Be("Real Caption");
    }

    [Fact]
    public void Uncaptioned_table_with_no_nearby_heading_falls_back_to_positional()
    {
        var html = "<table><tr><td>x</td><td>y</td></tr></table>";
        var items = new List<PdfStructureItem> { new("section_header", "Far Away", 1, 2) };
        for (var i = 0; i < 15; i++) items.Add(new("text", $"para {i}", 1, null)); // push heading out of the window
        items.Add(new("table", "", 1, null, html));
        var tables = MinerUTableCollector.Collect(new PdfStructureDocument("md", items), "phb14", "PHB");
        tables[0].Name.Should().Be("Table 1");
    }
```

- [ ] **Step 2: Run → confirm FAIL** (current code names the uncaptioned table `Table 1`, not `Draconic Ancestry`).
  `dotnet test DndMcpAICsharpFun.Tests --filter FullyQualifiedName~MinerUTableCollectorTests` (dangerouslyDisableSandbox).

- [ ] **Step 3: Update `Collect`** — Serena `replace_symbol_body` on `MinerUTableCollector/Collect`:

```csharp
    public static IReadOnlyList<CanonicalTable> Collect(
        PdfStructureDocument doc, string bookSlug, string sourceBook)
    {
        const int HeadingWindow = 10; // max items between a heading and the table it names
        var tables = new List<CanonicalTable>();
        var index = 0;
        string? lastHeading = null;
        var sinceHeading = int.MaxValue;

        foreach (var item in doc.Items)
        {
            if (string.Equals(item.Type, "section_header", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(item.Text)) { lastHeading = item.Text; sinceHeading = 0; }
                continue;
            }

            if (!string.Equals(item.Type, "table", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(item.Html))
            {
                if (sinceHeading != int.MaxValue) sinceHeading++;
                continue;
            }

            var caption = !string.IsNullOrWhiteSpace(item.Text) ? item.Text
                : (lastHeading is not null && sinceHeading <= HeadingWindow) ? lastHeading
                : $"Table {index + 1}";
            var id = $"{bookSlug}.table.{Slug(caption, index)}";
            var provenance = new ProvenanceRef($"{bookSlug}.block.table.{index}", sourceBook, item.PageNumber);

            if (sinceHeading != int.MaxValue) sinceHeading++;

            var table = HtmlTableParser.Parse(item.Html, id, caption, provenance);
            if (table is null) continue; // malformed table html — skip, don't fail the run
            tables.Add(table);
            index++;
        }
        return tables;
    }
```
(Keep the existing private `Slug` method unchanged.)

- [ ] **Step 4: Run tests → all `MinerUTableCollectorTests` pass (new 3 + existing). `dotnet build` → 0/0.**
- [ ] **Step 5: Commit:** `feat(ingestion): name caption-less MinerU tables from preceding heading`

---

## Task 2: Verify + optional cheap live-check

- [ ] **Step 1: Whole-solution build 0/0; FULL `dotnet test` green** (note the count); `dotnet format DndMcpAICsharpFun.slnx --include Features/Ingestion/EntityExtraction/MinerUTableCollector.cs DndMcpAICsharpFun.Tests/Entities/Extraction/MinerUTableCollectorTests.cs` clean.
- [ ] **Step 2 (cheap live-check, no full re-extract):** feed a REAL conversion-cache doc through `Collect` and confirm caption-less tables now get heading names. Pick a monster/reference book's `.mineru.json` from `books/conversion-cache/` (these books kept their MinerU tables), deserialize to `PdfStructureDocument`, call `Collect`, and print the fraction of tables named `Table N` before/after (should drop). If wiring this up is more than ~15 min, SKIP and rely on the unit tests — this change is deterministic and unit-covered. Record whichever you did.
- [ ] **Step 3:** Confirm `git diff --stat` is only `MinerUTableCollector.cs` + its test; `.http`/insomnia untouched.

---

## Self-Review notes
- Spec "caption-less → preceding heading (bounded window)" → Task 1 Step 3 (`HeadingWindow`, `sinceHeading`). "captioned unchanged" + "no heading → Table N" → the two other unit tests.
- id keeps the `-<n>` positional suffix (resolver id-alignment is a non-goal); this improves NAMES for the retained-MinerU books, not id-alignment.
- No behavior change for captioned tables or the `HtmlTableParser` path.
