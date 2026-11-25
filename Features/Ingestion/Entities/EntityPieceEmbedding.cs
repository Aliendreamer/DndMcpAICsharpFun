using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;

namespace DndMcpAICsharpFun.Features.Ingestion.Entities;

/// <summary>Embeds entities piece by piece, so the whole text of each is searchable.</summary>
public static class EntityPieceEmbedding
{
    /// <summary>One list of vectors per entity, in order.</summary>
    public static async Task<IReadOnlyList<IReadOnlyList<float[]>>> EmbedAsync(
        IEmbeddingService embeddings, IReadOnlyList<IReadOnlyList<string>> pieces, CancellationToken ct)
    {
        var all = pieces.SelectMany(p => p).ToList();
        var vectors = all.Count == 0 ? [] : await embeddings.EmbedAsync(all, ct);
        if (vectors.Count != all.Count)
            throw new InvalidOperationException($"EmbedAsync returned {vectors.Count} vectors for {all.Count} texts.");

        var result = new List<IReadOnlyList<float[]>>(pieces.Count);
        var at = 0;
        foreach (var p in pieces)
        {
            result.Add([.. vectors.Skip(at).Take(p.Count)]);
            at += p.Count;
        }
        return result;
    }

    /// <summary>
    /// The book's accepted Lore by normalised name: a monster's description is printed outside its stat block and is
    /// embedded with it ("a demon prince with two heads").
    /// </summary>
    public static ILookup<string, EntityEnvelope> LoreByName(IEnumerable<EntityEnvelope> entities) =>
        entities.Where(e => e.Type == EntityType.Lore && e.Disposition == EntityDisposition.Accepted)
            .ToLookup(e => Key(e.Name), StringComparer.Ordinal);

    public static IEnumerable<EntityEnvelope> LoreOf(EntityEnvelope e, ILookup<string, EntityEnvelope> loreByName) =>
        e.Type == EntityType.Monster ? loreByName[Key(e.Name)] : [];

    private static string Key(string name) => string.Concat(name.Where(char.IsLetterOrDigit)).ToLowerInvariant();
}