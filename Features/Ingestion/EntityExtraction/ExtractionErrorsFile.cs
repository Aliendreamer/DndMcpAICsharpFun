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

    public async Task WriteAsync(string path, IList<ExtractionErrorEntry> errors, CancellationToken ct)
    {
        if (errors.Count == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        var dir = Path.GetDirectoryName(path) ?? ".";
        Directory.CreateDirectory(dir);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, errors, JsonOptions, ct);
    }
}
