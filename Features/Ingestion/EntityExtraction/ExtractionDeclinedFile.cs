using System.Text.Json;
using System.Text.Json.Serialization;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed record DeclinedEntry(
    string Id,
    string Name,
    EntityType Type,
    string Reason);

public sealed class ExtractionDeclinedFile
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task WriteAsync(string path, IList<DeclinedEntry> declined, CancellationToken ct)
    {
        if (declined.Count == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        var dir = Path.GetDirectoryName(path) ?? ".";
        Directory.CreateDirectory(dir);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, declined, JsonOptions, ct);
    }
}
