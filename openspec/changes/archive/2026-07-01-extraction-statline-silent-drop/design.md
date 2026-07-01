## Context

Diagnosed via `SilentDropDiagnosisTests` (throwaway harness, left on disk): the deterministic path
(matcher → resolver → drop-filter → dedup) KEEPS these names (`ForceType(Spell)`); the loss is upstream
in section attribution. Root cause = MinerU tagging `Casting Time:` / `Range:` / `Duration:` lines as
`section_header`, and `BuildScannerInputs` letting each consecutive header overwrite `currentSection`
before any body text, so the spell-name heading never owns a section. Two stages, two fixes.

## Goals / Non-Goals

**Goals:** recover Mordenkainen's Sword / Private Sanctum (and Shield of Faith if same root); make any
future silent candidate-loss visible in logs. No regression to the current 333 spells / 9 races / 12
classes / 30 monsters / zero noise.

**Non-Goals:** the ~21 prose-merged / special-char names and remaining OCR-dropped Casting-Time anchors
(roadmap). Not re-architecting the heading-driven scanner.

## Decisions

**Fix #1 — demote spell stat-line headings to text (`MinerUPdfConverter`).** In the block→item mapping,
when a block is a heading (`TextLevel > 0`) but its trimmed text begins (case-insensitive) with a spell
stat label, emit it as a `text` item instead of a `section_header`. Label set (anchored at start, with
the trailing colon where applicable): `Casting Time:`, `Range:`, `Components:`, `Duration:`,
`Concentration`, `At Higher Levels`, `Ritual`. A single compiled regex (`^\s*(Casting Time|Range|
Components|Duration|At Higher Levels|Ritual|Concentration)\b`) drives it. Apply BEFORE the spell-splitter
/ heading-clean logic so the demoted line is plain text. *(Alt: fix it in `BuildScannerInputs` instead —
rejected: the mis-tag is parser output, so correcting it in the converter keeps one source of truth and
benefits the block-ingestion path too.)* Safe: these are never legitimate section titles.

**Fix #2 — traceability guard (`BuildScannerInputs` + `EntityCandidateScanner`).** Two log points, both
`LogWarning` (rare by design once #1 lands):
- In `BuildScannerInputs`: when a `section_header` overwrites `currentSection` while the prior section
  has received **no body text**, log the dropped title (`"section '{Prev}' got no body before '{Next}'"`).
- In `EntityCandidateScanner.Scan`: when a group is skipped because `toc.GetCategory(page)` →
  null/Unknown type, log the skipped section + page (today it is a silent `continue`).
These never change behavior — they only make a silent loss visible. *(This is the meta-fix: the bug cost
a full session of diagnosis precisely because the loss left no trace.)*

## Risks / Trade-offs

- **#1 over-demoting a real heading** that happens to start with one of these words → mitigated: the set
  is spell-stat-specific and anchored at line start; a real section title like "Range" alone is not a
  spell entry context. Validate the spell count rises and classes/monsters hold.
- **#2 log spam** if the no-body-overwrite is common in normal data → expected to be rare after #1; if
  noisy, it still points at real mis-tags worth seeing. Keep it `Warning`, one line per occurrence.
- **Shield of Faith may be a different root** (missing anchor, no name header at all) → confirm in impl;
  if #1 doesn't recover it, leave it on the roadmap (do not expand scope).

## Validation

Unit tests: #1 — a stat-line heading demotes to text, a real heading is untouched, and a Mordenkainen's-
shaped block sequence yields the spell as the section title. #2 — the warning fires on a no-body
overwrite and on a null-category skip. Then the live gate: clear `*.mineru.json`, re-extract PHB +
errorsOnly, confirm Mordenkainen's ×2 recovered, spell count > 333, counts otherwise unchanged, no noise.
Fold/clean the diagnostic `SilentDropDiagnosisTests.cs` (warnings-as-errors) — keep a trimmed regression
test or remove it.
