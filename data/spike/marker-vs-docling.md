# Marker vs Docling — Conversion Quality Report (Tasha's Cauldron of Everything)

Generated: 2026-06-07 16:35

## Headline

| Metric | Docling | Marker |
| --- | --- | --- |
| Structured items | 4832 | 4254 |
| Heading items | 1164 | 978 |
| Entity candidates | 408 | 401 |
| Flagged (garbled) names | 385 | 375 |
| Flagged rate | 94.4% | 93.5% |

## Candidates per entity type

| Type | Docling | Marker |
| --- | --- | --- |
| Class | 302 | 312 |
| Item | 57 | 56 |
| Monster | 15 | 4 |
| Spell | 34 | 29 |

## Sample: flagged Docling names vs Marker names on the same page

| Page | Docling name (flagged) | Marker name on same page |
| --- | --- | --- |
| 25 | BARBARIAN | BARBARIAN ⚠ |
| 25 | OPTIONAL CLASS FEATURES | BARBARIAN ⚠ |
| 25 | 3rd-level barbarian f eature | BARBARIAN ⚠ |
| 25 | 7th-level barbarian f eature | BARBARIAN ⚠ |
| 25 | PRIMAL PATHS | BARBARIAN ⚠ |
| 25 | PATH OF THE BEAST | BARBARIAN ⚠ |
| 25 | 3rd-level Path of the Beast f eature | BARBARIAN ⚠ |
| 26 | BESTIAL SOUL 6th-level Path of the Beast f eature | BESTIAL SOUL ⚠ |
| 26 | INFECTIOUS FURY 10th-level Path of the Beast f eature | BESTIAL SOUL ⚠ |
| 26 | CALL THE HUNT 14th-level Path of the Beast f eature | BESTIAL SOUL ⚠ |
| 26 | PATH OF WILD MAGIC | BESTIAL SOUL ⚠ |
| 26 | MAGIC AWARENESS 3rd-level Path of Wild Magic f eature | BESTIAL SOUL ⚠ |
| 26 | 3rd-level Path of  Wild Magic f eature | BESTIAL SOUL ⚠ |
| 27 | 6th-level Path of  W ild Magic f eature | d8 Magical Effect |
| 27 | 10th-level Path of W ild Magic f eature | d8 Magical Effect |
| 27 | 14th-level Path of  Wild Magic f eature | d8 Magical Effect |
| 28 | BARD | BARD ⚠ |
| 28 | 1st-level bard f eature | BARD ⚠ |
| 28 | 2nd-level bard f eature | BARD ⚠ |
| 29 | 4th-level bard f eature | BARDIC VERSATILITY ⚠ |
| 29 | BARD COLLEGES | BARDIC VERSATILITY ⚠ |
| 29 | COLLEGE OF CREATION | BARDIC VERSATILITY ⚠ |
| 29 | 3rd-level College of Creation f eature | BARDIC VERSATILITY ⚠ |
| 29 | 3rd-level College of  Creation f eature | BARDIC VERSATILITY ⚠ |
| 30 | DANCING ITEM | Animating Performance |

## Marker-only flagged names (first 15)

- p25: BARBARIAN
- p25: OPTIONAL C LASS FEATURES
- p25: PRIMAL KNOWLEDGE
- p25: INSTINCTIVE POUNCE
- p25: PRIMAL PATHS
- p25: PATH OF THE BEAST
- p25: ORIGIN OF THE BEAST
- p25: FORM OF THE BEAST
- p26: BESTIAL SOUL
- p26: INFECTIOUS FURY
- p26: CALL THE HUNT
- p26: PATH OF WILD MAGIC
- p26: MAGIC AWARENESS
- p26: WILD SURGE
- p27: BOLSTERING MAGIC

## Verdict inputs

- Docling flagged rate: **94.4%** — current pipeline baseline
- Marker flagged rate: **93.5%**
- Decision rule (design.md): Marker ≲15% and tables no worse → spec real migration; comparable/worse → delete spike.

## Refined analysis (all-caps excluded)

The headline flagged-rate is confounded: D&D books typeset *every* heading in ALL CAPS, so the
all-caps rule flags ~90% of names from both converters. Excluding it isolates true OCR garble
(split-words like "f eature", case-alternation like "YouR", noise):

| Metric (heading items) | Docling | Marker |
| --- | --- | --- |
| Headings | 1164 | 978 |
| Split-word artifacts | 241 (20.7%) | 96 (9.8%) |
| Case-alternation garble | 6 (0.5%) | 4 (0.4%) |
| **True garble rate** | **21.2%** | **10.2%** |

Qualitative differences:

- Docling's split-words are systematic: every "Nth-level <class> feature" subtitle renders as
  "f eature" (241 occurrences) and subtitles concatenate into the heading name
  ("BESTIAL SOUL 6th-level Path of the Beast f eature"). Marker keeps headings clean ("BESTIAL SOUL").
