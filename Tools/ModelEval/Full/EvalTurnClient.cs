using System.Net.Http.Json;
using System.Text.Json;

namespace DndMcpAICsharpFun.Tools.ModelEval.Full;

internal sealed record ToolCallDto(string Name, string ArgumentsJson, string? Result, string? Error);

internal sealed record TurnDto(string Model, string Text, List<string> OfferedTools, List<ToolCallDto> ToolCalls, long LatencyMs);

internal interface IEvalTurnClient
{
    Task<TurnDto> RunAsync(string prompt, CancellationToken ct);
}

/// <summary>HTTP client for the host's POST /admin/eval/turn endpoint.</summary>
internal sealed class EvalTurnClient : IEvalTurnClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly string _adminKey;

    public EvalTurnClient(HttpClient http, string adminKey)
    {
        _http = http;
        _adminKey = adminKey;
        _http.Timeout = TimeSpan.FromSeconds(180);
    }

    public async Task<TurnDto> RunAsync(string prompt, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/admin/eval/turn")
        {
            Content = JsonContent.Create(new { prompt, userId = (long?)null, allowWebSearch = false }, options: Json),
        };
        req.Headers.Add("X-Admin-Api-Key", _adminKey);

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}: {Truncate(body)}", null, resp.StatusCode);
        }

        var dto = await resp.Content.ReadFromJsonAsync<TurnDto>(Json, ct);
        return dto ?? throw new JsonException("Empty eval turn response body.");
    }

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300];
}
