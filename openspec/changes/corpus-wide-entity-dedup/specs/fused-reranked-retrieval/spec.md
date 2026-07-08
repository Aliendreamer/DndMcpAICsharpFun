## MODIFIED Requirements

### Requirement: Fused cross-channel retrieval

The system SHALL provide a fused retrieval that embeds the query once, fetches candidate pools
from both `dnd_blocks` and `dnd_entities`, collapses duplicate entity candidates by dedup key
before fusion and reranking, reranks the combined set with the cross-encoder via the shared
reranking service, and returns one merged top-K list. Duplicate entity candidates — those
sharing a dedup key `(EntityNameIndex.Normalize(name), Type, Edition)` — SHALL be collapsed to a
single representative chosen by `DuplicateResolver`, and that representative SHALL carry the
maximum similarity score among the collapsed group so its rank reflects the best-matching
duplicate. Prose (`dnd_blocks`) candidates SHALL NOT be collapsed. Each returned result SHALL
carry a `source` tag identifying it as `prose` or `entity`, along with its identifier and
snippet.

#### Scenario: Fused result mixes both sources, jointly ranked

- **WHEN** fused retrieval runs for a query that matches both a prose chunk and an entity
- **THEN** the returned list SHALL contain both, ordered by a single cross-encoder reranking over the union

#### Scenario: Each fused result is source-tagged

- **WHEN** fused retrieval returns results
- **THEN** every result SHALL have `source` equal to `prose` or `entity`

#### Scenario: Fused result respects top-K

- **WHEN** fused retrieval is asked for top-K of 8
- **THEN** at most 8 results SHALL be returned

#### Scenario: Duplicate entities collapse to one representative

- **WHEN** the entity candidate pool contains two entities sharing a dedup key (same normalized
  name, type, and edition, different id)
- **THEN** only the `DuplicateResolver` winner SHALL enter fusion, carrying the maximum
  similarity score of the two, and the loser SHALL NOT appear in the results

#### Scenario: Distinct editions both survive collapse

- **WHEN** the entity candidate pool contains the same-named, same-typed entity in both
  `Edition2014` and `Edition2024`
- **THEN** both SHALL survive collapse as separate candidates
