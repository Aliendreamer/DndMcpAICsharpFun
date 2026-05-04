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

    public async Task WriteAsync(string path, IList<ExtractionWarningEntry> warnings, CancellationToken ct)
    {
        if (warnings.Count == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        var dir = Path.GetDirectoryName(path) ?? ".";
        Directory.CreateDirectory(dir);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, warnings, JsonOptions, ct);
    }
}
