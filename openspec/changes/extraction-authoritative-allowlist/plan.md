# 5etools Authoritative Allowlist — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** For official books, decline non-matching candidates of the 8 gated types (no LLM call) instead of content-first extracting them, and record the declines to `<book-slug>.declined.json` — eliminating the chapter-body noise (397 bogus "Class", race fields, OCR garble) while keeping recall and real entities.

**Architecture:** Extend `DeterministicTypeResolver` with a `Decline` outcome and an `isOfficial` input; insert the gate into the ladder after the magic-item force and before `Defer`. The orchestrator passes `isOfficial` (from `record.FivetoolsSourceKey`), collects declined candidates, and writes them via a new `ExtractionDeclinedFile` writer mirroring `ExtractionWarningsFile`.

**Tech Stack:** .NET 10, xUnit + FluentAssertions, NSubstitute (existing test stack).

## Global Constraints

- Warnings-as-errors: `dotnet build` MUST be 0 warnings.
- Serena MCP is MANDATORY for all `.cs` reads/edits (built-in Read/Edit forbidden on code files).
- Gated set is EXACTLY: `Spell, Monster, Class, Race, Background, Feat, Condition, God`. Ungated: `Item, MagicItem, Plane` (and any other type).
- "Official" = `IngestionRecord.FivetoolsSourceKey` is non-empty.
- Ladder order MUST stay: `match → drop-non-entity-like → stat-block(ForceMonster) → magic-item(ForceMagicItem) → official-gated-decline → defer`. The entity-like DROP stays BEFORE the stat-block check (preserves the tutorial-fragment override guard). The stat-block guard fires even for official books (never drop a real monster).
- Decline fires only when the candidate's PRIMARY prior type (`TypePrior[0]`, the bookmark-derived first entry) is gated. Empty prior → fall through to `Defer`. NOTE: the scanner (`HeadingCategoryClassifier.ExpandPrior`) always appends a frequency floor `{Monster, Spell, Item, Class}` to every `TypePrior`, so the ungated `Item` is always present and an "all priors gated" test would NEVER fire — the gate MUST key off the primary (`TypePrior[0]`) only.
- A declined candidate makes NO extraction LLM call, is NOT emitted as an entity, and is NOT added to the checkpoint as extracted.
- `<book-slug>.declined.json` is separate from `errors.json`; the `errorsOnly` retry set is built from `errors.json` only and MUST NOT retry declines.

---

## Task 1: Decline outcome + gated-type set on the resolver

**Files:**
- Modify: `Features/Ingestion/EntityExtraction/DeterministicTypeResolver.cs`
- Test: `DndMcpAICsharpFun.Tests/Ingestion/EntityExtraction/DeterministicTypeResolverTests.cs`

**Interfaces:**
- Produces: `DeterministicOutcome.Decline`; `TypeResolution.DeclineReason` (`string?`); `TypeResolution.Decline(string reason)`; `DeterministicTypeResolver.GatedTypes` (`IReadOnlySet<EntityType>`).

**Current shapes (verbatim):**
```csharp
public enum DeterministicOutcome { Drop, ForceType, Defer }

public readonly record struct TypeResolution(DeterministicOutcome Outcome, EntityType ForcedType)
{
    public string? CanonicalName { get; init; }
    public static TypeResolution Force(EntityType type, string? canonicalName = null) =>
        new(DeterministicOutcome.ForceType, type) { CanonicalName = canonicalName };
    // ... Drop, Defer exist
}
```

- [ ] **Step 1: Write failing tests**
```csharp
[Fact]
public void GatedTypes_contains_the_eight_and_excludes_item_magicitem_plane()
{
    DeterministicTypeResolver.GatedTypes.Should().BeEquivalentTo(new[]
    {
        EntityType.Spell, EntityType.Monster, EntityType.Class, EntityType.Race,
        EntityType.Background, EntityType.Feat, EntityType.Condition, EntityType.God
    });
    DeterministicTypeResolver.GatedTypes.Should().NotContain(EntityType.Item);
    DeterministicTypeResolver.GatedTypes.Should().NotContain(EntityType.MagicItem);
    DeterministicTypeResolver.GatedTypes.Should().NotContain(EntityType.Plane);
}

[Fact]
public void Decline_carries_outcome_and_reason()
{
    var r = TypeResolution.Decline("no_5etools_match");
    r.Outcome.Should().Be(DeterministicOutcome.Decline);
    r.DeclineReason.Should().Be("no_5etools_match");
}
```
- [ ] **Step 2:** Run tests, verify they fail to compile/assert.
- [ ] **Step 3: Implement**
```csharp
public enum DeterministicOutcome { Drop, ForceType, Defer, Decline }

public readonly record struct TypeResolution(DeterministicOutcome Outcome, EntityType ForcedType)
{
    public string? CanonicalName { get; init; }
    public string? DeclineReason { get; init; }
    public static TypeResolution Force(EntityType type, string? canonicalName = null) =>
        new(DeterministicOutcome.ForceType, type) { CanonicalName = canonicalName };
    public static TypeResolution Decline(string reason) =>
        new(DeterministicOutcome.Decline, default) { DeclineReason = reason };
    // existing Drop, Defer unchanged
}

public static class DeterministicTypeResolver
{
    public static readonly IReadOnlySet<EntityType> GatedTypes = new HashSet<EntityType>
    {
        EntityType.Spell, EntityType.Monster, EntityType.Class, EntityType.Race,
        EntityType.Background, EntityType.Feat, EntityType.Condition, EntityType.God,
    };
    // Resolve updated in Task 2
}
```
- [ ] **Step 4:** Run tests, verify pass.
- [ ] **Step 5:** Commit (`feat(extraction): Decline outcome + gated-type set`).

