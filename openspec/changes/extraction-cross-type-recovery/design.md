## Context

Captured from a 2026-07-18 observation (`monster.light-armor` declined instead of typed Item; `class.spellcasting` declined as a rule). Since then: (a) the **rules-policy half shipped in `extraction-content-classification` Phase 1** — decline-bound + rule-signature candidates are now rescued as `EntityType.Rule` via `EntityExtractionOrchestrator.RescueAsRuleOrNull`; and (b) an **investigation of the live decline piles** shows the item-retyping premise is largely moot on the current corpus: every "item/armor/weapon"-keyword decline is actually a RULE/feature (`unarmored-defense`, `great-weapon-fighting`, `casting-in-armor`, `switching-weapons`) — correctly captured as `Rule` by (a) — and no genuine armor/weapon *instances* are in the decline pile (PHB has 2 Item entities; mundane weapons/armor live in 5etools, not the PDF entity layer). So there is currently nothing real for an item-rescue to recover.

## Goals / Non-Goals

**Goals:** add a **minimal, defensive item-rescue** mirroring the Rule rescue — a decline-bound candidate with a SPECIFIC mundane weapon/armor stat signature is admitted as `Item` — checked BEFORE the Rule rescue so a genuine item is never mis-typed as `Rule`. The signature must be specific enough that a rules passage (which has no damage-type/armor-stat line) is never grabbed as an Item. **Non-Goals:** value on the current corpus (there are no real item declines — this is forward/defensive machinery, validated by synthetic unit tests + a deterministic harness confirming it fires ~0 and never on rules); a new type (`Item` exists, non-gated); re-admitting category headers or noise; touching the Rule rescue's behavior.

## Decisions

- **D1 — `ItemSignature` is a SPECIFIC mundane-item stat marker, not a keyword.** TRUE only when the candidate text carries a mundane weapon-damage token (e.g. `\d*d\d+ (slashing|piercing|bludgeoning)`) OR an armor stat line (an "Armor Class"/"AC" figure paired with a `gp`/`sp` cost). Rules passages (`switching-weapons`, `casting-in-armor`) lack these, so they are never item-rescued — they fall through to the Rule rescue. Deliberately narrow to avoid the "mis-grab a rule as Item" risk the investigation flagged.
- **D2 — Item rescue is checked BEFORE the Rule rescue.** In each decline point, `RescueAsItemOrNull(candidate, outcome)` runs first (item is the more specific classification); only if it returns null does `RescueAsRuleOrNull` run; only if both return null is the candidate declined. Rescue rebinds `TypePrior = [EntityType.Item]` (single element — the failed gated type is not re-offered), exactly like the Rule rescue; the union then offers Item-or-none and a pick is `Accepted`/`canon-unindexed` (Item is non-gated).
- **D3 — Rules policy is DONE (option b, shipped in #2).** No further rules-policy work here; the `Rule` rescue is the applied policy. `switching-weapons` etc. correctly land as `Rule`, audited when they don't ground.

## Risks / Trade-offs

- **Mis-grabbing a rule as Item** → mitigated by D1 (a stat-marker signature, not a keyword) + D2 ordering (item is checked first BUT only fires on the stat marker, which rules lack); unit-tested with a real rule's text (`switching-weapons`) asserting it is NOT item-rescued.
- **Zero current value** → acknowledged; this is defensive machinery for future books that DO extract mundane items as candidates. The deterministic harness documents that it fires ~0 on the current corpus and never on the rule declines — safe, not harmful.
- **Interaction with #2** → item-before-rule ordering guarantees an item is never mis-rescued as Rule; a rule is never item-rescued (D1). Validated jointly by the DMG live re-extract.

## Migration Plan
1. Add `ItemSignature` + `RescueAsItemOrNull`; wire it before `RescueAsRuleOrNull` at both decline points (unit-green).
2. Deterministic harness: on real DMG/PHB candidates, assert `ItemSignature` fires on 0 (or few) declines and NEVER on a rule decline (`switching-weapons` stays Rule-eligible, not item).
3. Validated jointly with #2 by the DMG live re-extract (no separate live run).
4. Rollback = revert code.

## Open Questions
- None. If a future book extracts mundane weapons/armor as declined candidates, this rescue captures them as `Item`; until then it is dormant-but-safe.
