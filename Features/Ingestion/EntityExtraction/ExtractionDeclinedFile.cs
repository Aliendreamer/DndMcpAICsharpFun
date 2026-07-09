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

    public Task WriteAsync(string path, IReadOnlyList<DeclinedEntry> declined, CancellationToken ct)
        => SidecarJsonFileWriter.WriteAsync(path, declined, JsonOptions, ct);
}