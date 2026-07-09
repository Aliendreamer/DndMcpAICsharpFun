using System.Text.Json;
using System.Text.Json.Serialization;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Reads and writes crash-resume checkpoint files for the entity extraction pipeline.
/// Uses compact (non-indented) JSON so checkpoints are fast to write and read.
/// </summary>
public sealed class ExtractionCheckpointStore
{
    /// <summary>
    /// Serialization options used for checkpoint files — compact (non-indented),
    /// enum values as strings. Intentionally different from the indented canonical-write options.
    /// </summary>
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<(List<EntityEnvelope> Extracted, List<ExtractionErrorEntry> Errors, HashSet<string> DoneIds)>
        LoadCheckpointAsync(string progressPath, string errorsPath)
    {
        var extracted = new List<EntityEnvelope>();
        var errors = new List<ExtractionErrorEntry>();

        try
        {
            await using var s = File.OpenRead(progressPath);
            extracted = await JsonSerializer.DeserializeAsync<List<EntityEnvelope>>(s, Options) ?? [];
        }
        catch (FileNotFoundException) { }

        try
        {
            await using var s = File.OpenRead(errorsPath);
            errors = await JsonSerializer.DeserializeAsync<List<ExtractionErrorEntry>>(s, Options) ?? [];
        }
        catch (FileNotFoundException) { }

        var doneIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in extracted) doneIds.Add(e.Id);
        foreach (var e in errors) doneIds.Add(e.SourceEntityId);

        return (extracted, errors, doneIds);
    }

    public async Task WriteCheckpointAsync(
        string progressPath, string errorsPath,
        List<EntityEnvelope> extracted, List<ExtractionErrorEntry> errors)
    {
        var dir = Path.GetDirectoryName(progressPath) ?? ".";
        Directory.CreateDirectory(dir);

        var tmp1 = progressPath + ".tmp";
        await using (var s = File.Create(tmp1))
            await JsonSerializer.SerializeAsync(s, extracted, Options);
        try
        {
            File.Move(tmp1, progressPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp1); } catch { /* best effort */ }
            throw;
        }

        var tmp2 = errorsPath + ".tmp";
        await using (var s = File.Create(tmp2))
            await JsonSerializer.SerializeAsync(s, errors, Options);
        try
        {
            File.Move(tmp2, errorsPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp2); } catch { /* best effort */ }
            throw;
        }
    }
}