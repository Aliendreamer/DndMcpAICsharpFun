using System.Text.Json;
using System.Text.Json.Serialization;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Single source-of-truth for canonical JSON (de)serialization settings.
/// All writers of canonical *.json files share these options so that
/// enum values are consistently written as strings and formatting is uniform.
/// </summary>
public static class CanonicalJson
{
    public static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Atomically writes a <see cref="CanonicalJsonFile"/> to <paramref name="path"/>,
    /// appending a trailing newline for clean diffs.
    /// </summary>
    public static async Task WriteAsync(string path, CanonicalJsonFile file, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path) ?? ".";
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, file, WriteOptions, ct);
                await stream.WriteAsync("\n"u8.ToArray(), ct);
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
