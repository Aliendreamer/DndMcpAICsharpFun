# fused-reranked-retrieval Specification

## Purpose
TBD - created by archiving change fused-reranked-retrieval. Update Purpose after archive.
## Requirements
### Requirement: Shared reranking service

The system SHALL provide a single reusable reranking service that, given a query, a list of candidates, a text selector, and a final top-N, returns the candidates reranked by the cross-encoder and truncated to top-N. All retrieval paths (prose, entity, fused) SHALL use this service rather than duplicating rerank logic. When reranking is unavailable (disabled or model missing), the service SHALL return the first N candidates unchanged.

#### Scenario: Service reranks and truncates

- **WHEN** the reranking service is called with 20 candidates and final top-N of 5, reranking enabled
- **THEN** it SHALL return 5 candidates ordered by cross-encoder score

#### Scenario: Disabled reranking is a stable passthrough

- **WHEN** reranking is disabled and the service is called with candidates and top-N of 5
- **THEN** it SHALL return the first 5 candidates in their original order, without invoking the model

### Requirement: Entity search is reranked

`EntityRetrievalService` entity search SHALL, when entity reranking is enabled, over-fetch a candidate pool from `dnd_entities`, rerank candidates by each entity's `canonicalText` via the shared reranking service, and return the requested top-K. When entity reranking is disabled, it SHALL fetch and return top-K by vector score with no reranking.

#### Scenario: Entity search returns reranked results

- **WHEN** entity reranking is enabled and a search requests top-K of 10
- **THEN** the service SHALL fetch the configured candidate pool, rerank by `canonicalText`, and return the top 10 by rerank score

#### Scenario: Entity reranking can be disabled

- **WHEN** `RerankEntities` is false
- **THEN** entity search SHALL return top-K by vector score without calling the reranker

### Requirement: Reranking is tunable per channel and pool size

`RerankerOptions` SHALL expose a global `Enabled` kill-switch, per-channel `RerankBlocks` and `RerankEntities` toggles (default true), and a `CandidatePoolSize` (default 20) controlling how many candidates are fetched before reranking. When `Enabled` is false, neither channel reranks regardless of the per-channel flags.

#### Scenario: Global kill-switch overrides channel flags

- **WHEN** `Enabled` is false and `RerankEntities` is true
- **THEN** entity search SHALL NOT rerank

#### Scenario: Candidate pool size is honored

- **WHEN** `CandidatePoolSize` is 30 and a reranked search runs
- **THEN** up to 30 candidates SHALL be fetched before reranking

### Requirement: Fused cross-channel retrieval

The system SHALL provide a fused retrieval that embeds the query once, fetches candidate pools from both `dnd_blocks` and `dnd_entities`, reranks the combined set with the cross-encoder via the shared reranking service, and returns one merged top-K list. Each returned result SHALL carry a `source` tag identifying it as `prose` or `entity`, along with its identifier and snippet.

#### Scenario: Fused result mixes both sources, jointly ranked

- **WHEN** fused retrieval runs for a query that matches both a prose chunk and an entity
- **THEN** the returned list SHALL contain both, ordered by a single cross-encoder reranking over the union

#### Scenario: Each fused result is source-tagged

- **WHEN** fused retrieval returns results
- **THEN** every result SHALL have `source` equal to `prose` or `entity`

#### Scenario: Fused result respects top-K

- **WHEN** fused retrieval is asked for top-K of 8
- **THEN** at most 8 results SHALL be returned

