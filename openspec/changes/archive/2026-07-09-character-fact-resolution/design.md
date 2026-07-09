## Context

This is slice 1 of the parked `prose-grounded-knowledge-model` design (§I), cut to its thinnest
meaningful first cut. The companion today only retrieves (embeds `CanonicalText`, returns prose);
fact-precise, character-specific answers need structured facts + character state + a deterministic
engine (§H). The Dragonborn breath weapon is the canonical exemplar — it was the proof that the old
flat `entity{type, fields}` model was the wrong abstraction (the ancestry table was fabricated as a
Monster; it exists in no entity). Current ground truth: `AppDbContext` (Postgres) holds
`HeroSnapshot` with `CharacterSheet` as a JSON column (flat fields + freeform `Features`); prose is
in Qdrant `dnd_blocks`; chat is an MCP client (`Features/Chat`).

## Goals / Non-Goals

**Goals:**
- Represent a relational table + a choice-set as first-class canonical shapes with per-cell provenance.
- Project authored tables/choice-sets into queryable Postgres.
- Record a resolved player choice on `CharacterSheet` (structured ref).
- Compute the Dragonborn breath weapon deterministically (table + level + Con → cited fact) and
  expose it via one MCP tool.
- Prove the whole READ path (store → sheet → engine → MCP → cited answer) end-to-end on one feature.

**Non-Goals:**
- Automated prose→table extraction (author the tables by hand this slice).
- Per-class `Level` (`List<ClassLevel>`) — slice 2 (multiclass).
- General Bin-B query endpoints, a read router, or any race/feature beyond Dragonborn breath weapon.
- A Postgres prose table — prose stays in Qdrant; provenance is a `{blockId, sourceBook, page}` ref.

## Decisions

**1. Storage = Postgres (settled).** The resolve is `load character state → keyed table lookup ×2 →
arithmetic`. The lookup target (the rule tables) must be **joined to character state**, which already
lives in Postgres (`HeroSnapshot`). A graph store would split that join across datastores for zero
traversal benefit (relationships are 1-hop; provenance is a FK-style ref). Neo4j is deferred until a
genuinely multi-hop relationship-path query class appears.

**2. Tables/ChoiceSets are first-class canonical shapes, not flat entities.** New canonical types
`Table { columns, rows[ cells[ value, provenance ] ] }` and `ChoiceSet { options[ rowRef ] }`. This is
the "richer model" the rethink required; it is introduced minimally (only what the slice authors).

**3. Provenance is a stored reference, not copied prose.** `{ blockId, sourceBook, page }` on each
cell/option. Renders a citation; can fetch the prose span from Qdrant for the fallback path. No prose
duplicated into Postgres.

**4. Projection is a separate, reusable step (canonical → Postgres).** New EF entities
`StructuredEntity / StructuredTable / StructuredTableRow / ChoiceSet` (+ provenance columns) and an
idempotent upsert ingest. Mirrors the existing canonical→Qdrant ingest pattern but targets Postgres;
it is the reusable substrate, even though only the Dragonborn tables flow through it now.

**5. Minimal sheet change.** Add `ResolvedChoices` (a `Dictionary<string,string>` of
choice-key → choiceset-option-ref) to `CharacterSheet`. JSON-column add → backward compatible
(absent = empty). `Level` untouched. Migration is a no-op for existing rows (new optional field).

**6. Engine is a deterministic service behind MCP, not LLM reasoning.** `CharacterResolutionService`
loads the snapshot, fetches the templates from Postgres, projects the choice through the table, applies
the level/ability rules, and returns `{value, components[], provenance[], confidence}`. The chat LLM
picks the tool and renders; it never computes the rule. A `needsReview` component returns its prose
span (the structure-when-grounded / prose-when-not fallback).

**7. The breath-weapon composition (the actual rules):**
- ancestry row → `damageType`, `breathArea` (line vs cone), `saveAbility` (Dex/Con per ancestry).
- `breathDice` by tier table: L1–5 1d10 · L6–10 2d6 · L11–15 3d6 · L16–20 4d6.
- `saveDC = 8 + proficiencyBonus(totalLevel) + ConMod`. `proficiencyBonus` derives from level (existing
  helper or a small table). Each piece cites its source (ancestry table cell; breath-scaling prose;
  the racial-trait DC rule).

## Risks / Trade-offs

- **Hand-authored tables can drift from the books.** Mitigated by provenance (each cell cites its chunk;
  a reviewer can verify against the cited prose) and by keeping the authored set tiny (one race).
- **New EF tables + migration touch persistence.** Mitigated by the existing Testcontainers/Respawn
  persistence-test harness — the projection and resolver get real-Postgres integration tests.
- **The `feature` string ("breath weapon") is a stringly-typed entry point.** Acceptable for one
  feature; a typed feature catalog is a later concern (the read-router / query-catalog work).
- **Over-building the canonical Table model.** Mitigated by YAGNI — only columns/rows/cells/provenance
  and a row-referencing choice-set; no general schema, joins, or constraints this slice.
