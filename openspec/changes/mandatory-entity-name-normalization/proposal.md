## Why

All-caps entity names from PDF headings (e.g. `DECK OF MANY THINGS`, `QUICK NPCs`) currently survive extraction unless someone manually calls `POST /admin/canonical/normalize`. They also trip the OCR-artifact heuristic, so ~900 of 1080 entities carry `needsReview = true` purely for being uppercase — keeping corpus validation at 422 and burying genuine review candidates. Title-casing should be automatic and deterministic, not a manual afterthought that depends on the LLM obeying a prompt rule.

## What Changes

- Extract the casing rule into a focused, reusable `EntityNameNormalizer` (D&D title-case + a curated acronym allowlist). The existing `CanonicalNameNormalizerService` and the new extraction-time hook both delegate to it — one source of truth.
- Make name normalization a **mandatory** post-LLM step in `EntityExtractionOrchestrator`: each entity name is normalized before the entity is built and before the `needsReview` heuristic runs. Canonical JSON is written title-cased automatically.
- Preserve D&D acronyms during title-casing via an allowlist (`NPC, PC, DM, GP, SP, CP, PP, EP, XP, HP, AC, DC, CR, AoE, D&D`), so `750 GP ART OBJECTS` → `750 GP Art Objects`, not `750 Gp Art Objects`.
- **BREAKING (behavioral):** all-caps no longer flags `needsReview`. Because names are normalized first, the all-caps rule of the OCR heuristic never fires; the remaining heuristics (split-word, noise, case-alternation) and the low-confidence rule are unchanged. The persistent validation 422 shrinks to genuine review candidates.
- `CanonicalNameNormalizerService.NormalizeAsync` recomputes `needsReview` on the normalized name (today it renames but leaves the stored flag set), so re-normalizing existing canonical files also clears stale all-caps flags. The manual `/admin/canonical/normalize` endpoint is kept.
- Apply to existing data: run the normalizer on the shipped DMG + Tasha canonical files and re-ingest; the ~900 all-caps names become title-case with acronyms preserved.

## Capabilities

### New Capabilities

(none — the behavior extends existing capabilities)

### Modified Capabilities

- `entity-extraction-pipeline`: extraction now deterministically normalizes entity names as a mandatory post-processing step; the OCR-artifact heuristic no longer flags all-caps (names are normalized before it runs).
- `canonical-name-normalizer`: the title-case algorithm preserves a curated D&D acronym allowlist, and `NormalizeAsync` recomputes/clears `needsReview` on the normalized name.

## Impact

- Code: new `Features/Ingestion/EntityExtraction/EntityNameNormalizer.cs`; `EntityExtractionOrchestrator` (two candidate→entity sites) calls it before `ExtractionNeedsReview.Derive`; `CanonicalNameNormalizerService` delegates to it and recomputes `needsReview`; `ExtractionNeedsReview` all-caps rule effectively superseded for the extraction path.
- Data: DMG (id=1) + Tasha (id=2) canonical JSON re-normalized and re-ingested; `dnd_entities` stays 1080, ~900 names become title-case, `needsReview` count drops sharply.
- IDs: unaffected — `EntityIdSlug` already lowercases name slugs.
- No new dependencies. No HTTP contract changes (existing `/admin/canonical/normalize` retained).
