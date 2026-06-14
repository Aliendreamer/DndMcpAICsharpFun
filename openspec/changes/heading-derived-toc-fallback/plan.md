# Heading-Derived TOC Fallback — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make entity extraction work for bookmark-less PDFs (e.g. the SRD) by deriving the `TocCategoryMap` from Marker's heading structure items when PDF bookmarks are absent, reusing the existing deterministic keyword classifier (no new LLM usage).

**Architecture:** Extract the keyword classifier into a shared `HeadingCategoryClassifier`; add a `HeadingTocMapper` that emits sparse, confident-category-only `TocSectionEntry`s from `section_header` items; branch in `EntityExtractionOrchestrator.ExtractAsync` to use it only when the bookmark-derived TOC is empty. The page-range propagation in the existing `TocCategoryMap` fills the gaps. Bookmarked books are untouched.

**Tech Stack:** C# / .NET 10, xUnit + FluentAssertions, warnings-as-errors. **Serena for all `.cs` reads/edits.**

**Reference types (already exist):**
- `record TocSectionEntry(string Title, ContentCategory? Category, int StartPage, int? EndPage = null)` — `Domain/TocSectionEntry.cs`
- `record PdfStructureItem(string Type, string Text, int PageNumber, int? Level)` — `Features/Ingestion/Pdf/PdfStructureDocument.cs` (`section_header` items carry the heading text + page)
- `record PdfBookmark(string Title, int PageNumber, string? ParentTitle = null)`
- `TocCategoryMap` (`Features/Ingestion/Extraction/`) — sorts entries by `StartPage`, back-fills `EndPage`, exposes `bool IsEmpty` and `ContentCategory? GetCategory(int page)`
- `ContentCategory` enum (`Domain/ContentCategory.cs`): Spell, Monster, Class, Race, Background, Item, Rule, Combat, Adventuring, Condition, God, Plane, Treasure, Encounter, Trap, Trait, Lore, Unknown
- `EntityCandidateScanner.MapCategoryToEntityType` maps these to a concrete `EntityType` (non-null): Spell, Monster, Class, Race, Background, Item, Condition, God, Plane, Treasure→MagicItem, Trap. All others → null (skipped).

---

### Task 1: Shared keyword category classifier

**Files:**
- Create: `Features/Ingestion/Pdf/HeadingCategoryClassifier.cs`
- Modify: `Features/Ingestion/Pdf/BookmarkTocMapper.cs`
- Create test: `DndMcpAICsharpFun.Tests/Ingestion/Pdf/HeadingCategoryClassifierTests.cs`

- [ ] **Step 1: Write the failing test** (`HeadingCategoryClassifierTests.cs`)

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;

namespace DndMcpAICsharpFun.Tests.Ingestion.Pdf;

