# Design: Chunk-Based Extraction

**Date:** 2026-05-08
**Status:** Proposed — to be planned after current run completes

## Problem

Asking an 8B model to extract an entire page at once is too much context. The model struggles to produce valid structured JSON when the input is large, leading to extraction failures, schema validation errors, and high retry counts. The failure rate is not primarily a model-size problem — it is an input-size problem.

## Approach

Instead of sending a full page to the model, split each page into smaller block/section chunks first, extract each chunk independently to a small JSON fragment, then merge the fragments with deterministic code.

```
1 page
  → Docling block/section chunks
  → each chunk → LLM → small JSON (schema-validated)
  → merge JSONs with deterministic code
```

Full pipeline:

```
Docling → clean chunks → Qwen 7B/8B → schema validator → repair loop → merge
```

## Why This Works

The key fix is **not** model size alone. It is:

- **Small chunk** — model sees only the relevant text, not the whole page
- **Strict schema** — Ollama `format` field enforces the output shape
- **Examples** — few-shot examples in the system prompt for this chunk type
- **JSON validation** — NJsonSchema validates the output immediately
- **Retry repair** — on validation failure, re-prompt with the error and the bad output

Each chunk produces a small, focused JSON. Merging is a deterministic code operation — no LLM involved.

## Model Choice

Text-only pipeline only. **VL models are rejected** — cannot fit on hardware (8 GB VRAM, RTX 5070 Ti).

| Model | Verdict |
|---|---|
| `Qwen2.5-7B-Instruct` | Preferred — good structured extraction |
| `Qwen3-8B-Instruct` | Preferred — current default (`qwen3:8b`) |
| `Qwen2.5-VL-*` / `Qwen3-VL-*` | **No** — VRAM limit |
| `Gemma 3 12B` quantized | Maybe |
| `Gemma 3 27B` quantized | Likely too slow |
| `Llama 3.1/3.2 8B` | OK, but Qwen better for structured output |

## What Changes

| Component | Change |
|---|---|
| `DoclingPdfConverter` output | Expose block-level items (already available as `DoclingItem` list) |
| `EntityCandidateScanner` | Group `DoclingItem`s into chunk windows instead of full-page text |
| `ExtractionRequest` | `UserPrompt` contains one chunk, not one full candidate text |
| `EntityExtractionOrchestrator` | Iterate chunks per candidate; collect partial JSONs; merge |
| `ExtractionPromptBuilder` | Add chunk-level few-shot example to system prompt |
| New: `ChunkJsonMerger` | Deterministic merge of partial field JSONs for a single entity |

## What Does NOT Change

- `IEntityExtractionLlmClient` and `OllamaEntityExtractionClient` — abstraction unchanged
- Schema files — same JSON Schema validation
- `ExtractionRetryPolicy` — same retry logic applies per chunk
- Checkpoint / errorsOnly plumbing — unchanged
- Docling cache — unchanged

## Success Criteria

- Extraction failure rate drops from current ~1–2% toward zero
- Average tokens per LLM call drops significantly (chunk vs full page)
- No regression in entity quality vs current pipeline
- All existing tests pass; new chunk-merging tests added (TDD)
