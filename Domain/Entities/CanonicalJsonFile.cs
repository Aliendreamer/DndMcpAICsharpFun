namespace DndMcpAICsharpFun.Domain.Entities;

public sealed record CanonicalBookMetadata(
    string SourceBook,
    string Edition,
    string FileHash,
    string DisplayName);

public sealed record CanonicalJsonFile(
    string SchemaVersion,
    CanonicalBookMetadata Book,
    IReadOnlyList<EntityEnvelope> Entities);

public static class CanonicalJsonSchema
{
    public const string CurrentVersion = "1";
}
