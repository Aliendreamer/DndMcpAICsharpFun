# Coverage baseline (2026-11-23, extractor at 983b9492)

Unheld content lines per book. "Committed" = `tools/coverage_scan.py` over `books/canonical/*.json` (overrides and
declines applied, Declined excluded). "Raw extractor" = `ExtractionResult.coverage` of the current extractor
(`fullbook_compare.py --ref HEAD`), before overrides.

| Book | Content lines | Committed unheld | Raw extractor unheld |
| --- | ---: | ---: | ---: |
| bgg | 9349 | 540 | 368 |
| dmg14 | 18993 | 755 | 519 |
| egw | 22864 | 1478 | 715 |
| elemental-evil-player-s-companion | 1376 | 175 | 110 |
| erlw | 22489 | 1796 | 941 |
| ftd | 12871 | 857 | 274 |
| ggr | 13373 | 670 | 427 |
| mm14 | 18820 | 705 | 128 |
| mpmm | 14064 | 415 | 98 |
| mtf | 16344 | 1152 | 919 |
| phb14 | 19880 | 2701 | 1202 |
| scag | 13054 | 1378 | 678 |
| scc | 11642 | 529 | 426 |
| tce | 10662 | 447 | 256 |
| vrgr | 17754 | 616 | 733 |
| xge | 10058 | 207 | 261 |
| **Total** | **233593** | **14421** | **8055** |

Committed is higher than raw for most books: the committed files come from older extractor runs (PHB was never
re-extracted after the boundary fixes) and some review overrides replaced extracted text with OCR-corrected text,
which no longer matches the raw OCR line (a measurement artefact, not lost content).

## After re-extraction (2026-11-23, extractor at ba65af71, all 16 books re-extracted live)

| Book | Committed unheld before → after | Raw extractor unheld before → after |
| --- | ---: | ---: |
| bgg | 540 → 276 | 368 → 47 |
| dmg14 | 755 → 442 | 519 → 52 |
| egw | 1478 → 947 | 715 → 90 |
| elemental-evil-player-s-companion | 175 → 44 | 110 → 0 |
| erlw | 1796 → 1376 | 941 → 149 |
| ftd | 857 → 724 | 274 → 12 |
| ggr | 670 → 527 | 427 → 154 |
| mm14 | 705 → 656 | 128 → 2 |
| mpmm | 415 → 387 | 98 → 0 |
| mtf | 1152 → 399 | 919 → 3 |
| phb14 | 2701 → 703 | 1202 → 64 |
| scag | 1378 → 447 | 678 → 43 |
| scc | 529 → 354 | 426 → 32 |
| tce | 447 → 235 | 256 → 1 |
| vrgr | 616 → 322 | 733 → 23 |
| xge | 207 → 116 | 261 → 3 |
| **Total** | **14421 → 7955** | **8055 → 675** |

`fullbook_compare --prev 2045543a`: 0 newly orphaned lines in every book. 697 entities flagged for review (mostly the new
"unclaimed prose" Lore); 20 orphaned overrides.

Why the committed number stays higher than the raw one (lines unheld after overrides, by cause): left out on purpose
~2012 (the committed scan cannot see `left_out`), declined 250, raw-unheld 635, and **~5,500 lines whose text the new
extraction holds but a reviewed override replaces**: ~3,500 in prose entities (the override keeps the shorter reviewed
text, so text the extractor now appends is masked) and ~1,500 in Monsters (reviewed overrides that stripped lore glued
into a stat block; that lore then has no other holder). Follow-up: carry newly extracted text through text overrides,
and stop stat blocks claiming the lore that follows them.

## After the final-review fixes (extractor at 9a6e9284, all 16 books re-extracted again)

Committed-file scan (now counting front/back-matter lines as left out): bgg 225, dmg14 361, egw 885, eepc 3, erlw 1313,
ftd 665, ggr 419, mm14 579, mpmm 317, mtf 325, phb14 208, scag 260, scc 280, tce 220, vrgr 251, xge 38 — **total 6349**
(from 14421). Raw extractor unchanged at 675; 0 newly orphaned lines. 571 entities flagged for review; 17 orphaned
overrides.

Open follow-ups (not in this change):
- **Overrides mask newly extracted text** (~5,000 lines): a reviewed text override keeps its shorter value although the
  new extraction holds more of the section; Monster overrides that stripped glued lore leave that lore unheld. Needs
  the override merge to carry extra extracted text through, and a Monster stop rule so lore after a stat block becomes
  its own Lore (also fixes MM p107 Brass Dragon / Wyrmling).
- Review the ~570 flagged entities (mostly "unclaimed prose" Lore; table-cell fragments on ERLW/XGE pages remain noisy).
