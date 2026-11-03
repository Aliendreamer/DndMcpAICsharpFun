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
                call.Arguments ?? new Dictionary<string, object?>(), JsonSerializerDefaults_Web);

            string? text = null;
            string? error = null;
            if (results.TryGetValue(call.CallId, out var result))
            {
                if (result.Exception is not null)
                    error = result.Exception.Message;

                text = Render(result.Result);
                error ??= DetectError(result.Result, text);
            }

            traces.Add(new ToolCallTrace(call.Name, args, text, error));
        }

        return traces;
    }

    private static readonly JsonSerializerOptions JsonSerializerDefaults_Web = new(JsonSerializerDefaults.Web);

    private static string? Render(object? value) => value switch
    {
        null => null,
        string s => s,
        JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
        _ => JsonSerializer.Serialize(value, JsonSerializerDefaults_Web),
    };

    private static string? DetectError(object? raw, string? text)
    {
        if (text is null)
            return null;

        if (text.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
            return text;

        var trimmed = text.TrimStart();
        if (!trimmed.StartsWith('{'))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("error") && prop.Value.ValueKind != JsonValueKind.Null)
                    return prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.GetRawText();
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