## Task 2: Resolve gate (isOfficial + ladder insert)

**Files:**
- Modify: `Features/Ingestion/EntityExtraction/DeterministicTypeResolver.cs`
- Test: `DeterministicTypeResolverTests.cs`

**Interfaces:**
- Consumes: `EntityCandidate.DisplayName`, `EntityCandidate.Text`, `EntityCandidate.TypePrior` (`IReadOnlyList<EntityType>`), `EntityNameMatcher`.
- Produces: `Resolve(EntityCandidate candidate, EntityNameMatcher? matcher = null, bool isOfficial = false)`.

- [ ] **Step 1: Write failing tests** (use a candidate factory; gated example name that is entity-like but not in 5etools, e.g. "Rage" with `TypePrior=[Class]`; an ungated/mixed prior; a homebrew flag; a stat-block case):
```csharp
[Fact] // official + all-gated prior + no match + no stat block -> Decline
public void Official_gated_nonmatch_declines()
{
    var c = Candidate("Rage", text: "", prior: EntityType.Class);
    var r = DeterministicTypeResolver.Resolve(c, matcher: null, isOfficial: true);
    r.Outcome.Should().Be(DeterministicOutcome.Decline);
    r.DeclineReason.Should().Be("no_5etools_match");
}

[Fact] // homebrew -> Defer (gate never fires)
public void Homebrew_gated_nonmatch_defers()
{
    var c = Candidate("Rage", text: "", prior: EntityType.Class);
    DeterministicTypeResolver.Resolve(c, matcher: null, isOfficial: false)
        .Outcome.Should().Be(DeterministicOutcome.Defer);
}

[Fact] // official + ungated PRIMARY prior -> Defer
public void Official_ungated_primary_defers()
{
    var c = Candidate("Some Thing", text: "", prior: new[]{ EntityType.Item, EntityType.Class });
    DeterministicTypeResolver.Resolve(c, matcher: null, isOfficial: true)
        .Outcome.Should().Be(DeterministicOutcome.Defer);
}

[Fact] // official + gated PRIMARY + ungated floor (Item) present -> Decline (gate keys off primary)
public void Official_gated_primary_with_floor_declines()
{
    var c = Candidate("Rage", text: "", prior: new[]{ EntityType.Class, EntityType.Item });
    DeterministicTypeResolver.Resolve(c, matcher: null, isOfficial: true)
        .Outcome.Should().Be(DeterministicOutcome.Decline);
}

[Fact] // official + empty prior -> Defer
public void Official_empty_prior_defers()
{
    var c = Candidate("Some Thing", text: "", prior: Array.Empty<EntityType>());
    DeterministicTypeResolver.Resolve(c, matcher: null, isOfficial: true)
        .Outcome.Should().Be(DeterministicOutcome.Defer);
}

[Fact] // official + stat block + no match -> Force Monster (guard wins, NOT Decline)
public void Official_statblock_nonmatch_forces_monster_not_decline()
{
    var c = Candidate("Xyzgoblin Elder", text: "Armor Class 15\nHit Points 40\nChallenge 3", prior: EntityType.Monster);
    var r = DeterministicTypeResolver.Resolve(c, matcher: null, isOfficial: true);
    r.Outcome.Should().Be(DeterministicOutcome.ForceType);
    r.ForcedType.Should().Be(EntityType.Monster);
}

[Fact] // non-entity-like name -> Drop (before decline), even official+gated
public void Official_nonentitylike_drops_not_declines()
{
    var c = Candidate("ACTIONS", text: "", prior: EntityType.Monster);
    DeterministicTypeResolver.Resolve(c, matcher: null, isOfficial: true)
        .Outcome.Should().Be(DeterministicOutcome.Drop);
}
```
- [ ] **Step 2:** Run, verify fail.
- [ ] **Step 3: Implement Resolve** (insert gate after magic-item, before Defer; keep drop before stat-block):
```csharp
public static TypeResolution Resolve(EntityCandidate candidate, EntityNameMatcher? matcher = null, bool isOfficial = false)
{
    if (matcher?.Match(candidate.DisplayName) is { } m)
        return TypeResolution.Force(m.Type, m.Canonical);

    if (!ExtractionSignatures.IsEntityLikeName(candidate.DisplayName))
        return TypeResolution.Drop;
    if (ExtractionSignatures.IsCompleteStatBlock(candidate.Text))
        return TypeResolution.Force(EntityType.Monster);
    if (ExtractionSignatures.IsMagicItem(candidate.Text))
        return TypeResolution.Force(EntityType.MagicItem);

    if (isOfficial
        && candidate.TypePrior.Count > 0
        && GatedTypes.Contains(candidate.TypePrior[0]))   // PRIMARY prior only (floor always adds Item)
        return TypeResolution.Decline("no_5etools_match");

    return TypeResolution.Defer;
}
```
*(`EntityCandidate.TypePrior` is non-null `IReadOnlyList<EntityType>` (default `[Type]`). Gate on the PRIMARY `TypePrior[0]`: the scanner appends a frequency floor `{Monster, Spell, Item, Class}`, so `Item` is always present and `.All(GatedTypes.Contains)` would never be true.)*
- [ ] **Step 4:** Run all `DeterministicTypeResolverTests`; reconcile any pre-existing test that called `Resolve` with the 2-arg signature (the new param is optional, so they compile unchanged). Verify green.
- [ ] **Step 5:** Commit (`feat(extraction): official-book allowlist gate in resolver`).

