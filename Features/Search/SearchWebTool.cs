using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace DndMcpAICsharpFun.Features.Search;

[McpServerToolType]
public sealed class SearchWebTool(SearXNGClient searxng)
{
    private static readonly JsonSerializerOptions _json =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [McpServerTool, Description(
        "Search the live web for D&D rules, lore, or community discussions not found in local books. " +
        "Only call this when the user explicitly asks to search the web.")]
    public async Task<string> search_web(
        [Description("Search query")] string query,
        CancellationToken ct = default)
    {
        var results = await searxng.SearchAsync(query, ct);
        if (results.Count == 0)
            return "No web results found.";

        return JsonSerializer.Serialize(results.Select(r => new
        {
            title = r.Title,
            url = r.Url,
            snippet = r.Snippet
        }), _json);
    }
}
