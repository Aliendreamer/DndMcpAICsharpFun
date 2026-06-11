# Mandatory Entity Name Normalization — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make all-caps→title-case entity-name normalization an automatic, deterministic step at extraction time (with D&D acronym preservation), and recompute `needsReview` so all-caps no longer inflates the flag.

**Architecture:** A new pure `EntityNameNormalizer` owns the casing rule (D&D title-case + acronym allowlist) and a `TryNormalizeHeading` gate (title-case only all-caps names with no *other* OCR artifact, so genuine garble isn't masked). `EntityExtractionOrchestrator` calls the gate at its two candidate→entity sites before `ExtractionNeedsReview.Derive`. `CanonicalNameNormalizerService` delegates casing to the new type and clears `needsReview` on normalized names. Existing `DndTitleCase` stays as a thin delegate for backward compat.

**Tech Stack:** C# / .NET 10, xUnit + FluentAssertions, warnings-as-errors.

**Conventions:** Use Serena symbolic tools for reading/editing `.cs` files (project rule). Build is warnings-as-errors; every task ends green. Commit after each task.

---

### Task 1: `EntityNameNormalizer` — pure casing + acronym allowlist

**Files:**
- Create: `Features/Ingestion/EntityExtraction/EntityNameNormalizer.cs`
- Create: `DndMcpAICsharpFun.Tests/Entities/Extraction/EntityNameNormalizerTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `EntityNameNormalizerTests.cs`:

```csharp
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public class EntityNameNormalizerTests
{
    [Theory]
    [InlineData("CIRCLE OF SPORES", "Circle of Spores")]
    [InlineData("OF MICE AND MEN", "Of Mice and Men")]
    [InlineData("TASHA'S CAULDRON", "Tasha's Cauldron")]
    [InlineData("DECK OF MANY THINGS", "Deck of Many Things")]
    [InlineData("LOW-LEVEL FOLLOWERS", "Low-Level Followers")]
    [InlineData("Circle of Spores", "Circle of Spores")]
    public void TitleCase_applies_dnd_title_case(string input, string expected)
        => EntityNameNormalizer.TitleCase(input).Should().Be(expected);

    [Theory]
    [InlineData("750 GP ART OBJECTS", "750 GP Art Objects")]
    [InlineData("QUICK NPCs", "Quick NPCs")]
    [InlineData("MONSTERS AS NPCs", "Monsters as NPCs")]
    [InlineData("CHALLENGE 1 (200 XP)", "Challenge 1 (200 XP)")]
    public void TitleCase_preserves_dnd_acronyms(string input, string expected)
        => EntityNameNormalizer.TitleCase(input).Should().Be(expected);

    [Fact]
    public void TitleCase_is_idempotent()
    {
        var once = EntityNameNormalizer.TitleCase("750 GP ART OBJECTS");
        EntityNameNormalizer.TitleCase(once).Should().Be(once);
    }

    [Theory]
    [InlineData("BESTIAL SOUL", true)]            // all-caps, clean -> normalize
    [InlineData("Circle of Spores", false)]       // already clean -> leave
    [InlineData("Path of the Beast f eature", false)] // split-word artifact -> leave for heuristic
    public void TryNormalizeHeading_only_touches_clean_all_caps(string input, bool expectedChanged)
    {
        var changed = EntityNameNormalizer.TryNormalizeHeading(input, out var result);
        changed.Should().Be(expectedChanged);
        if (!changed) result.Should().Be(input);
        else result.Should().NotBe(input);
    }
}
```

- [ ] **Step 2: Run tests, verify they fail to compile (type missing)**

Run: `dotnet test --filter "FullyQualifiedName~EntityNameNormalizerTests"`
Expected: FAIL — `EntityNameNormalizer` does not exist.

- [ ] **Step 3: Implement `EntityNameNormalizer`**

Create `EntityNameNormalizer.cs`:

```csharp
using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Deterministic D&D entity-name casing. Pure casing transform (<see cref="TitleCase"/>) plus a
/// gate (<see cref="TryNormalizeHeading"/>) that title-cases only all-caps names with no other OCR
/// artifact, so genuinely garbled names are left intact for the artifact heuristic to catch.
/// </summary>
public static class EntityNameNormalizer
{
    private static readonly HashSet<string> SmallWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "but", "or", "for", "nor",
        "on", "at", "to", "in", "of",
    };

    // Case-insensitive lookup of D&D acronyms/units -> canonical casing.
    private static readonly Dictionary<string, string> Acronyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NPC"] = "NPC", ["NPCs"] = "NPCs", ["PC"] = "PC", ["PCs"] = "PCs", ["DM"] = "DM",
        ["GP"] = "GP", ["SP"] = "SP", ["CP"] = "CP", ["PP"] = "PP", ["EP"] = "EP",
        ["XP"] = "XP", ["HP"] = "HP", ["AC"] = "AC", ["DC"] = "DC", ["CR"] = "CR",
        ["AoE"] = "AoE", ["D&D"] = "D&D",
    };

    private static readonly Regex ApostropheUpperS = new(@"'[A-Z]", RegexOptions.Compiled);

    /// <summary>Pure D&D title-case transform (small words, hyphens, apostrophe-S, acronyms).</summary>
    public static string TitleCase(string name)
    {
        var parts = name.Split(' ');
        var result = new string[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Contains('-'))
            {
                var sub = part.Split('-');
                for (int j = 0; j < sub.Length; j++)
                    sub[j] = ConvertWord(sub[j], i == 0 && j == 0);
                result[i] = string.Join('-', sub);
            }
            else
            {
                result[i] = ConvertWord(part, i == 0);
            }
        }
        return string.Join(' ', result);
    }

    /// <summary>
    /// If <paramref name="name"/> is all-caps and has no other OCR artifact, returns true and the
    /// title-cased form; otherwise returns false and the original name unchanged.
    /// </summary>
    public static bool TryNormalizeHeading(string name, out string normalized)
    {
        bool isAllCaps = name.Length > 1 && name == name.ToUpperInvariant() && name.Any(char.IsLetter);
        bool hasOtherArtifacts = ExtractionNeedsReview.HasOcrArtifacts(name.ToLowerInvariant());
        if (isAllCaps && !hasOtherArtifacts)
        {
            normalized = TitleCase(name);
            return true;
        }
        normalized = name;
        return false;
    }

    private static string ConvertWord(string word, bool isFirst)
    {
        if (string.IsNullOrEmpty(word)) return word;
        if (Acronyms.TryGetValue(word, out var canonical)) return canonical;
        var low = word.ToLowerInvariant();
        if (!isFirst && SmallWords.Contains(low)) return low;
        var cap = char.ToUpperInvariant(low[0]) + low[1..];
        return ApostropheUpperS.Replace(cap, m => m.Value.ToLowerInvariant());
    }
}
```

- [ ] **Step 4: Run tests, verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~EntityNameNormalizerTests"`
Expected: PASS (all theories + facts).

