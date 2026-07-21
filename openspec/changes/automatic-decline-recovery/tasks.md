# Tasks — automatic-decline-recovery (recovery-framed Rule/Lore pass, inline, official books)

## 1. Verify/complete Lore wiring
- [ ] 1.1 Check `EntityType.Lore` is offerable: does `Schemas/canonical/LoreFields.schema.json` exist (globbed by `EntitySchemaProvider`), does `ExtractionPromptBuilder` handle `Lore`, and is a `Lore` canonical-text renderer registered? If any is missing, add a minimal one (mirror the existing `Rule` wiring — `{summary?, entries}` shape + a renderer). Unit-test that `ExtractionUnionSchemaBuilder.Build([Rule,Lore], schemas)` emits both branches.

## 2. Recovery-framed prompt + service
- [ ] 2.1 Add a recovery system prompt to `ExtractionPromptBuilder` (e.g. `BuildRecoverySystemPrompt(book, version)`): "this is real, official <book> content — NOT fabricated; classify as Rule (mechanical), Lore (worldbuilding/setting), or none (pure heading/TOC/fragment)."
- [ ] 2.2 Add a `DeclineRecovery` service: given a declined `EntityCandidate` (with its text), call `CandidateExtractor.ExtractUnionAsync` with `prior=[Rule,Lore]` and the recovery prompt; on a Rule/Lore pick run the grounding cascade + disposition; return a recovered `EntityEnvelope` (marked `dataSource:"decline-recovery"`) or null (stays declined). Unit-test: a rule-shaped text → Rule admitted; a heading/fragment → null (none); an ungrounded pick → null.

## 3. Wire as the automatic final extraction phase
- [ ] 3.1 In `EntityExtractionOrchestrator` (official books only), after the main loop, run `DeclineRecovery` over the still-declined candidates; append the recovered entities to the entity list and REMOVE them from the decline audit (`declined.json`/`errors.json`) before the final write / `EntitiesExtracted`. Mirror in `RunErrorsOnlyAsync` (or a shared helper). Skip for non-official books. Unit-test: declines that recover are added + removed from the audit; non-official book → recovery skipped.

## 4. Validate on DMG (fast, inline)
- [ ] 4.1 Re-extract DMG (the recovery runs automatically); report recovered Rule/Lore counts vs skip, spot-check that recovered entities ground correctly (e.g. `psychic-wind-effects` → Rule, a pantheon → Lore), no fabrication, main entities unchanged. If Lore over-admits narrative, tighten the prompt or restrict to Rule.

## 5. Gates
- [ ] 5.1 `dotnet build` 0/0; FULL `dotnet test` green; `dotnet format` clean on touched files. No HTTP endpoint change.
