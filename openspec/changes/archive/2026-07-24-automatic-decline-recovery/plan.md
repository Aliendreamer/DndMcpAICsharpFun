# Automatic Decline-Recovery — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** After the main extraction loop (official books), automatically re-classify the still-declined candidates with a RECOVERY-framed `[Rule,Lore]∪none` union call, admitting the grounded ones as `Rule`/`Lore` entities.

**Architecture:** A `DeclineRecovery` service reuses the extraction call with a recovery system prompt + prior `[Rule,Lore]`; `EntityExtractionOrchestrator` collects the declined candidates during the main loop and runs recovery after it (before ref-resolution + the final write), appending recovered entities and reconciling the decline audit. Anti-fabrication preserved — the grounding cascade still gates every admission.

**Tech Stack:** C# / .NET 10, xunit + FluentAssertions. Serena for all `.cs`. THIS IS THE HIGH-RISK EXTRACTION PATH — precise, conservative edits.

## Global Constraints
- **Serena only** for `.cs`; grep-verify after each edit. Built-in Read/Edit on `.cs` forbidden.
- **Work on `main`**; commit each task after review.
- Warnings-as-errors → build 0/0. `dotnet` needs `dangerouslyDisableSandbox: true`. Ignore LSP false CS0246 on tests.
- No HTTP endpoint change (recovery runs inside `extract-entities`). No change to the main extraction gate or `GatedTypes`.
- **STOP before the DMG re-extract** (Task 4) — validated live only on explicit go (it's ~part of a re-extract).

**Known shapes (verified):**
```csharp
// Lore is FULLY wired: Schemas/canonical/LoreFields.schema.json exists (globbed), LoreCanonicalTextRenderer registered, Lore NOT gated. No wiring work.
// EntityType.Rule likewise fully wired.
// Orchestrator RunFullExtractionAsync main loop (lines 164-216):
//   declineRes = DeterministicTypeResolver.Resolve(...); if Decline: rescue(#2/#3) else declined.Add(DeclinedEntry) + continue.   // deterministic decline
//   (envelope, error) = await runner.ExtractOneAsync(record, candidate, id, sourceBook, edition, schemas, ct, isOfficial);
//   if (error is not null) extractionErrors.Add(error);   // LLM none -> error with ErrorKind "extraction_declined"
//   else extracted.Add(envelope!);
// After loop: ref-resolution (218), canonicalFile built from `extracted` (249-257), writes (259-262), MarkEntitiesExtracted (265).
// CandidateExtractor.ExtractUnionAsync(record, candidate, IReadOnlyList<EntityType> prior, schemas, ct) — builds union from prior, uses promptBuilder.BuildUnionSystemPrompt internally.
// EntityExtractionRunner.ExtractOneAsync(record, candidate, id, sourceBook, edition, schemas, ct, isOfficial) -> (EntityEnvelope?, ExtractionErrorEntry?).
// ExtractionErrorEntry(SourceEntityId, FieldPath, MissingTargetId, ErrorKind, Detail).
// isOfficial = !string.IsNullOrWhiteSpace(record.FivetoolsSourceKey).
```

---

## Task 1: Recovery prompt + thread a system-prompt override through the extraction call

**Files:**
- Modify: `Features/Ingestion/EntityExtraction/ExtractionPromptBuilder.cs` (add `BuildRecoverySystemPrompt`)
- Modify: `Features/Ingestion/EntityExtraction/CandidateExtractor.cs` (`ExtractUnionAsync` optional `systemPromptOverride`)
- Modify: `Features/Ingestion/EntityExtraction/EntityExtractionRunner.cs` (`ExtractOneAsync` optional `systemPromptOverride`, threaded to `ExtractUnionAsync`)
- Test: `DndMcpAICsharpFun.Tests/Entities/Extraction/` (prompt + override plumbing)

**Interfaces:**
- `ExtractionPromptBuilder.BuildRecoverySystemPrompt(string displayName, DndVersion version) : string` — the recovery framing.
- `CandidateExtractor.ExtractUnionAsync(..., string? systemPromptOverride = null)` — uses the override instead of `BuildUnionSystemPrompt` when non-null (backward-compatible default).
- `EntityExtractionRunner.ExtractOneAsync(..., string? systemPromptOverride = null)` — threads to `ExtractUnionAsync`.

- [ ] **Step 1: Write failing tests** — read the existing prompt/extractor tests via Serena for style. Assert `BuildRecoverySystemPrompt` returns a non-empty string containing the recovery framing (e.g. mentions the book, "real"/"official", "Rule", "Lore"); assert `ExtractUnionAsync`/`ExtractOneAsync` accept the new optional param (compile-level + a fake-IChatClient test that the override prompt is the one passed, if the existing tests have that harness — otherwise a plumbing/no-regression test).

- [ ] **Step 2: Run → FAIL** (methods/params missing).

- [ ] **Step 3: Add `BuildRecoverySystemPrompt`** (Serena `insert_after_symbol` after `BuildUnionSystemPrompt`):
```csharp
    /// <summary>
    /// System prompt for the decline-recovery pass: the content is real, official book text (NOT
    /// fabricated); classify it as a Rule (mechanical) or Lore (worldbuilding/setting), or decline
    /// via entityType:none ONLY for a pure heading / table-of-contents / fragment. Recovery framing —
    /// distinct from the entity-hunting union prompt that over-declines real rules.
    /// </summary>
    public string BuildRecoverySystemPrompt(string displayName, DndVersion version) =>
        $"You are recovering real, official content from {displayName} ({VersionText(version)}). " +
        "This text is genuine published D&D material — it is NOT fabricated. Classify it as a " +
        "\"Rule\" (a mechanical rule, procedure, or option) or \"Lore\" (worldbuilding, setting, " +
        "cosmology, or narrative flavor). Choose entityType:none ONLY if it is a pure chapter heading, " +
        "table-of-contents entry, or a truncated fragment with no real content. Do not invent details " +
        "not present in the text.";
```
(Reuse the existing version-text helper the class already uses in `BuildUnionSystemPrompt` — grep for it; the exact helper name may differ.)

- [ ] **Step 4: Thread the override** — Serena `replace_symbol_body` on `ExtractUnionAsync` and `ExtractOneAsync`. In `ExtractUnionAsync`, add the trailing optional param and replace the `BuildUnionSystemPrompt(...)` call with `systemPromptOverride ?? promptBuilder.BuildUnionSystemPrompt(record.DisplayName, record.Version)`. In `ExtractOneAsync`, add the trailing optional param and pass it into the `ExtractUnionAsync(...)` call. Leave all other behavior identical (grep-verify the only change is the prompt source + the new params).

- [ ] **Step 5: Tests pass; `dotnet build` 0/0.** Format touched files.
- [ ] **Step 6: Commit:** `feat(extraction): recovery system prompt + system-prompt override plumbing`

---

## Task 2: `DeclineRecovery` service

**Files:**
- Create: `Features/Ingestion/EntityExtraction/DeclineRecovery.cs`
- Test: `DndMcpAICsharpFun.Tests/Entities/Extraction/DeclineRecoveryTests.cs`

**Interfaces:**
- `sealed class DeclineRecovery(EntityExtractionRunner runner)` with `Task<EntityEnvelope?> TryRecoverAsync(IngestionRecord record, EntityCandidate candidate, string sourceBook, string edition, IReadOnlyDictionary<EntityType,JsonElement> schemas, CancellationToken ct)`:
  - Rebind `candidate with { TypePrior = new[] { EntityType.Rule, EntityType.Lore } }`.
  - Compute id via `ExtractionEntityIds.RecordedEntityId(record, rebound, matcher, isOfficial:true)` (or reuse the candidate's stable id — match the orchestrator's id scheme).
  - `var (env, err) = await runner.ExtractOneAsync(record, rebound, id, sourceBook, edition, schemas, ct, isOfficial:true, systemPromptOverride: promptBuilder.BuildRecoverySystemPrompt(record.DisplayName, record.Version))`.
  - If `env is not null` (Rule/Lore pick that grounded → the runner already applied the grounding cascade + disposition), return `env with { … dataSource "decline-recovery" … }` (set via the envelope's dataSource field — grep how other paths set `dataSource`, e.g. `"5etools-backfill"`). Else return null (stays declined).
  - The runner needs a `promptBuilder`; inject `ExtractionPromptBuilder` into `DeclineRecovery` (or expose the recovery prompt through the runner). Keep DI consistent with how the orchestrator constructs these (check `AddEntityExtraction`/the orchestrator ctor).

- [ ] **Step 1: Write failing tests** — with a fake `IChatClient` (mirror existing extractor tests): a rule-shaped candidate whose recovery call returns Rule + grounds → `TryRecoverAsync` returns a Rule envelope with `dataSource:"decline-recovery"`; a heading/fragment whose recovery returns none → null; an ungrounded Rule pick → null.

- [ ] **Step 2: Run → FAIL** (type missing).
- [ ] **Step 3: Create `DeclineRecovery.cs`** per the interface.
- [ ] **Step 4: Tests pass; `dotnet build` 0/0.** Format.
- [ ] **Step 5: Commit:** `feat(extraction): DeclineRecovery service (recovery-framed Rule/Lore admission)`

---

## Task 3: Wire the automatic recovery phase into the orchestrator

**Files:**
- Modify: `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs`
- Test: `DndMcpAICsharpFun.Tests/Entities/Extraction/` (orchestration behavior — a fake runner)

**Interfaces:**
- During the main loop, collect declined candidates: at the deterministic-decline branch (before `continue`) add `candidate` to a `List<EntityCandidate> declinedCandidates`; at the LLM-error branch, if `error.ErrorKind == "extraction_declined"` add `candidate`. (Both kinds.)
- After the loop, BEFORE ref-resolution (line ~218), for **official books only** (`isOfficial`): for each `c in declinedCandidates`, `var rec = await declineRecovery.TryRecoverAsync(record, c, sourceBook, edition, schemas, ct)`; if `rec is not null`: `extracted.Add(rec)` AND remove the matching entry from `declined` (by id) or from `extractionErrors` (by SourceEntityId + ErrorKind=="extraction_declined"). Log the recovered count.
- `DeclineRecovery` is injected into the orchestrator (add to its ctor + `AddEntityExtraction` DI + the `FullContainerScopeValidationTests` replica — the DI discipline from dev-flow).

- [ ] **Step 1: Write failing test** — a fake runner where a specific declined candidate's recovery call returns a Rule envelope: assert it ends up in the written canonical's entities AND is removed from the decline audit; a non-official book → recovery not invoked (declines unchanged). Mirror the existing orchestrator tests' harness (find them via Serena).

- [ ] **Step 2: Run → FAIL.**
- [ ] **Step 3: Implement** — Serena edits: add `declinedCandidates` collection at both decline points; add the post-loop recovery block (official-only) before ref-resolution; add `DeclineRecovery` to the ctor + DI (`AddEntityExtraction` in Program AND the `FullContainerScopeValidationTests.BuildServiceCollection` replica). Grep-verify the recovery block sits before `refResolver.Resolve(extracted)` so recovered entities are ref-checked + written.

- [ ] **Step 4: Whole-solution build 0/0; FULL `dotnet test` green (incl. container-scope validation).** Format.
- [ ] **Step 5: Commit:** `feat(extraction): automatic decline-recovery phase (official books, inline)`

---

## Task 4: Validate on DMG (deferred to explicit go)
- [ ] **Step 1: STOP.** The live proof is a DMG re-extract (the recovery runs automatically). Run ONLY on explicit user go (needs the stack + a re-extract). On go: rebuild the app image, re-extract DMG, report recovered Rule/Lore vs skip counts, spot-check that recovered entities ground (`psychic-wind-effects`→Rule, a pantheon→Lore), no fabrication, main entities unchanged; re-run `ProjectTables dmg14` after to restore the 5etools tables. If Lore over-admits narrative, tighten `BuildRecoverySystemPrompt` or restrict Phase 1 to Rule and re-run.

## Task 5: Gates
- [ ] **Step 1:** `dotnet build` 0/0; FULL `dotnet test` green; `dotnet format --include <touched files>` clean; `git diff --stat` confined to the extraction files + tests; `.http`/insomnia untouched.

---

## Self-Review notes
- Spec "recover Rule/Lore, grounded, official-only, automatic, noise stays declined" → Tasks 2 (service) + 3 (wiring) + the grounding cascade (reused via the runner). "audit reconciliation" → Task 3 removal logic. "no endpoint / main gate unchanged" → recovery is a post-loop additive phase.
- Anti-fabrication preserved: recovery reuses `runner.ExtractOneAsync` → the SAME grounding cascade + disposition; only the prior (`[Rule,Lore]`) and system prompt differ.
- Lore already wired (Task 1 verify only). `GatedTypes` untouched. DI discipline (Program + scope-test replica) per dev-flow.
- Live DMG validation explicitly deferred (Task 4).
