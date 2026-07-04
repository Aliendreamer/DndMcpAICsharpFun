## Context

The entity type system (`Domain/Entities/EntityType.cs`, 22 values) has `Weapon`, `Armor`, `Trap`, `VehicleMount`, `Item`, `Monster`, etc., but no type for D&D **objects** — non-creatures that nonetheless have Armor Class, Hit Points, and sometimes attack actions (siege weapons, suspended cauldrons, animated doors/statues). Two failures result, both observed in the DMG (book 3) extraction:

1. **Schema gap.** The prior-type classifier tags a siege weapon's stat block as `Monster` (a gated type). With no matching 5etools monster, it is declined. A second candidate for the same object (the prose entry) is kept as `Item`, but `Item` has no stat fields, so the AC/HP/attack data is dropped.
2. **Reasoning leak.** When the LLM cannot fit a candidate to a type, it emits classification *reasoning* (e.g. "the schema doesn't include an 'object' type… safer to classify as 'none'"). The pipeline persists this as the entity's `canonicalText` with empty `fields`, producing an auditless garbage shell instead of a clean decline.

Constraints: `.NET 10`, warnings-as-errors; canonical schemas are generated from POCOs via `Tools/SchemaGenerator`; extraction runs local qwen3 via Ollama; the broader structured-extraction layer is under a parked rearchitecture (`mem: project_entity_extraction_rethink`), so this change stays deliberately additive and small.

## Goals / Non-Goals

**Goals:**
- Represent AC/HP-bearing non-creatures as a first-class `Object` entity with real stats.
- Stop persisting empty, reasoning-filled shells; route uncertain extractions to decline/error where they are auditable and re-triable.
- Re-extract DMG so siege weapons land as `Object` with populated fields.

**Non-Goals:**
- 5etools grounding/backfill for `Object` (v1 entries are hand-authorable).
- A broad object taxonomy beyond what appears in the source books.
- The parked full extraction rearchitecture.

## Decisions

- **New `Object` type with a Monster-subset field shape** rather than reusing `Monster` or `VehicleMount`. Objects are not creatures (reusing `Monster` is semantically wrong and would pollute monster retrieval/gating) and not vehicles. `ObjectFields` carries `armorClass`, `hitPoints`, damage immunities/resistances/vulnerabilities, condition immunities, optional attack `actions` (name, to-hit, damage, reach/range), and a short description — the subset of `MonsterFields` that objects actually use. *Alternative considered:* a `Monster` subtype flag — rejected as semantically loose and gating-hostile.
- **`Object` is non-gated.** Siege equipment is absent from the 5etools monster index; gating it there is exactly what caused the decline. `Object` candidates are LLM-extracted and never declined for lack of a 5etools match (no `Object` entry in the gated-type set / `DeterministicTypeResolver`). *Alternative:* gate against 5etools `vehicles.json` — deferred (out of scope v1).
- **Prior-type routing + prompt guidance** so a stat block that has AC/HP but is a non-creature is classified as `Object`, not `Monster`. This is where the split between the (declined) Monster candidate and the (kept-as-Item) prose candidate currently occurs.
- **Uncertain extraction ⇒ decline, never persist.** A parsed extraction is treated as failed when it has empty/meaningless `fields` AND a none/ambiguous signal (type `none`, or `canonicalText` that is meta-reasoning). It is recorded in `errors.json` (extraction failure) or `declined.json` — not written as an entity. *Alternative:* keep the shell but blank the reasoning — rejected; a stat-less object is not useful and hides the failure.

## Risks / Trade-offs

- **Reasoning-leak detection is heuristic** → false negatives (a shell slips through) or false positives (a legitimately sparse entity is declined). Mitigation: gate the decline on *both* empty fields AND an ambiguity signal; log declines so they are auditable and re-triable via `errorsOnly`.
- **Adding a type to a parked layer** → churn if the layer is later replaced. Mitigation: keep the change additive and small; `Object` follows existing type patterns so it rides along with any future rework.
- **Re-extraction cost** → DMG re-extract is a multi-hour GPU run. Mitigation: the change is validated on the few siege candidates; a full re-extract is a separate, scheduled step, not part of merging this change.

## Migration Plan

1. Ship the `Object` type + decline-leak fix; regenerate schemas; build/test green.
2. Re-extract affected books (DMG first) so objects reclassify; review the diff.
3. Rollback: `Object` is additive — reverting the enum/fields/renderer restores prior behavior; already-extracted `Object` entities would need a re-extract or manual retype, but none are committed until the DMG finalization lands.

## Open Questions

- None blocking. Whether to later ground `Object` against 5etools `vehicles.json` is deferred to a follow-up.
