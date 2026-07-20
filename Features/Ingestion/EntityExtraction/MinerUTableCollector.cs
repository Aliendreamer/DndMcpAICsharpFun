using System.Text;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Collects the <c>table</c> structure items MinerU now preserves (mineru-table-extraction) and parses
/// each into a <see cref="CanonicalTable"/> via <see cref="HtmlTableParser"/> — deterministic, no LLM.
/// The results populate <c>CanonicalJsonFile.Tables</c>, which the existing writer serializes and the
/// existing StructuredFactProjector lands in Postgres.
/// </summary>
public static class MinerUTableCollector
{
    public static IReadOnlyList<CanonicalTable> Collect(
        PdfStructureDocument doc, string bookSlug, string sourceBook)
    {
        const int HeadingWindow = 10; // max items between a heading and the table it names
        var tables = new List<CanonicalTable>();
        var index = 0;
        string? lastHeading = null;
        var sinceHeading = int.MaxValue;

        foreach (var item in doc.Items)
        {
            if (string.Equals(item.Type, "section_header", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(item.Text)) { lastHeading = item.Text; sinceHeading = 0; }
                continue;
            }

            if (!string.Equals(item.Type, "table", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(item.Html))
            {
                if (sinceHeading != int.MaxValue) sinceHeading++;
                continue;
            }

            var caption = !string.IsNullOrWhiteSpace(item.Text) ? item.Text
                : (lastHeading is not null && sinceHeading <= HeadingWindow) ? lastHeading
                : $"Table {index + 1}";
            var id = $"{bookSlug}.table.{Slug(caption, index)}";
            var provenance = new ProvenanceRef($"{bookSlug}.block.table.{index}", sourceBook, item.PageNumber);

            if (sinceHeading != int.MaxValue) sinceHeading++;

            var table = HtmlTableParser.Parse(item.Html, id, caption, provenance);
            if (table is null) continue; // malformed table html — skip, don't fail the run
            tables.Add(table);
            index++;
        }
        return tables;
    }

    // Slug the caption; always append the positional index so duplicate/absent captions never collide.
    private static string Slug(string caption, int index)
    {
        var sb = new StringBuilder(caption.Length);
        foreach (var c in caption.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : '-');
        var s = sb.ToString().Trim('-');
        while (s.Contains("--", StringComparison.Ordinal))
            s = s.Replace("--", "-", StringComparison.Ordinal);
        return string.IsNullOrEmpty(s) ? $"t{index}" : $"{s}-{index}";
    }
}
