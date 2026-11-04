using System.Text.Json;

namespace DndMcpAICsharpFun.Domain.Entities;

public sealed record EntityEnvelope(
    string Id,
    EntityType Type,
    string Name,
    string SourceBook,
    string Edition,
    int? Page,
    FirstAppearance FirstAppearedIn,
    IReadOnlyList<Revision> RevisedIn,
    IReadOnlyList<string> SettingTags,
    string CanonicalText,
    JsonElement Fields,
    string DataSource = "",
    string Authority = "",
    bool Srd = false,
    bool Srd52 = false,
    bool BasicRules2024 = false,
    bool NeedsReview = false,
    IReadOnlyList<string> Keywords = null!,
    EntityDisposition Disposition = EntityDisposition.Accepted,
    IReadOnlyList<EntityCheck> Checks = null!,
    EntityProvenance? Provenance = null)
{
    public IReadOnlyList<string> Keywords { get; init; } = Keywords ?? [];
    public IReadOnlyList<EntityCheck> Checks { get; init; } = Checks ?? [];
}

public static class DataSources
{
    public const string Programmatic = "programmatic";
}