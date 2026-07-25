using System.Text.Json;

using DndMcpAICsharpFun.Infrastructure.Ollama;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class OllamaEntityExtractionClient(
    IChatClient chat,
    PartialJsonRecoverer recoverer,
    IOptions<OllamaOptions> ollamaOptions,
    ILogger<OllamaEntityExtractionClient> logger) : IEntityExtractionLlmClient
{
    private readonly TimeSpan _callTimeout = TimeSpan.FromSeconds(ollamaOptions.Value.ChatTimeoutSeconds);

    public async Task<ExtractionResponse> ExtractAsync(ExtractionRequest req, CancellationToken ct)
    {
        // Suppress qwen3's thinking block for extraction: type selection is already handled
        // deterministically (DeterministicTypeResolver) + by the discriminated-union schema, so the
        // <think> block adds latency and causes runaway generations that exhaust the token budget
        // (empty responses) — see the extraction-think-mode change. `/no_think` at the end of the
        // user turn is the reliable qwen3 directive.
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, req.SystemPrompt),
            new(ChatRole.User, req.UserPrompt + "\n\n/no_think"),
        };

        var chatOptions = new ChatOptions
        {
            ModelId = req.ModelId,
            MaxOutputTokens = req.MaxOutputTokens,
            // Greedy/deterministic decoding: the model reliably picks the most-likely union branch
            // (a stat block -> Monster) instead of sampling the decline branch ~half the time.
            Temperature = 0f,
            ResponseFormat = ChatResponseFormat.ForJsonSchema(req.ToolInputSchema),
        };

        // Bound each generation with a per-call timeout so a hung ollama response (a giant stat block
        // qwen3 never returns from) is recorded as a failed candidate and the run continues, instead
        // of blocking the whole extraction forever (the extraction OllamaApiClient's HttpClient has an
        // infinite socket timeout, so cancellation is the only bound). A real caller cancellation via
        // `ct` still aborts the run; only a timeout that fires while `ct` is NOT cancelled is swallowed.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_callTimeout);

        try
        {
            var response = await chat.GetResponseAsync(messages, chatOptions, timeoutCts.Token);
            var rawText = response.Text ?? string.Empty;
            var inputTokens = (int)(response.Usage?.InputTokenCount ?? 0);
            var outputTokens = (int)(response.Usage?.OutputTokenCount ?? 0);
            var stopReason = response.FinishReason?.Value;

            if (string.IsNullOrWhiteSpace(rawText))
            {
                return new ExtractionResponse(
                    Success: false, ToolInput: null, StopReason: stopReason,
                    InputTokens: inputTokens, OutputTokens: outputTokens,
                    ErrorMessage: "Empty response from Ollama",
                    RawJson: null);
            }

            try
            {
                var doc = JsonDocument.Parse(rawText);
                return new ExtractionResponse(
                    Success: true, ToolInput: doc.RootElement.Clone(), StopReason: stopReason,
                    InputTokens: inputTokens, OutputTokens: outputTokens,
                    ErrorMessage: null, RawJson: rawText);
            }
            catch (JsonException ex)
            {
                if (recoverer.TryRecover(rawText, out var recovered))
                {
                    logger.LogWarning(
                        "Recovered partial JSON for model {Model}: {Recovered}/{Total} chars",
                        req.ModelId, recovered.Length, rawText.Length);
                    using var recoveredDoc = JsonDocument.Parse(recovered);
                    return new ExtractionResponse(
                        Success: true, ToolInput: recoveredDoc.RootElement.Clone(), StopReason: stopReason,
                        InputTokens: inputTokens, OutputTokens: outputTokens,
                        ErrorMessage: null, RawJson: recovered);
                }

                logger.LogWarning(
                    "Ollama returned non-JSON for model {Model}: {Err} — raw: {Raw}",
                    req.ModelId, ex.Message, rawText[..Math.Min(300, rawText.Length)]);
                return new ExtractionResponse(
                    Success: false, ToolInput: null, StopReason: stopReason,
                    InputTokens: inputTokens, OutputTokens: outputTokens,
                    ErrorMessage: $"Response was not valid JSON: {ex.Message}",
                    RawJson: rawText);
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            logger.LogWarning(
                "Ollama chat request timed out after {TimeoutSeconds}s for model {Model} — recording candidate as failed and continuing",
                _callTimeout.TotalSeconds, req.ModelId);
            return new ExtractionResponse(
                Success: false, ToolInput: null, StopReason: "timeout",
                InputTokens: 0, OutputTokens: 0,
                ErrorMessage: $"Ollama call timed out after {_callTimeout.TotalSeconds:0}s",
                RawJson: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Ollama chat request failed for model {Model}", req.ModelId);
            return new ExtractionResponse(
                Success: false, ToolInput: null, StopReason: null,
                InputTokens: 0, OutputTokens: 0,
                ErrorMessage: ex.Message,
                RawJson: null);
        }
    }
}