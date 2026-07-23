## Context

`MinerUPdfConverter` is the sole `IPdfStructureConverter` (MinerU service at `mineru:8000`), producing `PdfStructureDocument(Markdown, Items[])` with `PdfStructureItem(Type, Text, PageNumber, Level, Html)` — heading items `section_header`, tables `table` (Html). Downstream, `EntityCandidateBuilder` (+ scanners/classifier) builds candidates and `MinerUTableCollector` builds `CanonicalTable`s — both deterministic and already exercised by real-corpus harness tests (`RuleRescueHarnessTests` loads the real DMG conversion cache). Known MinerU deficits on PHB: 8 classes + 8 races never became candidates (parser gap, documented in `mem:project_parser_upgrade_mineru` / recall-fixes); tables ~45% degenerate corpus-wide pre-5etools-projection. Unlimited-OCR: ~3.3B DeepSeek-OCR-lineage VLM, ~7.3 GB FP16, OpenAI-compatible serving via vLLM/SGLang, outputs long-form markdown with layout (up to 32k tokens, whole-document or page batches).

## Goals / Non-Goals

**Goals:** hard numbers answering "does Unlimited-OCR beat MinerU as our parser?" on the real PHB — candidate recall (esp. the 16 known-dropped entities), table quality (count, degenerate %, heading-named %), section/heading fidelity; a written go/no-go. **Non-Goals:** production converter implementation/swap (follow-up change if GO); any LLM extraction run; other books (PHB only for the spike); prompt/config optimization beyond the repo's recommended modes (gundam/base); permanent infra (the serving container is torn down after).

## Decisions

- **D1 — Serve via container, weights download inside it.** vLLM (per the published recipe) or SGLang docker with `--gpus all`; HF weight download happens inside the container (the host sandbox's network allowlist doesn't include HF; the docker daemon/container path is not sandbox-restricted). One-time.
- **D2 — VRAM juggling.** The 8 GB card can't hold qwen3:8b AND Unlimited-OCR. Unload ollama's model (or stop the ollama container) for the batch, restore after. Chat is down during the parse — acceptable on the dev box; the spike report notes the wall-clock.
- **D3 — Page-image batches, whole-book.** Convert the PHB PDF to page images and process all pages (its long-context mode permits large batches; fall back to per-page if needed). Save the RAW outputs (markdown/layout per page/batch) under a git-ignored scratch dir (`.superpowers/spike-unlimited-ocr/`) so the adapter can iterate without re-running the GPU job.
- **D4 — Adapter is spike-quality and isolated.** A parser mapping raw output → `PdfStructureDocument` lives in the TEST project (or `Tools/`), not in `Features/Ingestion/Pdf`. Headings from markdown heading syntax/layout labels → `section_header` (with level), tables (markdown or HTML) → `table` items with Html (convert markdown tables to simple HTML so `HtmlTableParser` consumes them), remaining prose → `text` items with page numbers from the batch/page mapping.
- **D5 — A/B on the EXISTING deterministic paths.** Both documents (MinerU's from the real conversion cache; Unlimited-OCR's via the adapter) go through `EntityCandidateBuilder` (real `EntityNameIndex`) and `MinerUTableCollector`. Metrics: total candidates; presence of the 16 known-dropped PHB entities (the 8 classes: from the class roster; the 8 races: from the known-missing list — recover the exact names from `mem:project_extraction_recall_fixes`/the 5etools roster diff); tables total/degenerate%/named%; section_header count. Output: a comparison table in the spike report.
- **D6 — Go/no-go criteria (pre-committed).** GO if: (a) ≥half the known-dropped entities become candidates, OR (b) table degenerate rate drops by ≥half with comparable table counts — AND no catastrophic regression elsewhere (e.g. candidate count collapsing). Otherwise NO-GO (or PARTIAL: adopt for tables only / keep MinerU). Recorded with the numbers.

## Risks / Trade-offs

- **Environment friction (GPU/docker/CUDA/WSL2)** → time-box: if serving isn't up within ~45 min of attempts, STOP and report the blocker instead of grinding.
- **Output format surprises** (layout grounding format differs from docs) → the adapter iterates against SAVED raw outputs, no GPU re-runs needed.
- **8 GB is the floor for FP16** → if OOM, use the gundam (640-crop) mode or quantized weights; if still OOM, STOP and report.
- **Chat downtime during the batch** → noted to the user before the run; ollama restored in a `finally` step.

## Migration Plan

None — evaluation only. If GO: a follow-up change (`unlimited-ocr-converter`) implements a real `IPdfStructureConverter`, bumps the conversion-cache `ConverterVersion`, and re-validates per the dev-flow data gates.

## Open Questions

- Exact raw output schema (markdown-with-grounding vs plain) — resolved empirically in the spike from the first page's output.
