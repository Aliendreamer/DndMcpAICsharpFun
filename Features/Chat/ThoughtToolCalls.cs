using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Extensions.AI;

namespace DndMcpAICsharpFun.Features.Chat;

/// <summary>
/// qwen3 sometimes writes its tool call inside its thinking (<c>&lt;tool_call&gt;{"name":…,"arguments":{…}}&lt;/tool_call&gt;</c>),
/// where Ollama does not turn it into a call. This finds those calls so the chat can run them.
/// </summary>
internal static partial class ThoughtToolCalls
{
    private const int MaxCalls = 3;

    [GeneratedRegex(@"<tool_call>\s*(\{.*?\})\s*</tool_call>", RegexOptions.Singleline)]
    private static partial Regex CallPattern();

    public static IReadOnlyList<(string Name, Dictionary<string, object?> Arguments)> Find(ChatResponse response)
    {
        var calls = new List<(string Name, Dictionary<string, object?> Arguments)>();
        var thoughts = response.Messages.SelectMany(m => m.Contents).OfType<TextReasoningContent>().Select(c => c.Text);
        foreach (Match match in thoughts.SelectMany(t => CallPattern().Matches(t ?? string.Empty)))
        {
            if (Parse(match.Groups[1].Value) is { } call
                && !calls.Any(c => c.Name == call.Name && JsonSerializer.Serialize(c.Arguments) == JsonSerializer.Serialize(call.Arguments)))
                calls.Add(call);
            if (calls.Count == MaxCalls) break;
        }
        return calls;
    }

    private static (string Name, Dictionary<string, object?> Arguments)? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(name.GetString()))
                return null;
            var arguments = new Dictionary<string, object?>();
            if (root.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Object)
                foreach (var p in args.EnumerateObject())
                    arguments[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.Clone();
            return (name.GetString()!, arguments);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