- [ ] **Step 5: Commit**

```bash
git add Features/Ingestion/EntityExtraction/EntityNameNormalizer.cs DndMcpAICsharpFun.Tests/Entities/Extraction/EntityNameNormalizerTests.cs
git commit -m "feat(extraction): add EntityNameNormalizer (D&D title-case + acronym allowlist)"
```

---

### Task 2: Mandatory normalization hook in `EntityExtractionOrchestrator`

**Files:**
- Modify: `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs` (sites at ~line 203 and ~line 391)
- Test: `DndMcpAICsharpFun.Tests/Entities/Extraction/` (extend an existing extraction test or add a focused one if a seam exists)

> Note: the orchestrator builds `EntityEnvelope`s directly. Prefer a tiny private helper used at both sites so the gate is DRY. If no unit seam exists to test the orchestrator in isolation, the behavior is covered end-to-end by Task 5 (apply-to-data) and the `EntityNameNormalizer`/service tests; still add the helper and wire both sites.

- [ ] **Step 1: Add the private helper (Serena `insert_after_symbol` or `replace_content`)**

In `EntityExtractionOrchestrator`, add a private static helper:

```csharp
// Title-case clean all-caps display names before they become entity names + feed the heuristic.
private static string NormalizeDisplayName(string displayName)
    => EntityNameNormalizer.TryNormalizeHeading(displayName, out var n) ? n : displayName;
```

