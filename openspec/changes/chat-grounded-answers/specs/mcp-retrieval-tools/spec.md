## MODIFIED Requirements

### Requirement: search_entities tool performs structured entity search
The server SHALL expose a `search_entities` MCP tool that queries `IEntityRetrievalService` and returns matching entity records. A result whose name occurs in the query as whole words SHALL come before the others, the longest name first; Rule and Lore results only when the query is exactly their name, since questions hold words such as "attack" or "hit points". The first three results SHALL return wide text: up to 3,000 characters for a named result and up to 1,500 for another. Longer text SHALL be cut to a window that holds the best-ranked piece. Every other result SHALL return a snippet of 240 characters. The tool SHALL NOT take SRD filters: no indexed entity carries the flags.

Parameters:

- `query` (string, required) — search text
- `type` (string, optional) — entity type e.g. `"Spell"`, `"Monster"`, `"Class"`, `"Subclass"`
- `keyword` (string, optional) — trait tag keyword e.g. `"Amphibious"`, `"Pack Tactics"`
- `crMax` (double, optional) — maximum challenge rating (for monsters)
- `spellLevel` (int, optional) — spell level filter
- `topK` (int, optional, default 6) — number of results

#### Scenario: Basic entity search returns results

- **WHEN** a client calls `search_entities` with a non-empty `query`
- **THEN** the tool returns up to `topK` entity records with id, name, type, sourceBook, page (when known), edition, text and score

#### Scenario: search_entities with type filter narrows results

- **WHEN** a client calls `search_entities` with `query` and `type="Monster"`
- **THEN** only Monster entities are returned

#### Scenario: search_entities with crMax filter narrows monster results

- **WHEN** a client calls `search_entities` with `type="Monster"` and `crMax=2`
- **THEN** only monsters with CR ≤ 2 are returned

#### Scenario: search_entities with invalid type returns empty gracefully

- **WHEN** a client calls `search_entities` with an unrecognised `type` string
- **THEN** the tool returns an empty result list without throwing

#### Scenario: A named entity comes first with its whole text

- **WHEN** the query is "what are the armor class and hit points of the tarrasque?" and other entities rank above the Tarrasque
- **THEN** the Tarrasque is the first result and its text is not cut at 240 characters

#### Scenario: A long entity is cut around the match

- **WHEN** a named entity's text is longer than 3,000 characters and the best-ranked piece lies deep in it
- **THEN** the returned text holds that piece and is at most 3,000 characters plus ellipses

## ADDED Requirements

### Requirement: Retrieval results carry the printed page
Every chat-facing retrieval result (`search_entities`, `list_entities`, `get_entity`, `search_dnd`, `search_lore`, and the passages of `ask_rules`, `ask_setting_lore` and `plan_downtime`) SHALL carry the page printed in the book, derived from the stored PDF page and the book's offset in `BookCatalog`. When the offset is not known, or the stored page lies before printed page 1, no page SHALL be given. `search_dnd` results SHALL also name their source book.

#### Scenario: A stored PDF page becomes the printed page

- **WHEN** a result comes from the Player's Handbook and its stored page is 204
- **THEN** its page is 203

#### Scenario: A book without a known offset gives no page

- **WHEN** a result comes from the System Reference Document
- **THEN** it carries no page
