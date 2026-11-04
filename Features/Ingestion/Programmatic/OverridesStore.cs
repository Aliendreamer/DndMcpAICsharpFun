using System.Text.Json;
using System.Text.Json.Serialization;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.Programmatic;

/// <summary>Reads/writes <c>books/canonical/&lt;slug&gt;.overrides.json</c>.</summary>
public sealed class OverridesStore(IOptions<EntityIngestionOptions> options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private string PathFor(string bookSlug) =>
        Path.Combine(options.Value.CanonicalDirectory, bookSlug + ".overrides.json");

    public async Task<EntityOverrides> LoadAsync(string bookSlug, CancellationToken ct)
    {
        var path = PathFor(bookSlug);
        if (!File.Exists(path)) return EntityOverrides.Empty;
        await using var stream = File.OpenRead(path);
        var loaded = await JsonSerializer.DeserializeAsync<EntityOverrides>(stream, Json, ct);
        return loaded is null
            ? EntityOverrides.Empty
            : new EntityOverrides(loaded.Overrides ?? [], loaded.Approvals ?? []);
    }

    public async Task SaveAsync(string bookSlug, EntityOverrides overrides, CancellationToken ct)
    {
        var path = PathFor(bookSlug);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, overrides, Json, ct);
        }
        File.Move(tmp, path, overwrite: true);
    }
}
