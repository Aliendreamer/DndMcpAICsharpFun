# Design Amendment: Replace Anthropic API with MEAI + Ollama (Plan 2 D13)

**Date:** 2026-05-06  
**Amends:** Plan 2 architectural decision D13  
**Status:** Approved

## Context

Plan 2 was originally designed with `AnthropicMessagesClient` hitting `https://api.anthropic.com/v1/messages`.
The project runs fully on a local Ollama stack (no external API billing) and the hardware available is a
Lenovo Legion 5 Gen 10 with 64 GB RAM and 8 GB VRAM (NVIDIA RTX 5070).

## Decision

Replace D13 entirely:

- **LLM runtime:** Ollama (`http://ollama:11434`), already running in docker-compose
- **Model:** `qwen3:8b` — fits fully in 8 GB VRAM at Q4 quantisation (~4.7 GB); good instruction
  following and schema-constrained JSON output
- **SDK abstraction:** `Microsoft.Extensions.AI` (`IChatClient`) via `Microsoft.Extensions.AI.Ollama`
  NuGet — provider-portable; future swap to any MEAI-compatible backend is a one-line DI change
- **Schema constraint:** Ollama `format` field (JSON schema object) instead of Anthropic tool-use
- **Escalation model:** removed — running a 30b model CPU-only would be too slow on this hardware;
  retry logic stays (bounded retries with re-prompt) but all retries use the same `qwen3:8b`

## What Changes vs Plan 2

| Plan 2 (original) | This amendment |
| --- | --- |
| `AnthropicMessagesClient.cs` | `OllamaEntityExtractionClient.cs` |
| `AnthropicOptions.cs` (API key, base URL, model) | Removed; `OllamaOptions.ChatModel` field added |
| Anthropic tool-use schema constraint | Ollama `format: { ...json schema... }` |
| External HTTPS call | Local `http://ollama:11434` |
| `claude-sonnet` / `claude-opus` escalation | `qwen3:8b` only |

## What Stays Identical

`IEntityExtractionLlmClient`, `ExtractionRequest`, `ExtractionResponse`, `EntityExtractionOrchestrator`,
`ExtractionPromptBuilder`, `ExtractionRetryPolicy`, `CanonicalJsonWriter`, all tests (fake
`IEntityExtractionLlmClient` in unit tests is unaffected).

## New NuGet Package

```
Microsoft.Extensions.AI.Ollama
```

Registered in DI as `IChatClient`. `OllamaEntityExtractionClient` depends on `IChatClient` only —
not on `IOllamaApiClient` — so embedding and chat share the same Ollama server but use independent
client registrations.

## Config

```json
"Ollama": {
  "BaseUrl": "http://localhost:11434",
  "EmbeddingModel": "mxbai-embed-large",
  "ChatModel": "qwen3:8b"
}
```

No `Anthropic` config section.

## Docker-compose

`ollama-pull` entrypoint pulls both models on stack start:

```sh
ollama pull mxbai-embed-large && ollama pull qwen3:8b
```

## Observability

MEAI emits OpenTelemetry activities automatically. The existing `AddOpenTelemetryTracing` call in
`ServiceCollectionExtensions` picks them up; token counts and extraction latency flow into Grafana
with no extra instrumentation code.

## Expandability

To swap providers in future (e.g. back to Claude, or to Groq/OpenAI):

1. Replace the MEAI Ollama registration in `ServiceCollectionExtensions` with the target provider's
   MEAI adapter.
2. Update the relevant config section.
3. `OllamaEntityExtractionClient` and everything above it are unchanged.
