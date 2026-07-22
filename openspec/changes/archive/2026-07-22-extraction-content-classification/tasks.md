# Tasks — extraction-content-classification (Phase 1 = Rule; rescue would-be-declines)

## 1. RuleFields schema + Rule branch buildable
- [ ] 1.1 Add a minimal `RuleFields` record (`summary?`, `ruleTopics?[]`) + register a per-type schema for `EntityType.Rule` so `ExtractionUnionSchemaBuilder.Build` can emit a Rule branch. Unit-test the union builds a Rule branch when Rule is in the prior + schemas.

## 2. Rescue-would-be-declines gate
- [ ] 2.1 Add a `RuleSignature` predicate (substantial prose + non-entity-name heading + not a fragment/TOC) over an `EntityCandidate`.
- [ ] 2.2 In the orchestrator's extract path: when a candidate is decline-bound (`DeterministicTypeResolver` → Decline, or no non-gated entity option) AND `RuleSignature` holds, ADD `Rule` to the `prior` (and `RuleFields` to `schemas`) passed to `ExtractUnionAsync`. A resolver `Force(entityType)` / real-entity path is untouched (Rule not offered). Unit-test: decline-bound + signature → Rule offered; a real Spell/Monster candidate → Rule NOT offered; a fragment → not offered (filtered before).
- [ ] 2.3 Disposition: a `Rule` union pick is `Accepted` (non-gated, grounded by prose); ensure the Rule entity is built with prose in `canonicalText` and `RuleFields`, and is NOT sent down the gated 5etools-match path.

## 3. Cheap deterministic-path harness (NO GPU/LLM)
- [ ] 3.1 A unit/integration harness that feeds REAL DMG candidates (from the conversion cache) through the candidate build → resolver → rescue-gate and reports: how many decline-bound candidates now get Rule offered, that ZERO real-entity candidates get Rule offered, and the would-be decline-rate drop. Assert the gate targets the decline pile only (anti-flooding evidence) — this proves the GATE without any model call.

## 4. Gates + STOP before live re-extract
- [ ] 4.1 `dotnet build` 0/0; FULL `dotnet test` green; `dotnet format` clean on new/changed files. No HTTP endpoint change.
- [ ] 4.2 STOP. The full DMG re-extract (the true quality proof: Rule 0→N, entities unchanged, no flooding, entity-type-distribution diff) requires the stack + ~8h — run ONLY on explicit user go. Do not ingest Rule entities until that live validation passes.
