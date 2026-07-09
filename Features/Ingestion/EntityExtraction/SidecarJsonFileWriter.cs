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
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, items, options, ct);
    }
}