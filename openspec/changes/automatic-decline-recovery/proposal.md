## Why

The extraction gate is conservative by design (anti-fabrication), so real mechanical rules and worldbuilding lore get DECLINED, not just noise. On DMG's re-extract, ~230 candidates were LLM-declined — most are correctly-declined headings/narrative, but a real minority are genuine content (`psychic-wind-effects`, `ethereal-curtains` travel mechanics, subclass features, pantheon/cosmology lore) lost from the structured layer. #2's inline Rule rescue only recovered 5, because the inline entity gate keeps rejecting real rules as "not a discrete entity." A second, RECOVERY-framed pass — run automatically at the end of extraction, over the already-declined candidates — recovers the real Rule/Lore content the entity gate wrongly dropped, without a separate manual step and without re-running the ~8h extraction.

## What Changes

- Add an automatic **decline-recovery phase** as the final step of `EntityExtractionOrchestrator`'s extraction flow (no new endpoint — it runs inside `extract-entities`). After the main loop collects declines, it re-classifies the still-declined candidates (whose text is already in memory).
- Per still-declined candidate: one targeted union call offering `[Rule, Lore]` + `none` with a RECOVERY-framed system prompt ("this is real, official `<book>` content — NOT fabricated; classify as Rule / Lore / skip"). A `Rule`/`Lore` pick that grounds via the existing cascade is admitted as an entity; `none`/ungrounded stays declined.
- Recovered entities are marked `dataSource:"decline-recovery"`, `authority:"canon-unindexed"`, appended to the entity list, and dropped from the decline audit.
- Anti-fabrication is preserved: the grounding cascade still gates every recovered entity; the recovery only changes the CLASSIFICATION framing, never the grounding requirement.

## Capabilities

### New Capabilities
- `automatic-decline-recovery`: after the main extraction, still-declined candidates are automatically re-classified with a recovery-framed `[Rule, Lore] ∪ none` union call and admitted as grounded `Rule`/`Lore` entities, recovering real mechanical rules and worldbuilding lore the conservative entity gate declined — inline, no separate trigger.

### Modified Capabilities
<!-- Consumes the extraction pipeline / disposition; no existing spec's REQUIREMENTS change (the main extraction gate is unchanged — recovery is an additive final phase). -->

## Impact

- `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs` — add the recovery phase after the main loop (both `RunFullExtractionAsync` and `RunErrorsOnlyAsync`, or a shared helper); a new `DeclineRecovery` service that runs the recovery union call + grounding + admission.
- Reuses `CandidateExtractor.ExtractUnionAsync` (prior `[Rule,Lore]` + a recovery-framed prompt), `ExtractionPromptBuilder`, the grounding cascade, the canonical writer. `EntityType.Rule` is fully wired (schema/prompt/renderer); `EntityType.Lore` wiring verified/added.
- **Scope:** official books (the "real by definition" framing). Homebrew/keyless **web-vouch** recovery is out of scope (deferred — the web referee belongs there, not for official content).
- No HTTP endpoint change (recovery runs inside the existing `extract-entities` flow). Runs add ~minutes (one focused call per decline, not a whole-book re-process).
