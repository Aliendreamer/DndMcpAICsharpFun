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
