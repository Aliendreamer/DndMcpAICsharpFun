# extraction-name-resolution — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.
>
> **CRITICAL — Serena:** every implementer/reviewer MUST call `mcp__serena__initial_instructions` first and use Serena symbolic tools for ALL `.cs` reads/edits. Built-in Read/Edit on `.cs` forbidden. Grep-verify after every edit. Bash only for `dotnet`/`git`.

**Goal:** Fix the resolver recall leak by matching candidate headings against the local 5etools corpus (keep + clean canonical name + deterministic type), with an improved content-first fallback filter for non-matches — so `FIREBALL`/`ABOLETH`/`BARD` come back while `ACTIONS`/lair headings stay out.

**Architecture:** New `EntityNameIndex` (5etools name→canonical+type) + `EntityNameMatcher` (exact→fuzzy-with-threshold→null). `DeterministicTypeResolver` gains a first-priority 5etools-match step; `ExtractionSignatures.IsEntityLikeName` swaps its all-caps rule for a structural denylist + lair reject. Matched candidates use the canonical name; unmatched fall to the existing content-first union (never dropped wrongly).

**Tech Stack:** C# / .NET 10, System.Text.Json, xUnit + FluentAssertions, Serena.

## Global Constraints

- .NET 10, nullable, implicit usings, **warnings-as-errors** (0 warnings).
- TDD: failing test → minimal impl → green → commit. Commits per-task on a feature branch; pause for the user's go-ahead before each `git commit`.
- Namespaces follow folder: index/matcher/resolver in `DndMcpAICsharpFun.Features.Ingestion.EntityExtraction`.
- `EntityType` values: Background, Class, Condition, Feat, God, Item, MagicItem, Monster, Plane, Race, Spell, Trap.
- 5etools data is local at `5etools/`. Shapes: spell `{name,source,level,school}` in `spells/spells-*.json` (key `spell`); monster `{name,source,type,cr}` in `bestiary/bestiary-*.json` (key `monster`); item `{name,source,rarity,type}` in `items.json` (key `item`, magic — has rarity) + `items-base.json` (mundane); class in `class/class-*.json` (key `class`); `backgrounds.json` (key `background`); `races.json` (key `race`); `feats.json` (key `feat`); `conditionsdiseases.json` (key `condition`); `deities.json` (key `deity`).
- **Safety invariant:** a 5etools non-match NEVER drops a candidate — it falls to the content-first union.

---

### Task 1: 5etools → EntityType mapping

**Files:** Create `Features/Ingestion/EntityExtraction/FivetoolsEntityTypeMap.cs`; Test `DndMcpAICsharpFun.Tests/Ingestion/EntityExtraction/FivetoolsEntityTypeMapTests.cs`

**Produces:** `static class FivetoolsEntityTypeMap` with `EntityType ForItem(string? rarity)` and the per-file type constants used by Task 2.

- [ ] **Step 1: Failing test**
```csharp
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;
namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;
public sealed class FivetoolsEntityTypeMapTests
{
    [Theory]
    [InlineData("uncommon", EntityType.MagicItem)]
    [InlineData("legendary", EntityType.MagicItem)]
    [InlineData("rare", EntityType.MagicItem)]
    [InlineData("none", EntityType.Item)]
    [InlineData(null, EntityType.Item)]
    [InlineData("unknown", EntityType.Item)]
    public void ForItem_maps_rarity(string? rarity, EntityType expected) =>
        FivetoolsEntityTypeMap.ForItem(rarity).Should().Be(expected);
}
```
- [ ] **Step 2:** `dotnet test --filter FivetoolsEntityTypeMapTests` → FAIL.
- [ ] **Step 3:** implement:
```csharp
using DndMcpAICsharpFun.Domain.Entities;
namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
public static class FivetoolsEntityTypeMap
{
    private static readonly HashSet<string> MagicRarities =
        new(StringComparer.OrdinalIgnoreCase) { "common", "uncommon", "rare", "very rare", "legendary", "artifact" };
    public static EntityType ForItem(string? rarity) =>
        rarity is not null && MagicRarities.Contains(rarity) ? EntityType.MagicItem : EntityType.Item;
}
```
- [ ] **Step 4:** build 0 warnings; test PASS.
- [ ] **Step 5: Commit** (pause for go-ahead): `feat(extraction): 5etools item rarity -> EntityType map`.

