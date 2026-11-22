using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

/// <summary>
/// The text an entity is embedded as: its canonical text, then its margin notes under a "Notes:" line, so a search
/// for a note's words finds the entity. The stored canonical text itself never holds the notes.
/// </summary>
public static class EntityEmbeddingText
{
    public static string For(string canonicalText, IReadOnlyList<EntityNote>? notes)
    {
        if (notes is not { Count: > 0 }) return canonicalText;
        var lines = notes.Select(n => string.IsNullOrWhiteSpace(n.By) ? n.Text : $"{n.Text} (—{n.By})");
        return $"{canonicalText}\nNotes: {string.Join(" | ", lines)}";
    }
}
