# Extraction Content Classification — Phase 1 (Rule) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Admit decline-bound rule content as a first-class `EntityType.Rule` entity by offering the (already-wired) Rule union branch ONLY to would-be-declined candidates that carry a rule signature — never to real entities.

**Architecture:** The Rule pipeline already exists end-to-end (schema `Schemas/canonical/RuleFields.schema.json` globbed into `schemas[Rule]`; `ExtractionPromptBuilder` `case Rule`; `RuleCanonicalTextRenderer` registered). Rule is simply never in a candidate's `TypePrior`, so it is never offered. The change is 3 edits: a `RuleSignature` predicate, and — at the orchestrator's two decline points — rebind a decline-bound + rule-signature candidate to `TypePrior = [EntityType.Rule]` and fall through to the normal accept path (a `TypePrior` swap; the runner re-resolves → `Defer` (Rule non-gated) → union offers Rule-or-none → a Rule pick → `Accepted`/`canon-unindexed` envelope; `canonicalText` filled at ingest by the existing renderer).

**Tech Stack:** C# / .NET 10, xunit + FluentAssertions. Serena MCP for all `.cs`.

## Global Constraints

- **Serena only** for `.cs`; grep-verify after each edit (REAL newlines; attributes survive). Built-in Read/Edit on `.cs` forbidden.
- **Work on `main`**; commit each task after the reviewer passes.
- Warnings-as-errors → `dotnet build` 0/0. `dotnet` needs `dangerouslyDisableSandbox: true` (git-crypt).
- Ignore LSP false CS0246/CS1061 on test files; trust `dotnet build`/test.
- No HTTP endpoint change. Reuse the existing `RuleFields.schema.json` (`{ruleType?, entries}`) — NO new schema/prompt/renderer.
- **`GatedTypes` must NOT gain `Rule`** — Rule must stay non-gated so the re-resolve takes `Defer`, not `Decline` again.
- **STOP before the live re-extract** (Task 3) — it runs only on explicit user go.

**Key shapes (verbatim, from the hook map):**
```csharp
public enum DeterministicOutcome { Drop, ForceType, Defer, Decline }
public sealed record EntityCandidate(EntityType Type, string DisplayName, string Text, int? Page, IReadOnlyList<EntityType> TypePrior = null!) { public IReadOnlyList<EntityType> TypePrior { get; init; } = TypePrior ?? [Type]; }
// Orchestrator decline (RunFullExtractionAsync ~171-176):
//   var declineRes = DeterministicTypeResolver.Resolve(candidate, _matcher, isOfficial);
//   if (declineRes.Outcome == DeterministicOutcome.Decline) { declined.Add(new DeclinedEntry(...)); continue; }
//   var id = ExtractionEntityIds.RecordedEntityId(record, candidate, _matcher, isOfficial);
//   var (envelope, error) = await runner.ExtractOneAsync(record, candidate, id, sourceBook, edition, schemas, ct, isOfficial);
// Errors-only (RunErrorsOnlyAsync ~335-336):
//   if (DeterministicTypeResolver.Resolve(candidate, _matcher, isOfficial).Outcome == DeterministicOutcome.Decline) continue;
// ExtractionSignatures: IsEntityLikeName(string?), IsRealEntity(EntityCandidate) at ~lines 38-93.
```

---

## Task 1: `RuleSignature` predicate + orchestrator rescue

**Files:**
- Modify: `Features/Ingestion/EntityExtraction/ExtractionSignatures.cs` (add `RuleSignature`)
- Modify: `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs` (rescue at both decline points, via one private helper)
- Test: `DndMcpAICsharpFun.Tests/Entities/Extraction/` — a new `RuleRescueTests.cs` (or extend an existing extraction test file)

