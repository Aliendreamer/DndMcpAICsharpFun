using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.Programmatic;

public sealed record ExtractorJobStatus(string Status, int PagesDone, int PagesTotal, string? Error);

public sealed record ExtractedEntity(
    string Id,
    EntityType Type,
    string Name,
    int? Page,
    string CanonicalText,
    JsonElement Fields,
    IReadOnlyList<EntityCheck> Checks = null!,
    EntityProvenance Provenance = null!)
{
    // The extractor may omit these; normalise so downstream code (OverrideMerger) never sees null.
    public IReadOnlyList<EntityCheck> Checks { get; init; } = Checks ?? [];
    public EntityProvenance Provenance { get; init; } = Provenance ?? new EntityProvenance([]);
}

public sealed record ExtractorResult(IReadOnlyList<ExtractedEntity> Entities, JsonElement Report);

public interface IBookExtractorClient
{
    Task<string> SubmitAsync(string pdfPath, string bookSlug, CancellationToken ct);
    Task<ExtractorJobStatus> GetStatusAsync(string jobId, CancellationToken ct);
    Task<ExtractorResult> GetResultAsync(string jobId, CancellationToken ct);
    Task<byte[]> GetPageImageAsync(string pdfPath, int page, CancellationToken ct);
}

/// <summary>HTTP client for the Python book-extractor service (<c>Tools/BookExtractor</c>).</summary>
public sealed class BookExtractorClient(HttpClient http) : IBookExtractorClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record SubmitResponse(string JobId);

    public async Task<string> SubmitAsync(string pdfPath, string bookSlug, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("/jobs", new { pdfPath, bookSlug }, Json, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SubmitResponse>(Json, ct);
        return body?.JobId ?? throw new InvalidOperationException("Extractor returned no jobId.");
    }

    public async Task<ExtractorJobStatus> GetStatusAsync(string jobId, CancellationToken ct) =>
        await GetJsonAsync<ExtractorJobStatus>($"/jobs/{Uri.EscapeDataString(jobId)}", ct);

    public async Task<ExtractorResult> GetResultAsync(string jobId, CancellationToken ct) =>
        await GetJsonAsync<ExtractorResult>($"/jobs/{Uri.EscapeDataString(jobId)}/result", ct);

    public async Task<byte[]> GetPageImageAsync(string pdfPath, int page, CancellationToken ct)
    {
        using var response = await http.GetAsync(
            $"/page-image?pdfPath={Uri.EscapeDataString(pdfPath)}&page={page}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task<T> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
            ?? throw new InvalidOperationException($"Extractor returned an empty body for {url}.");
    }
}
