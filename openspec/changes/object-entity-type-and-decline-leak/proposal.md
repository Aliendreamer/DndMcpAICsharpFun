## Why

The DMG (book 3) fresh extraction exposed two defects in how the pipeline handles candidates that don't fit the existing type schema. D&D "objects" — siege weapons (ballista, cannon, ram), suspended cauldrons, animated doors — carry Armor Class, Hit Points, and attack actions, but there is no entity type for them: `Weapon` has no AC/HP, and `Monster` is a creature (so they get gated + declined). Faced with the mismatch, the LLM punts to "none/ambiguous" and its classification *reasoning* is persisted as the entity's `canonicalText` with empty `fields` — producing garbage shells (e.g. Ballista's canonicalText reads "the schema doesn't include an 'object' type… safer to classify as 'none'"). The actual AC/HP/attack stats are lost.

## What Changes

- Add an `Object` entity type for AC/HP-bearing non-creatures, with fields, a canonical-text renderer, and extraction routing so siege weapons and similar objects extract with real stats instead of empty `Item` shells.
- Fix the reasoning-leak defect: when an LLM extraction signals uncertainty (type `none`, empty `fields`, or a `canonicalText` that is classification reasoning rather than entity content), the pipeline records a decline/error instead of persisting an empty entity.
- `Object` is **not** a 5etools-gated type — it is LLM-extracted and never declined for lack of a monster-index match. No 5etools grounding/backfill for `Object` in v1 (entries remain hand-authorable).

## Capabilities

### New Capabilities
- `object-entity-type`: A new `Object` entity type (enum value, `ObjectFields` schema/POCO, canonical-text renderer, non-gated extraction disposition, and prompt/routing guidance) representing D&D objects that have combat stats but are not creatures.

### Modified Capabilities
- `extraction-disposition`: An LLM extraction whose output signals uncertainty (type `none`, empty/meaningless `fields`, or reasoning-as-content) MUST be recorded as a decline/error and MUST NOT be persisted as an entity.

## Impact

- **Domain**: `Domain/Entities/EntityType.cs` (+`Object`); new `ObjectFields` POCO.
- **Schemas**: new `Schemas/canonical/ObjectFields.schema.json` (regenerated via `Tools/SchemaGenerator`).
- **Extraction**: candidate prior-type routing + extraction prompt (AC/HP non-creatures → `Object`); the extraction-disposition/decoding path that currently persists uncertain results.
- **Rendering**: new `Object` canonical-text renderer wired into `EntityCanonicalTextDispatcher`.
- **Data**: re-extracting DMG reclassifies ballista/cannon/ram/cauldron as `Object` with populated stats; other books benefit on their next extraction. No migration of existing canonical files beyond re-extraction.
- **Out of scope**: 5etools grounding/backfill for `Object`; a broad object taxonomy; the parked full extraction rearchitecture.
