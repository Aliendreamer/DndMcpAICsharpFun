# extract-entities fails at 100s if qwen3:8b is cold

**Symptom:** `POST /admin/books/{id}/extract-entities` → book status `EntitiesFailed` (9), error `The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing`, NO progress checkpoint written (fails on the first candidate). Stack: `OllamaEntityExtractionClient.ExtractAsync` → OllamaSharp `ChatAsync` → `TaskCanceledException`.

**Root cause:** `OllamaEntityExtractionClient` (Features/Ingestion/EntityExtraction/OllamaEntityExtractionClient.cs:37) calls Ollama through an HttpClient with the .NET **default 100s timeout**. When qwen3:8b is NOT already loaded (`ollama ps` empty — Ollama unloads after its keep_alive, default ~5min idle), the first extraction call pays cold model load (~10-30s, 5.2GB into VRAM) + first big generation, which can exceed 100s → cancels → `ExtractionRetryPolicy` retries also cold → whole run fails before the 100-candidate checkpoint. Model config is correct (`Chat:ChatModel = qwen3:8b`), NOT a 14b/VRAM issue — GPU is 8GB total and qwen3:8b (100% GPU) fits (only ~1.6GB baseline used).

**Immediate fix (no code):** pre-warm before firing extraction —
`docker exec personalcommandcenter-ollama-1 ollama run qwen3:8b "say ok"` (cold load+gen ~23s), confirm `ollama ps` shows `qwen3:8b 100% GPU`, THEN `POST .../extract-entities`. Warm-run candidates are ~15-65s each (<100s) so it proceeds. NOTE the ollama container has NO curl/wget — use `ollama run` (or the ollama CLI), not an API curl.

**Durability caveat:** keep_alive is ~4-5min; continuous extraction use keeps it warm, BUT at a book->book transition (candidate-building phase does PDF-cache + CPU work, no Ollama calls) a >5min gap can unload the model → the NEXT book's first candidate cold-fails again. Re-warm at each book transition, or set a longer keep_alive.

**Proper fix (follow-up, code):** bump the Ollama extraction client's HttpClient.Timeout well above worst-case cold-start+gen (e.g. 5-10min), so cold starts don't fail. This is the real bug — 100s is too tight for a local 8B on an 8GB card. See [[extraction_pipeline_state]], [[companion_roadmap]] (local-model ruled out at current VRAM).