---

### Task 2: EntityNameIndex (load 5etools → name→(canonical,type))

**Files:** Create `Features/Ingestion/EntityExtraction/EntityNameIndex.cs`; Test `DndMcpAICsharpFun.Tests/Ingestion/EntityExtraction/EntityNameIndexTests.cs`

**Consumes:** Task 1. **Produces:** `sealed class EntityNameIndex` with ctor `(string fivetoolsDir)`; `IReadOnlyDictionary<string,(string Canonical, EntityType Type)> Entries`; `static string Normalize(string name)` (lowercase, strip non-alphanumeric, collapse). Built once.

- [ ] **Step 1: Failing test** — point the index at the real `5etools/` dir (use `TestPaths.RepoFile("5etools")`), assert: `Normalize("FIREBALL")` is in `Entries` → ("Fireball", Spell); "ABOLETH" → ("Aboleth", Monster); "BARD" → ("Bard", Class); "BAG OF HOLDING" → ("Bag of Holding", MagicItem); and `Normalize("Spellcasting")`/`Normalize("Archery")` are NOT present (sub-features excluded).
- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3:** implement: in the ctor, for each top-level type, glob+load its file(s) and add `Normalize(name) → (name, EntityType)` (first-wins on collision; prefer keeping any existing). Types + files: spell `spells/spells-*.json`/`spell`→Spell; monster `bestiary/bestiary-*.json`/`monster`→Monster; item `items.json`/`item`→`FivetoolsEntityTypeMap.ForItem(rarity)` + `items-base.json`/`baseitem`→Item; class `class/class-*.json`/`class`→Class; `backgrounds.json`/`background`→Background; `races.json`/`race`→Race; `feats.json`/`feat`→Feat; `conditionsdiseases.json`/`condition`→Condition; `deities.json`/`deity`→God. Do NOT load optionalfeatures or subclass/feature files. `Normalize` = `new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray())`. Use `System.Text.Json` with a tolerant reader (skip entries missing `name`).
- [ ] **Step 4:** build 0 warnings; test PASS (index loads from the real 5etools data).
- [ ] **Step 5: Commit:** `feat(extraction): EntityNameIndex over the local 5etools corpus`.

---

### Task 3: EntityNameMatcher (exact + fuzzy threshold)

**Files:** Create `Features/Ingestion/EntityExtraction/EntityNameMatcher.cs`; Test `DndMcpAICsharpFun.Tests/Ingestion/EntityExtraction/EntityNameMatcherTests.cs`

**Consumes:** Task 2. **Produces:** `sealed class EntityNameMatcher(EntityNameIndex index)` with `(string Canonical, EntityType Type)? Match(string rawName)`.

- [ ] **Step 1: Failing tests** — build the matcher over the real index: `Match("FIREBALL")` → ("Fireball", Spell); `Match("MAGEARMOR")` → ("Mage Armor", Spell) (fuzzy, OCR-merged); `Match("ACTIONS")` → null; `Match("A RED DRAGON'S LAIR")` → null; `Match("Zxqwv Nonsense")` → null (below threshold, no wrong neighbour).
- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3:** implement: `Match` = `EntityNameIndex.Normalize(raw)`; exact dictionary hit → return it. Else fuzzy: compute normalized **Levenshtein ratio** (`1 - dist/maxLen`) against index keys; accept the best **only if ratio ≥ 0.90 AND the length difference is small** (so `magearmor`↔`magearmor`(from "Mage Armor") is exact after normalization actually — note "Mage Armor" normalizes to "magearmor", so this is an EXACT hit, not fuzzy; keep the fuzzy path for true OCR typos like one-char errors). Implement a private static Levenshtein. To bound cost, only fuzzy-compare against keys whose length is within ±2 of the query. Return null below threshold.
  - NOTE for the implementer: confirm "Mage Armor" → `Normalize` = "magearmor" == `Normalize("MAGEARMOR")` → this is actually an EXACT match (the normalization removes the space). Verify in the test; the fuzzy path then only handles genuine 1–2 char OCR errors. Keep both paths.
