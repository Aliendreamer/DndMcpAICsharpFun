## Context

The content-first extraction gate (`content-first-extraction`, `discriminated-union-extraction-decoding`, `extraction-disposition`) is deliberately conservative: it declines rather than fabricate. `EntityExtractionOrchestrator.RunFullExtractionAsync` loops over candidates, and a decline (deterministic `DeterministicTypeResolver.Decline`, or an LLM `entityType:none`) records the candidate to the decline audit (`<slug>.declined.json` / the `extraction_declined` entries in `<slug>.errors.json`). #2 (`extraction-content-classification`) added an INLINE Rule rescue (`RescueAsRuleOrNull`) that re-offers rule-signature declines during the main loop — but the DMG live re-extract showed it recovers only ~5 of ~230 declines: qwen3, in the same inline entity-hunting frame, keeps picking `none` for real rules ("not a discrete game entity"). The still-declined set contains genuine mechanical rules (`psychic-wind-effects`, `ethereal-curtains`) and worldbuilding lore (`loose-pantheons`, `monotheism`), all real official content, only in `dnd_blocks` prose. `EntityType.Rule` is fully wired (schema/prompt/renderer from 5etools variantrules ingestion); `EntityType.Lore` exists.

## Goals / Non-Goals

**Goals:** automatically (no separate trigger) recover the real `Rule`/`Lore` content the conservative gate declined, via a RECOVERY-framed re-classification pass at the end of extraction; preserve anti-fabrication (grounding still gates every admission). **Non-Goals:** a new endpoint (runs inside `extract-entities`); tables (the tables pipeline owns those); Item/other entity types (already tried inline); homebrew/keyless web-vouch (deferred — the web referee belongs there); changing the main extraction gate (recovery is additive).

## Decisions

- **D1 — Automatic final phase, not an endpoint.** The recovery runs inside `EntityExtractionOrchestrator` after the main loop, before the final canonical write / `EntitiesExtracted` status. The declined candidates are still in memory WITH their text, so no cache re-fetch is needed. One `extract-entities` call now does extract → recover → done.
- **D2 — Recovery-framed union over `[Rule, Lore] ∪ none`.** For each still-declined candidate, one `CandidateExtractor.ExtractUnionAsync` call with `prior=[Rule, Lore]` and a RECOVERY system prompt: "this is real, official `<book>` content — NOT fabricated; classify it as a Rule (mechanical), Lore (worldbuilding/setting), or `none` (pure heading/TOC/fragment)." The different framing (recover-a-real-thing, not is-this-an-entity) is the mechanism that beats the inline gate. `none` is always offered so a bad recovery degrades to a re-decline, never a fabrication.
- **D3 — Grounding preserved.** A `Rule`/`Lore` pick runs the existing grounding cascade against the candidate text; only a grounded result is admitted (disposition per policy — Accepted / NeedsReview). Ungrounded → stays declined. Recovery changes the classification prompt, never the anti-fabrication contract.
- **D4 — Marking + audit reconciliation.** Recovered entities: `dataSource:"decline-recovery"`, `authority:"canon-unindexed"`, appended to the entity list; each recovered candidate is REMOVED from the decline audit (`declined.json` / `errors.json`) so the audit reflects only what truly stayed declined.
- **D5 — Composition with #2.** #2's inline `RescueAsRuleOrNull` still runs in the main loop (rule-signature → Rule). The recovery phase operates on whatever REMAINS declined after that (adds Lore + recovery framing). They stack; the recovery is the more capable second chance.
- **D6 — Scope official-only (Phase 1).** The "real by definition" framing only holds for official books. For homebrew/keyless books the recovery phase is skipped (a web-vouch recovery is the deferred follow-on).

## Risks / Trade-offs

- **Recovery flooding (over-admitting narrative as Lore)** → mitigated by the grounding cascade (fields must ground) + `none` always offered + the recovery prompt explicitly listing "pure heading/TOC/fragment → none"; measured on the DMG run (recovered Rule/Lore vs skip). If Lore floods, tighten the prompt or drop Lore.
- **qwen3 judgment ceiling** → the recovery framing helps but qwen3 is still the classifier; yield is bounded by it. Acceptable: it strictly recovers from the currently-100%-declined set, so a wrong `none` is status quo and a wrong Lore is caught by grounding.
- **Inline cost + iterability** → runs add ~minutes (one call per decline). Because it's inline (per the user's explicit choice), tuning the recovery prompt requires a re-extract — accepted trade-off for automatic operation.
- **Lore not fully wired** → `EntityType.Lore` may lack a `LoreFields` schema/prompt/renderer; verify and add a minimal one (mirror Rule) as part of the work.

## Migration Plan
1. Add the `DeclineRecovery` service + wire it as the automatic final phase (official books) behind the grounding cascade (unit-green).
2. Verify/complete `Lore` wiring (schema/prompt/renderer).
3. Validate on a DMG re-extract: recovered Rule/Lore vs skip counts, spot-check the recovered entities ground correctly, no fabrication.
4. Rollback = revert code; the recovery is additive (no schema/DB migration; recovered entities are ordinary entities, not ingested until reviewed).

## Open Questions
- The exact recovery prompt wording is tuned on the DMG run (start with the D2 framing). If Lore over-admits, restrict Phase 1 to Rule and defer Lore.
