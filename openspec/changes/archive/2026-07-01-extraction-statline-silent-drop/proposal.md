## Why

Bucket C of the recall tail: a class of spells (Mordenkainen's Sword, Mordenkainen's Private Sanctum,
likely Shield of Faith) vanishes with **zero trace** — no entity, no `declined.json`, no error, no log.
A diagnostic harness pinned the mechanism (and refuted the page→category theory — page 263 IS Spell,
same as Fireball's 242):

**MinerU mis-tags a spell's stat lines as headings, and `BuildScannerInputs` lets consecutive headings
silently overwrite the section title.** For Fireball, `Casting Time:` / `Range:` are `text`, so
`currentSection` stays `FIREBALL` and the body attaches → candidate. For Mordenkainen's:

```
HDR  "MORDENKAINEN'S SWORD"      ← real heading
HDR  "Casting Time: 1 action"    ← mis-tagged heading — OVERWRITES currentSection
HDR  "Range: 60 feet"
text "You make an area..."       ← body attaches to "Casting Time: 1 action", not the spell
```

The spell name never becomes a candidate title, so it's lost before the resolver/LLM ever run — and any
decline lands under the junk title `"Casting Time: 1 action"`, never under the spell name. This is the
same traceless silent-drop that ate Gnome (hand-authored). It can quietly eat any entity whose sub-lines
get mis-tagged as headings, corpus-wide.

## What Changes

- **Fix #1 — demote spell stat-line headings to text** (`MinerUPdfConverter`): a `section_header` whose
  text begins with a spell stat label (`Casting Time:` / `Range:` / `Components:` / `Duration:` /
  `Concentration` / `At Higher Levels`…) is a MinerU mis-tag → emit it as a `text` item, not a heading.
  The spell-name section then keeps its body and becomes a candidate. Recovers Mordenkainen's ×2 (and,
  to confirm in impl, Shield of Faith).
- **Fix #2 — traceability guard** (`EntityExtractionOrchestrator.BuildScannerInputs` /
  `EntityCandidateScanner`): make a silent candidate-loss impossible. Log a warning when a heading is
  immediately overwritten by another heading with no intervening body, and when the scanner skips a
  candidate (null/Unknown category). The bug hid all session because the drop was traceless; this surfaces
  the next one in seconds.

## Capabilities

### Modified Capabilities
- `mineru-pdf-conversion`: the converter demotes spell stat-line mis-tagged headings to text, so a spell
  whose stat lines were mis-tagged keeps its name as the section title.
- `entity-extraction-pipeline`: candidate scanning logs (rather than silently swallows) a heading
  overwritten with no body and a candidate skipped for a null category.

## Impact

- Code: `MinerUPdfConverter` (stat-line demotion in the item mapping); `BuildScannerInputs` /
  `EntityCandidateScanner` (warning logs on overwrite/skip). Unit tests per fix. Clean up / fold in the
  throwaway `SilentDropDiagnosisTests.cs` (it currently violates warnings-as-errors).
- Validation: clear the `*.mineru.json` cache, re-extract PHB through `mineru:8000` + errorsOnly; expect
  Mordenkainen's ×2 (and Shield of Faith if same root) recovered, spell count > 333, classes/races/Monster
  unchanged, no noise — and any remaining silent loss now visible in the logs.
- Non-goals: the ~21 prose-merged / special-char spell names and the remaining OCR-dropped Casting-Time
  anchors stay on the roadmap.
