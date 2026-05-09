using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.VectorStore.Entities;

public sealed record EntityFilters(
    EntityType? Type = null,
    string? SourceBook = null,
    string? Edition = null,
    string? BookType = null,
    string? SettingTag = null,
    string? Keyword = null,
    double? CrNumericLte = null,
    double? CrNumericGte = null,
    int? SpellLevel = null,
    string? DamageType = null,
    bool? Srd = null,
    bool? Srd52 = null,
    bool? BasicRules2024 = null);

public interface IEntityVectorStore
{
    Task UpsertAsync(IList<EntityPoint> points, CancellationToken ct = default);
    Task DeleteByFileHashAsync(string fileHash, CancellationToken ct = default);
    Task<EntityEnvelope?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<IList<EntitySearchHit>> SearchAsync(float[] queryVector, EntityFilters filters, int topK, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetDataSourcesAsync(
        IReadOnlyList<string> entityIds, CancellationToken ct = default);
}
