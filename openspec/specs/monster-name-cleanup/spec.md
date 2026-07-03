# monster-name-cleanup Specification

## Purpose
TBD - created by archiving change mm-canonical-name-cleanup. Update Purpose after archive.
## Requirements
### Requirement: In-place canonical monster-name cleanup transform

The system SHALL provide a one-time transform that rewrites stat-line-garbled canonical Monster names to their clean
5etools canonical form WITHOUT re-extraction. For each Monster entity in a book's canonical it MUST resolve the
entity's stored name through the SAME name matcher and id generator the extraction pipeline uses (`EntityNameMatcher`
with stat-line stripping, `EntityIdSlug.For`), and when that resolves to a canonical name different from the stored
name it MUST rewrite the entity's `name` to the clean form and recompute its `id` accordingly, preserving all other
fields (stat-block `fields`, `dataSource`, flags). The transform MUST be gap-only (an entity whose name already
matches its clean canonical form is left unchanged) and idempotent (a second run makes no further changes). It MUST
NOT touch non-Monster entities. It MUST be exercised through a developer console rather than an HTTP endpoint.

#### Scenario: Garbled dragon name rewritten to clean canonical, id recomputed

- **WHEN** the transform runs over a canonical holding a grounded Monster named
  `ANCIENT BLACK DRAGON Gargantuan dragon, chaotic evil`
- **THEN** that entity's `name` becomes `Ancient Black Dragon`, its `id` is recomputed via the same slug logic
  (e.g. `mm14.monster.ancient-black-dragon`), and its stat-block `fields` and `dataSource` are preserved unchanged

#### Scenario: Clean names and non-monsters are untouched, run is idempotent

- **WHEN** the transform runs over a canonical whose Monster names are already clean (e.g. `Dragon Turtle`) and which
  contains non-Monster entities
- **THEN** no Monster name or id changes, no non-Monster entity changes, and running the transform a second time
  produces no further changes

### Requirement: De-duplicate cleaned monsters against backfilled duplicates

The transform MUST de-duplicate: when rewriting a grounded Monster name produces a name that collides (by normalized
name) with an existing `5etools-backfill` entity, it MUST drop the backfill duplicate and keep the grounded entity
(the real, book-sourced record). It MUST NOT delete a grounded entity. When two or more grounded entities collide on
the cleaned name (rare), it MUST designate a single winner (preferring an entity already carrying the clean canonical
id, otherwise the first in order), rewrite the winner to the clean name + canonical id, and for every other colliding
grounded entity leave its ORIGINAL id and name unchanged while setting `NeedsReview = true` — so the flagged duplicate
keeps a distinct id rather than colliding with the winner. The resulting entity list MUST contain no duplicate ids
(the canonical must remain loadable). It MUST report counts of names cleaned, backfill duplicates dropped, and
grounded collisions flagged.

#### Scenario: Cleaned grounded dragon drops its backfill duplicate

- **WHEN** the canonical holds a grounded garbled `ANCIENT BLACK DRAGON Gargantuan dragon, chaotic evil` and a
  separate `5etools-backfill` entity named `Ancient Black Dragon`
- **THEN** after the transform a single `Ancient Black Dragon` remains — the grounded (now-clean) entity — the
  `5etools-backfill` duplicate is removed, and the report counts one cleaned and one backfill duplicate dropped

#### Scenario: Grounded-vs-grounded collision keeps a unique winner and flags the other

- **WHEN** two distinct grounded entities both resolve to the same clean canonical name (in either source order)
- **THEN** exactly one winner holds the clean name + canonical id, the other is retained with `NeedsReview = true`
  and its original distinct id (not deleted, not id-colliding), the two entities have different ids, and the report
  counts one grounded collision flagged

#### Scenario: Cleaned canonical contains no duplicate ids

- **WHEN** the transform runs over any canonical (including one with grounded-vs-grounded collisions)
- **THEN** every entity in the output list has a unique id, so the written canonical re-loads without a duplicate-id
  error

### Requirement: Cleanup preserves recall completeness and improves grounding ratio

Applying the transform to a book that previously reached full monster recall MUST leave recall complete (no monster
becomes missing) while raising the grounded-to-backfilled ratio, because grounded entities that were counted as
`extra` (garbled) and separately backfilled (clean) collapse into a single grounded entity.

#### Scenario: Monster Manual stays at full recall with a better grounding ratio

- **WHEN** the transform is applied to the Monster Manual canonical and the monster recall check is re-run
- **THEN** monster recall remains complete (0 missing, still 450/450) and the grounded : backfilled ratio improves
  over the pre-cleanup 337 : 152

