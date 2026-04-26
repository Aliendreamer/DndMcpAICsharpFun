## Why

The DM's Guide contains content types — treasure hoards, encounter tables, and trap descriptions — that don't map to any existing `ContentCategory` value. These chunks fall through as `Unknown`, making them unsearchable by category in retrieval queries.

## What Changes

- Add three new values to the `ContentCategory` enum: `Treasure`, `Encounter`, `Trap`
- Add three new `IPatternDetector` implementations with keyword-based scoring for each category
- Register the new detectors in DI (`Program.cs`)

## Capabilities

### New Capabilities

- `dm-content-categories`: The ingestion pipeline recognises and tags Treasure, Encounter, and Trap chunks from DM-style source books

### Modified Capabilities

None — existing categories and detectors are unchanged.

## Impact

- Modified: `Domain/ContentCategory.cs` — 3 new enum values
- New: `Features/Ingestion/Chunking/Detectors/TreasurePatternDetector.cs`
- New: `Features/Ingestion/Chunking/Detectors/EncounterPatternDetector.cs`
- New: `Features/Ingestion/Chunking/Detectors/TrapPatternDetector.cs`
- Modified: `Program.cs` — 3 new `IPatternDetector` registrations
- No API, config, or database changes
