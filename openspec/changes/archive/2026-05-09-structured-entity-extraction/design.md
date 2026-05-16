## Context

The current ingestion pipeline produces only flat text blocks indexed in the `dnd_blocks` Qdrant collection with simple metadata (source_book, category, page, section, book_type, edition). This serves *prose retrieval* well — "tell me about grappling", "what does Mystra do" — but it cannot answer the four conversation classes that define the project's north-star companion-agent goal:

1. **Character planning** ("plan a swashbuckler rogue", "scale to level 15") — needs per-level class/subclass features, ASI/feat slots, multiclass rules, equipment choices.
2. **Encounter design** ("3 amphibian monsters for level-5 party") — needs typed monster records with CR, type, descriptive keywords, structured action data, plus DMG encounter math.
3. **Setting-aware lore + ranking** ("Eberron gods by influence, good/neutral") — needs setting tags on every entity plus structured alignment / sphere data.
4. **Provenance** ("which book introduced the artificer") — needs first-appearance metadata on every entity.

None of those queries are satisfiable by vector search alone. They require **typed entities with structured fields**.

A previous LLM-based extraction path was removed (see `archive/2026-05-03-remove-llm-ingestion-path/`) because it ran at every ingest, was slow (hours per book), low quality on small local models, and had no hand-correction surface. The new design re-introduces LLM extraction but with three architectural changes that make it sustainable: it runs **once per book** (not per ingest), produces a **checked-in JSON artifact** that is hand-correctable, and its output **complements** rather than replaces the existing block index.

## Goals / Non-Goals

**Goals:**

- Produce a typed, structured corpus covering 20 entity types (Class, Subclass, Race, Subrace, Background, Feat, Spell, Weapon, Armor, Item, Magic Item, Monster, Trap, Disease/Poison, Vehicle/Mount, God, Plane, Faction, Location, Condition) with Tier 3 fidelity — full progression data, not just flat fields.
- Make the structured data **hand-correctable** via a checked-in canonical JSON artifact per book, so LLM extraction errors are fixable in PRs rather than invisible.
- Add an `dnd_entities` Qdrant collection that stores embedded entities for vector lookup and structured-filter queries, alongside the unchanged `dnd_blocks` collection.
- Provide ID-based exact lookup (`get_entity_by_id`) for build/plan workflows where the agent needs the canonical Rogue or Swashbuckler record.
- Support cross-entity references via stable slug IDs (`phb14.class.fighter`), so the agent can follow links (e.g., Class → Subclass → Feature → Spell).
- Capture **provenance** (`firstAppearedIn`, `revisedIn[]`) and **setting tags** (`settingTags[]`) on every entity so the agent can answer "which book added X" and "Eberron-only deities".
- Be re-runnable: re-extracting a book or all books on a schema change is a bounded batch job (≈10 books × hours-each).

**Non-Goals:**

- No MCP server in this change. The MCP layer rides on top of this data backbone in a subsequent change.
- No agent capability surface (encounter math, multiclass planner, character-builder logic). These are separate downstream changes — this change just provides the data they require.
- No replacement of the `dnd_blocks` index. Block-level retrieval continues serving prose queries.
- No multi-book unification (e.g., merging "Fighter" from PHB14 and PHB24 into one record). Each book's records stand independently with their own slug; cross-edition reconciliation is a future concern.
- No NPC entity type. NPC data isn't standardised in books; deferred.
- No "what this book added" book-level summaries. The `revisedIn[]` field on entities supports the data, but emitting natural-language change summaries per book is downstream.

## Decisions

### D1. Canonical JSON artifact per book, checked into git

Each book that's been entity-extracted gets a `data/canonical/<book-slug>.json` file committed to the repo. This is the single source of truth for that book's structured entities. Ingestion reads it; LLM extraction produces it; humans correct it.

**Why over alternatives:**

- *Computed-at-ingest, no artifact:* LLM mistakes are invisible — only discoverable via wrong agent answers, with nowhere to fix them. Rejected.
- *Database-only canonical (SQLite):* loses git history, diffability, code-review of corrections. Schema changes require migrations.
- *Database with JSON export:* doubles machinery and creates a sync question.

The checked-in JSON serves as a hand-correctable, version-controlled, diffable knowledge artifact that's *itself* a deliverable beyond this app.

**Trade-offs:** Repo size grows with each ingested book (estimate: 2–5 MB per book post-pruning). Acceptable for a hobby project's lifetime book set. Adding a new book becomes a "register → run extraction → review JSON in PR → ingest" workflow, not a single click — accepted as the cost of correctability.