## Task 3: ExtractionDeclinedFile writer + DeclinedEntry

**Files:**
- Create: `Features/Ingestion/EntityExtraction/ExtractionDeclinedFile.cs`
- Test: `DndMcpAICsharpFun.Tests/Ingestion/EntityExtraction/ExtractionDeclinedFileTests.cs`

**Interfaces:**
- Produces: `record DeclinedEntry(string Id, string Name, EntityType Type, string Reason)`; `ExtractionDeclinedFile.WriteAsync(string path, IList<DeclinedEntry> declined, CancellationToken ct)`.

- [ ] **Step 1: Failing test** (mirror warnings-file behavior: writes JSON list; deletes file when empty):
```csharp
[Fact]
public async Task Writes_declined_list_and_deletes_when_empty()
{
    var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    var path = Path.Combine(dir, "phb14.declined.json");
    var sut = new ExtractionDeclinedFile();
    await sut.WriteAsync(path, new List<DeclinedEntry>{ new("phb14.class.rage","Rage",EntityType.Class,"no_5etools_match") }, default);
    File.Exists(path).Should().BeTrue();
    (await File.ReadAllTextAsync(path)).Should().Contain("no_5etools_match");
    await sut.WriteAsync(path, new List<DeclinedEntry>(), default);
    File.Exists(path).Should().BeFalse();
}
```
- [ ] **Step 2:** Run, fail. **Step 3:** Implement mirroring `ExtractionWarningsFile` (web JSON options, `WriteIndented = true`, delete on empty, `Directory.CreateDirectory`). **Step 4:** pass. **Step 5:** Commit (`feat(extraction): declined-records sibling writer`).

## Task 4: Orchestrator — pass isOfficial, collect + write declines

**Files:**
- Modify: `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs`
- Test: `DndMcpAICsharpFun.Tests/Entities/Extraction/EntityExtractionOrchestratorTests.cs`

**Interfaces:**
- Consumes: `IngestionRecord.FivetoolsSourceKey`; inject `ExtractionDeclinedFile declinedFile` (constructor param, mirror `errorsFile`/`warningsFile`).

**Notes from current code:** constructor takes `ExtractionErrorsFile errorsFile, ExtractionWarningsFile warningsFile`; paths built as `bookSlug + ".errors.json"` etc.; full-run loop ~L172 and errors-only loop ~L323 both call `DeterministicTypeResolver.Resolve(...)` via the `RecordedEntityId` helper / directly; writes via `errorsFile.WriteAsync(errorsPath, ...)`.