- [ ] **Step 2: Wire site 1 (~line 200-208)**

Replace:
```csharp
            var needsReview = ExtractionNeedsReview.Derive(candidate.DisplayName, confidence);

            var envelope = new EntityEnvelope(
                Id:              id,
                Type:            candidate.Type,
                Name:            candidate.DisplayName,
```
with:
```csharp
            var displayName = NormalizeDisplayName(candidate.DisplayName);
            var needsReview = ExtractionNeedsReview.Derive(displayName, confidence);

            var envelope = new EntityEnvelope(
                Id:              id,
                Type:            candidate.Type,
                Name:            displayName,
```

- [ ] **Step 3: Wire site 2 (~line 388-396)**

Replace:
```csharp
            var needsReview2 = ExtractionNeedsReview.Derive(candidate.DisplayName, confidence2);

            newlyExtracted.Add(new EntityEnvelope(
                Id:              id,
                Type:            candidate.Type,
                Name:            candidate.DisplayName,
```
with:
```csharp
            var displayName2 = NormalizeDisplayName(candidate.DisplayName);
            var needsReview2 = ExtractionNeedsReview.Derive(displayName2, confidence2);

            newlyExtracted.Add(new EntityEnvelope(
                Id:              id,
                Type:            candidate.Type,
                Name:            displayName2,
```

> Note: `id` is derived from `candidate.DisplayName` via `EntityIdSlug` earlier in each loop; that already lowercases the slug, so title-casing the display name does not change the id. Leave id derivation untouched.

- [ ] **Step 4: Build + run extraction tests**

Run: `dotnet build` then `dotnet test --filter "FullyQualifiedName~Extraction"`
Expected: clean build (0 warnings), tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs
git commit -m "feat(extraction): normalize entity names at extraction time (mandatory)"
```

---

### Task 3: `CanonicalNameNormalizerService` delegates + clears `needsReview`

**Files:**
- Modify: `Features/Admin/CanonicalNameNormalizerService.cs`
- Modify: `DndMcpAICsharpFun.Tests/Entities/Admin/CanonicalNameNormalizerServiceTests.cs`

- [ ] **Step 1: Add a failing test for flag-clearing**

In `CanonicalNameNormalizerServiceTests.cs`, add an integration test asserting that an all-caps-clean entity with `needsReview: true` becomes title-cased **and** `needsReview: false` after `NormalizeAsync(dryRun:false)`. Mirror the existing integration-test setup in the file (write a canonical JSON to a temp dir, run the service, reload, assert). Assert specifically:

```csharp
// after normalize:
entity.Name.Should().Be("Circle of Spores");
entity.NeedsReview.Should().BeFalse();
```

Also keep an assertion that a split-word entity (`"Path of the Beast f eature"`) stays unchanged with `NeedsReview == true`.

- [ ] **Step 2: Run it, verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~CanonicalNameNormalizerServiceTests"`
Expected: FAIL — current code leaves `needsReview` unchanged in the all-caps branch.

- [ ] **Step 3: Update the service (Serena `replace_symbol_body` on the relevant parts)**

In `CanonicalNameNormalizerService`:
- Replace the body of `DndTitleCase` with a delegate: `public static string DndTitleCase(string name) => EntityNameNormalizer.TitleCase(name);` and remove the now-unused private `ConvertWord`, `SmallWords`, `ApostropheUpperS` members (their logic now lives in `EntityNameNormalizer`). Add `using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;`.
- In the `normalized` projection inside `NormalizeAsync`, change the all-caps-clean branch from
  `return entity with { Name = DndTitleCase(name) };`
  to
  `return entity with { Name = EntityNameNormalizer.TitleCase(name), NeedsReview = false };`

