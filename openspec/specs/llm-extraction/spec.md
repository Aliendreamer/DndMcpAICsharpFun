# llm-extraction

## Purpose

This capability previously specified an LLM-driven per-page entity extraction pipeline (`Pass 1: classify → Pass 2: extract → JSON-on-disk → Stage 2 embed/upsert`). It was removed in `archive/2026-05-03-remove-llm-ingestion-path/` after empirical results showed it produced sparse, scrambled output on real D&D rulebooks while costing hours of inference per book on local hardware.

The active ingestion path is the no-LLM `block-ingestion` capability (Docstrum layout blocks + bookmark-derived metadata, embedded directly into Qdrant). This spec is retained as an inactive placeholder so that future work — for example, structured-entity extraction to power a frontend that displays spell/monster cards — can re-introduce typed entity extraction as an opt-in feature targeting its own Qdrant collection without conflicting with the live block path.
## Requirements
### Requirement: LLM-based ingestion is intentionally absent
The system SHALL NOT perform LLM-based entity extraction during ingestion. There is no `OllamaLlmEntityExtractor`, no per-page JSON store, no LLM-related admin endpoint (`/extract`, `/ingest-json`, `/extract-page`, `/cancel-extract` are all unregistered), and no `Ollama:ExtractionModel` configuration. The only Ollama interaction during ingestion or retrieval is the embedding model.

#### Scenario: No LLM call is made during block ingestion

- **WHEN** `POST /admin/books/{id}/ingest-blocks` runs to completion against a registered PDF
- **THEN** the only Ollama HTTP requests issued are `POST /api/embed` for the configured `Ollama:EmbeddingModel`; no `POST /api/chat` or `POST /api/generate` is sent

#### Scenario: Removed admin endpoints are not registered

- **WHEN** the application starts and the route table is enumerated
- **THEN** `/admin/books/{id}/extract`, `/admin/books/{id}/ingest-json`, `/admin/books/{id}/extract-page/{n}`, and `/admin/books/{id}/cancel-extract` are absent

### Requirement: Extraction writes source field from registered source key
When `POST /admin/books/{id}/extract-entities` runs and the book's `IngestionRecord` has a non-null `FivetoolsSourceKey`, each entity written to the canonical JSON SHALL have its `source` field set to that key. When `FivetoolsSourceKey` is null, `source` SHALL remain empty.

#### Scenario: Extraction with bound source key

- **WHEN** extraction runs for a book with `FivetoolsSourceKey="XDMG"`
- **THEN** every entity in the output canonical JSON has `"source": "XDMG"`

#### Scenario: Extraction without source key

- **WHEN** extraction runs for a book with `FivetoolsSourceKey=null`
- **THEN** entity `source` fields are empty strings (existing behaviour unchanged)

### Requirement: Extraction derives edition from source key via registry
When `FivetoolsSourceKey` is non-null, the entity `edition` field SHALL be derived from the `PublishedYear` in `BookSourceRegistry`: year ≥ 2024 → `"Edition2024"`, year < 2024 → `"Edition2014"`.

#### Scenario: 2024 source key sets Edition2024

- **WHEN** extraction runs for a book with `FivetoolsSourceKey="XPHB"` (published 2024)
- **THEN** all extracted entities have `"edition": "Edition2024"`

#### Scenario: 2014 source key sets Edition2014

- **WHEN** extraction runs for a book with `FivetoolsSourceKey="PHB"` (published 2014)
- **THEN** all extracted entities have `"edition": "Edition2014"`

