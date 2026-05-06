using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class OllamaEntityExtractionClient(
    IChatClient chat,
    ILogger<OllamaEntityExtractionClient> logger) : IEntityExtractionLlmClient
{
    public async Task<ExtractionResponse> ExtractAsync(ExtractionRequest req, CancellationToken ct)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, req.SystemPrompt),
            new(ChatRole.User, req.UserPrompt),
        };

        var chatOptions = new ChatOptions
        {
            ModelId = req.ModelId,
            MaxOutputTokens = req.MaxOutputTokens,
            ResponseFormat = ChatResponseFormat.ForJsonSchema(req.ToolInputSchema),
        };

        try
        {
            var response = await chat.GetResponseAsync(messages, chatOptions, ct);
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
