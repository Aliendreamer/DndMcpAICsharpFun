using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class CanonicalJsonWriter
{
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public async ValueTask WriteAsync(string path, CanonicalJsonFile file, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path) ?? ".";
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, file, WriteOptions, ct);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* swallow cleanup */ }
            throw;
        }
    }
}
