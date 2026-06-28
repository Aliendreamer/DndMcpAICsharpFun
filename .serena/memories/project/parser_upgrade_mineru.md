# Parser upgrade: Marker → MinerU (+ local OCR models)

User found (2026-06-28) **MinerU** + "unlimited" local OCR models and wants to move the PDF parsing layer to it. This supersedes several papered-over parser-quality issues.

## Why
Current parser = **Marker** (`MarkerPdfConverter` → `IPdfStructureConverter`, `marker:5002`, cache `books/conversion-cache/*.marker.json`, keys `markdown`+`items`). Marker's heading/layout/OCR on dense RPG layouts causes:
- **Candidate-gen gap** — `project/class_name_candidate_gap`: 8/12 class-name headings not emitted as `items` → never become candidates. Better layout/heading detection fixes this.
- **OCR garble** typed as entities (`HIT POlNTS`, `EQUlPMENT`, `Small Ano Practical`, `••• Draconians`).
- **De-spacing residuals** (`MAGEARMOR`, merged words) — relates to the `heading-despacing` / `canonical-name-normalizer` specs (file roadmap Item 4b).

MinerU = layout analysis + pluggable OCR (PaddleOCR etc.) → markdown/JSON; stronger on multi-column/stat-block layouts; runs fully LOCAL with unlimited OCR (fits the single-user all-local stance, `mem:companion_roadmap`).

## Clean swap point
Pipeline is parser-agnostic behind `IPdfStructureConverter`. Add a `MinerUPdfConverter` implementing it (map MinerU output → `PdfStructureDocument` items: heading/text + page). Keep the disk-cache pattern (`PdfConversionDiskCache`, new `*.mineru.json`). Downstream (`EntityCandidateScanner` → `DeterministicTypeResolver` → allowlist gate) is unchanged. Then re-convert + re-extract the books.

## Status / next
⬜ Not started. Own brainstorm→spec→plan when picked up. Likely SUPERSEDES: `project/class_name_candidate_gap` (candidate-gen gap), the standalone OCR de-spacing follow-up (file roadmap Item 4b). Sequence: land it before the next big extraction re-run so all books get the better parse.
