using System.Net;
using System.Text.RegularExpressions;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

/// <summary>
/// Deterministic parser for a MinerU table's <c>table_body</c> HTML into a <see cref="CanonicalTable"/>
/// (mineru-table-extraction). MinerU has already done the vision/table-structure recognition, so this
/// is a straight HTML→grid parse — no LLM. The first row supplies the column names; the remaining rows
/// become cells. Whitespace and HTML entities are normalized (OCR-tolerant). Malformed or empty HTML
/// yields <c>null</c> rather than throwing.
/// </summary>
public static partial class HtmlTableParser
{
    /// <summary>Parse table HTML into a CanonicalTable; every cell carries the shared table provenance.</summary>
    public static CanonicalTable? Parse(string? html, string tableId, string name, ProvenanceRef cellProvenance)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        var rows = ExtractRows(html);
        if (rows.Count == 0) return null;

        var columns = rows[0];
        if (columns.Count == 0) return null;

        var dataRows = rows.Skip(1)
            .Select(r => new CanonicalTableRow(
                r.Select(cell => new CanonicalCell(cell, cellProvenance)).ToList()))
            .ToList();

        return new CanonicalTable(tableId, name, columns, dataRows);
    }

    private static List<List<string>> ExtractRows(string html)
    {
        var rows = new List<List<string>>();
        foreach (Match tr in RowRx().Matches(html))
        {
            var cells = new List<string>();
            foreach (Match cell in CellRx().Matches(tr.Groups[1].Value))
                cells.Add(Clean(cell.Groups[1].Value));
            if (cells.Count > 0) rows.Add(cells);
        }
        return rows;
    }

    private static string Clean(string cellHtml)
    {
        var noTags = TagRx().Replace(cellHtml, " ");
        var decoded = WebUtility.HtmlDecode(noTags);
        return WhitespaceRx().Replace(decoded, " ").Trim();
    }

    [GeneratedRegex(@"<tr\b[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex RowRx();

    [GeneratedRegex(@"<t[hd]\b[^>]*>(.*?)</t[hd]>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CellRx();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRx();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRx();
}