public sealed class HeadingCategoryClassifierTests
{
    [Theory]
    [InlineData("Spells", ContentCategory.Spell)]
    [InlineData("Barbarian", ContentCategory.Class)]
    [InlineData("Class Features", ContentCategory.Class)] // "class" beats "feat" in "Features"
    [InlineData("Races", ContentCategory.Race)]
    [InlineData("Monsters", ContentCategory.Monster)]
    [InlineData("Magic Items", ContentCategory.Item)]
    [InlineData("Conditions", ContentCategory.Condition)]
    [InlineData("Rage", ContentCategory.Rule)]   // no keyword → Rule
    [InlineData("Hill Dwarf", ContentCategory.Rule)]
    [InlineData("", ContentCategory.Rule)]
    public void Guess_ReturnsExpectedCategory(string title, ContentCategory expected)
    {
        HeadingCategoryClassifier.Guess(title).Should().Be(expected);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter HeadingCategoryClassifierTests`
Expected: FAIL — `HeadingCategoryClassifier` does not exist.

- [ ] **Step 3: Create `HeadingCategoryClassifier.cs`** (move the existing `GuessCategory` body verbatim, make it public static)

```csharp
using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public static class HeadingCategoryClassifier
{
    public static ContentCategory Guess(string title)
    {
        var t = title.ToLowerInvariant();

        if (Contains(t, "spell")) return ContentCategory.Spell;
        if (ContainsAny(t, "monster", "bestiary", "creature",
                            "aberration", "beast", "celestial", "dragon", "elemental",
                            "fey", "fiend", "giant", "humanoid", "monstrosit",
                            "ooze", "plant", "undead", "npc", "nonplayer character"))
            return ContentCategory.Monster;
        if (ContainsAny(t, "equipment", "gear", "weapon", "armor", "armour", "magic item")) return ContentCategory.Item;
        // Class first — "Class Features" should land on Class, not Trait.
        if (ContainsAny(t, "class", "barbarian", "bard", "cleric", "druid", "fighter", "monk", "paladin", "ranger", "rogue", "sorcerer", "warlock", "wizard")) return ContentCategory.Class;
        if (ContainsAny(t, "feat", "trait", "personality")) return ContentCategory.Trait;
        if (ContainsAny(t, "background")) return ContentCategory.Background;
        if (ContainsAny(t, "race", "species")) return ContentCategory.Race;
        if (ContainsAny(t, "condition")) return ContentCategory.Condition;
        if (ContainsAny(t, "god", "deity", "deities", "pantheon")) return ContentCategory.God;
        if (ContainsAny(t, "plane", "cosmology", "multiverse")) return ContentCategory.Plane;
        if (ContainsAny(t, "treasure", "loot", "hoard")) return ContentCategory.Treasure;
        if (ContainsAny(t, "encounter", "dungeon", "random encounter")) return ContentCategory.Encounter;
        if (ContainsAny(t, "trap")) return ContentCategory.Trap;
        if (ContainsAny(t, "lore", "history", "world")) return ContentCategory.Lore;
        if (ContainsAny(t, "combat", "attack")) return ContentCategory.Combat;
        if (ContainsAny(t, "adventuring", "exploration", "resting", "travel", "adventure environment")) return ContentCategory.Adventuring;

        return ContentCategory.Rule;
    }

    private static bool Contains(string text, string keyword) =>
        text.Contains(keyword, StringComparison.Ordinal);

    private static bool ContainsAny(string text, params string[] keywords)
    {
        foreach (var k in keywords)
            if (text.Contains(k, StringComparison.Ordinal)) return true;
        return false;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter HeadingCategoryClassifierTests`
Expected: PASS (all `[Theory]` rows).

- [ ] **Step 5: Refactor `BookmarkTocMapper` to delegate** (use Serena `replace_symbol_body`/`replace_content`)

Replace the two `GuessCategory(...)` calls with `HeadingCategoryClassifier.Guess(...)` and delete the now-duplicated private `GuessCategory`, `Contains`, and `ContainsAny`. Final file:

```csharp
using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public static class BookmarkTocMapper
{
    public static IReadOnlyList<TocSectionEntry> Map(IReadOnlyList<PdfBookmark> bookmarks)
    {
        if (bookmarks.Count == 0) return [];

        var entries = new List<TocSectionEntry>(bookmarks.Count);
        foreach (var b in bookmarks)
        {
            var category = HeadingCategoryClassifier.Guess(b.Title);
            // Fall back to the parent bookmark's category when the leaf title
            // matches no keyword (e.g. monster names under "Monsters (A-Z)").
            if (category == ContentCategory.Rule && !string.IsNullOrEmpty(b.ParentTitle))
            {
                var parentCategory = HeadingCategoryClassifier.Guess(b.ParentTitle);
                if (parentCategory != ContentCategory.Rule)
                    category = parentCategory;
            }
            entries.Add(new TocSectionEntry(b.Title, category, b.PageNumber));
        }
        return entries;
    }
}
```

- [ ] **Step 6: Run the full suite** — proves the bookmark path is byte-for-byte unchanged.

Run: `dotnet build` (expect 0 warnings) then `dotnet test`
Expected: PASS, including existing `BookmarkTocMapperTests` and `TocCategoryMapTests`.

- [ ] **Step 7: Commit**

```bash
git add Features/Ingestion/Pdf/HeadingCategoryClassifier.cs Features/Ingestion/Pdf/BookmarkTocMapper.cs DndMcpAICsharpFun.Tests/Ingestion/Pdf/HeadingCategoryClassifierTests.cs
git commit -m "refactor(extraction): extract shared HeadingCategoryClassifier"
```

---

### Task 2: HeadingTocMapper (sparse, confident-only)

**Files:**
- Create: `Features/Ingestion/Pdf/HeadingTocMapper.cs`
- Create test: `DndMcpAICsharpFun.Tests/Ingestion/Pdf/HeadingTocMapperTests.cs`

- [ ] **Step 1: Write the failing tests** (`HeadingTocMapperTests.cs`)

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;

namespace DndMcpAICsharpFun.Tests.Ingestion.Pdf;

public sealed class HeadingTocMapperTests
{
    private static PdfStructureItem H(string text, int page) => new("section_header", text, page, 1);

    [Fact]
    public void Map_EmitsOnlyConfidentHeadings_DroppingKeywordlessSubHeadings()
    {
        var headings = new[] { H("Barbarian", 1), H("Rage", 2), H("Hill Dwarf", 3), H("Spells", 4) };

        var entries = HeadingTocMapper.Map(headings);

        entries.Should().HaveCount(2);
        entries[0].Title.Should().Be("Barbarian");
        entries[0].Category.Should().Be(ContentCategory.Class);
        entries[0].StartPage.Should().Be(1);
        entries[1].Title.Should().Be("Spells");
        entries[1].Category.Should().Be(ContentCategory.Spell);
        entries[1].StartPage.Should().Be(4);
    }

    [Fact]
    public void Map_SkipsBlankTitles()
    {
        var entries = HeadingTocMapper.Map(new[] { H("   ", 1), H("Races", 2) });
        entries.Should().ContainSingle();
        entries[0].Category.Should().Be(ContentCategory.Race);
    }

    [Fact]
    public void Map_DropsNonEntityCategories()
    {
        // Combat / Adventuring map to no EntityType → must be dropped.
        var entries = HeadingTocMapper.Map(new[] { H("Combat", 1), H("Adventuring", 2) });
        entries.Should().BeEmpty();
    }

    [Fact]
    public void Map_FeedsTocCategoryMap_SoRangesPropagate()
    {
        var headings = new[] { H("Barbarian", 1), H("Rage", 2), H("Hill Dwarf", 3), H("Spells", 4) };

        var map = new TocCategoryMap(HeadingTocMapper.Map(headings));

        map.GetCategory(3).Should().Be(ContentCategory.Class); // inherited from Barbarian
        map.GetCategory(4).Should().Be(ContentCategory.Spell);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter HeadingTocMapperTests`
Expected: FAIL — `HeadingTocMapper` does not exist.

- [ ] **Step 3: Create `HeadingTocMapper.cs`**

```csharp
using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public static class HeadingTocMapper
{
    // Categories that EntityCandidateScanner.MapCategoryToEntityType maps to a concrete EntityType.
    // Only these "confident" categories are emitted, so keyword-less sub-headings (which classify to
    // Rule) are dropped and do not reset the surrounding page-range category. Keep in sync with
    // EntityCandidateScanner.MapCategoryToEntityType.
    private static readonly HashSet<ContentCategory> Confident =
    [
        ContentCategory.Spell, ContentCategory.Monster, ContentCategory.Class, ContentCategory.Race,
        ContentCategory.Background, ContentCategory.Item, ContentCategory.Condition, ContentCategory.God,
        ContentCategory.Plane, ContentCategory.Treasure, ContentCategory.Trap,
    ];

    public static IReadOnlyList<TocSectionEntry> Map(IReadOnlyList<PdfStructureItem> headings)
    {
        ArgumentNullException.ThrowIfNull(headings);

        var entries = new List<TocSectionEntry>();
        foreach (var h in headings)
        {
            if (string.IsNullOrWhiteSpace(h.Text)) continue;
            var category = HeadingCategoryClassifier.Guess(h.Text);
            if (!Confident.Contains(category)) continue;
            entries.Add(new TocSectionEntry(h.Text.Trim(), category, h.PageNumber));
        }
        return entries;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter HeadingTocMapperTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Features/Ingestion/Pdf/HeadingTocMapper.cs DndMcpAICsharpFun.Tests/Ingestion/Pdf/HeadingTocMapperTests.cs
git commit -m "feat(extraction): HeadingTocMapper for bookmark-less TOC"
```

---

### Task 3: Wire the fallback into the orchestrator + fix the log

**Files:**
- Modify: `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs` (in `ExtractAsync`, right after `var tocMap = new TocCategoryMap(tocEntries);`)
- Modify: `Features/Ingestion/Pdf/PdfPigBookmarkReader.cs` (the `LogNoBookmarks` `[LoggerMessage]`)

- [ ] **Step 1: Add the fallback branch in `ExtractAsync`** (Serena `replace_content`)

After the existing line building `tocMap` from bookmarks, insert:

```csharp
            // 2b. No embedded bookmarks → derive the TOC from Marker's heading structure items,
            // reusing the same deterministic keyword classifier (no LLM). Bookmarked books skip this.
            if (tocMap.IsEmpty)
            {
                var headingItems = doc.Items
                    .Where(i => string.Equals(i.Type, "section_header", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var headingEntries = HeadingTocMapper.Map(headingItems);
                tocMap = new TocCategoryMap(headingEntries);
                logger.LogInformation(
                    "No bookmarks for book {BookId}; derived TOC from {HeadingCount} headings → {EntryCount} confident category entries (heading-derived fallback)",
                    bookId, headingItems.Count, headingEntries.Count);
            }
```

(`HeadingTocMapper` is in `DndMcpAICsharpFun.Features.Ingestion.Pdf`, already imported via `BookmarkTocMapper`. `tocMap` must be a local `var` that can be reassigned — it already is.)

- [ ] **Step 2: Fix the misleading log in `PdfPigBookmarkReader`** (Serena `replace_content`)

Replace the `[LoggerMessage]` so it no longer claims a fallback the reader doesn't perform; the orchestrator now logs the real fallback:

```csharp
    [LoggerMessage(Level = LogLevel.Information, Message = "No embedded bookmarks found in {FileName}")]
    private static partial void LogNoBookmarks(ILogger logger, string fileName);
```

- [ ] **Step 3: Build + full suite**

Run: `dotnet build` (expect 0 warnings) then `dotnet test`
Expected: PASS, 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs Features/Ingestion/Pdf/PdfPigBookmarkReader.cs
git commit -m "feat(extraction): wire heading-derived TOC fallback into extraction"
```

---

### Task 4: Integration + regression coverage

**Files:**
- Create test: `DndMcpAICsharpFun.Tests/Ingestion/Pdf/HeadingFallbackScannerTests.cs`

- [ ] **Step 1: Write the failing integration test** (no-bookmark path yields typed candidates)

```csharp
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;

namespace DndMcpAICsharpFun.Tests.Ingestion.Pdf;

public sealed class HeadingFallbackScannerTests
{
    [Fact]
    public void HeadingDerivedToc_ProducesTypedCandidates_ForBookmarklessInput()
    {
        // Headings drive the category map (as in a bookmark-less PDF like the SRD).
        var headings = new[] { new PdfStructureItem("section_header", "Barbarian", 1, 1) };
        var tocMap = new TocCategoryMap(HeadingTocMapper.Map(headings));

        // Content sits under the "Barbarian" section on page 1.
        var inputs = new[] { new ScannerInput("Barbarian", 1, "The barbarian can enter a rage as a bonus action.") };

        var candidates = new EntityCandidateScanner().Scan(inputs, tocMap).ToList();

        candidates.Should().ContainSingle(c => c.Type == EntityType.Class);
    }

    [Fact]
    public void BookmarkDerivedToc_IsNonEmpty_SoFallbackBranchIsSkipped()
    {
        // Guards the orchestrator guard: when bookmarks exist, the TocCategoryMap is non-empty,
        // so `if (tocMap.IsEmpty)` is false and HeadingTocMapper is never invoked.
        var tocMap = new TocCategoryMap(BookmarkTocMapper.Map(new[] { new PdfBookmark("Spells", 100) }));
        tocMap.IsEmpty.Should().BeFalse();
    }
}
```

> Note: `EntityCandidateScanner` is constructed parameterless here — match the construction used in `DndMcpAICsharpFun.Tests/Entities/Extraction/EntityCandidateScannerTests.cs`. If that test uses a different constructor, mirror it.

- [ ] **Step 2: Run to verify it fails/then passes**

Run: `dotnet test --filter HeadingFallbackScannerTests`
Expected: with Tasks 1–3 already implemented, this should pass once the test compiles. If `Scan` returns 0 candidates, verify `MapCategoryToEntityType(Class)` is non-null and the page numbers align (heading page 1 == input page 1).

- [ ] **Step 3: Build + full suite green**

Run: `dotnet build` (0 warnings) then `dotnet test`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add DndMcpAICsharpFun.Tests/Ingestion/Pdf/HeadingFallbackScannerTests.cs
git commit -m "test(extraction): no-bookmark candidate path + bookmark-guard regression"
```

---

### Task 5: Validation

- [ ] **Step 1:** Run `openspec validate heading-derived-toc-fallback` → expect "is valid".
- [ ] **Step 2:** Confirm no endpoint surface changed → no `DndMcpAICsharpFun.http` / `dnd-mcp-api.insomnia.json` edits needed. State this explicitly in the final summary.

---

## Self-Review

- **Spec coverage:** Req "bookmark-less → headings" → Tasks 2+3+4.1. Req "sparse/confident-only + propagation" → Task 2 (all four tests). Req "bookmark path unaffected" → Task 1.6 (existing suite green after refactor) + Task 4.2 guard. All three requirements covered.
- **Placeholder scan:** none — every code step has complete code.
- **Type consistency:** `HeadingCategoryClassifier.Guess`, `HeadingTocMapper.Map(IReadOnlyList<PdfStructureItem>)`, `TocSectionEntry(Title, Category, StartPage)`, `TocCategoryMap.IsEmpty`/`GetCategory`, `EntityType.Class` — consistent across tasks and match the confirmed real signatures.
