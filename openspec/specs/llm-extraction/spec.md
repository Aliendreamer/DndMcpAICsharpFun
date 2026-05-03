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
