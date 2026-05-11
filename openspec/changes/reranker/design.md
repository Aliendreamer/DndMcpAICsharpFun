## Context

The RAG pipeline currently retrieves the top-`Limit` blocks from Qdrant by vector similarity and passes them directly to the LLM. Vector similarity (bi-encoder) is fast but imprecise — it encodes query and document independently and compares embeddings. A cross-encoder processes the query and each candidate together, attending to the relationship between them, giving much higher relevance precision at the cost of running a forward pass per candidate.

`ms-marco-MiniLM-L-6-v2` is a 22M parameter cross-encoder trained on MS MARCO passage retrieval. In ONNX format it runs in ~5-15ms per (query, passage) pair on CPU — reranking 20 candidates takes ~100-300ms, acceptable given that Ollama generation already takes 20-65 seconds.

The model is loaded once at startup via `Microsoft.ML.OnnxRuntime` and kept in memory. `Microsoft.ML.Tokenizers` handles BERT tokenization (WordPiece, 512 token limit). The ONNX file is downloaded from HuggingFace on first startup and cached in the `/app/models` volume.

## Goals / Non-Goals

**Goals:**
- Rerank Qdrant retrieval results using a cross-encoder before passing to the LLM
- Download and cache the ONNX model at startup with a clear log message
- Keep reranking in-process (no sidecar, no extra container)
- Apply reranking to all retrieval paths (block search and entity search)
- Configurable TopK (candidates fetched from Qdrant) and TopN (passed to LLM)

**Non-Goals:**
- GPU inference — CPU only for now
- Training or fine-tuning the reranker on D&D data
- Streaming reranker scores to callers
- Replacing the embedding model or changing chunk strategy

## Decisions

### Model — ms-marco-MiniLM-L-6-v2

Small (22M params), fast on CPU (~5-15ms/pair), strong MS MARCO performance, ONNX export available on HuggingFace. Larger models (MiniLM-L-12, MiniLM-L-12-v2) offer marginally better accuracy at 2x the cost — not worth it for a local setup.

**Alternative considered:** `cross-encoder/ms-marco-TinyBERT-L-2-v2` — faster but lower quality. Rejected — the extra 50ms for L-6 is worth the quality gain given our 20-65s generation time.

### Runtime — ONNX Runtime in-process

`Microsoft.ML.OnnxRuntime` runs the model directly in the .NET process. No extra container, no HTTP overhead, no deployment complexity. The model is ~70MB — loaded once, kept in memory as a static `InferenceSession`.

**Alternative considered:** Python sidecar container. Rejected — adds operational overhead (another container, health check, network call) with no benefit for a single model that runs fine on CPU.

### Model download — startup with cache check

On startup, check if `{Reranker:ModelPath}/model.onnx` exists. If not, download from HuggingFace (`https://huggingface.co/cross-encoder/ms-marco-MiniLM-L-6-v2/resolve/main/onnx/model.onnx`) using `HttpClient`. Log progress. If download fails, log a warning and disable reranking (fall back to top-K results ordered by Qdrant score). Never fail startup due to missing model.

### TopK / TopN separation

Qdrant is asked for TopK candidates (default 20). The reranker scores all K and returns TopN (default 5) to the LLM. K > N ensures the reranker has enough candidates to improve ordering. Making both configurable allows tuning without a code change.

### Tokenizer — Microsoft.ML.Tokenizers

`Microsoft.ML.Tokenizers` provides BERT WordPiece tokenization matching the model's training tokenizer. Texts exceeding 512 tokens are truncated to 510 + `[CLS]`/`[SEP]` tokens (BERT limit). This is acceptable — D&D blocks are typically 100-300 tokens.

## Risks / Trade-offs

[+100-300ms latency per query on CPU] → Mitigation: negligible relative to 20-65s Ollama generation; disable via `Reranker:Enabled = false` if unacceptable.

[Model download fails on first startup] → Mitigation: graceful fallback to Qdrant ordering, clear warning log; operator can pre-download the file into the models volume.

[512 token truncation cuts long passages] → Mitigation: D&D blocks are short by design; truncation is rare in practice.

[ONNX model file not in git (70MB)] → Mitigation: downloaded at startup into mounted Docker volume; volume persists across container restarts.

## Migration Plan

1. Add `models_data` volume to `docker-compose.yml`
2. Deploy updated image — on first startup, model downloads automatically (~70MB, logged)
3. Subsequent startups load from volume instantly
4. Rollback: set `Reranker:Enabled = false` in environment to bypass reranking without redeployment
