## 1. Bare-header promotion (`MinerUPdfConverter`)

- [ ] 1.1 In the block→item loop, for a `text` block (`TextLevel is null or 0`): take the first line (up to first `\n`, else whole text). If `IsLevelSchoolLine(firstLine)` AND `StripLevelSchool(firstLine)` is a 2–40-char non-empty name AND the first line is short (≤ ~55 chars) AND its norm != lastHeadingNorm, emit a synthetic `section_header` for the name. Run AFTER the existing Casting-Time-anchored splitter paths (avoid double-promote; lastHeadingNorm dedup). Reuse existing regexes. TDD: "GREATER RESTORATION 5th-level abjuration"→"GREATER RESTORATION"; "SHIELD OF FAITH 1st-level abjuration"→"SHIELD OF FAITH"; "5TH LEVEL Banishing Smite Circle of Power..."→NOT promoted; a long prose line with a school word → NOT promoted. Keep all existing converter tests green.

## 2. Build

- [ ] 2.1 `dotnet build` 0 warnings; full non-persistence suite green. No `.http`/CLAUDE.md change.

## 3. Live validation

- [ ] 3.1 Clear `*.mineru.json` cache; re-extract PHB (force) + errorsOnly; re-add hand-authored Gnome.
- [ ] 3.2 Confirm Greater Restoration + Shield of Faith (+ any similar) recovered, spell count **> 350**, classes 12 / races 9 / Monster 30 unchanged, no new noise/junk. Record which spells still remain → iteration 3 or B backfill.
