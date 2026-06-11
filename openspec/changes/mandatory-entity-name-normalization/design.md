## Context

Entity names are produced by the LLM extraction step in `EntityExtractionOrchestrator`. A prompt rule asks the LLM to title-case names, but it is unreliable — PDF headings frequently survive as all-caps. A separate, manual `CanonicalNameNormalizerService` (`POST /admin/canonical/normalize`) can title-case them after the fact, but it must be invoked by hand and it only renames; it leaves the stored `needsReview` flag set. Meanwhile `ExtractionNeedsReview.HasOcrArtifacts` treats all-caps as an OCR artifact, so every uppercase name is flagged `needsReview`, holding corpus validation at 422 and drowning genuine review candidates.

The casing logic (`DndTitleCase`, small-word handling, apostrophe-S) already exists and is tested inside `CanonicalNameNormalizerService`. This change promotes it to a shared, mandatory pipeline step and adds acronym preservation.

## Goals / Non-Goals

**Goals:**
- Make all-caps → title-case normalization automatic at extraction time, so canonical JSON is written clean without any manual step.
- Preserve D&D acronyms (`NPC`, `GP`, `XP`, `HP`, `AC`, `DC`, `CR`, …) during title-casing.
- Stop all-caps from driving `needsReview`, so the persistent 422 shrinks to genuine review candidates.
- One source of truth for the casing rule, shared by the extraction hook and the admin endpoint.

**Non-Goals:**
- Normalizing nested field/entry names (only the top-level entity `name` in v1).
- Removing the manual `/admin/canonical/normalize` endpoint (kept for re-normalizing hand-edited files).
- Changing the other OCR heuristics (split-word, noise, case-alternation) or the low-confidence rule.
- Touching entity IDs (already lowercased slugs via `EntityIdSlug`).

## Decisions

**1. New `EntityNameNormalizer` (Features/Ingestion/EntityExtraction/).** A static helper owning `TitleCase(string name)` (D&D title-case + acronym allowlist) — a pure casing transform. `CanonicalNameNormalizerService.DndTitleCase` is moved here; the service and the extraction hook both call it. This keeps the casing rule in one place and unit-testable. The *decision of when to apply it* lives in the callers (see #3/#4), not in the function.

**2. Acronym allowlist.** A `static readonly HashSet<string>` (ordinal-ignore-case) of canonical-cased tokens: `NPC, NPCs, PC, PCs, DM, GP, SP, CP, PP, EP, XP, HP, AC, DC, CR, AoE, D&D`. During word conversion, if a token matches the allowlist case-insensitively it is emitted in its allowlist casing instead of being lowercased. Small-word handling and apostrophe-S correction are unchanged.

**3. Conditional application (avoids hiding artifacts).** Blindly title-casing every name would mask split-word OCR (`"...f eature"` → `"...F Eature"`, which the lowercase split-word regex would then miss). So both callers reuse the existing gate: title-case a name only when it is **all-caps AND has no *other* artifact** — where "other artifact" is `ExtractionNeedsReview.HasOcrArtifacts(name.ToLowerInvariant())` (lowercasing neutralizes the all-caps signal so only split-word/noise/case-alternation remain). Genuinely garbled names are left unchanged and flagged for review.

**4. Mandatory hook at extraction time.** In `EntityExtractionOrchestrator`, at the two candidate→entity sites (~lines 203 and 391), apply the gate from #3 to `candidate.DisplayName`: if all-caps-and-clean, replace it with `EntityNameNormalizer.TitleCase(name)`. Then `ExtractionNeedsReview.Derive(normalizedName, confidence)` runs as today. Because a clean all-caps name is now title-case, the all-caps rule no longer fires for it; low-confidence and the other artifact rules are unchanged.

**5. `needsReview` recompute in the admin path.** `CanonicalNameNormalizerService.NormalizeAsync` keeps its existing branch structure but, in the all-caps-and-clean branch, additionally **sets `needsReview = false`** (today it title-cases but leaves the stored flag set). The garbled branch still sets `needsReview = true` and leaves the name unchanged. This clears stale all-caps-only flags on existing canonical files while keeping the flag on genuinely garbled names. The service stays idempotent.

**5. Apply to existing data.** After the code ships: `POST /admin/canonical/normalize` (writes title-cased names + recomputed flags to DMG/Tasha canonical JSON), then re-ingest both books. `dnd_entities` stays 1080; the ~900 all-caps names become title-case; `needsReview` count drops.

## Risks / Trade-offs

- **Acronym allowlist is finite.** Unknown acronyms (e.g. a rare stat block label) will be title-cased like normal words. Mitigation: the allowlist is a one-line edit to extend; v1 covers the common D&D set. Accepted.
- **Behavioral change to `needsReview`.** Anything previously flagged solely for being all-caps loses the flag. This is intended (all-caps alone is not a quality defect once title-cased), but reviewers relying on that signal lose it. Documented as BREAKING (behavioral) in the proposal.
- **Idempotency.** Normalizing an already-title-cased name must be a no-op (the name is not all-caps, acronyms already canonical). Covered by a passthrough unit test and the existing idempotency scenario.
- **Double source during transition.** `ExtractionNeedsReview` still contains the all-caps rule; it is simply never reached with an all-caps name on the extraction path. Left in place to avoid touching the admin/other callers' contracts; the spec records that extraction normalizes before the heuristic.
