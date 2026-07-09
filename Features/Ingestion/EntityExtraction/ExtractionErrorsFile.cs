using System.Text.Json;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed record ExtractionErrorEntry(
    string SourceEntityId,
    string FieldPath,
    string MissingTargetId,
    string ErrorKind,   // "no_schema" | "extraction_failure" | "intra_book_dangling_ref" | "schema_validation_failure"
    string? Detail);

public sealed class ExtractionErrorsFile
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public Task WriteAsync(string path, IReadOnlyList<ExtractionErrorEntry> errors, CancellationToken ct)
        => SidecarJsonFileWriter.WriteAsync(path, errors, JsonOptions, ct);
}