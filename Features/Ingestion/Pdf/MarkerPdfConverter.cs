using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DndMcpAICsharpFun.Infrastructure.Marker;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

/// <summary>
/// SPIKE — Marker-based implementation of <see cref="IPdfStructureConverter"/> for the
/// marker-vs-docling conversion comparison (openspec/changes/marker-converter-spike).
/// Not registered in DI; constructed directly by the comparison harness.
/// Maps the Marker JSON block tree onto <see cref="PdfStructureDocument"/> items.
/// </summary>
/// <summary>
/// Production Marker-based implementation of <see cref="IPdfStructureConverter"/>.
/// Submits the PDF to the Marker wrapper API, polls for completion, and maps
/// the resulting JSON block tree onto <see cref="PdfStructureDocument"/>.
/// </summary>
public sealed partial class MarkerPdfConverter(
    IHttpClientFactory httpClientFactory,
    IOptions<MarkerOptions> options,
    ILogger<MarkerPdfConverter> logger) : IPdfStructureConverter
{
    [GeneratedRegex(@"^/page/(\d+)")]
    private static partial Regex PageIdPattern();

    [GeneratedRegex(@"<h(\d)")]
    private static partial Regex HeadingTagPattern();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagPattern();

    /// <summary>
    /// Matches text that begins with a dice token (d4, D8, d20, etc.) —
    /// these are table-roll captions, not section headings.
    /// </summary>
    [GeneratedRegex(@"^[dD]\d+\b")]
    private static partial Regex DicePrefixPattern();

    public async Task<PdfStructureDocument> ConvertAsync(string filePath, CancellationToken ct = default)
    {
        var opts = options.Value;
        var fileName = Path.GetFileName(filePath);
        var containerPath = $"{opts.BooksMountPath.TrimEnd('/')}/{fileName}";

        var http = httpClientFactory.CreateClient(nameof(MarkerPdfConverter));

        logger.LogInformation(
            "Marker conversion started for {FileName} (container path: {ContainerPath})",
            fileName, containerPath);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        var resp = await http.PostAsJsonAsync(
            $"{opts.Url}/convert-by-path",
            new { file_path = containerPath },
            ct);
        resp.EnsureSuccessStatusCode();

        var job = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        var jobId = job.GetProperty("job_id").GetString()!;

        var timeout = TimeSpan.FromMinutes(opts.ConversionTimeoutMinutes);
        var pollInterval = TimeSpan.FromSeconds(opts.PollIntervalSeconds);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (sw.Elapsed >= timeout)
                throw new TimeoutException(
                    $"Marker conversion timed out after {sw.Elapsed.TotalMinutes:F1} minutes " +
                    $"for {fileName} (job {jobId}).");

            await Task.Delay(pollInterval, ct);

            var status = await http.GetFromJsonAsync<JsonElement>(
                $"{opts.Url}/status/{jobId}", ct);
            var state = status.GetProperty("state").GetString();

            if (state == "done") break;

            if (state == "failed")
                throw new InvalidOperationException(
                    $"Marker conversion failed for {fileName}: " +
                    $"{(status.TryGetProperty("error", out var err) ? err.GetString() : "unknown error")}");
        }

        var result = await http.GetFromJsonAsync<JsonElement>($"{opts.Url}/result/{jobId}", ct);

        logger.LogInformation(
            "Marker conversion completed for {FileName} in {Elapsed:F1}s",
            fileName, sw.Elapsed.TotalSeconds);

        return FromMarkerJson(result);
    }

    /// <summary>Maps a Marker JSON document (output_format=json) onto PdfStructureDocument.</summary>
    public static PdfStructureDocument FromMarkerJson(JsonElement result)
    {
        var items = new List<PdfStructureItem>();
        if (result.TryGetProperty("children", out var pages) && pages.ValueKind == JsonValueKind.Array)
        {
            foreach (var page in pages.EnumerateArray())
                WalkBlock(page, PageNumberOf(page), items);
        }

        var markdown = string.Join("\n\n", items.Select(i => i.Text));
        return new PdfStructureDocument(markdown, items);
    }

    private static int PageNumberOf(JsonElement block)
    {
        if (block.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
        {
            var m = PageIdPattern().Match(id.GetString()!);
            if (m.Success) return int.Parse(m.Groups[1].Value) + 1; // marker pages are 0-based
        }
        return 0;
    }

    private static void WalkBlock(JsonElement block, int page, List<PdfStructureItem> items)
    {
        var blockType = block.TryGetProperty("block_type", out var bt) ? bt.GetString() ?? "" : "";
        var html = block.TryGetProperty("html", out var h) && h.ValueKind == JsonValueKind.String
            ? h.GetString()! : "";
        var hasChildren = block.TryGetProperty("children", out var children)
            && children.ValueKind == JsonValueKind.Array
            && children.GetArrayLength() > 0;

        switch (blockType)
        {
            case "SectionHeader":
                var rawText = StripHtml(html);
                if (!string.IsNullOrWhiteSpace(rawText))
                {
                    // Dice-table captions (d4 Wild Magic, D8 Magical Effect, …) are NOT headings.
                    if (DicePrefixPattern().IsMatch(rawText))
                    {
                        items.Add(new PdfStructureItem("text", rawText, page, null));
                        return;
                    }

                    var levelMatch = HeadingTagPattern().Match(html);
                    int? level = levelMatch.Success ? int.Parse(levelMatch.Groups[1].Value) : null;

                    // Apply despacer to fix letter-spaced all-caps headings from PDF converters.
                    var normalizedText = HeadingDespacer.Normalize(rawText);
                    items.Add(new PdfStructureItem("section_header", normalizedText, page, level));
                }
                return;

            case "PageHeader" or "PageFooter" or "Picture" or "Figure":
                return; // noise — running heads, images

            case "Page" or "Document" or "Group" or "ListGroup" or "TableGroup" or "FigureGroup" or "PictureGroup":
                if (hasChildren)
                {
                    foreach (var child in children.EnumerateArray())
                        WalkBlock(child, page, items);
                }
                return;

            default:
                // Leaf, text-bearing block (Text, ListItem, Table, Caption, Footnote, …).
                // Tables keep cell text separated by spaces after tag stripping.
                var body = StripHtml(html);
                if (!string.IsNullOrWhiteSpace(body))
                    items.Add(new PdfStructureItem("text", body, page, null));
                else if (hasChildren)
                {
                    foreach (var child in children.EnumerateArray())
                        WalkBlock(child, page, items);
                }
                return;
        }
    }

    private static string StripHtml(string html)
    {
        var text = HtmlTagPattern().Replace(html, " ");
        return System.Net.WebUtility.HtmlDecode(text).Trim();
    }
}
