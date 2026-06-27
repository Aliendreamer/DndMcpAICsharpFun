# Consolidate Extraction Signatures — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **CRITICAL — Serena:** Every implementer and reviewer subagent MUST call `mcp__serena__initial_instructions` first and use Serena symbolic tools (`find_symbol`, `replace_content`, `replace_symbol_body`, `search_for_pattern`) for ALL code reads/edits. Built-in Read/Edit on `.cs` files is forbidden. After every Serena edit, grep-verify the change landed (Serena `insert_after_symbol` has silently no-op'd here before).

**Goal:** Consolidate the four scattered stat-block detectors into one tested `ExtractionSignatures` utility and route per-candidate typing through one deterministic `DeterministicTypeResolver` ladder, fixing three run-surfaced behaviours (override misfire, garbage-named candidates, magic items typed `Item`).

**Architecture:** A pure static `ExtractionSignatures` answers all content/name questions. A pure static `DeterministicTypeResolver.Resolve(candidate)` returns `Drop | Force(type) | Defer`. The orchestrator filters `Drop` candidates before the loop and, per surviving candidate, forces the type or defers to the existing content-first union. `StatBlockSignature` folds into `ExtractionSignatures`; the deduplicator and scanner stop hand-matching marker strings.

**Tech Stack:** C# / .NET 10, xUnit + FluentAssertions, Serena for edits.

## Global Constraints

- `net10.0`, nullable enabled, implicit usings, **warnings-as-errors** (every project) — build must be 0 warnings.
- TDD: failing test first, minimal impl, green, commit. Never commit without explicit user permission — these commits are gated; the executor pauses for the user's go-ahead before each `git commit` step.
- Edits via Serena symbolic tools only; grep-verify after each edit.
- Behaviour-preserving for the consolidation: the full non-persistence suite (676 tests) must stay green except where a test asserts one of the three intended behaviour changes.
- Namespace for all new files: `DndMcpAICsharpFun.Features.Ingestion.EntityExtraction`.
- `EntityType` values available: Background, Class, Condition, Feat, God, Item, MagicItem, Monster, Plane, Race, Spell, Trap.
- `EntityCandidate` shape: `(EntityType Type, string DisplayName, string Text, int Page, IReadOnlyList<EntityType> TypePrior)`.

---

### Task 1: `ExtractionSignatures` utility

**Files:**
- Create: `Features/Ingestion/EntityExtraction/ExtractionSignatures.cs`
- Test: `DndMcpAICsharpFun.Tests/Ingestion/EntityExtraction/ExtractionSignaturesTests.cs`

**Interfaces:**
- Produces: `static class ExtractionSignatures` with
  `bool IsCompleteStatBlock(string text)`, `bool IsMagicItem(string text)`, `bool IsEntityLikeName(string? name)`,
  and primitives `bool HasArmorClass(string)`, `bool HasHitPoints(string)`, `bool HasChallenge(string)`.

- [ ] **Step 1: Write the failing tests**

Create `ExtractionSignaturesTests.cs`:
```csharp
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;

public sealed class ExtractionSignaturesTests
{
    [Theory]
    [InlineData("Large aberration, lawful evil  Armor Class 17  Hit Points 135  Challenge 10 (5,900 XP)", true)]
    [InlineData("Armor Class 19  Hit Points 50  damage threshold 15", false)] // no Challenge
    [InlineData("Aboleths are ancient horrors of the deep.", false)]
    [InlineData("", false)]
    public void IsCompleteStatBlock_matches_AC_HP_Challenge(string text, bool expected) =>
        ExtractionSignatures.IsCompleteStatBlock(text).Should().Be(expected);

    [Theory]
    [InlineData("Weapon (any sword that deals slashing damage), legendary (requires attunement)", true)]
    [InlineData("Wondrous item, rare", true)]
    [InlineData("Ring, uncommon (requires attunement)", true)]
    [InlineData("A sturdy leather backpack that holds 30 pounds.", false)] // plain item
    [InlineData("Fireball: a bright streak flashes to a point you choose.", false)] // spell
    [InlineData("Huge giant, chaotic evil  Armor Class 14  Legendary Actions", false)] // monster, not item
    public void IsMagicItem_matches_item_signatures(string text, bool expected) =>
        ExtractionSignatures.IsMagicItem(text).Should().Be(expected);

    [Theory]
    [InlineData("Aboleth", true)]
    [InlineData("Bag of Holding", true)]
    [InlineData("Fireball", true)]
    [InlineData("ACTIONS", false)]
    [InlineData("REACTIONS", false)]
    [InlineData("Appendix D: Creature Statistics", false)]
    [InlineData("Step 2. Basic Statistics", false)]
    [InlineData("Challenge 7 (2,900 XP)", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsEntityLikeName_rejects_headings_and_fragments(string? name, bool expected) =>
        ExtractionSignatures.IsEntityLikeName(name).Should().Be(expected);
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ExtractionSignaturesTests"`
Expected: FAIL — `ExtractionSignatures` does not exist.

