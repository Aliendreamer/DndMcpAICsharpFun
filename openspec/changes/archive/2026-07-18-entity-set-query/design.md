## Context

`dnd_entities` (Qdrant) holds ~2300 typed entities with indexed payload fields: `type`, `cr_numeric`, `spell_level`, `damage_type`, `keywords`, `source_book`, `srd`/`srd52`/`basic_rules_2024`. The read path has two tools: `search_entities` (vector similarity + payload filter, top-K) and `get_entity` (by id). Neither returns a **complete set** — `search_entities` ranks by cosine and caps at K, so "all CR-5 flyers" silently drops matches beyond K and orders by similarity to a query string that a set query doesn't even have.

`QdrantEntityVectorStore` already does filter-only paginated retrieval via `ScrollAsync` (`GetByIdsAsync`, `ScrollAllAsync`) and the Qdrant client exposes `CountAsync(filter)`. So the completeness primitive already exists in the substrate — this slice exposes it as a first-class query.

## Goals / Non-Goals

**Goals:**

- Answer a structured filter query with the **complete** matching set (deterministic, filter-only — no similarity ranking, no silent K-truncation).
- Compact results (a set is a list of names, not a wall of stat blocks); the user drills into any row with `get_entity`.
- Honest counts: report the true total even when the returned rows are capped.

**Non-Goals:**

- JOINs (spell↔class "spells a Wizard can learn") and multi-attribute aggregation over the entity `fields` JSON — deferred; they need relationship modeling / a richer (Postgres) substrate.
- Changing `search_entities` (stays semantic top-K) or any existing retrieval behavior.
- New storage — Qdrant Scroll + Count over existing indexed fields is sufficient.

## Decisions

**D1 — Completeness = Count + Scroll, filter-only.** `ListByFilterAsync(filters, cap)`: `client.CountAsync(collection, BuildFilter(filters))` → `Total`; `client.ScrollAsync(collection, filter, limit: cap)` → the rows. No `queryVector` — order is Qdrant's scroll order (stable, id-based), not similarity. Reuses the store's existing `BuildFilter`. A **max-scan/cap guard** bounds a pathological "all entities" request.

**D2 — Compact row shape.** `EntitySetRow { Id, Name, Type, SourceBook, Page, Cr?, SpellLevel?, DamageType? }` — the discriminating fields only, projected from the scrolled payload; **no `canonicalText`, no full `fields`**. A 100-row set is 100 one-liners; the LLM/user fetches detail via `get_entity`. Rationale: set queries answer "which ones", not "everything about each".

**D3 — Honest total-vs-returned.** `EntitySetResult { int Total, int Returned, IReadOnlyList<EntitySetRow> Rows }` with `Returned = min(Total, cap)`. When `Total > Returned`, the tool/endpoint response makes truncation explicit ("137 match; showing 50 — narrow the filter"). Never a silent cut. Default cap ~50; a hard maximum caps the scroll.

**D4 — A distinct tool, not an overloaded one.** New `list_entities` MCP tool with complete-set semantics; `search_entities` unchanged. Overloading `search_entities` with a `complete=true` flag would force the 8B to learn *when* to flip it — a new misroute surface the router can't help. Two clearly-named tools (`list_entities` = "all/every/how many", `search_entities` = "find me a…") route cleanly. `list_entities` is added to the router's `structured-lookup` group.

**D5 — Public endpoint parity.** `GET /retrieval/entities/list` over the same service (rate-limited like `/retrieval/entities/search`), with `.http` + `.insomnia` updated in the same commit (contract rule). Aids testing/curl and keeps the retrieval surface consistent.

## Risks / Trade-offs

- **Huge sets** ("all monsters" ≈ 1092) → Mitigation: D3 cap + honest total; the LLM is told to narrow. `CountAsync` is O(index), not a full scan; Scroll only fetches `cap` rows.
- **"Flyers"/trait filters are keyword-based** — "flying" is a `keywords` (traitTag) match, not a dedicated field; so completeness is only as good as the keyword coverage. Acceptable: same coverage `search_entities` already exposes; documented in the tool description.
- **Order isn't semantic** — filter-set order is scroll order, not relevance. That's correct for a set (there is no query to be relevant to); the compact rows let the reader scan.
- **Two set-ish tools could still confuse the 8B** → Mitigation: sharp, contrasting tool descriptions + the router narrowing to the structured-lookup group (both live there, so a mispick between them still lands in-group and both are set/lookup tools).
