## Why

The project's north-star goal is a D&D AI **companion agent** that plans characters, designs encounters, answers setting-aware lore queries, and answers provenance/publication questions ("which book introduced the artificer"). The current data layer can only do flat semantic search over book text blocks — it has no notion of typed entities (Class, Spell, Monster, etc.) and no structured fields to filter or compose with. As a result, the agent cannot answer queries like *"all CR-3 amphibian monsters"*, *"plan a swashbuckler rogue to level 15"*, or *"feats for a cleric/fighter multiclass"*: the data simply isn't there. This change introduces a typed, structured corpus alongside the existing block index so the future agent has the data it needs to plan, recommend, and reason — without which the MCP server and agent layers cannot be built meaningfully.

## What Changes

- **NEW** typed entity model with 17 types: Class, Subclass, Race, Subrace, Background, Feat, Spell, Weapon, Armor, Item, Magic Item, Monster, Trap, Disease/Poison, Vehicle/Mount, God, Plane, Faction, Location, Condition.
- **NEW** common entity envelope (`id`, `type`, `name`, `sourceBook`, `edition`, `page`, `firstAppearedIn`, `revisedIn[]`, `settingTags[]`, `canonicalText`, `fields`) with deterministic slug-style IDs (`<bookslug>.<type>.<entityslug>`) so cross-references between entities are stable and the agent can fetch by ID.
- **NEW** Tier 3 per-type field schemas — full progression data, not just flat fields. Class includes `featuresByLevel`, `subclasses`, `spellcasting`, `multiclass`, `asiLevels`. Monster includes structured `actions[]`, `traits[]`, `legendaryActions`, `lairActions`, `spellcasting`, `keywords[]` (powering descriptive queries like "amphibian"), `crNumeric` for sortable filtering. Other 15 types follow the same envelope-plus-fields pattern.
- **NEW** canonical JSON artifact per book at `data/canonical/<book>.json`, **checked into git**, hand-correctable, version-controlled. This is the source of truth for structured entities.
- **NEW** one-time LLM extraction pipeline that reads Docling output for a registered book and produces the canonical JSON. Runs once per book (or when re-extracting), not at every ingestion. Slow but bounded — the book set is small and stable.
- **NEW** `dnd_entities` Qdrant collection alongside the existing `dnd_blocks`. Each entity is embedded once via its `canonicalText`. Block index keeps serving prose/lore/rules-text queries; entity index serves typed-entity lookup, structured filtering, and build/plan workflows.
- **NEW** ingestion path that, given a registered book, reads the canonical JSON, embeds each entity, and upserts into `dnd_entities` with full structured payload. Idempotent like the existing block flow.
- **MODIFIED** ingestion orchestration (`/admin/books/{id}/ingest-blocks` and the registration flow) to optionally trigger entity extraction and entity-collection ingestion as additional pipeline stages.
- **NEW** retrieval endpoints for entity lookup by ID, entity vector search, and entity structured-filter queries (e.g. `?type=Monster&crNumeric_lte=3&keyword=amphibian`).

This change does **not** introduce the MCP server or agent layer — those are downstream. It is the data backbone that unblocks them.

## Capabilities

### New Capabilities

- `structured-entities`: The typed entity data model — envelope, ID/slug scheme, per-type field schemas for all 17 entity types, canonical JSON artifact format, provenance fields (`firstAppearedIn`, `revisedIn`), setting tags. The contract for what an entity record looks like.
- `entity-extraction-pipeline`: The one-time, per-book LLM extraction pipeline that consumes Docling output and produces canonical JSON conforming to the entity model. Includes orchestration, schema enforcement, error handling, and the workflow for re-running extraction when a schema or book changes.
- `entity-vector-store`: The `dnd_entities` Qdrant collection — collection schema, payload-index design for structured filters, ingestion of canonical JSON into the collection, retrieval endpoints (by-ID, vector, structured filter).

### Modified Capabilities

- `ingestion-pipeline`: Adds entity-extraction and entity-vector-store stages as pipeline steps that run alongside (or after) the existing block extraction/ingestion path. Ingestion orchestrator gains awareness of canonical JSON artifacts.
- `rag-retrieval`: Gains entity-aware retrieval semantics — the retrieval surface now exposes both `dnd_blocks` (existing) and `dnd_entities` (new), with the agent or caller picking the appropriate index per query type.

## Impact

- **Code:** New project areas under `Features/Entities/` (entity domain types + schemas), `Features/Ingestion/EntityExtraction/` (LLM extraction pipeline), `Features/VectorStore/Entities/` (Qdrant entity collection client). Modifications to ingestion orchestrator, retrieval endpoints, admin endpoints.
- **Data:** New `data/canonical/<book>.json` artifacts checked into git per registered book. Each is the hand-correctable ground truth.
- **Infra:** New Qdrant collection `dnd_entities`. Existing `dnd_blocks` unchanged.
- **Dependencies:** LLM extraction needs an Ollama (or remote LLM) call path with strict JSON-schema-constrained output. May reuse the existing Ollama client or add a stricter wrapper.
- **Performance:** One-time extraction is slow (hours per book) but happens out-of-band. Steady-state ingestion remains fast because it just reads the JSON.
- **Operational:** Adding a new book becomes a "register → run extraction → review/correct JSON → ingest" workflow rather than a single click. Re-extracting all books on schema change is a multi-hour batch job — bounded by book count (~10).
- **Out of scope (downstream):** MCP server, agent capability surface, rule-procedure tools (encounter math, multiclass rules), per-book "what this book added" summaries. These ride on top of this data backbone in subsequent changes.
