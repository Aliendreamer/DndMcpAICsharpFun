# Override Trim Rescue Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Printed lines a `canonicalText` override removes from its entity, and that no other entity holds, are kept in a stable, flagged Lore "trimmed text" entity.

**Architecture:** A pure static helper `TrimRescue` computes rescue entities from (base text, final entities); `OverrideMerger.Apply` calls it after its main loop and runs the rescue entities through the same per-entity override/approval/decline handling. Reapply drops and recomputes earlier rescues.

**Tech Stack:** C# / .NET 10, xUnit + FluentAssertions (`DndMcpAICsharpFun.Tests`), System.Text.Json.

**Spec:** `openspec/changes/override-trim-rescue/` (proposal.md, design.md, specs/override-trim-rescue/spec.md)

## Global Constraints

- Removed line: a line of the base text whose reduced form (lower-case, only `[a-z0-9]`) has 30+ characters, is not a substring of the reduced final `canonicalText` of ANY entity of the merged book (host included; Declined excluded; rescue entities excluded), AND is not a typo-fixed line: it is treated as kept (the override only corrected it) when ONE line of the host's final `canonicalText` contains at least 70% of this line's words of 3+ characters (lower-case `[a-z0-9]+` tokens, multiset).
- Base text: `Apply` → the extracted entity's own `canonicalText` before its overrides; `Reapply` → the `Extracted` of the host's `canonicalText` override when it is a JSON string. Only hosts with a `canonicalText` override are considered.
- Rescue id: host id with its second dot-segment (the type) replaced by `lore`, plus `-trimmed` (`mm14.monster.adult-blue-dracolich` → `mm14.lore.adult-blue-dracolich-trimmed`); if an extracted entity already has that id, append `-2`.
- Rescue entity: Type `Lore`, `DataSource = DataSources.Programmatic`, host's SourceBook/Edition/Page/FirstAppearedIn/RevisedIn/SettingTags/Provenance; `Fields = {"entries":[...]}`; `CanonicalText = Name + "\n" + string.Join("\n", entries)`; Name = the short heading line (reduced length < 30, no terminal `.`) directly above the first rescued line when the base text has one, else `"<host name> (trimmed text)"`; Checks = one failed `text-complete` check with reason `text a review override removed from <host id>; no other entity holds it`.
- Entries: consecutive rescued lines joined into one paragraph with a space; a line ending in `-` joins the next without the hyphen; a break in the base text between rescued lines starts a new entry.
- Rescue entities are recomputed on every Apply/Reapply (existing ones in the input are removed first); overrides/approvals/declines on a rescue id are applied like any entity's and are not reported as orphans while that rescue exists.
- C# code edits through Serena; warnings are errors; no change to endpoints, UI, drift logic or ApplyOne.

## Review Focus

- A host whose override only fixes typos (all base lines still held in reduced form after the fix): no rescue — pinned in Task 1.
- A Declined host: its removed lines are rescued only if not held elsewhere; a rescue of a Declined host must not be created (the reviewer dropped the whole entity on purpose) — pinned in Task 1.
- Reapply twice: exactly one rescue per host, same id, same text — pinned in Task 2.
- A decline recorded for a rescue id persists across reapply — pinned in Task 2.
- Lines held by another entity only after that entity's own override (final text) count as held — pinned in Task 1.

---

### Task 1: TrimRescue helper

**Files:**
- Create: `Features/Ingestion/Programmatic/TrimRescue.cs`
- Test: `DndMcpAICsharpFun.Tests/Programmatic/TrimRescueTests.cs`

**Interfaces:**
- Produces: `public static class TrimRescue` with
  - `public const string Suffix = "-trimmed";`
  - `public static bool IsRescueId(string id)` (ends with `-trimmed` or `-trimmed-2`)
  - `public static string RescueId(string hostId, IReadOnlySet<string> existingIds)`
  - `public static IReadOnlyList<EntityEnvelope> Compute(IReadOnlyList<(EntityEnvelope Host, string BaseText)> trimmedHosts, IReadOnlyList<EntityEnvelope> finalEntities)` — `finalEntities` is the whole merged book after overrides (rescues excluded); returns one rescue per host with at least one removed line; hosts with Disposition Declined are skipped.

