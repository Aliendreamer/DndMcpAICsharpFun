## Why

`CharacterResolutionService` deterministically resolves only breath weapon, spell slots, save DC, and attack — all the *computed* facts. Two high-value character facts the chat model routinely fabricates remain unresolved: (1) which base-class features a character has at their level, and (2) which spells a spellcasting subclass grants (Cleric domain, Warlock patron, etc.). The class-progression tables we just projected (`project-tables-from-5etools`) already put (1)'s data in Postgres `StructuredTables`; (2)'s data (`additionalSpells`) is clean structured 5etools JSON that only needs a projection step. Both become grounded, cited resolver answers instead of model guesses.

## What Changes

- Add a `"class features"` resolver: for each of a character's classes, read the projected `phb14.table.<class-slug>` table and return the cumulative base-class features (levels 1..current) plus the current proficiency bonus, cited to the table. Multiclass-aware (one entry per class); a class with no projected table resolves `needsReview`, never fabricated.
- Add a `SubclassSpellsProjector` (console step, mirrors `DraconicAncestryResolutionProjector`) that projects each spellcasting subclass's 5etools `additionalSpells` (`prepared`/`known`/`expanded`) into a `CanonicalTable` `<slug>.table.<subclass-slug>-spells` (columns `level`, `spells`), landed by the existing `StructuredFactProjector`.
- Add a `"subclass spells"` resolver: from the character's `Subclass`, read the subclass-spells table and return the spells granted at levels ≤ current, cited. A subclass with no additional spells resolves an honest "none".
- Update `ResolveForSheetAsync` dispatch and the `resolve_character_feature` MCP tool description's supported-features list.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities
- `character-fact-resolution`: adds two resolvable features — `"class features"` (class-progression-table lookup) and `"subclass spells"` (subclass-spells-table lookup) — both exposed through the existing `resolve_character_feature` tool.
- `fivetools-table-projection`: adds projection of subclass `additionalSpells` into per-subclass `<slug>.table.<subclass-slug>-spells` canonical tables during `ProjectTables`.

## Impact

- **New:** `Features/Resolution/` — a class-features resolver path + a subclass-spells resolver path in `CharacterResolutionService`; `Features/Ingestion/FivetoolsIngestion/SubclassSpellsProjector.cs`; console wiring in `ProjectTablesRunner`.
- **Data:** `ProjectTables --all` adds `<slug>.table.<subclass-slug>-spells` tables to projected canonicals (PHB and other projected books where 5etools has subclass `additionalSpells`); class-progression tables unchanged.
- **Downstream (unchanged code):** `StructuredFactProjector` → Postgres `StructuredTables` → `CharacterResolutionService`; `resolve_character_feature` MCP tool + `ResolveForUserAsync` ownership path unchanged.
- **No new HTTP endpoint** (console + in-process resolver + existing per-user MCP tool). No `CharacterSheet` schema change (`Subclass`, `Classes`, `Level` already present).
