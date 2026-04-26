## 1. Extend ContentCategory enum

- [x] 1.1 Add `Treasure`, `Encounter`, and `Trap` values to `Domain/ContentCategory.cs`
- [x] 1.2 Run `dotnet build` — must pass with 0 errors, 0 warnings

## 2. Add TreasurePatternDetector

- [x] 2.1 Create `Features/Ingestion/Chunking/Detectors/TreasurePatternDetector.cs` with signals: `"Treasure Hoard"`, `"Art Objects"`, `"Gemstones"` (score = hits / 3f); `IsEntityBoundary` returns true for lines containing `"Treasure Hoard"`
- [x] 2.2 Run `dotnet build` — must pass with 0 errors, 0 warnings

## 3. Add EncounterPatternDetector

- [x] 3.1 Create `Features/Ingestion/Chunking/Detectors/EncounterPatternDetector.cs` with signals: `"Encounter Difficulty"`, `"XP Threshold"`, `"Random Encounter"` (score = hits / 3f); `IsEntityBoundary` returns true for lines containing `"Encounter Difficulty"` or `"Random Encounter"`
- [x] 3.2 Run `dotnet build` — must pass with 0 errors, 0 warnings

## 4. Add TrapPatternDetector

- [x] 4.1 Create `Features/Ingestion/Chunking/Detectors/TrapPatternDetector.cs` with signals: `"Trigger:"`, `"Effect:"`, `"Disarm DC:"` (score = hits / 3f); `IsEntityBoundary` returns true for lines containing `"Trigger:"`
- [x] 4.2 Run `dotnet build` — must pass with 0 errors, 0 warnings

## 5. Register detectors in DI

- [x] 5.1 Add three `AddSingleton<IPatternDetector, TreasurePatternDetector>()`, `AddSingleton<IPatternDetector, EncounterPatternDetector>()`, `AddSingleton<IPatternDetector, TrapPatternDetector>()` registrations to `Program.cs` alongside existing detectors
- [x] 5.2 Run `dotnet build` — must pass with 0 errors, 0 warnings

## 6. Final verification

- [x] 6.1 Run `dotnet clean && dotnet build` — 0 errors, 0 warnings
- [x] 6.2 Confirm `ContentCategory` enum has 10 values: `Spell`, `Monster`, `Class`, `Background`, `Item`, `Rule`, `Unknown`, `Treasure`, `Encounter`, `Trap`
- [x] 6.3 Confirm 7 `IPatternDetector` registrations exist in `Program.cs`
