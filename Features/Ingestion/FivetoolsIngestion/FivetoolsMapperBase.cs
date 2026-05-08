using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

public abstract class FivetoolsMapperBase : IFivetoolsEntityMapper
{
    // Sources released under the 2024 D&D ruleset revision.
    private static readonly HashSet<string> Edition2024Sources = new(StringComparer.OrdinalIgnoreCase)
        { "PHB24", "DMG24", "MM25", "XPHB", "XDMG" };

    protected abstract EntityType EntityType { get; }

    public virtual EntityEnvelope? Map(JsonElement entry)
    {
        if (!entry.TryGetProperty("name", out var nameProp)
            || nameProp.ValueKind != JsonValueKind.String)
            return null;
        var name = nameProp.GetString()!;
        if (string.IsNullOrWhiteSpace(name)) return null;

        var source = entry.TryGetProperty("source", out var src)
            && src.ValueKind == JsonValueKind.String ? src.GetString()! : "Unknown";
        int? page = entry.TryGetProperty("page", out var pg) && pg.TryGetInt32(out var pv) ? pv : null;
        var edition = Edition2024Sources.Contains(source) ? "Edition2024" : "Edition2014";

        var id = EntityIdSlug.For(source, EntityType, name);

        return new EntityEnvelope(
            Id: id,
            Type: EntityType,
            Name: name,
            SourceBook: source,
            Edition: edition,
            Page: page,
            FirstAppearedIn: new FirstAppearance(source, edition, page),
            RevisedIn: Array.Empty<Revision>(),
            SettingTags: Array.Empty<string>(),
            CanonicalText: "",
            Fields: BuildFields(entry),
            DataSource: "5etools");
    }

    protected virtual JsonElement BuildFields(JsonElement entry) => entry.Clone();
}
