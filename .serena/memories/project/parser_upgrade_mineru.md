# Parser: MinerU is the main parser — SHIPPED + VALIDATED (2026-06-29)

MinerU + `-m ocr` + the spell-chapter splitter replaced Marker as the sole production PDF parser. Live prod run validated.

## VALIDATION (2026-06-29): mineru-main-parser PASSED ✅
Live prod run via the `mineru:8000` service (no CLI, no Marker) = **byte-identical to the spike**: 443 total, **12/12 classes, 7/9 races, 323/361 PHB spells, 0 noise, 583 declined**. Run ~4.4h (1032 candidates × ~30s qwen3). The production HTTP path (POST mineru:8000/file_parse → splitter → extraction → 5etools gate) reproduces the recipe exactly — acceptance gate met.
Code on main: implementer `86d55a0` + Development ServiceUrl fix `2b2c724` + prod-compose `0bc3aad`. MinerU service container in the PCC stack (`fc555d2` there). Spec `mineru-main-parser` (review-clean) — ready to archive.

## The recipe (why each piece)
- MinerU `pipeline` backend (`-b pipeline`) — layout model gets class/race section headings Marker misses.
- `-m ocr` (NOT txt/auto) — REQUIRED: PHB PDF has a corrupt embedded text layer (`1st-level`→`/st-/evel`, `You`→`Vou`); txt extracts garbage, ocr re-reads rendered pixels → clean text, keeps headings. ~30-90min/book on 8GB GPU; conversion disk-cached (`.mineru.json` suffix).
- Spell-chapter splitter (`MinerUPdfConverter`): spell NAMES are run-in labels, never headings; anchor on "Casting Time:" blocks + preceding level/school line, cut name at first digit/school → synthetic heading.
- VLM/`hybrid-engine` backend — RULED OUT (didn't fix spell headings, heavier).

## Architecture (production)
`MinerUPdfConverter` POSTs the PDF to `mineru:8000/file_parse` (`backend=pipeline`, `parse_method=ocr`, `return_content_list=true`); response `results.<firstKey>.content_list` is a JSON STRING → blocks → map+splitter. HttpClient timeout = `ConversionTimeoutMinutes` (120). Sole `IPdfStructureConverter` (+ `PdfConversionDiskCache`). MinerU service = `mineru-api` (FastAPI) in a vLLM/CUDA-13 image, GPU, in BOTH the PCC dev stack and `docker-compose.prod.yml` (replaced the marker service).

## Long-tail follow-ups (roadmap — root-caused 2026-06-29 on the LIVE run)
- **38 missing PHB spells** (323/361) — NOT garble (OCR fixed that); column-break / spell-detection edges in the splitter (entries split across the 2-column boundary, or "Casting Time:" predecessor not recognized). Chase via better entry-boundary detection. Long tail; diminishing returns. (Not yet deep-dived.)
- **"2 missing races" = TWO DIFFERENT causes (root-caused):**
  - **Dwarf — NOT missing, MIS-TYPED.** It's in phb14.json as **Monster**, not Race. The `DWARF` heading IS detected (p18); the candidate resolved to a 5etools **"Dwarf" creature** collision instead of the Race. → fix in TYPE RESOLUTION: on a Race-vs-Monster name collision, prefer Race when the candidate sits in the races chapter / has a Race prior. (Our logic, not the parser.)
  - **Gnome — parser heading-miss.** MinerU tagged `GNOME NAMES`/`GNOME TRAITS`/`Rock Gnome` but NOT the bare `GNOME` title (p36), unlike Dwarf/Elf/Halfling. → no clean Gnome race candidate. Fix: race-section fallback (anchor "X TRAITS" → promote "X"), analogous to the spell splitter.

## Deferred polish (Minor, from the review — apply at next rebuild)
- Converter: clearer exception on empty MinerU `results` (currently raw InvalidOperationException; fails loudly — acceptable).
- prod compose: add explicit `MinerU__ServiceUrl=http://mineru:8000` env for parity (works via code default).
- `cspell.json` still lists "Marker" (harmless dictionary word).

## Next steps
1. Re-convert + re-extract MM and DMG through the live service (then ingest corpus-wide).
2. Finish sequence when user says: archive `mineru-main-parser` + skill-optimizer.
Relates to `mem:project_class_name_candidate_gap` (resolved), `mem:companion_roadmap`.
