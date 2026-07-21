# Tasks — extraction-cross-type-recovery (minimal defensive item-rescue; item-before-rule)

> Investigation (2026-07-21) found the item-retyping premise moot on the current corpus (all
> item-shaped declines are rules, handled by extraction-content-classification #2). This builds a
> minimal, SPECIFIC, defensive item-rescue anyway: dormant-but-safe today, captures real mundane
> items if a future book extracts them as declined candidates. Validated jointly with #2 by the
> DMG live re-extract.

## 1. ItemSignature + item-rescue (before the Rule rescue)
- [ ] 1.1 Add `ExtractionSignatures.ItemSignature(EntityCandidate)` — TRUE only on a SPECIFIC mundane-item stat marker: a weapon damage-type token (`\d*d\d+\s+(slashing|piercing|bludgeoning)`, case-insensitive) OR an armor stat line (an AC figure + a `gp`/`sp` cost). Rules passages lack these. Unit-test: a weapon-stat text → true; an armor-stat text → true; a rule's text ("When you attack... you can switch weapons...") → false; a short fragment → false.
- [ ] 1.2 Add `EntityExtractionOrchestrator.RescueAsItemOrNull(EntityCandidate, DeterministicOutcome)` — returns `candidate with { TypePrior = [EntityType.Item] }` when `outcome == Decline && ItemSignature(candidate)`, else null. Wire it BEFORE `RescueAsRuleOrNull` at BOTH decline points (RunFullExtractionAsync + RunErrorsOnlyAsync): item first, then rule, then decline. Unit-test the ordering: an item-signature candidate → Item (not Rule); a rule-signature-only candidate → Rule; a real entity → neither.

## 2. Deterministic harness (moot-but-safe evidence)
- [ ] 2.1 Extend/add a harness on real DMG candidates: assert `ItemSignature` fires on a SMALL/zero count of the decline pile (no real items today), and that NO decline that is a rule (e.g. the `switching-weapons` decline) is item-rescued — item and rule rescues are disjoint on the real corpus. Log the counts. This documents dormant-but-safe.

## 3. Gates + STOP before live re-extract
- [ ] 3.1 `dotnet build` 0/0; FULL `dotnet test` green; `dotnet format` clean on touched files. No HTTP endpoint change.
- [ ] 3.2 STOP. Validated jointly with #2 by the DMG live re-extract (the next step) — not a separate run. Do not ingest until that passes.
