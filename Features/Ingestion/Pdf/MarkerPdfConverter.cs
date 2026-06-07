using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

/// <summary>
/// SPIKE — Marker-based implementation of <see cref="IPdfStructureConverter"/> for the
/// marker-vs-docling conversion comparison (openspec/changes/marker-converter-spike).
/// Not registered in DI; constructed directly by the comparison harness.
/// Maps the Marker JSON block tree onto <see cref="PdfStructureDocument"/> items.
/// </summary>
public sealed partial class MarkerPdfConverter(string baseUrl, string containerFilePath) : IPdfStructureConverter
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    [GeneratedRegex(@"^/page/(\d+)")]
    private static partial Regex PageIdPattern();

    [GeneratedRegex(@"<h(\d)")]
    private static partial Regex HeadingTagPattern();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagPattern();

    public async Task<PdfStructureDocument> ConvertAsync(string filePath, CancellationToken ct = default)
    {
        // filePath is the host path; the marker container sees the book under /books.
        var resp = await Http.PostAsJsonAsync(
            $"{baseUrl}/convert-by-path", new { file_path = containerFilePath }, ct);
        resp.EnsureSuccessStatusCode();
        var job = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        var jobId = job.GetProperty("job_id").GetString()!;

        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(15), ct);
            var status = await Http.GetFromJsonAsync<JsonElement>($"{baseUrl}/status/{jobId}", ct);
            var state = status.GetProperty("state").GetString();
            if (state == "done") break;
            if (state == "failed")
                throw new InvalidOperationException(
                    $"Marker conversion failed: {status.GetProperty("error").GetString()}");
        }

        var result = await Http.GetFromJsonAsync<JsonElement>($"{baseUrl}/result/{jobId}", ct);
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
                var text = StripHtml(html);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var levelMatch = HeadingTagPattern().Match(html);
                    int? level = levelMatch.Success ? int.Parse(levelMatch.Groups[1].Value) : null;
                    items.Add(new PdfStructureItem("section_header", text, page, level));
                }
                return;

            case "PageHeader" or "PageFooter" or "Picture" or "Figure":
                return; // noise — Docling output excludes running heads too

            case "Page" or "Document" or "Group" or "ListGroup" or "TableGroup" or "FigureGroup" or "PictureGroup":
                if (hasChildren)
                {
                    foreach (var child in children.EnumerateArray())
                        WalkBlock(child, page, items);
                }
                return;

            default:
                // Leaf, text-bearing block (Text, ListItem, Table, Caption, Footnote, ...).
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
