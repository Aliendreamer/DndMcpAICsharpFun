## Context

The project has two ingestion paths:

1. **Legacy chunking path** — `IngestionBackgroundService` polls every 24 hours for `Pending` / `Failed` records and calls `IngestBookAsync` (PdfPig extract → chunk → embed → Qdrant).
2. **LLM extraction path** — operator calls `/extract` then `/ingest-json` to classify pages, extract typed entities, and embed them.

When a book is registered via `POST /admin/books/register` or `POST /admin/books/register-path`, a `Task.Run` block immediately fires the legacy pipeline. This conflicts with the LLM extraction workflow: every registration auto-runs the old path regardless of the operator's intent.

## Goals / Non-Goals

**Goals:**
- Book registration saves the file and creates a `Pending` record — nothing more.
- All pipeline execution is explicit: `/reingest` for the legacy path; `/extract` + `/ingest-json` for LLM extraction.
- Remove dead code: `IngestionBackgroundService`, its DI wiring, and its tests.

**Non-Goals:**
- No changes to `/reingest`, `/extract`, `/ingest-json`, or `IngestBookAsync` / `ExtractBookAsync` / `IngestJsonAsync` logic.
- No changes to the HTTP request/response shapes or authentication.
- No new retry or scheduling mechanism to replace the background service.

## Decisions

### Remove the background service entirely rather than gating it behind a flag

**Chosen:** Delete `IngestionBackgroundService` and its registration unconditionally.

**Alternatives considered:**
- Feature flag (`Ingestion:AutoIngest: false`) — adds configuration surface and keeps dead code alive. Not worth it; the explicit-trigger model is the intended design going forward.
- Keep background service but disable auto-fire on register — splits the "auto-ingest" behavior across two places, making the intent harder to follow.

**Rationale:** The simplest path is the correct one. Operators have `/reingest` for the legacy path; there is no scenario where silent background polling is preferable to explicit control.

### Remove `IServiceScopeFactory` from both register handlers

The only reason both handlers injected `IServiceScopeFactory` was to create a fresh DI scope for the fire-and-forget `Task.Run`. With that block removed, the parameter is dead. Removing it shrinks the handler signatures and makes the no-auto-fire contract obvious at the call site.

## Risks / Trade-offs

- **Operators must now explicitly trigger ingestion** → Documented in API reference and README. New books sit `Pending` until an operator acts.
- **Existing deployments with books stuck in `Pending`** → Operators call `/reingest` on any `Pending` book to resume the legacy path, or `/extract` + `/ingest-json` for LLM extraction.

## Migration Plan

No data migrations or Qdrant changes. Deployment steps:

1. Deploy the updated image.
2. For any book stuck in `Pending` status: call `POST /admin/books/{id}/reingest` (legacy path) or start LLM extraction with `POST /admin/books/{id}/extract`.
3. No rollback complexity — the endpoints and data model are unchanged.
