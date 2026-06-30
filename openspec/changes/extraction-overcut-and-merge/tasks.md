## 1. Bug A — splitter strips the level/school SUFFIX (`MinerUPdfConverter`)

- [ ] 1.1 Rework `StripLevelSchool` so the cut-point anchors on the suffix, not the first school word: if a level digit is present, cut at the first digit (unchanged); else strip a trailing `"<school> cantrip"` (the school immediately before `cantrip`). Keep `SchoolRx`/`CantripRx`/`OcrLevelWordRx`. TDD: "MINOR ILLUSION Illusion cantrip"→"MINOR ILLUSION"; "PROGRAMMED ILLUSION Illusion cantrip"→"PROGRAMMED ILLUSION"; "PRESTIDIGITATION Transmutation cantrip"→"PRESTIDIGITATION"; "SHIELD OF FAITH 1st-level abjuration"→"SHIELD OF FAITH"; "ALTER SELF 2nd-level transmutation"→"ALTER SELF". Keep the existing splitter + heading-clean tests green.

## 2. Bug B — scanner page-proximity merge guard (`EntityCandidateScanner`)

- [ ] 2.1 Replace `GroupBy(SectionTitle)` in `Scan` with a single ordered pass that starts a NEW group when the `SectionTitle` changes OR the block's page jumps more than `W` (default 3) pages beyond the current group's page span. Each group keeps the existing shape (Section, FirstIndex, min Page, joined Text), preserving first-occurrence order. TDD: two "DARKVISION" runs at p184 and p230 → two candidates (keyed 184 and 230); same title on adjacent pages → one candidate; a single contiguous section → unchanged. Make `W` a small const (document it).

## 3. Build, docs

- [ ] 3.1 `dotnet build` 0 warnings; full non-persistence suite green.
- [ ] 3.2 No `.http`/insomnia change. No CLAUDE.md change.

## 4. Live validation (acceptance gate)

- [ ] 4.1 **Clear the conversion cache** (`docker exec … rm -f /books/conversion-cache/*.mineru.json`) — Bug A is in the converter. Re-extract PHB through `mineru:8000` (force) + an `errorsOnly` pass.
- [ ] 4.2 Early-checkpoint spot-check, then confirm: **Minor Illusion, Programmed Illusion, Darkvision present**, spell count **> 329**, classes **12** / races **9** / Monster **30** unchanged, zero new noise. Record the before/after delta and which entities changed.