### D2. One-time LLM extraction per book

Extraction runs as an explicit admin-triggered job per book, **not** as part of every `ingest-blocks` call. The job consumes the same Docling output that block ingestion uses (so no double layout-analysis pass), runs the LLM with strict per-content-type schemas, and writes the canonical JSON.

**Why over alternatives:**

- *Per-block LLM at every ingest:* recreates the slow-ingest problem we just escaped.
- *Pure deterministic regex parsers:* breaks on every supplement that formats stat blocks slightly differently. Tier 3 prose-heavy fields (Class.featuresByLevel, Subclass features) are unreasonable for regex.
- *Hybrid (parsers + LLM fallback) at ingest:* still slow per ingest; complexity for marginal gain over D2.

Once-per-book amortises the cost: the book set is small (~10 books) and the schema rarely changes, so total LLM cost over the project's lifetime is bounded.

### D3. Dual-collection vector store: `dnd_entities` + `dnd_blocks`

Two separate Qdrant collections:

- `dnd_blocks` (unchanged) — continues serving prose/lore/rules-text queries.
- `dnd_entities` (new) — one point per entity, embedded via the entity's `canonicalText`. Payload carries the full envelope (id, type, name, sourceBook, edition, page, firstAppearedIn, revisedIn, settingTags) plus a flattened/indexed subset of the type-specific fields for filtering (e.g., `crNumeric`, `spellLevel`, `damageType`).

**Why over alternatives:**

- *Single collection with mixed records:* makes it hard to scope vector searches and confuses payload schemas.
- *Entities-only (replace blocks):* loses retrieval over rules-prose, adventure narrative, and setting lore that doesn't fit a clean entity schema.

Different query shapes need different indexes. Agent (or caller) picks the right one per query type — and can union when needed (e.g., "tell me about the Sword Coast" hits blocks; "List swashbuckler features at level 5" hits entities).

### D4. Stable slug ID scheme: `<book-slug>.<type>.<entity-slug>`

Entity IDs are deterministic from `(book, type, name)`: `phb14.class.fighter`, `mm14.monster.aboleth`, `tasha.subclass.swashbuckler`. They are *not* random GUIDs.

**Why:**

- Cross-references between entities (Class → Subclass refs, Background → Skill refs, Class → Spell List ref) need stable IDs.
- The agent can use IDs in its outputs and the user can deep-link to specific entities.
- Re-running extraction on the same book produces the same IDs (idempotency).

**Trade-offs:** PHB14 and PHB24 each have a "Fighter" record with separate IDs (`phb14.class.fighter` and `phb24.class.fighter`). Cross-edition unification is left to the agent or to future reconciliation logic.

### D5. Common envelope + per-type `fields` block

Every entity record has the same outer envelope (id, type, name, sourceBook, edition, page, firstAppearedIn, revisedIn[], settingTags[], canonicalText, fields), with type-specific fields nested under `fields`. The Class and Monster `fields` schemas were ratified during brainstorming as exemplars (see `proposal.md`); the other 15 follow the same pattern.

**Why:**

- Common envelope makes generic operations (lookup-by-id, vector search, provenance queries) work uniformly across types.
- Per-type fields avoid forcing all 20 types into one bloated shared schema.

### D6. `canonicalText` is the embedded representation

Each entity has a `canonicalText` field containing a deterministic textual rendering of the entity (e.g. for a Spell, the full spell text including header + body + at-higher-levels; for a Class, a textual summary plus a rendering of the level table; for a Monster, the stat block as text). This text is what the embedding model sees — not the raw block text from the PDF.

**Why:**

- Decouples the embedded representation from PDF-extraction noise.
- Lets us tune the canonical-text format per type for retrieval quality.
- Agents querying the entity index get clean, structured text plus the structured `fields`.

### D7. `keywords[]` are free-form, hand-curated

Descriptive keywords (e.g., `amphibian` on Bullywug, `aquatic` on Sahuagin) are not derivable from the standard creature-type taxonomy. The LLM proposes them during extraction; humans correct them in the canonical JSON. No controlled vocabulary at this stage.

**Trade-off:** keyword inconsistency across books is possible. If it becomes a problem, we'll add a lint pass over canonical JSON to normalize. For now, accepting the freedom over the rigour.

### D8. Spellcasting block reused across Class and Monster

