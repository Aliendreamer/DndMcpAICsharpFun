# Entity Schema Refinement Design

**Date:** 2026-05-07  
**Status:** Approved

## Goal

Fix systematic mis-classification in the structured entity extraction pipeline: section headings are being extracted as `God`/`Plane`/`Monster` entities, and there is no type for rulebook narrative or mechanical content. This design adds two new types, tightens required fields on existing types, and improves the extraction prompt.

## Background

Evidence from the DMG extraction run (Plan 2/3):
- 73/99 early entities were `Plane` — chapter headings like "HAPTER 2: CREATING A MULTIVERSE" passed because `PlaneFields` has no required fields.
- `God` entities included "FORCES AND PHILOSOPHIES", "LOOSE PANTHEONS", "HUMANOIDS AND THE GODS" — section headings, not deities.
- `Monster` included "FEYWILD" and "ETHEREAL PLANE ENCOUNTERS" — wrong type, no gate.
- Narrative/worldbuilding content (philosophies, cosmology overviews, religious systems) and mechanical content (encounter tables, DMing procedures) have nowhere to go, so the LLM shoehorns them into the nearest type.

## New Entity Types

### `Lore`
Flavour and worldbuilding content that is not a discrete game entity: cosmology overviews, pantheon descriptions, religious philosophies, cultural lore, setting history, named concepts from the world.

**Schema (`LoreFields.schema.json`):**
```json
required: ["category", "description"]
properties:
  category: string        -- e.g. "Cosmology", "Religion", "Culture", "History", "Philosophy"
  description: string     -- summary of the lore content
  settingContext: string? -- which setting/world this applies to (e.g. "Forgotten Realms")
```

### `Rule`
Mechanical and procedural rulebook content: encounter building, adventure design guidelines, random tables, DMing procedures, game system explanations. Intentionally loose schema — content is too varied to constrain further at this stage.

**Schema (`RuleFields.schema.json`):**
```json
required: ["category", "description"]
properties:
  category: string       -- e.g. "EncounterBuilding", "AdventureDesign", "RandomTable", "DungeonDesign", "Procedure"
  description: string    -- summary of the rule or procedure
  sourceTable: string?   -- name of the random table, if applicable
```

## Schema Tightening on Existing Types

Add `required` arrays to force the LLM to prove it has a real entity. Section headings cannot fill these fields and will be routed to `Lore`, `Rule`, or skipped.

| Type | New required fields |
| --- | --- |
| `God` | `alignment`, `domains`, `description` |
| `Plane` | `category`, `description` |
| `Monster` | `challengeRating`, `size`, `type` |
| `Location` | `description` |
| `Faction` | `description` |

All other types (Class, Spell, Background, etc.) already have sufficient structure to naturally reject headings.

## Magic Item Variants

Pages like "ARMOR, +1, +2, OR +3" describe multiple tiers of the same item. The LLM currently tries to emit all variants in one massive JSON, exceeding the 8192-token output limit and failing after 3 retries.

**Fix:** Add an optional `variants` array to `MagicItemFields.schema.json`. One entity captures all tiers; the LLM emits a compact structured list instead of verbose prose per variant.

**Schema addition to `MagicItemFields.schema.json`:**
```json
"variants": {
  "type": ["array", "null"],
  "items": {
    "type": "object",
    "additionalProperties": false,
    "properties": {
      "suffix":      { "type": "string" },        -- e.g. "+1", "+2", "Greater", "Lesser"
      "rarity":      { "type": "string" },        -- e.g. "Uncommon", "Rare", "Very Rare"
      "bonus":       { "type": ["integer","null"], "format": "int32" },
      "description": { "type": ["null","string"] } -- variant-specific note if any
    }
  }
}
```

**Prompt addition for `MagicItem` type:**
> "If the source text describes multiple tiers or variants of the same item (e.g. +1/+2/+3), extract them as a single entity with a `variants` array rather than separate entities."

