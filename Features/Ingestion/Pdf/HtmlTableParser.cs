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
        // filter-degenerate-tables D1: a real table needs >=2 columns and >=1 data row.
        // (dataRows is rows.Skip(1), so rows.Count < 2 means zero data rows — a header-only grid.)
        if (columns.Count < 2 || rows.Count < 2) return null;

        // filter-degenerate-tables D2: a monster stat-block ability line ("STR 22 (+6) …") MinerU
        // mis-tags as a table; drop it even though it technically has a data row.
        if (IsStatBlockFragment(rows)) return null;

        var dataRows = rows.Skip(1)
            .Select(r => new CanonicalTableRow(
                r.Select(cell => new CanonicalCell(cell, cellProvenance)).ToList()))
            .ToList();

        return new CanonicalTable(tableId, name, columns, dataRows);
    }

    // A small grid (<=2 rows) dominated by ability-score tokens like "STR 22" / "DEX 19 (+4)" is a
    // stat-block fragment, not a table. Narrow by design — the ability regex + >=3-match threshold +
    // <=2-row guard keep genuine multi-row reference tables (which never carry 3 such cells) safe.
    private static bool IsStatBlockFragment(List<List<string>> rows)
    {
        if (rows.Count > 2) return false;
        var abilityCells = rows.SelectMany(r => r).Count(cell => AbilityTokenRx().IsMatch(cell));
        return abilityCells >= 3;
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

    [GeneratedRegex(@"\b(STR|DEX|CON|INT|WIS|CHA)\b\s*\d")]
    private static partial Regex AbilityTokenRx();
}