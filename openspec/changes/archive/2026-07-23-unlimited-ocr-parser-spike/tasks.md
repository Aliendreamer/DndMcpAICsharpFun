# Tasks — unlimited-ocr-parser-spike (evaluation only; time-boxed)

## 1. Serve + convert (operational)
- [ ] 1.1 Deploy Unlimited-OCR locally (vLLM per the published recipe, or SGLang docker; `--gpus all`; weights auto-download inside the container). Unload/stop ollama first (8 GB VRAM), restore after (finally). Time-box: 45 min of environment attempts, then STOP and report.
- [ ] 1.2 Run the real PHB PDF through it (page images, whole book; gundam mode if OOM). Save ALL raw outputs under `.superpowers/spike-unlimited-ocr/` (git-ignored) so later steps never need the GPU again. Note wall-clock + pages/sec.

## 2. Adapter (spike-quality, isolated)
- [ ] 2.1 Map raw output → `PdfStructureDocument` (`section_header` with level, `text` with page, `table` with Html — markdown tables converted to simple HTML for `HtmlTableParser`). Lives in the test project or Tools/, NOT `Features/Ingestion/Pdf`.

## 3. Deterministic A/B
- [ ] 3.1 Harness: score BOTH documents (MinerU's from the real PHB conversion cache; the adapter's) through `EntityCandidateBuilder` (real `EntityNameIndex`) + `MinerUTableCollector`. Metrics: total candidates; recall of the 16 known-dropped PHB entities (8 classes, 8 races); tables total / degenerate% (<2 cols or 0 rows) / heading-named%; section_header count.
- [ ] 3.2 Report `.superpowers/spike-unlimited-ocr/REPORT.md`: the comparison table + go/no-go against D6's pre-committed criteria (GO: ≥half the dropped entities recovered OR degenerate rate halved, no catastrophic regression).

## 4. Wrap
- [ ] 4.1 Tear down the serving container; ollama restored + verified; working tree clean of untracked junk (raw outputs stay in git-ignored scratch). Build 0/0 + full suite green if any tracked code was added (the harness). Present the go/no-go.