Both Class and Monster carry an optional `spellcasting` block with the same shape (`type`: full|half|third|pact|innate|none, `ability`, `spellList`, `spellSlotsByLevel`, etc.). This means agent code that reasons about "what can this thing cast" works identically for a player Wizard and a Drow Mage.

### D9. Action `type` enum

Monster (and any entity with structured actions) tags each action with a typed `type` field: `multiattack | melee_weapon_attack | ranged_weapon_attack | melee_or_ranged_weapon_attack | save | passive | other`. Combined with optional `recharge`, this lets the agent answer structural queries like "monsters with breath weapons" (`actions[].type==save && recharge!=null`).

### D10. Provenance via `firstAppearedIn` + `revisedIn[]`

Every entity carries:

- `firstAppearedIn: { book, edition, page? }` — where the entity *first appeared* in any D&D edition (may differ from `sourceBook` when the record came from a later reprint).
- `revisedIn[]: [{ book, edition, summary }]` — every meaningful revision.

This powers "which book introduced the artificer" and "what did Tasha's add to the ranger". For 5e-only content, `firstAppearedIn` may equal `sourceBook`.

### D11. Setting tags on every entity

`settingTags: ["core" | "forgotten-realms" | "eberron" | "ravnica" | "wildemount" | ...]` (free-form array). Powers "Eberron gods" filters. `["core"]` for content tied to no specific setting.

### D12. Schema versioning on canonical JSON

Each canonical JSON file carries a top-level `schemaVersion: "1"` field. Ingestion validates it before reading. Schema bumps trigger re-extraction (or a migration script). This avoids silent drift between the LLM-extracted JSON and the consumer code.

### D13. Extraction LLM: Claude (remote API)

Plan 2's extraction workhorse is Claude (default model: **Claude Sonnet** for cost/quality balance; Opus reserved as a spot-recovery escalation if Sonnet repeatedly fails on a specific entity). The implementation lives behind an `IEntityExtractionLlmClient` abstraction so the model is swappable.

**Why over alternatives:**

- *Local Ollama with a stronger 14B–70B model:* free, but 4–12 hours per book on consumer hardware and meaningfully worse at nested-schema structured extraction (this is exactly the failure mode that motivated removing the previous Ollama-based extractor in `archive/2026-05-03-remove-llm-ingestion-path/`). Hand-correction load would dominate.
- *Hybrid local-first + remote fallback:* the entries that need the remote fallback are precisely the high-value Tier 3 ones (Class.featuresByLevel, Monster.legendaryActions). Falling back doesn't help if those are routinely the failure case. Two clients to maintain for marginal gain.

**Cost envelope:** ~$5–20 per book at typical rulebook size; ~$50–150 lifetime cost for the bounded ~10-book corpus. One-time per book.

**Pre-flight check at implementation time:** confirm the API tier supports programmatic Messages API access. If only chat-UI access is available, switch to a per-token API-key model immediately; the abstraction layer absorbs the change.

### D14. Per-type JSON Schemas generated from C# records

Per-entity-type JSON Schema files live at `Schemas/canonical/<TypeName>Fields.schema.json`, generated from each `<Type>Fields` record via **NJsonSchema** at build time. Schemas are checked into git so reviewers can see what the LLM is constrained against. The C# record remains the single source of truth; the schema is its derived artifact.