- [ ] **Step 4:** build 0 warnings; tests PASS.
- [ ] **Step 5: Commit:** `feat(extraction): EntityNameMatcher (exact + bounded fuzzy, no-match->null)`.

---

### Task 4: Fix IsEntityLikeName (denylist + lair, drop all-caps rule)

**Files:** Modify `Features/Ingestion/EntityExtraction/ExtractionSignatures.cs`; Test: extend `DndMcpAICsharpFun.Tests/Ingestion/EntityExtraction/ExtractionSignaturesTests.cs`

- [ ] **Step 1: Failing tests** — add to `IsEntityLikeName` theory: `FIREBALL`→true, `ABOLETH`→true, `BARD`→true, `LION`→true (all-caps single-word entities now kept); `ACTIONS`→false, `REACTIONS`→false, `LEGENDARY ACTIONS`→false, `A RED DRAGON'S LAIR`→false; keep existing (`Step 2…`, `Challenge 7…`, `Appendix…`, `Creating…`, `Monster Features`→false).
- [ ] **Step 2:** run → FAIL (FIREBALL currently returns false).
- [ ] **Step 3:** in `ExtractionSignatures`: DELETE the line `if (n.Length >= 4 && n == n.ToUpperInvariant() && !n.Contains(' ')) return false;`. Add a structural-sub-header denylist check and a lair check:
```csharp
private static readonly HashSet<string> StructuralHeaders = new(StringComparer.OrdinalIgnoreCase)
{ "ACTIONS", "REACTIONS", "TRAITS", "BONUS ACTIONS", "LEGENDARY ACTIONS", "LAIR ACTIONS", "REGIONAL EFFECTS" };
private static readonly Regex LairHeading = new(@"\blair\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
```
In `IsEntityLikeName`, after the existing checks (Step/Challenge/SectionHeading/Appendix), add:
```csharp
if (StructuralHeaders.Contains(n)) return false;
if (LairHeading.IsMatch(n) && n.StartsWith("A ", StringComparison.OrdinalIgnoreCase)) return false;
```
(The `A …lair…` guard targets "A RED DRAGON'S LAIR" while leaving a hypothetical real entity merely containing "lair" alone unless it's an "A …'s LAIR" heading.)
- [ ] **Step 4:** build 0 warnings; run `ExtractionSignaturesTests` + `DeterministicTypeResolverTests` — reconcile any that asserted the old all-caps behaviour (intended change). All PASS.
- [ ] **Step 5: Commit:** `fix(extraction): IsEntityLikeName denylist+lair, drop the all-caps over-reject`.

---

### Task 5: DeterministicTypeResolver — 5etools match as step 1

**Files:** Modify `Features/Ingestion/EntityExtraction/DeterministicTypeResolver.cs`; Test: extend `DeterministicTypeResolverTests`

**Consumes:** Task 3 (`EntityNameMatcher`). **Produces:** `TypeResolution` carries an optional `CanonicalName`; `Resolve(EntityCandidate, EntityNameMatcher)` (matcher passed in — keep `DeterministicTypeResolver` static).

- [ ] **Step 1: Failing tests** — `Resolve(C("FIREBALL", "...spell text..."), matcher)` → Outcome ForceType, ForcedType Spell, CanonicalName "Fireball"; `Resolve(C("ACTIONS", "stat-block-ish text"), matcher)` → Drop (no 5etools match, not entity-like); an unmatched entity-like ordinary candidate → Defer.
- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3:** extend `TypeResolution` with `string? CanonicalName` (default null); `Force(EntityType, string? canonicalName = null)`. Change `Resolve` signature to `Resolve(EntityCandidate candidate, EntityNameMatcher matcher)`. Add as the FIRST step:
```csharp
var match = matcher.Match(candidate.DisplayName);
if (match is { } m) return TypeResolution.Force(m.Type, m.Canonical);
```
then the existing ladder (IsEntityLikeName drop → Monster → MagicItem → Defer) unchanged.
- [ ] **Step 4:** build 0 warnings; tests PASS.
- [ ] **Step 5: Commit:** `feat(extraction): DeterministicTypeResolver 5etools match step 1 (+canonical name)`.

---

### Task 6: Wire matcher + canonical name into the orchestrator + DI

**Files:** Modify `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs`, `Extensions/ServiceCollectionExtensions.cs`; Test: extend `EntityExtractionOrchestratorTests`

**Consumes:** Tasks 2–5. The orchestrator calls `DeterministicTypeResolver.Resolve(c, matcher)` (inject `EntityNameMatcher`), and for the drop-filter at candidate build uses the same matcher-aware resolve. When a candidate is force-typed with a `CanonicalName`, the entity `Name` and `EntityIdSlug` use the canonical name (not the raw heading).

- [ ] **Step 1: Failing test** — orchestrator harness (follow the existing pattern): a candidate named "FIREBALL" under a Spells section, with a Spell schema present and `EntityNameMatcher` wired to the real index → the written canonical has an entity named "Fireball" typed Spell (canonical name used). A candidate "ACTIONS" → dropped (no entity).
- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3:** add `EntityNameMatcher matcher` to the orchestrator constructor. At candidate build, the drop filter becomes `candidates.Where(c => DeterministicTypeResolver.Resolve(c, matcher).Outcome != DeterministicOutcome.Drop)`. In `ExtractOneAsync`, `var resolution = DeterministicTypeResolver.Resolve(candidate, matcher);` and in the ForceType branch, if `resolution.CanonicalName is { } cn`, use `cn` for `displayName`/`id` (compute `id = EntityIdSlug.For(BookKey(record), resolution.ForcedType, cn)` and the envelope Name = NormalizeDisplayName(cn)). Register `services.AddSingleton<EntityNameIndex>(sp => new EntityNameIndex(<5etools path from config/default "5etools">)); services.AddSingleton<EntityNameMatcher>();` in `ServiceCollectionExtensions`.
- [ ] **Step 4:** `dotnet build` 0 warnings; full non-persistence suite `dotnet test --filter "FullyQualifiedName!~Persistence"` green (reconcile intended-change tests).
- [ ] **Step 5: Commit:** `feat(extraction): orchestrator uses 5etools matcher (canonical name + forced type)`.

---

### Task 7: Live validation — re-run all 4 books (operator task, main session)

- [ ] **Step 1:** Rebuild app image; recreate app; qwen3 on GPU; confirm the 5etools index loads at startup (log line / no error).
- [ ] **Step 2:** Re-extract MM, PHB, DMG, and the **4th book** (confirm with user: Tasha `tce` or SRD) with `?force=true`.
- [ ] **Step 3:** Validate vs the recall losses: Fireball→Spell, Aboleth→Monster, Bard→Class, Lion→Monster, Counterspell→Spell present + correctly typed; names clean (Mage Armor not MAGEARMOR); precision holds (no ACTIONS/REACTIONS/lair Monsters); accepted spell/class counts ≥ the prior playerhandbook-2014 run (recall recovered).
- [ ] **Step 4:** Record before/after deltas in the change; ready to archive.

---

## Self-Review

- **Spec coverage:** entity-name-resolution {index → T2; type map → T1; matcher exact/fuzzy/no-match → T3} ✓. deterministic-type-resolution {IsEntityLikeName denylist+lair, all-caps kept → T4; 5etools step-1 + canonical name + precedes-drop → T5; orchestrator canonical-name use → T6} ✓. Live recall/precision validation → T7. Every spec scenario maps to a test.
- **Placeholder scan:** none — code + test cases concrete. (T3 flags the "Mage Armor" normalization subtlety explicitly: it's an exact match post-normalization; the fuzzy path covers true 1–2 char OCR typos.)
- **Type consistency:** `EntityNameIndex.Normalize`/`Entries`, `EntityNameMatcher.Match`→`(Canonical,Type)?`, `TypeResolution.CanonicalName`/`Force(type,canonical)`, `Resolve(candidate, matcher)` — consistent across T2–T6. `FivetoolsEntityTypeMap.ForItem` (T1) used in T2.
- **Open item:** the 4th book for T7 needs the user's pick (Tasha vs SRD) before the re-run.
