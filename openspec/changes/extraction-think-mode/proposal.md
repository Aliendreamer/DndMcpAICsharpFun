## Why

qwen3:8b is a hybrid reasoning model that emits a `<think>` block by default, and the extraction client never sets Ollama's `think` option — so every candidate is extracted with thinking on. Two problems suggest thinking may be costing more than it returns: (1) the DMG run's "Empty response from Ollama" failures all hit long/hard candidates (Flame Tongue, Ioun Stone, Robe of Eyes…), the classic signature of the model spending its whole `MaxOutputTokensPerEntity` (8192) budget on reasoning and never emitting the tool call; (2) thinking is verbose, so it roughly doubles per-candidate wall time. But thinking generally *improves* classification accuracy — and the new Object-vs-Monster decision is exactly the kind of nuanced call it may help. We can't decide blind: we need `think` to be configurable and an A/B procedure to measure the trade-off before adopting `think:false`.

## What Changes

- Add a configurable `Think` extraction option threaded through to Ollama's `think` request field. **Default preserves current behaviour** (thinking on).
- Defensively strip any leaked `<think>…</think>` block from a response before it is parsed, so reasoning never lands in extracted content regardless of think mode.
- Record the think mode and per-candidate timing + outcome (ok / empty / declined) so two runs over the same sample are directly comparable.
- Document a repeatable A/B procedure and acceptance criteria (in design.md) to measure think-on vs think-off on a fixed candidate sample: wall time, empty-response failure rate, and type-classification accuracy (do siege weapons become `Object`; do known entities extract correctly).

## Capabilities

### New Capabilities
- `extraction-think-mode`: A configurable qwen3 think mode for entity extraction (Ollama `think` option), defensive `<think>` stripping, and the per-candidate observability needed to A/B-measure thinking-on vs thinking-off.

## Impact

- **Config/extraction**: `EntityExtractionOptions` (new `Think` option), `ExtractionRequest`, `OllamaEntityExtractionClient` (set the Ollama `think` field; strip `<think>`), Ollama request model.
- **Observability**: per-candidate timing + outcome logging keyed by think mode.
- **No schema/endpoint change**; the extraction endpoints are unchanged.
- **Out of scope**: adopting `think:false` permanently (that's a data-driven decision after the A/B), and the "no_think-on-retry" fallback pattern (a possible follow-up once the A/B shows the trade-off).
