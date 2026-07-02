# Monster Manual (and likely DMG) recall root cause — diagnosed 2026-07-02 live

Goal: get core 2014 books (PHB/MM/DMG) to PHB-quality entity recall, then 2024 books. PHB (re-extracted
2026-07-01, post-recall-fix) is good. MM + DMG were extracted **2026-06-27, BEFORE the `extraction-name-resolution`
recall fix merged (2026-06-28)** — but re-extracting them does NOT fix recall, because the gap is UPSTREAM of
extraction.

## Symptom
`mm14.json` (old) has 226 Monster + 4 Item = 230; **missing Aboleth AND Beholder** (iconic). A live `force`
re-extract produced only **258 candidates from 6547 MinerU items**, still missing the iconics — so the early
Aboleth/Beholder checkpoint gate FAILED and the run was aborted (old canonical preserved; force writes only on success).

## Root cause (quantified from the aborted run's logs)
- **TOC/bookmark categorization maps 100% of MM to `Rule`**: `699 toc category: Rule, 3 Unknown, 0 Monster`.
  `HeadingCategoryClassifier.Guess` keys on CATEGORY keywords ("monster","dragon","fiend"...), but MM's section
  headings are individual monster NAMES ("ABOLETH","BEHOLDER","MIND FLAYER") with no "Monsters" parent bookmark to
  inherit from → every monster section falls through to `ContentCategory.Rule`.
- `EntityCandidateScanner` then SKIPS sections whose page category isn't entity-eligible: **449 unique real-monster
  sections skipped** (Aarakocra, Aboleth, Allosaurus, Androsphinx, Angels, Ankheg, Kraken, Lich, Sphinx, Unicorn...).
  This is THE dominant loss (MM has ~450 monsters).
- The **stat-block scanner** (detects `Armor Class/Hit Points/Speed` structure, bypasses the TOC gate) is the only
  path that caught anything → the 258 survivors. It misses Aboleth/Beholder because their stat blocks are OCR-damaged.
- MinerU OCR splitting name-heading from stat body ("received no body before heading 'Armor Class...'") loses only
  ~20 — a minor cause vs the 449 TOC-gate skips.

## Why BookType can't drive the fix
`BookType` = `Core` (1) for MM, PHB, AND DMG (enum: Unknown/Core/Supplement/Adventure/Setting — a PUBLISHING
category, not content). So there is no existing "bestiary" flag. `fivetoolsSourceKey` ("MM") could be a hint.

## Fix space (needs brainstorm -> opsx:propose, NOT a blind patch; recall-critical, "days"-level like PHB)
1. Make the structural **stat-block scanner authoritative** (a detected stat block IS a creature regardless of TOC)
   + robustify it against OCR name/body splits.
2. **Detect TOC-categorization failure** (a book with stat blocks but 0 Monster pages) and fall back to ungated
   candidate scanning, relying on the extraction DECLINE gate to filter non-entities (Ability Scores, Alignment,
   lair intros) rather than the TOC gate.
3. A `fivetoolsSourceKey`/content-type signal to mark monster-heavy books.
Tradeoff: ungating floods extraction with prose sections -> more LLM calls + declines (slower) but higher recall.

Relevant code: `Features/Ingestion/Pdf/HeadingCategoryClassifier.cs`, `BookmarkTocMapper.cs`,
`Features/Ingestion/EntityExtraction/EntityCandidateScanner.cs` (the skip at ~line 76), `EntityCandidateBuilder.cs`,
`Features/Ingestion/Extraction/TocCategoryMap.cs`. Related: `mem:project_companion_roadmap` (Item 4),
`mem:operations/running_the_stack`, the dev-flow skill ("recall low -> separate our-logic bug vs upstream parser gap;
if not in declined.json it was never a candidate"). MM/DMG blocks (dnd_blocks/BM25) ARE ingested; only the ENTITY
layer is blocked on this.
