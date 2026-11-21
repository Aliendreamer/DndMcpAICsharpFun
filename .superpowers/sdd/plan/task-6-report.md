# Task 6 report
Implemented three-way override outcome in OverrideMerger.Apply: retire on TextMatch.Matches(new,want) (recorded in new MergeResult.Retired, override not applied); apply quietly if baseline equal or extraction got closer (TokenF1 of FlattenText); else apply + override-drift. Reapply unchanged (empty Retired). Only construction site of MergeResult was Apply (no other sites in solution).
Note: edits made via a python script, not Serena (auto mode).

RED (retire/closer logic disabled): `dotnet test DndMcpAICsharpFun.Tests --filter OverrideMergerTests`
 Failed: Apply_retires_one_override_and_drifts_another, Apply_retires_an_override_the_extraction_now_matches, Apply_keeps_an_override_quietly_when_extraction_got_closer -> Failed: 3, Passed: 36, Total: 39
GREEN: same command -> Passed! Failed: 0, Passed: 39, Total: 39
Full suite: 16 failures (inotify instance limit in endpoint tests, ManualCasesTests fixture variants) -- identical 16 on stashed baseline, unrelated.
Files: Features/Ingestion/Programmatic/OverrideMerger.cs, DndMcpAICsharpFun.Tests/Programmatic/OverrideMergerTests.cs

## Fix round 1
Drift condition now uses TextMatch.Matches(new, baseline) on fresh Apply (Reapply keeps DeepEquals); ApplyOne doc comment describes the D4 order. Added Apply_cosmetic_reextraction_difference_applies_without_drift (hyphen/whitespace). Edits via Serena.
Command: dotnet test DndMcpAICsharpFun.Tests --filter OverrideMergerTests
Output: Passed! Failed: 0, Passed: 40, Total: 40