**Interfaces:**
- Produces:
  - `ExtractionSignatures.RuleSignature(EntityCandidate candidate) : bool` — TRUE when the candidate has substantial prose (name-shape/fragment already filtered upstream by `EntityCandidateBuilder`'s Drop-filter, so this is essentially a length/substance threshold, e.g. `candidate.Text` trimmed length ≥ a constant like 200 chars). Start permissive.
  - A private orchestrator helper `EntityCandidate? RescueAsRuleOrNull(EntityCandidate candidate, DeterministicOutcome outcome)` returning `candidate with { TypePrior = [EntityType.Rule] }` when `outcome == Decline && ExtractionSignatures.RuleSignature(candidate)`, else `null`.

- [ ] **Step 1: Write the failing tests.** Read the existing extraction tests (e.g. `EntityExtractionRunnerDeclineTests` / `ExtractionSignaturesTests` if present) via Serena to mirror the candidate-construction helpers. Assert on `RuleSignature` + the rescue helper (test the pure logic, not a live LLM call):

```csharp
// RuleSignature: substantial-prose decline-bound candidate → true; a short fragment → false.
[Fact] public void RuleSignature_true_for_substantial_prose()
{
    var c = new EntityCandidate(EntityType.Background, "Chase Complications",
        new string('x', 400), 100); // >= threshold prose
    ExtractionSignatures.RuleSignature(c).Should().BeTrue();
}
[Fact] public void RuleSignature_false_for_short_fragment()
{
    var c = new EntityCandidate(EntityType.Background, "Blah", "too short", 1);
    ExtractionSignatures.RuleSignature(c).Should().BeFalse();
}
```
Plus a test for the rescue helper behavior: a decline-bound candidate with a rule signature → the helper returns a candidate whose `TypePrior` is exactly `[EntityType.Rule]`; a `Defer`/`ForceType` outcome → helper returns null (no rescue). If the helper is private, test the observable effect via the orchestrator (see Task 2's harness) OR make the helper `internal` + `[assembly:InternalsVisibleTo]` if the test project already uses that pattern (check first; prefer testing `RuleSignature` publicly + the rescue via the Task-2 harness if internal wiring isn't already present).

- [ ] **Step 2: Run → confirm FAIL** (`RuleSignature` doesn't exist). `dotnet test --filter FullyQualifiedName~RuleRescue` (or your test name), dangerouslyDisableSandbox.

- [ ] **Step 3: Add `RuleSignature`** — Serena `insert_after_symbol` near `IsRealEntity` in `ExtractionSignatures.cs`:

```csharp
    /// <summary>
    /// A decline-bound candidate that is worth rescuing as a Rule: it has substantial prose.
    /// Name-shape / fragment / TOC filtering already happened upstream (EntityCandidateBuilder's
    /// Drop-filter via IsEntityLikeName), so by the time a candidate reaches the orchestrator's
    /// decline branch it is not a bare heading — this only adds a prose-substance floor so a thin
    /// declined stub does not become a Rule. Start permissive; the LLM's Rule-vs-none pick is the
    /// real gate (extraction-content-classification, Phase 1).
    /// </summary>
    public static bool RuleSignature(EntityCandidate candidate) =>
        (candidate.Text?.Trim().Length ?? 0) >= 200;
```

- [ ] **Step 4: Add the rescue at both decline points** — Serena edits in `EntityExtractionOrchestrator.cs`. Add a private helper and use it at `RunFullExtractionAsync` (the `declineRes.Outcome == Decline` block) and `RunErrorsOnlyAsync` (the silent-skip decline check):

```csharp
    // Rescue a would-be-declined candidate that reads as a rule: re-offer it as Rule-or-none
    // (TypePrior swapped to [Rule] only — the failed gated type is NOT re-offered, so the LLM
    // cannot fabricate an ungrounded canon entity). Returns the rebound candidate, or null to decline.
    private static EntityCandidate? RescueAsRuleOrNull(EntityCandidate candidate, DeterministicOutcome outcome) =>
        outcome == DeterministicOutcome.Decline && ExtractionSignatures.RuleSignature(candidate)
            ? candidate with { TypePrior = new[] { EntityType.Rule } }
            : null;
```
`RunFullExtractionAsync` decline block becomes:
```csharp
        var declineRes = DeterministicTypeResolver.Resolve(candidate, _matcher, isOfficial);
        if (declineRes.Outcome == DeterministicOutcome.Decline)
        {
            var rescued = RescueAsRuleOrNull(candidate, declineRes.Outcome);
            if (rescued is null)
            {
                var rawId = EntityIdSlug.For(ExtractionEntityIds.BookKey(record), candidate.TypePrior.FirstOrDefault(), candidate.DisplayName);
                declined.Add(new DeclinedEntry(rawId, candidate.DisplayName, candidate.TypePrior.FirstOrDefault(), declineRes.DeclineReason ?? "no_5etools_match"));
                continue;
            }
            candidate = rescued;
        }
        // …unchanged: id computation + runner.ExtractOneAsync(record, candidate, …)
```
`RunErrorsOnlyAsync` silent-skip becomes:
```csharp
        var errDecline = DeterministicTypeResolver.Resolve(candidate, _matcher, isOfficial);
        if (errDecline.Outcome == DeterministicOutcome.Decline)
        {
            var rescued = RescueAsRuleOrNull(candidate, errDecline.Outcome);
            if (rescued is null) continue;
            candidate = rescued;
        }
        // …unchanged: id computation + runner.ExtractOneAsync(…)
```
(Grep-verify both edits landed correctly; the `candidate` local must be reassigned so the downstream `runner.ExtractOneAsync` uses the rebound candidate.)

- [ ] **Step 5: Run tests → pass; whole-solution `dotnet build` → 0/0.** `dotnet format` the touched files.
- [ ] **Step 6: Commit:** `feat(extraction): rescue decline-bound rule content as Rule entities (Phase 1)`

---

## Task 2: Cheap deterministic-path harness (NO GPU/LLM)

**Files:**
- Test: `DndMcpAICsharpFun.Tests/Entities/Extraction/RuleRescueHarnessTests.cs`

**Goal:** prove the GATE on REAL data without any model call — the rescue targets ONLY the decline pile and never a real entity.

- [ ] **Step 1:** Read how the existing extraction tests load real candidates from the conversion cache (`books/conversion-cache/*.mineru.json`) — `EntityCandidateBuilder` + `EntityNameIndex(TestPaths.RepoFile("5etools"))`. Mirror that to build the real DMG candidate list (DMG is rules-heavy; pick its conversion-cache file — find it via the book hash, or reuse a smaller book if DMG's cache is unwieldy; PHB works too).
- [ ] **Step 2:** For each candidate, compute `DeterministicTypeResolver.Resolve(c, matcher, isOfficial:true).Outcome`. Assert:
  - Of the candidates that resolve to `Decline`, a non-zero number have `RuleSignature == true` (the rescue pile is non-empty — rules ARE being recovered).
  - EVERY candidate that resolves to `ForceType` (a real entity) has `RescueAsRuleOrNull(...) == null` — a real entity is NEVER rescued as Rule (anti-flooding evidence). (If the helper is private, replicate its one-line condition in the test: `outcome==Decline && RuleSignature(c)`.)
  - Log the counts (declines, rescued-as-rule, forced-entities) so the decline-rate drop is visible.
- [ ] **Step 3:** `dotnet build` 0/0; the harness test passes and prints the counts. Commit: `test(extraction): deterministic harness proves Rule rescue targets only the decline pile`.

---

## Task 3: Gates + STOP before live re-extract

- [ ] **Step 1:** Whole-solution `dotnet build` 0/0; FULL `dotnet test` green (note count); `dotnet format --include <touched files>` clean. `git diff --stat` = only `ExtractionSignatures.cs`, `EntityExtractionOrchestrator.cs`, the two test files. `.http`/insomnia untouched.
- [ ] **Step 2: STOP — do NOT run the full re-extract.** The true quality proof (Rule 0→N on a real book, entities unchanged, no flooding, entity-type-distribution diff, spot-check the Rule entities) needs the stack + ~8h and runs ONLY on explicit user go. Report that the code + deterministic harness are green and the live re-extract is the remaining gate awaiting the user's go. Do NOT ingest Rule entities until it passes.

---

## Self-Review notes
- Spec "decline-bound + rule-signature → Rule offered" → Task 1 (rescue) + Task 2 (harness proof). "real entity never offered Rule" → Task 2 assertion. "true noise still declined" → the upstream Drop-filter (untouched) + `RuleSignature`'s prose floor. "shares ContentCategory.Rule taxonomy" → uses `EntityType.Rule` which maps 1:1.
- `GatedTypes` untouched (Rule stays non-gated). No schema/prompt/renderer change (reuse existing Rule wiring). `candidate.Type` untouched (id identity stable) — only `TypePrior` swapped.
- Live re-extract explicitly deferred (Task 3 Step 2).
