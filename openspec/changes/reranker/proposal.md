## Why

Qdrant retrieves the top-K candidates by approximate vector similarity; the top result is not always the most relevant passage for the user's query. A cross-encoder reranker scores each (query, candidate) pair jointly — looking at both texts together — which is significantly more accurate than the bi-encoder similarity used for retrieval. Adding a reranking pass after retrieval improves the quality of context fed to the LLM without changing chunk size, embedding model, or retrieval infrastructure.

## What Changes

- Add `Microsoft.ML.OnnxRuntime` and `Microsoft.ML.Tokenizers` NuGet packages to the main app
- Download `ms-marco-MiniLM-L-6-v2` cross-encoder in ONNX format at startup into a mounted volume (`/app/models`)
- Create a `CrossEncoderReranker` service that scores (query, passage) pairs using the ONNX model
- After Qdrant retrieval, rerank the top-K candidates and pass only the top-N to the LLM (K > N, default K=20, N=5)
- Expose configurable `Reranker:TopK`, `Reranker:TopN`, and `Reranker:ModelPath` in `appsettings.json`
- Add `models` volume to `docker-compose.yml` mounted at `/app/models`

## Capabilities

### New Capabilities

- `cross-encoder-reranker`: ONNX-based cross-encoder reranking of retrieval candidates; downloads model at startup; configurable TopK/TopN; applied to all RAG retrieval paths

### Modified Capabilities

- `rag-retrieval`: retrieval now fetches TopK candidates and reranks to TopN before returning to callers; result count changes from a single `Limit` to TopN

## Impact

- `DndMcpAICsharpFun` — RAG retrieval service, startup, docker-compose
- New NuGet: `Microsoft.ML.OnnxRuntime`, `Microsoft.ML.Tokenizers`
- New Docker volume: `models_data` mounted at `/app/models`
- Model download (~70MB ONNX file) on first startup — subsequent startups use cached file
- No breaking changes to MCP tool signatures or API response schema
