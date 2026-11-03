using System.Text.Json;

using Microsoft.Extensions.AI;

namespace DndMcpAICsharpFun.Features.Chat;

public sealed record ToolCallTrace(string Name, string ArgumentsJson, string? Result, string? Error);

public sealed record ChatTurnResult(
    string Text,
    IReadOnlyList<string> OfferedTools,
    IReadOnlyList<ToolCallTrace> ToolCalls,
    long LatencyMs);

public static class ChatTurnTraceBuilder
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<ToolCallTrace> Build(ChatResponse response)
    {
        var contents = response.Messages.SelectMany(m => m.Contents).ToList();
        var results = new Dictionary<string, FunctionResultContent>();
        foreach (var r in contents.OfType<FunctionResultContent>())
            results[r.CallId] = r;

        var traces = new List<ToolCallTrace>();
        foreach (var call in contents.OfType<FunctionCallContent>())
        {
            var args = JsonSerializer.Serialize(
                call.Arguments ?? new Dictionary<string, object?>(), WebJson);

            string? text = null;
            string? error = null;
            if (results.TryGetValue(call.CallId, out var result))
            {
                if (result.Exception is not null)
                    error = result.Exception.Message;

                text = Render(result.Result);
                if (TryUnwrapMcp(text, out var unwrapped, out var mcpError))
                {
                    text = unwrapped;
                    error ??= mcpError;
                }

                error ??= DetectError(text);
            }

            traces.Add(new ToolCallTrace(call.Name, args, text, error));
        }

        return traces;
    }

    private static string? Render(object? value) => value switch
    {
        null => null,
        string s => s,
        JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
        _ => JsonSerializer.Serialize(value, WebJson),
    };

    // MCP CallToolResult serializes as {"content":[{"type":"text","text":"..."}],"isError":bool}.
    private static bool TryUnwrapMcp(string? text, out string? unwrapped, out string? error)
    {
        unwrapped = null;
        error = null;
        if (text is null || !text.TrimStart().StartsWith('{'))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Array)
                return false;

            var parts = content.EnumerateArray()
                .Where(i => i.ValueKind == JsonValueKind.Object
                    && i.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() == "text"
                    && i.TryGetProperty("text", out var tx) && tx.ValueKind == JsonValueKind.String)
                .Select(i => i.GetProperty("text").GetString()!);
            unwrapped = string.Join("\n", parts);

            if (root.TryGetProperty("isError", out var ie) && ie.ValueKind == JsonValueKind.True)
                error = unwrapped.Length == 0 ? "MCP tool error" : unwrapped;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? DetectError(string? text)
    {
        if (text is null)
            return null;

        if (text.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
            return text;

        if (!text.TrimStart().StartsWith('{'))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("error") && prop.Value.ValueKind != JsonValueKind.Null)
                {
                    var msg = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.GetRawText();
                    return string.IsNullOrWhiteSpace(msg) ? "error" : msg;
                }
                if (prop.NameEquals("isError") && prop.Value.ValueKind == JsonValueKind.True)
                    return text;
            }
        }
        catch (JsonException)
        {
            // not JSON; treat as plain text
        }

        return null;
    }
}
