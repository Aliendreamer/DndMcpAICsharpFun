## Why

Today the companion can only *retrieve* — it embeds `CanonicalText` and returns prose. It cannot
*compute* a character-specific, rule-accurate, cited answer. "What's my breath weapon as a Red
Dragonborn?" has no structured path: the Draconic Ancestry table was never represented (it got
fabricated as a Monster stat block; the real table lives only in prose), the `CharacterSheet` has
nowhere to record the chosen ancestry, and there is no engine to project the choice through the
table. This change builds the **thinnest meaningful vertical slice** of a deterministic
character-fact resolution engine — the first query the companion *computes and cites* instead of
retrieving — proving the storage + engine + read path end-to-end on one concrete feature.

## What Changes

- **Richer canonical model:** add first-class `Table` and `ChoiceSet` entity shapes with **per-cell
  provenance** (`{blockId, sourceBook, page}` reference values; prose stays in Qdrant). Author the
  Draconic Ancestry table, the breath-damage-by-tier table, and the ancestry choice-set by hand
  from PHB prose. (Automated table extraction is **out of scope**.)
- **Projection canonical JSON → Postgres:** new EF Core tables (`StructuredEntity`, `StructuredTable`,
  `StructuredTableRow`, `ChoiceSet`, with provenance columns) + an ingest step that upserts the
  authored tables/choice-sets into Postgres. Reusable for future tables.
- **CharacterSheet resolved-choices slot:** add `ResolvedChoices` (e.g. `ancestry →
  phb14.choiceset.draconic-ancestry:Red`) to `CharacterSheet`, with a backward-compatible
  `HeroSnapshot` JSON migration. `Level` stays an `int` (per-class breakdown deferred to slice 2).
- **`CharacterResolutionService`** (new, deterministic): `resolve(heroId, feature)` composes the
  ancestry table (type/area/save) + character level → damage dice + the save-DC formula
  (`8 + proficiency + Con mod`), each component carrying provenance; a `needsReview` fact returns its
  prose span (fallback).
- **MCP tool `resolve_character_feature(heroId, feature)`** the chat client calls; the LLM renders the
  cited answer ("15-ft cone of fire, Dex save DC 15, 3d6 [PHB p.35]").

## Capabilities

### New Capabilities
- `structured-knowledge-store`: first-class `Table`/`ChoiceSet` canonical shapes with per-cell
  provenance, and the projection of authored canonical JSON into queryable Postgres tables.
- `character-fact-resolution`: the resolved-choices character-sheet slot, the deterministic
  `CharacterResolutionService` that composes structured facts + character state into a cited fact,
  and the MCP tool that exposes it to chat.

### Modified Capabilities
<!-- None. The CharacterSheet ResolvedChoices addition is an ADDED requirement of
     character-fact-resolution, not a behaviour change to an existing spec. -->

## Impact

- New: canonical `Table`/`ChoiceSet` types; EF entities + a migration for the structured-fact tables
  and `HeroSnapshot.CharacterSheet.ResolvedChoices`; `CharacterResolutionService`; one MCP tool;
  authored Dragonborn canonical fixtures.
- Modified: `CharacterSheet` (new `ResolvedChoices` field), `AppDbContext` (new DbSets + config),
  the MCP server registration, `Program.cs`/DI for the new service.
- Storage: **Postgres** (the resolve is a join + keyed lookup + arithmetic over character state that
  already lives in Postgres; not a graph traversal — Neo4j deferred).
- Out of scope: automated table extraction, per-class `Level`, general Bin-B query endpoints, a read
  router, and any race/feature beyond Dragonborn breath weapon.
