using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>Projects captioned 5etools {type:table} blocks embedded in an entity into CanonicalTables.</summary>
public static class CaptionedTableProjector
{
    public static IReadOnlyList<CanonicalTable> Project(JsonElement entity, string bookKey, int? page)
    {
        var provenance = new ProvenanceRef($"{EntityIdSlug.BookSlug(bookKey)}.5etools", bookKey, page);
        var tables = new List<CanonicalTable>();
        Walk(entity, bookKey, provenance, tables);
        return tables;
    }

    private static void Walk(JsonElement node, string bookKey, ProvenanceRef prov, List<CanonicalTable> acc)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                if (node.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                    && t.GetString() == "table"
                    && node.TryGetProperty("caption", out var capEl) && capEl.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(capEl.GetString()))
                {
                    acc.Add(ToTable(node, capEl.GetString()!, bookKey, prov));
                }
                foreach (var p in node.EnumerateObject()) Walk(p.Value, bookKey, prov, acc);
                break;
            case JsonValueKind.Array:
                foreach (var e in node.EnumerateArray()) Walk(e, bookKey, prov, acc);
                break;
        }
    }

    private static CanonicalTable ToTable(JsonElement tbl, string caption, string bookKey, ProvenanceRef prov)
    {
        var columns = tbl.TryGetProperty("colLabels", out var cl)
            ? FivetoolsJson.StringList(cl).Select(FivetoolsJson.StripMarkup).ToList()
            : new List<string>();
        var rows = new List<CanonicalTableRow>();
        if (tbl.TryGetProperty("rows", out var rs) && rs.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in rs.EnumerateArray())
            {
                var cells = new List<CanonicalCell>();
                if (r.ValueKind == JsonValueKind.Array)
                    foreach (var c in r.EnumerateArray())
                        cells.Add(new CanonicalCell(
                            c.ValueKind == JsonValueKind.String ? FivetoolsJson.StripMarkup(c.GetString()!) : c.ToString(),
                            prov));
                rows.Add(new CanonicalTableRow(cells));
            }
        }
        return new CanonicalTable(EntityIdSlug.Table(bookKey, caption), caption, columns, rows);
    }
}