- Marker's split-words are OCR misreads *inside decorative caps headings*
  ("CUSTOMI ZING YOUR O RIGIN", "TH E MACI C OF ARTI FI CE") — fewer, but uglier when they occur.
- Marker found only 4 Monster candidates vs Docling's 15 — possible heading-detection loss in
  stat blocks; needs investigation before any migration.

## Operational cost

- Marker conversion of Tasha's (256 pages): **2 h 20 min** on GPU (RTX, 99% util throughout).
- Docling (CPU container): minutes-scale, already disk-cached per file hash.
- Mitigation if migrating: identical disk-cache pattern makes it a one-time cost per book.

## Verdict

Mixed (per design decision rule → discuss):

- ✅ Marker **halves true OCR garble** (10.2% vs 21.2%) and eliminates the systematic
  "f eature" subtitle corruption — the single largest hand-correction burden.
- ⚠ Monster candidate count regression (4 vs 15) unexplained.
- ⚠ 2h20m GPU per book vs minutes on CPU for Docling.
- ➖ All-caps headings unchanged either way (downstream LLM title-casing already handles this).

## Monster drop — root cause (investigated)

Full diagnostics in `monster-drop-investigation.md`. Findings:

1. **Page alignment is perfect** — shared headings land on identical pages in both converters.
   Not a page-mapping bug.
2. **No content is lost** — Marker finds MORE headings on the Monster pages than Docling
   (19 vs 12 on p149, 12 vs 6 on p150), including all the creature-type headings.
3. **Root cause: table-caption promotion.** Marker emits the dice-table header
   `"d4 Desired Offering"` as a `SectionHeader` after every creature-type heading
   (11 times across the two pages). Under the scanner's "current section" rule, the text
   following each creature heading is attributed to `"d4 Desired Offering"` instead of
   `BEASTS`/`CELESTIALS`/etc., so the creature-type sections end up with no text blocks
   and produce no candidates.
4. Fixable in the spike's `MarkerPdfConverter` mapping — e.g. don't promote a
   `SectionHeader` that matches a dice-column caption pattern (`^d\d+\b`) or that sits
   immediately before/inside a table block.
5. Side observation: Marker's split-word OCR garble is *worse than Docling on these
   decorative creature-type headings* ("ABER R ATIONS", "CE LESTIALS", "OOZ ES",
   "H U MANOIDS") — though Docling has its own ("FI E N DS", "UN  DEAD").

## Follow-up spike: use_llm mode (qwen2.5vl:3b via local Ollama)

Slice test (pages 24-30, 148-150; CPU-pinned model, 300s timeout, ~25 min for 10 pages):

- **Headings: byte-identical to baseline.** Same 9 garbled headings, same 13 `d4 Desired
  Offering` caption-headings. Marker's `use_llm` processors target tables, forms, equations
  and complex regions — they never touch `SectionHeader` text. A bigger vision model would
  not change this; heading text simply isn't in the LLM path.
- **Tables: all reworked** (0 of 7 table blocks identical to baseline) — `use_llm` is a
  table-quality lever, potentially relevant for stat blocks, at ~2.5 min/page extra on CPU.
- Operational note: on the 8 GB GPU, marker's own models leave ~2.8 GiB free — too little
  for even the 3B vision model, so LLM mode requires CPU pinning (works, ~5-60s/call).

### Same-page heading comparison (slice, 1-based pages 24-30 + 148-150)

| | Docling | Marker (±LLM) |
| --- | --- | --- |
| Headings | 79 | 84 |
| Split-word garbled | 20 (25%) | 9 (11%) |
| Subtitle-polluted headings ("Nth-level X f eature") | 17 | 0 |

### New insight: much garble is mechanically fixable, converter-independent

The dominant residual pattern in BOTH converters is letter-spacing breakage in decorative
caps: `ABER R ATIONS`, `H U MANOIDS`, `OPTIONAL C LASS FEATURES`, `TH E WAR R IOR` (Marker)
and `FI E N DS`, `UN  DEAD`, `A CTI O NS` (Docling). Both also inherit text-layer defects
like `lOTH-lEVEL` (l-for-1) — proof some garble lives in the PDF itself, beyond any
converter. A deterministic de-spacing normalizer (collapse stray 1-2 letter fragments
inside all-caps headings) would fix most of these for either converter — a cheaper,
converter-independent win than switching converters.

### Follow-up verdict

- `use_llm` is NOT a heading-quality lever — don't pursue bigger vision models for names.
- Marker's structural advantage (subtitle separation: 17→0 polluted headings) is real and
  is the main migration argument.
- Recommended next experiment if any: heading de-spacing normalizer in our pipeline
  (works with Docling today, no conversion cost), then re-evaluate whether Marker
  migration is still worth 2h20m/book.
