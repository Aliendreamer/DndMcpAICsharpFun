using System.Text.Json;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Shared write-or-delete-if-empty logic for the extraction sidecar files
/// (errors, warnings, declined). Serializes <paramref name="items"/> to
/// <paramref name="path"/>, or deletes the file when the list is empty.
/// </summary>
internal static class SidecarJsonFileWriter
{
    public static async Task WriteAsync<T>(
        string path,
        IReadOnlyList<T> items,
        JsonSerializerOptions options,
        CancellationToken ct)
    {
        if (items.Count == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        var dir = Path.GetDirectoryName(path) ?? ".";
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, items, options, ct);
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