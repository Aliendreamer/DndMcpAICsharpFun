## Context

The qwen3:8b extraction pipeline reads PDF text via Docling, which delivers heading text in ALL CAPS (as it appears in the physical book). The LLM faithfully copies these headings into the `name` field, producing canonical JSON like `"name": "CIRCLE OF SPORES"`. Some pages have OCR artifacts that further corrupt names (`"f eature"`, `"Gons OF YouR WoRLD"`).

Two canonical files are affected today (tce.json: 234 all-caps / 140 mixed; dmg14.json: 506 all-caps / 116 mixed). The issue will recur on every new extraction unless fixed at the prompt level.

## Goals / Non-Goals

**Goals:**
- Extraction prompt instructs the LLM to output title-case names going forward
- LLM emits a `confidence` field per entity; post-processing maps it + heuristic → `needsReview`
- `EntityEnvelope` carries `NeedsReview`; canonical JSON persists it
- Validation surfaces `needsReview` entity counts as warnings
- One-time normalization script fixes existing canonical JSONs without re-extraction

**Non-Goals:**
- Automatically correcting OCR-garbled names (requires human judgment)
- Blocking ingestion of `needsReview` entities (review is advisory, not a gate)
- Re-extracting all books automatically (per-book on-demand only)

## Decisions

### D1: `confidence` field consumed at extraction time, not persisted

**Decision:** The LLM `confidence` value is read during the extraction post-processing step and used to set `needsReview`, but is not stored in the canonical JSON or `EntityEnvelope`.

**Rationale:** Confidence is a transient extraction signal, not a durable domain property. Storing it would clutter the schema and mislead future readers (stale confidence from an old extraction run is meaningless). Only the actionable flag `needsReview` persists.

**Alternative considered:** Store confidence alongside `needsReview` to aid reviewer triage. Rejected — the heuristic and type information give sufficient triage signal without an extra field.

### D2: Heuristic runs on `name` field only, not `canonicalText` or `fields`

**Decision:** The OCR-artifact heuristic checks only the entity `name` field.

**Rationale:** `canonicalText` is re-rendered at ingest time from structured fields, so artifacts there self-correct once `name` is fixed. `fields` is raw 5etools JSON or LLM-extracted prose; normalizing it is out of scope.

**Heuristic rules (any match → flag):**
1. Name is all-caps and length > 1 (`name.isupper()`)
2. Name contains a letter-space-letter break typical of split OCR words (regex `[a-z] [a-z]` after lowercasing, but only within a word context — e.g., `f eature`)
3. Name contains noise sequences: `....`, repeated punctuation, or `l:` patterns
4. More than 3 case-alternation transitions within a single word (e.g., `WoRLD`)

### D3: One-time script handles existing files; future re-extractions handle new files

**Decision:** A Python normalization script (`scripts/normalize_canonical_names.py`) processes existing canonical JSONs in-place. It applies mechanical title-case conversion to cleanly all-caps names, and sets `needsReview: true` on names that fail the heuristic. This script is idempotent.

**Rationale:** Re-extracting tce.json and dmg14.json would take hours (qwen3:8b at 20–65 s/entity, 1000+ entities). The mechanical title-case fix is deterministic and correct for ~80% of cases. The remaining ~20% get flagged for human review.

### D4: Title-case algorithm

**Decision:** Use Python's `str.title()` as the baseline, then lowercase a fixed set of small words (`of, the, a, an, in, on, at, to, and, or, but, for, nor`) except when they start the name. Apostrophes are handled by post-processing `'S` → `'s`.

**Rationale:** D&D entity names follow Chicago Manual of Style title casing. `str.title()` over-capitalizes (e.g., `"Tasha'S"`) so apostrophe and small-word correction is necessary. A full grammar-aware titlecase library is overkill for this domain.

## Risks / Trade-offs

**[Risk] Title-case algorithm miscapitalizes abbreviations (e.g., `"DMG"` → `"Dmg"`)** → Mitigation: the normalization script only applies to names that are fully `isupper()`. Names with mixed case (abbreviations embedded in phrases) are left untouched and flagged with `needsReview` if they fail the heuristic.

**[Risk] `needsReview` flag accumulates and is never cleared** → Mitigation: validation reports the count prominently. Documented workflow: fix → clear flag → validate → re-ingest. No automated enforcement, but the signal is visible.

**[Risk] LLM ignores naming-case instruction** → Mitigation: the all-caps heuristic catches violations regardless of whether the LLM followed the prompt. `needsReview` is set defensively.

## Migration Plan

1. Run `scripts/normalize_canonical_names.py` against existing `data/canonical/tce.json` and `data/canonical/dmg14.json`
2. Run `POST /admin/canonical/validate` — confirm warnings list `needsReview` counts, zero errors
3. Re-ingest both books (`POST /admin/books/1/ingest-entities`, `POST /admin/books/2/ingest-entities`)
4. Deploy updated app (extraction prompt + `needsReview` schema changes)
5. Human reviewers open flagged canonical JSONs, fix names/types, clear flags, re-validate, re-ingest