- [ ] **Step 4: Run tests, verify green (incl. the pre-existing `DndTitleCase` theory)**

Run: `dotnet test --filter "FullyQualifiedName~CanonicalNameNormalizerServiceTests"`
Expected: PASS — including the existing `DndTitleCase_converts_all_caps_correctly` theory (now delegating) and the new flag-clearing test.

- [ ] **Step 5: Commit**

```bash
git add Features/Admin/CanonicalNameNormalizerService.cs DndMcpAICsharpFun.Tests/Entities/Admin/CanonicalNameNormalizerServiceTests.cs
git commit -m "feat(admin): normalizer delegates to EntityNameNormalizer and clears needsReview"
```

---

### Task 4: Full build + suite green

- [ ] **Step 1: Build (warnings-as-errors)**

Run: `dotnet build`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 2: Full test suite**

Run: `dotnet test`
Expected: all green (existing 502 + new tests). Fix any regressions before proceeding.

- [ ] **Step 3: Commit (only if any fixups were needed)**

```bash
git add -A
git commit -m "test: keep suite green for mandatory name normalization"
```

---

### Task 5: Apply to existing DMG + Tasha data

> Requires the running stack (rebuild the app image so it carries the new code): `docker compose build app && docker compose up -d app`, wait healthy. Admin header: `X-Admin-Api-Key: devXXXdev`.

- [ ] **Step 1: Dry-run the normalizer**

Run: `curl -s -X POST "http://localhost:5101/admin/canonical/normalize?dryRun=true" -H "X-Admin-Api-Key: devXXXdev"`
Expected: report shows a large `titleCased` count across `dmg14.json` + `tce.json` (the ~900 all-caps names), small `flagged`.

- [ ] **Step 2: Apply, then re-validate**

Run: `curl -s -X POST "http://localhost:5101/admin/canonical/normalize" -H "X-Admin-Api-Key: devXXXdev"` then
`curl -s -X POST "http://localhost:5101/admin/canonical/validate" -H "X-Admin-Api-Key: devXXXdev"`
Expected: validate `failures: 0`, `warnings: 0`, and the `needsReview` per-file counts dropped sharply from ~647/389.

- [ ] **Step 3: Re-ingest both books and verify count**

Run: `for id in 1 2; do curl -s -o /dev/null -w "book $id %{http_code}\n" -X POST "http://localhost:5101/admin/books/$id/ingest-entities" -H "X-Admin-Api-Key: devXXXdev"; done`
Then `curl -s -X POST http://localhost:6333/collections/dnd_entities/points/count -H 'Content-Type: application/json' -d '{"exact":true}'`
Expected: `count: 1080` (no orphans).

- [ ] **Step 4: Spot-check normalized names with acronyms**

Run: `curl -s "http://localhost:5101/retrieval/entities/search?q=deck%20of%20many%20things&limit=1"` and `...q=750%20gp%20art%20objects...`
Expected: names render as `Deck of Many Things`, `750 GP Art Objects` (acronym preserved).

- [ ] **Step 5: Review the canonical diff and commit the data**

```bash
git add books/canonical/dmg14.json books/canonical/tce.json
git commit -m "data(canonical): title-case all-caps entity names (mandatory normalization)"
```

---

### Task 6: Archive the change

- [ ] **Step 1: Tick `tasks.md` boxes** in `openspec/changes/mandatory-entity-name-normalization/tasks.md`.

- [ ] **Step 2: Archive**

Run: `openspec archive mandatory-entity-name-normalization -y`
Expected: syncs the `entity-extraction-pipeline` and `canonical-name-normalizer` specs, moves the change to `openspec/changes/archive/`.

- [ ] **Step 3: Commit the archive**

```bash
git add -A openspec/
git commit -m "docs(openspec): archive mandatory-entity-name-normalization; sync specs"
```