- [ ] **Step 1: Failing test** (orchestrator harness with a real official record `FivetoolsSourceKey="PHB"`; a converter yielding a gated noise candidate "Rage" (prior Class) that does NOT 5etools-match, plus a real matched candidate; assert: the LLM extractor is NOT called for "Rage"; "Rage" is absent from the canonical `entities`; `phb14.declined.json` contains a `no_5etools_match` record for it):
```csharp
[Fact]
public async Task Official_gated_noise_is_declined_not_extracted_and_recorded()
{
    // arrange: official record (FivetoolsSourceKey "PHB"); converter -> "Rage" (Class bookmark) + a real spell;
    //          real EntityNameIndex from TestPaths.RepoFile("5etools"); LLM mock records calls.
    // act: run full ExtractAsync
    // assert:
    //   llm.DidNotReceive() for "Rage"; canonical entities do not contain "Rage";
    //   <slug>.declined.json exists and contains {name:"Rage", reason:"no_5etools_match"}.
}
```
- [ ] **Step 2:** Run, fail.
- [ ] **Step 3: Implement**
  - Inject `ExtractionDeclinedFile declinedFile` into the orchestrator constructor.
  - Compute `bool isOfficial = !string.IsNullOrWhiteSpace(record.FivetoolsSourceKey);` once per run; pass it into BOTH `Resolve` call sites (full-run loop and errors-only loop — thread it through `RecordedEntityId` if that helper calls `Resolve`, by adding an `isOfficial` parameter to the helper).
  - Where the loop branches on outcome, add: on `DeterministicOutcome.Decline`, build `new DeclinedEntry(id, candidate.DisplayName, candidate.TypePrior.FirstOrDefault(), resolution.DeclineReason ?? "no_5etools_match")`, append to a per-run `declined` list, `continue` (no LLM, not added to `extracted`, not counted as failure). Use the raw id `EntityIdSlug.For(BookKey(record), candidate.TypePrior.FirstOrDefault(), candidate.DisplayName)` for the declined record id.
  - Add `var declinedPath = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".declined.json");` and `await declinedFile.WriteAsync(declinedPath, declined, ct);` at the end of the full run (alongside the errors/warnings writes ~L251).
  - For the errors-only loop: declines should not occur there (the retry set is errors only), but if `Resolve` returns Decline for a retried candidate, skip it (do not add to errors); leave the declined.json from the full run intact (do not overwrite in errors-only mode unless re-deriving).
- [ ] **Step 4:** Run the orchestrator test + full non-persistence suite; reconcile `BuildOrchestrator` helper + all construction sites to supply the new `ExtractionDeclinedFile` (default `new()`), mirroring how `errorsFile`/`warningsFile` are defaulted. Verify green.
- [ ] **Step 5:** Commit (`feat(extraction): orchestrator declines official gated non-matches`).

## Task 5: DI, build, docs

**Files:**
- Modify: `Extensions/ServiceCollectionExtensions.cs` (register `ExtractionDeclinedFile` if errors/warnings files are DI-registered; mirror them)
- Modify: `CLAUDE.md` (note the new `<book-slug>.declined.json` sibling under the extraction section)

- [ ] **Step 1:** Register `ExtractionDeclinedFile` the same way `ExtractionErrorsFile`/`ExtractionWarningsFile` are registered (grep to confirm their registration; mirror).
- [ ] **Step 2:** `dotnet build` — 0 warnings, 0 errors.
- [ ] **Step 3:** `dotnet test --filter "FullyQualifiedName!~Persistence"` — full suite green.
- [ ] **Step 4:** Add a one-line CLAUDE.md note (extraction section) on the `<book-slug>.declined.json` sibling (declined official gated non-matches, auditable, ignored by ingestion + errorsOnly). Lint markdown (`pnpm lint:md:fix` then `pnpm lint:md` → 0 errors). No `.http`/insomnia change (no endpoint change).
- [ ] **Step 5:** Commit (`feat(extraction): DI + docs for declined records`).

## Task 6: Live validation — PHB re-run (controller-run acceptance gate, not a code task)

Performed by the controller after the branch merges/while reviewing (mirrors the prior smoke):
- [ ] 6.1 Rebuild app image (`5etools/` mounted, qwen3 on GPU), re-extract PHB `force=true`.
- [ ] 6.2 Inspect first checkpoint + final canonical: Class ~12 (was 397), race stat-block fields gone, OCR garble gone; `phb14.declined.json` populated.
- [ ] 6.3 Confirm recall intact: Bard→Class, appendix animals→Monster, real spells present, clean names; spot-check `declined.json` for any wrongly-declined real entity.
- [ ] 6.4 Record before/after deltas; ready to archive (after `extraction-name-resolution`) + ingest.
