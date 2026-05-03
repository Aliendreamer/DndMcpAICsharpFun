## Context

The registration form for `/admin/books/register` accepts `sourceName`, `version`, `displayName`, `bookType`, and the file. Of these, every field except `sourceName` flows through to retrieval-time payloads and filters. `sourceName` is stored on the SQLite record and shown in the admin list, but never makes it into a Qdrant block point and is never read by the orchestrator, embedding service, or retrieval service. It's a leftover from an older shape of the pipeline.

## Goals / Non-Goals

**Goals:**
- Remove `sourceName` from the registration surface and from the SQLite schema.
- Keep all other fields and behaviours unchanged.
- Make the EF migration idempotent and reversible (`Down` re-adds the column as nullable).

**Non-Goals:**
- Adding a new short-form tag in its place. If MCP design surfaces a real need for one, it's a separate change.
- Migrating the `sourceName` data anywhere. The data is dropped on migration. It was never used; nothing depends on it.

## Decisions

**1. Hard remove rather than deprecate.**
Alternative considered: keep the column with `[Obsolete]` and stop reading it. Rejected — keeping a column to honour zero readers is the worst of both worlds (data, schema noise, no actual capability). The whole point is to shed dead surface.

**2. Multipart parser silently ignores unknown fields.**
The current `RegisterBook` only switches on the four field names it cares about; anything else is dropped. After we delete the `case "sourceName":` branch, callers who still send the field get HTTP 202 as before with no record of the value. No need to add explicit "unknown field" rejection — the existing behaviour is the right one.

**3. Migration drops the column.**
SQLite supports column drop directly in modern EF Core via `DropColumn`. Existing data is gone. Reversible via the migration's `Down`, which adds it back as a nullable string with no data restored.