- [ ] **Step 3: Implement `ExtractionSignatures`**

Create `ExtractionSignatures.cs`:
```csharp
using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Single source of truth for content/name recognition in the extraction pipeline. OCR-tolerant,
/// case-insensitive. All stat-block / magic-item / name-quality checks route through here rather
/// than re-matching marker strings independently.
/// </summary>
public static class ExtractionSignatures
{
    public static bool HasArmorClass(string text) => Has(text, "Armor Class");
    public static bool HasHitPoints(string text) => Has(text, "Hit Points");
    public static bool HasChallenge(string text) => Has(text, "Challenge");

    /// A complete creature stat block: Armor Class + Hit Points + Challenge ("Challenge X (Y XP)"
    /// is creature-specific, so tables/items that merely mention AC/HP are not matched).
    public static bool IsCompleteStatBlock(string text) =>
        !string.IsNullOrEmpty(text) && HasArmorClass(text) && HasHitPoints(text) && HasChallenge(text);

    /// A magic item: explicit attunement / wondrous-item phrasing, or a "<category>, <rarity>" header
    /// line (e.g. "Weapon (any sword), legendary"). Only reached for non-stat-block candidates, so a
    /// monster's "Legendary Actions" never triggers it.
    public static bool IsMagicItem(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (Has(text, "requires attunement") || Has(text, "wondrous item")) return true;
        return MagicItemHeader.IsMatch(text);
    }

    /// Whether a candidate name is a real entity name (true) versus a section heading or stat-block
    /// fragment (false). Conservative: rejects only clear non-entities; when unsure returns true so a
    /// borderline candidate is kept (it can still decline at the union).
    public static bool IsEntityLikeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim();
        if (!n.Any(char.IsLetter)) return false;                       // pure digits/punctuation
        if (char.IsDigit(n[0])) return false;                          // "12. Something"
        if (StepHeading.IsMatch(n)) return false;                      // "Step 2. Basic Statistics"
        if (ChallengeFragment.IsMatch(n)) return false;                // "Challenge 7 (2,900 XP)"
        if (n.StartsWith("Appendix", StringComparison.OrdinalIgnoreCase)) return false;
        if (n.Length >= 4 && n == n.ToUpperInvariant() && !n.Contains(' ')) return false; // "ACTIONS"
        return true;
    }

    private static readonly Regex MagicItemHeader = new(
        @"\b(weapon|armou?r|ring|rod|staff|wand|potion|scroll|wondrous)\b[^.\n]{0,40}?,\s*(common|uncommon|rare|very rare|legendary|artifact)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StepHeading = new(
        @"^step\s+\d+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ChallengeFragment = new(
        @"^challenge\s+\d", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool Has(string text, string token) =>
        !string.IsNullOrEmpty(text) && text.Contains(token, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~ExtractionSignaturesTests"`
Expected: PASS (all theories).

- [ ] **Step 5: Commit** (pause for user go-ahead first)

```bash
git add Features/Ingestion/EntityExtraction/ExtractionSignatures.cs DndMcpAICsharpFun.Tests/Ingestion/EntityExtraction/ExtractionSignaturesTests.cs
git commit -m "feat(extraction): ExtractionSignatures — single source for stat-block/magic-item/name signatures"
```

---

### Task 2: Fold `StatBlockSignature` + consolidate the deduplicator and scanner

**Files:**
- Delete: `Features/Ingestion/EntityExtraction/StatBlockSignature.cs`
- Modify: `Features/Ingestion/EntityExtraction/ExtractionCandidateDeduplicator.cs` (the `HasStatBlock` helper, lines ~30-32)
- Modify: `Features/Ingestion/EntityExtraction/StatBlockScanner.cs` (its "Armor Class" matching)
- Modify: `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs:436` (`StatBlockSignature.IsCompleteStatBlock` → `ExtractionSignatures.IsCompleteStatBlock`) — this call is removed entirely in Task 4, but update it now so the build stays green between tasks.
- Delete the now-redundant `StatBlockSignatureTests.cs` (its cases are covered by `ExtractionSignaturesTests`).

