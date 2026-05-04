using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class AnthropicMessagesClient(
    HttpClient http,
    IOptions<AnthropicOptions> options,
    ILogger<AnthropicMessagesClient> logger) : IEntityExtractionLlmClient
{
    private readonly AnthropicOptions _opts = options.Value;

    public async Task<ExtractionResponse> ExtractAsync(ExtractionRequest req, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = req.ModelId,
            ["max_tokens"] = req.MaxOutputTokens,
            ["system"] = req.SystemPrompt,
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = req.UserPrompt,
            }),
            ["tools"] = new JsonArray(new JsonObject
            {
                ["name"] = req.ToolName,
                ["description"] = req.ToolDescription,
                ["input_schema"] = JsonNode.Parse(req.ToolInputSchema.GetRawText()),
            }),
            ["tool_choice"] = new JsonObject { ["type"] = "tool", ["name"] = req.ToolName },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _opts.BaseUrl)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("x-api-key", _opts.ApiKey);
        request.Headers.Add("anthropic-version", _opts.ApiVersion);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_opts.RequestTimeoutSeconds));

        try
        {
            using var resp = await http.SendAsync(request, cts.Token);
            var responseText = await resp.Content.ReadAsStringAsync(cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Anthropic API error {Status}: {Body}", (int)resp.StatusCode, responseText);
                return new ExtractionResponse(
                    Success: false, ToolInput: null, StopReason: null,
                    InputTokens: 0, OutputTokens: 0,
                    ErrorMessage: $"HTTP {(int)resp.StatusCode}: {responseText}",
                    RawJson: responseText);
            }

            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;
            var stopReason = root.TryGetProperty("stop_reason", out var sr) ? sr.GetString() : null;
            var usage = root.TryGetProperty("usage", out var u) ? u : default;
            var inputTokens = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0;
            var outputTokens = usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0;

            JsonElement? toolInput = null;
            if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in content.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var t)
                        && t.GetString() == "tool_use"
                        && block.TryGetProperty("input", out var input))
                    {
                        toolInput = input.Clone();
                        break;
                    }
                }
            }

            if (toolInput is null)
            {
                return new ExtractionResponse(
                    Success: false, ToolInput: null, StopReason: stopReason,
                    InputTokens: inputTokens, OutputTokens: outputTokens,
                    ErrorMessage: $"No tool_use block in response (stop_reason={stopReason})",
                    RawJson: responseText);
            }

            return new ExtractionResponse(
                Success: true, ToolInput: toolInput, StopReason: stopReason,
                InputTokens: inputTokens, OutputTokens: outputTokens,
                ErrorMessage: null, RawJson: responseText);
        }
        catch (TaskCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return new ExtractionResponse(
                Success: false, ToolInput: null, StopReason: null,
                InputTokens: 0, OutputTokens: 0,
                ErrorMessage: $"Anthropic request exceeded {_opts.RequestTimeoutSeconds}s timeout",
                RawJson: null);
        }
    }
}
