## 1. Fix #1 — demote spell stat-line headings to text (`MinerUPdfConverter`)

- [x] 1.1 In the block→item mapping, BEFORE the spell-splitter / heading-clean logic, if a block is heading-tagged (`TextLevel > 0`) and its trimmed text matches `^\s*(Casting Time|Range|Components|Duration|At Higher Levels|Ritual|Concentration)\b` (case-insensitive, compiled regex), emit it as a `text` item instead of a `section_header`. A real heading not matching → unchanged. TDD: "Casting Time: 1 action" heading → text; a `MORDENKAINEN'S SWORD` then heading `Casting Time:`/`Range:` then body sequence → body attributed to MORDENKAINEN'S SWORD (use the existing stubbed-HTTP converter test path); "FIREBALL"/"DWARF TRAITS" → unchanged section_header. Keep all existing converter/splitter/heading-clean tests green.

## 2. Fix #2 — traceability guard (`BuildScannerInputs` + `EntityCandidateScanner`)

- [x] 2.1 In `EntityExtractionOrchestrator.BuildScannerInputs`: when a `section_header` overwrites `currentSection` while the prior section received NO body text, `LogWarning` the dropped title. Behavior unchanged — log only. TDD: a heading→heading (no body) sequence logs the warning; a normal heading→body sequence does not.
- [x] 2.2 In `EntityCandidateScanner.Scan`: replace the silent `continue` on a null/Unknown category with a `LogWarning` naming the section + page, then continue. (Scanner needs an `ILogger`; inject it — check the DI registration.) TDD: a section on a page with no category logs the warning.

## 3. Diagnostic test cleanup

- [x] 3.1 The throwaway `DndMcpAICsharpFun.Tests/Ingestion/EntityExtraction/SilentDropDiagnosisTests.cs` currently violates warnings-as-errors (CS8019 unnecessary using). Either delete it, or trim it to a clean, fast regression test (the Mordenkainen's stat-line sequence → spell becomes a candidate) with no diagnostic-only `ITestOutputHelper` dumps. Decide and do one.

## 4. Build, docs

- [x] 4.1 `dotnet build` 0 warnings; full non-persistence suite green.
- [x] 4.2 No `.http`/insomnia change. No CLAUDE.md change.

## 5. Live validation (acceptance gate)

- [x] 5.1 **Clear the conversion cache** (`docker exec … rm -f /books/conversion-cache/*.mineru.json`). Re-extract PHB through `mineru:8000` (force) + an `errorsOnly` pass. Re-add hand-authored Gnome afterward (force overwrites it).
- [x] 5.2 Confirm: **Mordenkainen's Sword + Mordenkainen's Private Sanctum present** (and Shield of Faith if same root — report either way), spell count **> 333**, classes 12 / races 9 / Monster 30 unchanged, zero new noise. Check the new warning logs for any remaining silent losses. Record the before/after.
