## MODIFIED Requirements

### Requirement: Entity search is reranked

`EntityRetrievalService` entity search SHALL, when entity reranking is enabled, over-fetch a candidate pool from `dnd_entities`, rerank candidates by the best-scoring piece of each entity's `canonicalText` via the shared reranking service, and return the requested top-K. When entity reranking is disabled, it SHALL fetch and return top-K by vector score with no reranking.

#### Scenario: Entity search returns reranked results

- **WHEN** entity reranking is enabled and a search requests top-K of 10
- **THEN** the service SHALL fetch the configured candidate pool, rerank by each candidate's best piece, and return the top 10 by rerank score

#### Scenario: Entity reranking can be disabled

- **WHEN** `RerankEntities` is false
- **THEN** entity search SHALL return top-K by vector score without calling the reranker