**Interfaces:**
- Consumes: `ExtractionSignatures` from Task 1.

- [ ] **Step 1: Replace the deduplicator's private `HasStatBlock`**

In `ExtractionCandidateDeduplicator.cs`, replace the `OrderByDescending(HasStatBlock)` reference so it routes through the shared primitives while **preserving the exact old behaviour** (AC + HP, no Challenge):
```csharp
// in the OrderByDescending call:
.OrderByDescending(c => ExtractionSignatures.HasArmorClass(c.Text) && ExtractionSignatures.HasHitPoints(c.Text))
```
Then delete the private `HasStatBlock` method. (This is behaviour-preserving — the dedup tie-break's "has a stat block" question stays AC+HP, now expressed via the shared primitives rather than inline string matches. Deduplicator tests should pass unchanged.)

- [ ] **Step 2: Route `StatBlockScanner` through the primitive**

In `StatBlockScanner.cs`, replace any literal `Contains("Armor Class", ...)` with `ExtractionSignatures.HasArmorClass(...)`. Keep the size/type-line regex and span logic local (that is the scanner's own job).

- [ ] **Step 3: Point the orchestrator's override call at the new utility (temporary)**

In `EntityExtractionOrchestrator.cs:436`, change `StatBlockSignature.IsCompleteStatBlock(candidate.Text)` → `ExtractionSignatures.IsCompleteStatBlock(candidate.Text)`.

- [ ] **Step 4: Delete `StatBlockSignature.cs` and `StatBlockSignatureTests.cs`**

Use Serena `safe_delete_symbol` / file delete. Grep to confirm no remaining references:
Run: `grep -rn "StatBlockSignature" --include=*.cs .` → Expected: no matches.

- [ ] **Step 5: Build + run extraction tests**

Run: `dotnet build` (expect 0 warnings) then
`dotnet test --filter "FullyQualifiedName~EntityExtraction|FullyQualifiedName~Deduplicator|FullyQualifiedName~StatBlock"`
Expected: PASS.

- [ ] **Step 6: Verify the consolidation invariant**

Run: `grep -rn "\"Armor Class\"\|\"Hit Points\"\|\"Challenge\"" Features/Ingestion/EntityExtraction --include=*.cs | grep -v ExtractionSignatures.cs`
Expected: no matches (only `ExtractionSignatures` matches the marker strings).

- [ ] **Step 7: Commit** (pause for user go-ahead first)

```bash
git add -A
git commit -m "refactor(extraction): route stat-block detection through ExtractionSignatures; remove StatBlockSignature"
```

---

### Task 3: `DeterministicTypeResolver` ladder

**Files:**
- Create: `Features/Ingestion/EntityExtraction/DeterministicTypeResolver.cs`
- Test: `DndMcpAICsharpFun.Tests/Ingestion/EntityExtraction/DeterministicTypeResolverTests.cs`

**Interfaces:**
- Consumes: `ExtractionSignatures`, `EntityCandidate`, `EntityType`.
- Produces:
  `enum DeterministicOutcome { Drop, ForceType, Defer }`,
  `readonly record struct TypeResolution(DeterministicOutcome Outcome, EntityType ForcedType)` with statics `Drop`, `Defer`, `Force(EntityType)`,
  `static class DeterministicTypeResolver` with `TypeResolution Resolve(EntityCandidate candidate)`.

- [ ] **Step 1: Write the failing tests**

Create `DeterministicTypeResolverTests.cs`:
```csharp
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;

public sealed class DeterministicTypeResolverTests
{
    private static EntityCandidate C(string name, string text) =>
        new(EntityType.Monster, name, text, 1, new[] { EntityType.Monster });

    [Fact]
    public void Non_entity_name_is_dropped()
    {
        var r = DeterministicTypeResolver.Resolve(C("ACTIONS", "Armor Class 14 Hit Points 30 Challenge 1 (200 XP)"));
        r.Outcome.Should().Be(DeterministicOutcome.Drop);
    }

    [Fact]
    public void Complete_stat_block_with_creature_name_forces_Monster()
    {
        var r = DeterministicTypeResolver.Resolve(C("Aboleth", "Large aberration  Armor Class 17  Hit Points 135  Challenge 10 (5,900 XP)"));
        r.Outcome.Should().Be(DeterministicOutcome.ForceType);
        r.ForcedType.Should().Be(EntityType.Monster);
    }

    [Fact]
    public void Tutorial_fragment_stat_block_is_not_forced_Monster()
    {
        // DMG "Creating a Monster" example — name is a fragment, so it drops before the Monster branch.
        var r = DeterministicTypeResolver.Resolve(C("Step 2. Basic Statistics", "Armor Class 15  Hit Points 100  Challenge 5 (1,800 XP)"));
        r.Outcome.Should().Be(DeterministicOutcome.Drop);
    }

    [Fact]
    public void Magic_item_signature_forces_MagicItem()
    {
        var r = DeterministicTypeResolver.Resolve(C("Vorpal Sword", "Weapon (any sword that deals slashing damage), legendary (requires attunement)"));
        r.Outcome.Should().Be(DeterministicOutcome.ForceType);
        r.ForcedType.Should().Be(EntityType.MagicItem);
    }

    [Fact]
    public void Ordinary_entity_defers_to_union()
    {
        var r = DeterministicTypeResolver.Resolve(C("Fireball", "A bright streak flashes to a point you choose, then blossoms into flame."));
        r.Outcome.Should().Be(DeterministicOutcome.Defer);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~DeterministicTypeResolverTests"`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Implement the resolver**

Create `DeterministicTypeResolver.cs`:
```csharp
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public enum DeterministicOutcome { Drop, ForceType, Defer }

public readonly record struct TypeResolution(DeterministicOutcome Outcome, EntityType ForcedType)
{
    public static readonly TypeResolution Drop = new(DeterministicOutcome.Drop, default);
    public static readonly TypeResolution Defer = new(DeterministicOutcome.Defer, default);
    public static TypeResolution Force(EntityType type) => new(DeterministicOutcome.ForceType, type);
}

/// <summary>
/// One deterministic per-candidate type decision, applied before the content-first union:
/// drop non-entity-named candidates → force Monster on a complete stat block (the name is already
/// entity-like, so the drop step is the override misfire guard) → force MagicItem on a magic-item
/// signature → otherwise defer to the union pick-or-decline.
/// </summary>
public static class DeterministicTypeResolver
{
    public static TypeResolution Resolve(EntityCandidate candidate)
    {
        if (!ExtractionSignatures.IsEntityLikeName(candidate.DisplayName))
            return TypeResolution.Drop;
        if (ExtractionSignatures.IsCompleteStatBlock(candidate.Text))
            return TypeResolution.Force(EntityType.Monster);
        if (ExtractionSignatures.IsMagicItem(candidate.Text))
            return TypeResolution.Force(EntityType.MagicItem);
        return TypeResolution.Defer;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~DeterministicTypeResolverTests"`
Expected: PASS.

- [ ] **Step 5: Commit** (pause for user go-ahead first)

```bash
git add Features/Ingestion/EntityExtraction/DeterministicTypeResolver.cs DndMcpAICsharpFun.Tests/Ingestion/EntityExtraction/DeterministicTypeResolverTests.cs
git commit -m "feat(extraction): DeterministicTypeResolver ladder (drop / Monster / MagicItem / defer)"
```

---

### Task 4: Wire the resolver into the orchestrator

**Files:**
- Modify: `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs` — candidate build (~line 92, after `Dedupe`) and `ExtractOneAsync` (the inline Monster-override block ~lines 430-450).
- Test: `DndMcpAICsharpFun.Tests/Entities/Extraction/EntityExtractionOrchestratorTests.cs` (add drop + MagicItem coverage).

**Interfaces:**
- Consumes: `DeterministicTypeResolver.Resolve`, the existing `BuildTypedEnvelope`, `candidateExtractor.ExtractFieldsAsync`, `CandidateExtractor.StripConfidence`.

- [ ] **Step 1: Filter Drop candidates after dedup**

After the `Dedupe(...)` assignment (~line 92), add:
```csharp
var keptCandidates = candidates
    .Where(c => DeterministicTypeResolver.Resolve(c).Outcome != DeterministicOutcome.Drop)
    .ToList();
var droppedCount = candidates.Count - keptCandidates.Count;
if (droppedCount > 0)
    logger.LogInformation("Dropped {Count} non-entity-named candidates before extraction", droppedCount);
candidates = keptCandidates;
```
Grep-verify the block landed and `candidates` is reassigned.

- [ ] **Step 2: Replace the inline Monster override in `ExtractOneAsync` with the resolver**

Find the block (currently ~436) that reads `if (schemas.TryGetValue(EntityType.Monster, out var monsterSchema) && StatBlockSignature.IsCompleteStatBlock(candidate.Text))`. Replace the whole override block with a general forced-type path:
```csharp
// Deterministic type resolution. Drop never reaches here (filtered at candidate build); a forced
// type extracts with that type's schema directly (no decline branch); Defer uses the union.
var resolution = DeterministicTypeResolver.Resolve(candidate);
if (resolution.Outcome == DeterministicOutcome.ForceType &&
    schemas.TryGetValue(resolution.ForcedType, out var forcedSchema))
{
    var (forcedFields, forcedError) = await candidateExtractor.ExtractFieldsAsync(
        record, candidate with { Type = resolution.ForcedType }, forcedSchema, ct);
    if (forcedFields is null)
    {
        logger.LogWarning("Forced {Type} extraction failed for '{Name}' (page {Page}): {Error}",
            resolution.ForcedType, candidate.DisplayName, candidate.Page, forcedError);
        return (null, new ExtractionErrorEntry(
            SourceEntityId: id, FieldPath: "(extraction)", MissingTargetId: string.Empty,
            ErrorKind: "extraction_failure", Detail: forcedError));
    }
    var forcedConfidence = forcedFields.Value.TryGetProperty("confidence", out var fcp) ? fcp.GetString() : null;
    var forcedClean = CandidateExtractor.StripConfidence(forcedFields.Value);
    return (BuildTypedEnvelope(id, resolution.ForcedType, displayName, sourceBook, edition, candidate, forcedClean, forcedConfidence), null);
}
```
Keep the existing `var result = await candidateExtractor.ExtractUnionAsync(...)` and its switch exactly as-is below this block. Grep-verify `StatBlockSignature` is gone and `DeterministicTypeResolver` is referenced.

- [ ] **Step 3: Add orchestrator tests for drop + MagicItem**

In `EntityExtractionOrchestratorTests.cs`, add two tests using the existing harness pattern: (a) a candidate named "ACTIONS" with stat-block text produces NO entity and NO LLM call; (b) a candidate "Vorpal Sword" with magic-item text is written as `MagicItem`. (Follow the existing `FivetoolsSourceKey_propagates…` test's harness + a Monster/MagicItem schema file in `schemasDir`.)

- [ ] **Step 4: Build + full non-persistence suite**

Run: `dotnet build` (0 warnings) then `dotnet test --filter "FullyQualifiedName!~Persistence"`
Expected: PASS — the 676 existing tests plus the new ones. If a pre-existing test asserted the old inline-override behaviour, reconcile it to the resolver (intended change).

- [ ] **Step 5: Commit** (pause for user go-ahead first)

```bash
git add -A
git commit -m "refactor(extraction): route ExtractOneAsync through DeterministicTypeResolver; add MagicItem override + drop guard"
```

---

### Task 5: Live validation re-run (MM + PHB + DMG) — operator task, not a subagent

This task is run by the main session (Docker + GPU), not a code subagent.

- [ ] **Step 1:** Rebuild the app image (`docker compose build app`), recreate the app (`docker compose up -d --force-recreate app`), ensure qwen3 is loaded on the shared GPU.
- [ ] **Step 2:** Re-extract MM (book 1), PHB (book 2), DMG (book 3) with `?force=true`, 30-min check-ins.
- [ ] **Step 3:** Compare to the prior runs and record in the change:
  - DMG: tutorial-fragment Monsters ("Step 2. Basic Statistics", "Challenge 7 (2,900 XP)") are GONE; Vorpal Sword (and peers) → `MagicItem`.
  - All three: no real entity lost vs the known-good Accepted lists (Aboleth/Bugbear/Cyclops still Monster; Fireball→Spell; Bag of Holding→Item/MagicItem); override still catches real stat blocks.
  - Note any `IsEntityLikeName` over-drop and tune the regex conservatively if a real entity was lost.
- [ ] **Step 4:** Record before/after distribution deltas in `proposal.md` or a results note, then this change is ready to archive.

---

## Self-Review

- **Spec coverage:** Req "single source of truth" → Tasks 1-2. Req "entity-like name" → Task 1 + 3. Req "deterministic ladder" (drop/Monster-guard/MagicItem/defer + union fallthrough) → Tasks 3-4. All spec scenarios map to a test. ✓
- **Placeholder scan:** none — every code step has full code. ✓
- **Type consistency:** `TypeResolution`/`DeterministicOutcome`/`Resolve` names consistent across Tasks 3-4; `ExtractionSignatures` method names consistent across Tasks 1-4; `BuildTypedEnvelope`/`ExtractFieldsAsync`/`StripConfidence` match the existing orchestrator. ✓
- **Conflict resolved:** the dedup tie-break stays AC+HP (behaviour-preserving), now expressed via shared primitives — not upgraded to AC+HP+Challenge — so it does not violate the behaviour-preserving global constraint.
