using System.Net.Http.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Search;

public sealed class SearXNGClient(
    HttpClient httpClient,
    IOptions<SearXNGOptions> options,
    ILogger<SearXNGClient> logger)
{
    private readonly SearXNGOptions _opts = options.Value;

    public async Task<IReadOnlyList<SearXNGResult>> SearchAsync(string query, CancellationToken ct)
    {
        try
        {
            var encoded = Uri.EscapeDataString(query);
            var url = $"/search?q={encoded}&format=json&language=en";
            var response = await httpClient.GetFromJsonAsync<SearXNGResponse>(url, ct);
            if (response?.Results is null) return [];

            return response.Results
                .Where(r => _opts.AllowedDomains.Length == 0 ||
                            (Uri.TryCreate(r.Url, UriKind.Absolute, out var uri) &&
                             _opts.AllowedDomains.Any(d =>
                                 uri.Host.Equals(d, StringComparison.OrdinalIgnoreCase) ||
                                 uri.Host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase))))
                .Take(_opts.MaxResults)
                .Select(r => new SearXNGResult(r.Title ?? "", r.Url ?? "", r.Content ?? ""))
                .ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Log the full exception (transport/deserialization failure) so a genuine error is
            // distinguishable from a legitimate empty result set (NET-11).
            logger.LogWarning(ex, "SearXNG search failed; returning no results for this query.");
            return [];
        }
    }

    private sealed record SearXNGResponse(
        [property: JsonPropertyName("results")] List<SearXNGRaw>? Results);

    private sealed record SearXNGRaw(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("content")] string? Content);
}