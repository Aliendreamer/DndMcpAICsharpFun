using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

/// <summary>
/// Spike MinerU-based <see cref="IPdfStructureConverter"/>. Reads a MinerU pipeline
/// <c>&lt;stem&gt;_content_list.json</c> (produced offline by the <c>mineru</c> CLI) from
/// <see cref="MinerUOptions.OutputDirectory"/> and maps its typed blocks onto
/// <see cref="PdfStructureDocument"/>:
/// <list type="bullet">
///   <item>a block carrying a <c>text_level</c> becomes a <c>section_header</c> item (heading candidate);</item>
///   <item>a plain <c>text</c> block becomes a <c>text</c> item;</item>
///   <item>headers / footers / page numbers / images / tables / equations are dropped.</item>
/// </list>
/// MinerU page indices are 0-based; they are shifted to 1-based to align with the bookmark TOC.
/// </summary>
public sealed class MinerUPdfConverter(
    IOptions<MinerUOptions> options,
    ILogger<MinerUPdfConverter> logger) : IPdfStructureConverter
{
    public async Task<PdfStructureDocument> ConvertAsync(string filePath, CancellationToken ct = default)
    {
        var stem = Path.GetFileNameWithoutExtension(filePath);
        var root = options.Value.OutputDirectory;
        var bookDir = Path.Combine(root, stem);

        var contentListPath = Directory.Exists(bookDir)
            ? Directory.EnumerateFiles(bookDir, $"{stem}_content_list.json", SearchOption.AllDirectories)
                .FirstOrDefault()
            : null;

        if (contentListPath is null)
            throw new FileNotFoundException(
                $"MinerU content_list not found for '{stem}' under '{root}'. " +
                "Run the mineru CLI for this PDF first.");

        await using var fs = File.OpenRead(contentListPath);
        var blocks = await JsonSerializer.DeserializeAsync<List<MinerUBlock>>(fs, cancellationToken: ct) ?? [];

        var items = new List<PdfStructureItem>(blocks.Count);
        foreach (var b in blocks)
        {
            var text = b.Text?.Trim();
            if (string.IsNullOrEmpty(text)) continue;

            var page = b.PageIdx + 1; // MinerU page_idx is 0-based

            if (b.TextLevel is > 0)
                items.Add(new PdfStructureItem("section_header", text, page, b.TextLevel));
            else if (string.Equals(b.Type, "text", StringComparison.OrdinalIgnoreCase))
                items.Add(new PdfStructureItem("text", text, page, null));
            // image / table / header / footer / page_number / equation are intentionally dropped
        }

        var mdPath = Path.Combine(Path.GetDirectoryName(contentListPath)!, $"{stem}.md");
        var markdown = File.Exists(mdPath) ? await File.ReadAllTextAsync(mdPath, ct) : string.Empty;

        logger.LogInformation(
            "MinerU converted {Stem}: {Items} items ({Headings} headings) from {Path}",
            stem, items.Count, items.Count(i => i.Type == "section_header"), contentListPath);

        return new PdfStructureDocument(markdown, items);
    }

    private sealed record MinerUBlock(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("text_level")] int? TextLevel,
        [property: JsonPropertyName("page_idx")] int PageIdx);
}
