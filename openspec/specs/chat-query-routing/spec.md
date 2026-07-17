# chat-query-routing Specification

## Purpose
TBD - created by archiving change chat-query-router. Update Purpose after archive.
## Requirements
### Requirement: The chat tool set SHALL be narrowed to the query's tool group when classification is confident

Before the chat LLM turn, the system SHALL classify the user's latest message to a tool group and offer the model only that group's tools plus the always-safe core. This narrowing SHALL apply only when the classifier's confidence meets the configured threshold; the LLM turn, tool execution, and streaming are otherwise unchanged.

#### Scenario: A character-resolution query offers the resolution tools
- **WHEN** the user asks "what's my breath weapon?" (a possessive/character-referential query) and confidence meets the threshold
- **THEN** the offered tool set is the `character-resolution` group's tools plus the always-safe core, not the full ~15-tool suite

#### Scenario: A structured-lookup query offers the entity tools
- **WHEN** the user asks "list all CR-5 flyers" (a set/quantifier query)
- **THEN** the offered tool set is the `structured-lookup` group (`search_entities`, `get_entity`) plus the always-safe core

### Requirement: A confident classification SHALL come from deterministic signals or an embedding backstop

The classifier SHALL first apply high-precision deterministic signals (possessive/character-referential → character-resolution; set/quantifier → structured-lookup; imperative-create → generation), treating a signal hit as maximally confident. When no signal fires, it SHALL embed the query via the existing embedding service and take the argmax cosine against per-group exemplar centroids as the group and confidence.

#### Scenario: A deterministic signal short-circuits the embedding path
- **WHEN** a query contains a character-referential signal (e.g. "my", "for my character")
- **THEN** it routes to `character-resolution` via the signal pass without an embedding call

#### Scenario: The embedding backstop handles a signal-free query
- **WHEN** a query fires no deterministic signal
- **THEN** the query is embedded and routed to the highest-cosine group, with that cosine as the confidence

### Requirement: The router SHALL never strand the model — safe full-set fallback

Every narrowed tool set SHALL include an always-safe core (the fused prose-search tool). When confidence is below the threshold, the classification is empty, or a tool is not in the group map, the system SHALL offer the full tool set (the pre-router behavior). The router SHALL only remove redundant tools when confident and SHALL never remove the always-safe core.

#### Scenario: Low confidence falls back to the full tool set
- **WHEN** the classifier's confidence is below the configured threshold
- **THEN** the full tool set is offered (identical to the behavior before this change)

#### Scenario: The always-safe core is always present in a narrowed set
- **WHEN** any group is offered under confidence
- **THEN** the offered set still includes the fused prose-search tool

#### Scenario: An unmapped tool is always offered
- **WHEN** a registered chat tool is not present in the tool-group map
- **THEN** it is treated as always-offered and is never hidden by narrowing

### Requirement: Each routing decision SHALL be observable

The system SHALL log/meter each routing decision with at least the chosen group, the confidence, the classification path (signal / embedding / fallback), and the offered-vs-total tool counts, so misroutes are auditable and the exemplars and threshold can be tuned from real usage.

#### Scenario: A routing decision is recorded
- **WHEN** the router narrows or falls back for a query
- **THEN** a structured record of `{group, confidence, path, offeredCount, totalCount}` is emitted