**Two-layer enforcement:** at extraction time the schema is passed to Claude as a tool-input constraint (Claude's structured-output feature uses the schema to constrain generation). On the way back, the loader validates raw JSON against the schema *before* deserializing into the C# record, so schema drift between record and schema surfaces immediately.

**Per-type hand-overrides allowed.** If the generated schema is awkward for a specific type (notably enum-string fields like `ActionType` where NJsonSchema may emit a generic `"type": "string"` instead of an `enum:` array), commit a hand-tuned version with a comment header documenting the deviation and the regeneration suppression.

**Why over alternatives:**

- *Hand-written schemas:* duplicates the C# records as a second source of truth; drift risk.
- *No schema, prompt-only description:* loses Claude's structured-output constraint feature; quality drop on nested types.

### D15. Reference integrity: strict intra-book + corpus-wide validate endpoint

Cross-entity references are validated with two-tier strictness:

- **Intra-book (extraction time):** `EntityReferenceResolver` at extraction time is invoked with awareness of the current book's slug prefix. Dangling refs whose target slug starts with the current book's slug prefix are FAILURES — they are recorded in `data/canonical/<book>.errors.json` and the offending entities are excluded from the canonical JSON. Claude is given a retry pass with the failure context, asked to either produce the missing entity or remove the ref. After the bounded retry budget, unresolved entries stay in the errors file for human review.
- **Inter-book (extraction time):** dangling refs whose target slug points at a *different* book are warnings — they're written to a sibling `data/canonical/<book>.warnings.json` so they're a checked-in record but don't block extraction.
- **Ingestion time:** unchanged from Plan 1 — warning-only.

**NEW endpoint** `POST /admin/canonical/validate` runs the resolver across the entire `data/canonical/` corpus on demand. Returns 200 with a structured report when clean (per-book inter-book dangling refs, schema-version mismatches, duplicate IDs across books) and 422 with the same body shape when the report contains any FAIL-class issues (intra-book dangles or schema mismatches). Designed as an operator pre-merge check after a new book lands.

**Why over alternatives:**

- *Pure warn-only:* extraction quality bugs (the LLM forgot to emit the Subclass entity its parent Class references) become invisible until agent-runtime.
- *Strict everywhere including inter-book:* impossible — book A can legitimately reference book B's entities before B is extracted; "strict everywhere" would prevent extraction order from being incremental.

### D16. Errata refresh: no special tooling

When WotC publishes errata, the operator workflow is: replace the PDF on disk → call `extract-entities?force=true` → review the canonical JSON diff in a PR → merge. `git diff` is the review tool. No selective per-entity re-extraction, no errata-document-aware patching, no diff-mode endpoint.

**Why over alternatives:**

- Errata are infrequent; bounded book count caps the operational cost.
- The `?force=true` re-extraction cost (~20–30 min, ~$5–20) is acceptable for what's effectively a once-a-year event per book.
- Building selective re-extraction or errata patching is YAGNI for a hobby project.

If friction proves real later (e.g. errata cadence accelerates), revisit with selective re-extraction (`?onlyTypes=`/`?onlyIds=`).

### D17. Plan 2 ships as one coherent implementation slice

The Plan 2 scope is: extraction pipeline + Claude client + JSON-schema generation + corpus-wide validate endpoint + the strict-intra-book post-pass. No further sub-plans within Plan 2 — it ships as a single TDD plan via `superpowers:subagent-driven-development`. Plan 3 (backfill/rollout) remains a separate future plan per `series.md`.

## Risks / Trade-offs

- **[LLM extraction quality is uneven]** → Mitigated by D1 (hand-correctable JSON) and an extraction-validation pass that flags incomplete or schema-violating records for human review before commit.
- **[Large canonical JSON files inflate git history]** → Estimated 2–5 MB per book × ~10 books = 20–50 MB total. Acceptable for the project's lifetime. If it becomes painful, switch to git-lfs.
- **[Schema drift between extraction prompt and consumer code]** → Mitigated by D12 (schemaVersion gate) and JSON-schema validation in tests.
- **[Adding a book is multi-step]** → Acknowledged as the cost of correctability. Documented in README. The trade vs. invisible bad data is worth it.
- **[Two collections double the embed cost at ingest]** → Acceptable. Embedding canonical entity text is fast (one embed per entity; far fewer entities than blocks per book).
- **[Cross-edition entities (Fighter PHB14 vs PHB24) live as separate records]** → Acknowledged; cross-edition reconciliation is a future concern, not a v1 requirement.
- **[Free-form `keywords[]` may diverge across books]** → Accepted; lint pass possible later if needed.
- **[Re-extracting all books on a schema change is hours]** → Bounded by book count. Run as a batch job overnight.

## Migration Plan

This change is additive — `dnd_blocks` and the existing block ingestion path are untouched. Rollout order:

1. Ship the entity model (envelope + per-type schemas as code) and JSON-schema validators.
2. Ship the LLM extraction pipeline. Initially behind a flag / admin-only endpoint.
3. Run extraction against one or two books, hand-correct the resulting JSON, commit the artifacts.
4. Ship `dnd_entities` Qdrant collection + entity-ingestion path. Ingest the canonical JSON into the new collection.
5. Ship retrieval endpoints for the entity index (by-ID, vector, structured-filter).
6. Backfill remaining books one at a time.

Rollback: drop the `dnd_entities` collection, leave canonical JSON in repo (still useful as data even if the entity index is gone). The block index and existing retrieval are unaffected at every step.

## Open Questions

All Plan 1 and Plan 2 design questions are now resolved (D1–D17). Plan 3 (backfill / rollout) will surface its own questions when its design is brainstormed; nothing else is open today.
