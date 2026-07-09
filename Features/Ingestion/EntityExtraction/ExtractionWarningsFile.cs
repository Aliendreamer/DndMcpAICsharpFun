using System.Text.Json;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed record ExtractionWarningEntry(
    string SourceEntityId,
    string FieldPath,
    string MissingTargetId,
    string WarningKind);        // "inter_book_dangling_ref"

public sealed class ExtractionWarningsFile
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public Task WriteAsync(string path, IReadOnlyList<ExtractionWarningEntry> warnings, CancellationToken ct)
        => SidecarJsonFileWriter.WriteAsync(path, warnings, JsonOptions, ct);
}