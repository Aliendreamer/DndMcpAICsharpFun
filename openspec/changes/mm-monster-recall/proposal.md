## Why

The Monster Manual (`mm14.json`) extraction has a severe, silent recall gap: the current
canonical (extracted 2026-06-27, before the recall fix) holds 226 monsters and is **missing
iconic creatures including Aboleth and Beholder**. A live `force` re-extraction on the current
pipeline confirmed the cause is **upstream of extraction**, not the `extraction-name-resolution`
fix: MM's per-monster section headings are individual names (`ABOLETH`, `BEHOLDER`) that classify
as TOC category `Rule` (the book scores `699 Rule / 3 Unknown / 0 Monster` pages), so
`EntityCandidateScanner` **skips 449 real-monster sections** before extraction ever runs. Only the
stat-block scanner survives (258 candidates), and it misses the OCR-damaged iconics. Re-extraction
cannot recover an entity that never becomes a candidate — such monsters never even reach
`declined.json`.

`BookType` is `Core` for MM/PHB/DMG alike, so there is no "bestiary" flag to key on. Getting MM to
the PHB-quality bar (PHB reached 361/361 spells only after a deterministic 5etools backfill closed
the residual) requires fixing candidate generation *and* a 5etools completeness net — the same shape
that worked for spells.

Scope: **monsters, Monster Manual first.** DMG and the other gated types are an explicit later pass.

## What Changes

- **Candidate recovery (grounded).** For official books (with a `fivetoolsSourceKey`), before the
  candidate scanner skips a section on TOC-category grounds, fuzzy-match its heading against the
  5etools monster roster (reusing `EntityNameMatcher`/`EntityNameIndex` at the existing 0.90
  confidence bar). A match recovers it as a `Monster` candidate. Additionally: make the stat-block
  scanner **authoritative** (a detected `Armor Class/Hit Points/Speed` block is always a candidate,
  regardless of TOC category), and **ungate section scanning** when a book has stat blocks but zero
  Monster TOC pages (TOC categorization has clearly failed). Recovered non-monster sections are
  filtered by the existing decline gate, so recall rises without lowering precision.
- **Monster recall check.** A service + admin endpoint that loads the authoritative MM monster
  roster from the local 5etools bestiary (`source == MM`) and diffs it against the extracted
  canonical's monster names, returning the precise list of still-missing (and extra) monsters — the
  validation oracle and the input to backfill.
- **Monster backfill (completeness net).** A `MonsterBackfillService` and
  `POST /admin/books/{id}/backfill-monsters`, modeled on the existing `SpellBackfillService`: for
  each MM-roster monster missing from the canonical, project the 5etools monster into a canonical
  entity marked `dataSource: "5etools-backfill"`, appended idempotently (gap-only). This closes the
  residual the PDF cannot yield.
- **Live validation on MM:** re-extract → Aboleth + Beholder return as `Monster` (grounded) →
  recall check shows near-complete vs the 5etools MM roster → backfill closes the residual →
  ~100% monster recall, mostly grounded with backfilled entries explicitly marked.

## Capabilities

### New Capabilities

- `monster-candidate-recovery`: recover monster candidates that TOC-category classification would
  drop (5etools-roster fuzzy-match for official books; authoritative stat-block scanning and
  TOC-failure ungating for any book), plus the 5etools recall check that diffs the extracted
  canonical against the authoritative MM monster roster.
- `fivetools-monster-backfill`: deterministically backfill monsters present in the 5etools roster
  but missing from a book's canonical, marked as a 5etools-sourced backfill (gap-only, idempotent).

## Impact

- Modified: `Features/Ingestion/EntityExtraction/EntityCandidateBuilder.cs` /
  `EntityCandidateScanner.cs` (candidate recovery + authoritative stat-blocks + TOC-failure ungate;
  reuses `EntityNameMatcher`, `EntityNameIndex`).
- New: `MonsterRecallService` + admin recall endpoint; `MonsterBackfillService` +
  `POST /admin/books/{id}/backfill-monsters` (mirrors `SpellBackfillService`).
- Contracts: new endpoint(s) added to `DndMcpAICsharpFun.http` and `dnd-mcp-api.insomnia.json`.
- Data: no schema/migration change — canonical JSON only; 5etools bestiary read from the existing
  local `5etools/` data via the current source/registry plumbing.
- No change to the qwen3 extraction step, the type-resolution fix, or non-monster types (later pass).