- [ ] **Step 1: Failing tests** (build small `EntityEnvelope`s like `OverrideMergerTests.Grell`):
  `Removed_lore_held_nowhere_becomes_a_flagged_lore_rescue` (host Monster "mm14.monster.x", base "X\nStat line\nLong lore paragraph line one that is long enough\nand its continuation line that is long enough", final host text keeps only "X\nStat line" → one rescue, Id "mm14.lore.x-trimmed", Type Lore, Disposition NeedsReview after SetDisposition is not required here — assert its Checks has one failed `text-complete` whose reason contains "mm14.monster.x", entries has 1 paragraph joining both lines);
  `Removed_lines_held_by_another_entity_are_not_rescued`;
  `Typo_fix_only_creates_no_rescue` (base "Teh dragon sleeps under the mountain for a hundred years", final "The dragon sleeps under the mountain for a hundred years" → no rescue);
  `Short_label_only_creates_no_rescue` (removed "CHAPTER 3 | DOMAINS" only → none);
  `Heading_above_rescued_text_becomes_the_name`;
  `Declined_host_gets_no_rescue`;
  `Hyphenated_line_break_joins_without_hyphen`;
  `Rescue_id_gets_suffix_2_on_clash`.
- [ ] **Step 2:** Run `dotnet test DndMcpAICsharpFun.Tests --filter TrimRescueTests` → FAIL (type missing).
- [ ] **Step 3:** Implement per Global Constraints.
- [ ] **Step 4:** Tests PASS. **Step 5:** Commit `feat(overrides): TrimRescue keeps text an override removed and nobody holds`.

### Task 2: Wire into OverrideMerger

**Files:**
- Modify: `Features/Ingestion/Programmatic/OverrideMerger.cs` (`Apply(extracted, o, reapply)`)
- Test: `DndMcpAICsharpFun.Tests/Programmatic/OverrideMergerTests.cs`

**Interfaces:** consumes Task 1's `TrimRescue.Compute`, `TrimRescue.IsRescueId`.

- [ ] **Step 1: Failing tests:**
  `Apply_creates_rescue_for_text_an_override_removed` (extracted Monster with lore lines; `canonicalText` override keeps the stat lines → result contains `…-trimmed` Lore, NeedsReview);
  `Reapply_twice_keeps_exactly_one_rescue` (input already holding the rescue → result has one, same id/text);
  `Decline_of_rescue_persists` (declines list holds the rescue id → rescue present with Disposition Declined; it is not in `OrphanedEntityIds`);
  `Rescue_disappears_when_text_is_held_again` (another extracted entity now holds the lines → no rescue);
  `Override_on_rescue_applies` (a `canonicalText` override for the rescue id changes its text).
- [ ] **Step 2:** FAIL. **Step 3:** Implement: filter `TrimRescue.IsRescueId` entities out of `extracted` first; in the main loop record `(host, baseText)` for entities with a `canonicalText` override (base per Global Constraints); after the loop call `Compute(trimmedHosts, result)`; run each rescue through the same body as the loop (overrides for its id via `ApplyOne`, then `SetDisposition` with its approval/decline); exclude rescue ids from `orphans`.
- [ ] **Step 4:** `dotnet test DndMcpAICsharpFun.Tests --filter "OverrideMerger|TrimRescue|OverridesApplier|ProgrammaticExtraction|EntityReview"` PASS, then the full suite (the known `ManualCasesTests.ShippedManual_OpportunityAttack_RequiresLeavingReach` failure is pre-existing).
- [ ] **Step 5:** Commit `feat(overrides): rescue trimmed text on apply and re-extraction`.

### Task 3: Data (controller)

- [ ] Rebuild app (`docker compose -f docker-compose.yml up -d --build app`), `apply-overrides` on all 16 books, count rescue entities and rescued lines, run `tools/coverage_scan.py` (committed unheld must fall), commit data.
- [ ] AI review loop over the flagged rescues (rv8), loss/junk checks, apply, commit.
