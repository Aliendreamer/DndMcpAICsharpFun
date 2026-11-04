namespace DndMcpAICsharpFun.Domain.Entities;

public sealed record CanonicalBookMetadata(
    string SourceBook,
    string Edition,
    string FileHash,
    string DisplayName);

public sealed record CanonicalJsonFile(
    string SchemaVersion,
    CanonicalBookMetadata Book,
    IReadOnlyList<EntityEnvelope> Entities,
    IReadOnlyList<CanonicalTable> Tables = null!,
    IReadOnlyList<CanonicalChoiceSet> ChoiceSets = null!)
{
    public IReadOnlyList<CanonicalTable> Tables { get; init; } = Tables ?? [];
    public IReadOnlyList<CanonicalChoiceSet> ChoiceSets { get; init; } = ChoiceSets ?? [];
}

public static class CanonicalJsonSchema
{
    public const string CurrentVersion = "2";

    /// <summary>Version written by the legacy qwen3 extraction pipeline; "2" means programmatic with checks/provenance.</summary>
    public const string LegacyVersion = "1";

    public static readonly IReadOnlyList<string> SupportedVersions = ["1", "2"];
}