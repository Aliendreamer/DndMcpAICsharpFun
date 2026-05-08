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
    string DataSource = "");
