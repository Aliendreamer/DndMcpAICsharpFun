## 1. Dependencies and Configuration

- [x] 1.1 Add `Microsoft.ML.OnnxRuntime` and `Microsoft.ML.Tokenizers` NuGet packages to `DndMcpAICsharpFun.csproj`
- [x] 1.2 Add `models_data` volume to `docker-compose.yml` and mount it at `/app/models` for the `app` service
- [x] 1.3 Add `Reranker` section to `Config/appsettings.json`: `Enabled: true`, `ModelPath: "models"`, `ModelUrl: "https://huggingface.co/cross-encoder/ms-marco-MiniLM-L-6-v2/resolve/main/onnx/model.onnx"`, `TopK: 20`, `TopN: 5`
- [x] 1.4 Create `Features/Retrieval/RerankerOptions.cs` — record with `Enabled`, `ModelPath`, `ModelUrl`, `TopK`, `TopN` bound to `"Reranker"` section

## 2. Model Download Service

- [x] 2.1 Create `Features/Retrieval/ModelDownloader.cs` — static async method `EnsureModelAsync(RerankerOptions opts, ILogger logger, CancellationToken ct)` that checks for `{ModelPath}/model.onnx`, downloads from `ModelUrl` via `HttpClient` with progress logging if missing, returns `bool` indicating success
- [x] 2.2 Handle download failure gracefully — catch exceptions, log warning `"Reranker model download failed: {ex.Message}. Reranking disabled for this session."`, return false without throwing

## 3. Cross-Encoder Reranker

- [x] 3.1 Create `Features/Retrieval/CrossEncoderReranker.cs` — singleton service; constructor takes `RerankerOptions`, `ILogger`; on construction calls `ModelDownloader.EnsureModelAsync`; if successful loads `InferenceSession` from model file; sets `_enabled` flag
- [x] 3.2 Implement `RerankAsync(string query, IReadOnlyList<string> passages, CancellationToken ct)` — tokenise each `[CLS] query [SEP] passage [SEP]` pair using `BertTokenizer` from `Microsoft.ML.Tokenizers`, truncate to 512 tokens, run ONNX inference, return `float[]` scores in input order
- [x] 3.3 Implement `SelectTopN(IReadOnlyList<RetrievalResult> candidates, float[] scores, int topN)` — zip candidates with scores, sort descending by score, return first topN
- [x] 3.4 Register `CrossEncoderReranker` as singleton in `Program.cs` and call initialisation during startup (after app build, before `app.Run()`)

## 4. Retrieval Integration

- [x] 4.1 Update the retrieval service to fetch `RerankerOptions.TopK` candidates from Qdrant instead of the previous fixed limit
- [x] 4.2 After fetching candidates, call `CrossEncoderReranker.RerankAsync` with the query text and candidate passage texts; when `_enabled` is false, skip reranking and return first TopN by Qdrant score
- [x] 4.3 Verify result mapping (text, score, payload fields) is unchanged — no callers need updating

## 5. Tests

- [x] 5.1 Unit test `ModelDownloader` — when file exists, no download attempted; when download fails, returns false without throwing
- [x] 5.2 Unit test `CrossEncoderReranker.SelectTopN` — given scores `[0.1, 0.9, 0.5]` and TopN=2, returns candidates at indices 1 and 2 in that order
- [x] 5.3 Unit test fallback — when `_enabled = false`, `RerankAsync` is not called and first TopN Qdrant results are returned
- [x] 5.4 Run `dotnet build` and `dotnet test` — all tests pass
