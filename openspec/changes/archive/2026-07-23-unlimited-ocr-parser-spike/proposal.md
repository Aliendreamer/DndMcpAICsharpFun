## Why

The extraction pipeline's biggest measured wall is the PDF parser, not the LLM: MinerU's output is ~45% degenerate on tables (column-collapse) and it silently dropped 8 classes + 8 races from the PHB before any model saw them. Baidu's **Unlimited-OCR** (DeepSeek-OCR lineage, ~3.3B params, ~7.3 GB VRAM FP16 — fits the 8 GB card as a batch job, MIT, vLLM/SGLang-deployable) is a candidate replacement for MinerU that attacks exactly this wall. Because `IPdfStructureConverter` is a clean seam, the conversion cache is version-keyed, and the deterministic harnesses can score candidates/tables with ZERO LLM hours, we can answer "is it better?" with real numbers in one spike.

## What Changes

A time-boxed EVALUATION SPIKE — no production converter swap:

- Deploy Unlimited-OCR locally (vLLM/SGLang container, GPU; qwen3 unloaded during the batch) and run the REAL PHB PDF through it once, saving the raw output.
- Write a minimal adapter mapping its output (markdown + layout) into a `PdfStructureDocument` (`section_header`/`text`/`table` items) — spike-quality, in the test project or a Tools console, NOT wired into `IPdfStructureConverter`.
- A deterministic A/B harness: run BOTH parsers' documents through the existing `EntityCandidateBuilder` + `MinerUTableCollector` paths and diff — candidate counts, the 16 known-dropped PHB entities (8 classes/8 races), table count + degenerate rate (<2 cols or 0 rows), heading-named vs positional tables, section_header counts.
- A go/no-go report with the numbers → if GO, a follow-up change promotes it to a real `IPdfStructureConverter` implementation.

## Capabilities

### New Capabilities
- `parser-evaluation`: parser candidates are evaluated against the incumbent with deterministic, LLM-free A/B evidence (candidate recall on known-dropped entities, table degeneracy rate, heading quality) before any production swap.

### Modified Capabilities
<!-- none — evaluation only, no production behavior changes -->

## Impact

- New: a spike adapter + A/B harness (test project / Tools; NOT production), raw conversion artifacts under a git-ignored scratch dir.
- Operational: one-time ~7 GB weight download inside a container; ollama/qwen3 unloaded during the parse (VRAM), restored after.
- No production code paths, endpoints, or converter changes. Go/no-go recorded in the spike report; promotion is a separate change.