**Roll tables** (`MAGIC ITEM TABLE A`, `TABLE B`, etc.) are not items — route them to `Rule` with `category: "RandomTable"`. They produce small outputs and don't hit the token limit.

**Files additionally affected:**

| File | Change |
| --- | --- |
| `Schemas/canonical/MagicItemFields.schema.json` | Add optional `variants` array |
| `Features/Ingestion/EntityExtraction/ExtractionPromptBuilder.cs` | Add MagicItem variant instruction |

## Prompt Changes

Two additions to `ExtractionPromptBuilder.BuildSystemPrompt`:

**1. Heading filter:**
> "Do not extract chapter titles, section headings, or table headers as entities. Only extract named, discrete game elements."

**2. Type routing guidance** (appended after the existing entity type line):
> "Use `Lore` for named worldbuilding concepts, cosmology descriptions, pantheon overviews, religious philosophies, and cultural/setting flavour that is not a discrete game entity."  
> "Use `Rule` for mechanical procedures, encounter tables, adventure design guidelines, random tables, and DMing system explanations."  
> "Use `God` only when the entity is a named deity with known alignment and at least one domain."  
> "Use `Plane` only when the entity is a named plane of existence with a defined category (Inner, Outer, Transitive, Material, etc.)."  
> "Use `Monster` only when the entity has a stat block with a challenge rating."

**3. OCR hint (already added in Plan 3 follow-up):**
> "The source text may contain OCR artifacts (e.g. 'gons' → 'gods', 'lhe' → 'the'). Use surrounding context to infer the correct meaning."

## Files Affected

| File | Change |
| --- | --- |
| `Domain/Entities/EntityType.cs` | Add `Lore`, `Rule` to enum |
| `Schemas/canonical/LoreFields.schema.json` | New file |
| `Schemas/canonical/RuleFields.schema.json` | New file |
| `Schemas/canonical/GodFields.schema.json` | Add `required: ["alignment", "domains", "description"]` |
| `Schemas/canonical/PlaneFields.schema.json` | Add `required: ["category", "description"]` |
| `Schemas/canonical/MonsterFields.schema.json` | Add `required: ["challengeRating", "size", "type"]` |
| `Schemas/canonical/LocationFields.schema.json` | Add `required: ["description"]` |
| `Schemas/canonical/FactionFields.schema.json` | Add `required: ["description"]` |
| `Features/Ingestion/EntityExtraction/ExtractionPromptBuilder.cs` | Add heading filter, type routing guidance, MagicItem variant instruction |
| `Schemas/canonical/MagicItemFields.schema.json` | Add optional `variants` array |
| `Features/Ingestion/EntityExtraction/ExtractionPromptBuilder.cs` | Add heading filter + type routing + MagicItem variant instruction |
| `Features/Retrieval/EntitySearch/EntitySearchFilters.cs` | Add `Lore`, `Rule` to any type filter enums/mappings |

## What This Does NOT Change

- Existing canonical JSON files — already-extracted entities are not retroactively re-typed. The `errorsOnly` pass after the next full extraction will handle failures; mis-typed entities from the current run are corrected by hand or by re-running extraction.
- Extraction pipeline logic — no changes to orchestrator, checkpointing, or Docling cache.
- Ingestion pipeline — `Lore` and `Rule` entities are ingested into `dnd_entities` the same way as all other types.

## Success Criteria

After the next full DMG extraction with these changes:
- Section headings no longer appear as `God` or `Plane` entities.
- `Lore` captures philosophical/worldbuilding content (philosophies, pantheon descriptions, cosmology overviews).
- `Rule` captures mechanical content (encounter tables, adventure design procedures).
- `God` entities all have a name, alignment, and at least one domain.
- `Plane` entities all have a named plane and a category.
- Multi-tier magic items (armor +1/+2/+3, potions of healing variants, etc.) are extracted as a single entity with a `variants` array.
- Magic item roll tables (`TABLE A`, `TABLE B`, etc.) are extracted as `Rule` entities, not `Item`/`MagicItem`.
