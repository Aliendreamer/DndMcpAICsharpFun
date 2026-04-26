## Context

The chunking pipeline uses `IPatternDetector` implementations to score a chunk of text against known content patterns. `ContentCategoryDetector` picks the highest-scoring detector above a 0.7 confidence threshold, falling back to the chapter default. Adding a new category requires exactly two things: a new enum value and a new detector class — no changes to the pipeline itself.

Each detector implements:
- `Detect(string text) → float` — score in [0, 1]; counts keyword hits / total signals
- `IsEntityBoundary(string line) → bool` — true when a line marks the start of a new entity of this type
- `Category` property — returns the matching `ContentCategory` value

## Goals / Non-Goals

**Goals:**
- Tag treasure hoard/table chunks, encounter table/difficulty chunks, and trap description chunks correctly during ingestion
- Follow the exact same pattern as existing detectors (no architectural change)

**Non-Goals:**
- Adventure hooks, dungeon room descriptions, NPC stat blocks (future work)
- Retroactive re-ingestion of already-indexed books (run reingest via `/admin/books/{id}/reingest` if needed)
- Fuzzy/ML-based detection

## Decisions

### D1 — Detection signals per category

**`TreasurePatternDetector`** (3 signals, threshold 2/3 = 0.67 → rounds to 1 hit needed for 0.33, 2 for 0.67):

| Signal | Rationale |
|--------|-----------|
| `"Treasure Hoard"` | Explicit DMG section header |
| `"Art Objects"` | Consistent label in treasure tables |
| `"Gemstones"` | Consistent label in treasure tables |

`IsEntityBoundary`: line contains `"Treasure Hoard"`.

**`EncounterPatternDetector`** (3 signals):

| Signal | Rationale |
|--------|-----------|
| `"Encounter Difficulty"` | DMG encounter-building section phrase |
| `"XP Threshold"` | Used in encounter difficulty tables |
| `"Random Encounter"` | Explicit encounter table heading |

`IsEntityBoundary`: line contains `"Encounter Difficulty"` or `"Random Encounter"`.

**`TrapPatternDetector`** (3 signals):

| Signal | Rationale |
|--------|-----------|
| `"Trigger:"` | Every trap description starts with a Trigger block |
| `"Effect:"` | Every trap description has an Effect block |
| `"Disarm DC:"` | Trap-specific mechanic label |

`IsEntityBoundary`: line contains `"Trigger:"` — marks the start of a new trap.

### D2 — No changes to confidence threshold or pipeline

The existing `ConfidenceThreshold = 0.7f` in `ContentCategoryDetector` is left as-is. New detectors must hit ≥ 2 of 3 signals (0.67 falls just below — so **any 2 of 3 signals gives 0.67 which is below 0.7**).

Correction: 2/3 = 0.667 which is below 0.7. To reliably fire, a chunk needs all 3 signals, OR we accept that lightly-matching chunks fall through to the chapter default. This is acceptable — a Treasure Hoard section that only mentions "Art Objects" without "Treasure Hoard" or "Gemstones" is an edge case.

Alternatively, treat 2/3 hits as sufficient by normalising over 2 instead of 3. **Decision: keep denominator at 3 and accept the threshold behaviour.** A chunk with all 3 signals fires cleanly; partial matches fall to chapter default, which is correct conservative behaviour.

## Risks / Trade-offs

- **Signal collision with Monster blocks**: `"Hit Points"` and `"Speed"` phrases occasionally appear in encounter-building text. The signals chosen (`XP Threshold`, `Encounter Difficulty`, `Random Encounter`) are unlikely to appear in Monster stat blocks, so collision risk is low.
- **Treasure ≠ individual magic items**: `Item` category covers individual magic item stat blocks. `Treasure` covers tables and hoards. A chunk that mentions both (e.g., a table listing magic items) could score for both; the higher score wins.

## Migration Plan

1. Add enum values
2. Add detector files
3. Register in DI
4. Build must pass
5. No data migration — existing indexed chunks retain their current category; re-ingest if fresh categorisation is needed
