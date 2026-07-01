## 1. Single-block stat-header splitter (`MinerUPdfConverter`)

- [x] 1.1 In the block→item loop, for a `text` block (`TextLevel is null or 0`): if its text contains a level/school token (`LevelRx`/`SchoolRx`/`CantripRx`) within the first ~60 chars AND contains `Casting Time` (case-insensitive), take the first line (up to first `\n`, else whole text), extract the name with `StripLevelSchool`, and if it's 2–40 chars, non-empty, name-shaped, and != lastHeadingNorm, emit a synthetic `section_header` for it before emitting the block. Reuse existing regexes; do not break the existing separate-block splitter or heading-clean logic. TDD: "CLOUD OF DAGGERS\n2nd-level conjuration\nCasting Time:..."→"CLOUD OF DAGGERS"; "DISGUISE SELF 1st-level illusion Casting Time:..."→"DISGUISE SELF"; a prose block mentioning "casting time" mid-sentence → NOT split; keep all existing converter tests green.

## 2. Build

- [x] 2.1 `dotnet build` 0 warnings; full non-persistence suite green. No `.http`/CLAUDE.md change.

## 3. Live validation

- [x] 3.1 Clear `*.mineru.json` cache; re-extract PHB (force) + errorsOnly; re-add hand-authored Gnome.
- [x] 3.2 Confirm ≥15 of the 21 recovered (Cloud of Daggers, Blindness/Deafness, Divine Favor, Disguise Self, …), spell count **> 335**, classes 12 / races 9 / Monster 30 unchanged, no new noise/junk. Record which of the 26 still remain → A-iteration 2 or B.
