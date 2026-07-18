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
        var tables = new List<CanonicalTable>();
        var index = 0;
        foreach (var item in doc.Items)
        {
            if (!string.Equals(item.Type, "table", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(item.Html))
                continue;

            var caption = string.IsNullOrWhiteSpace(item.Text) ? $"Table {index + 1}" : item.Text;
            var id = $"{bookSlug}.table.{Slug(caption, index)}";
            var provenance = new ProvenanceRef($"{bookSlug}.block.table.{index}", sourceBook, item.PageNumber);

            var table = HtmlTableParser.Parse(item.Html, id, caption, provenance);
            if (table is null) continue; // malformed table HTML — skip, don't fail the run
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
