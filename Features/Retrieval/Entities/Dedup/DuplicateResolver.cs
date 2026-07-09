using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup;

/// <summary>Picks the single winner of a duplicate group by authority-first precedence.</summary>
public static class DuplicateResolver
{
    private static readonly HashSet<string> AuthoritativeSources =
        new(StringComparer.OrdinalIgnoreCase) { "5etools-backfill", "hand-authored" };

    public static EntityEnvelope Winner(
        IReadOnlyList<EntityEnvelope> group,
        IReadOnlyDictionary<string, BookType> bookTypeBySourceBook)
    {
        if (group.Count == 0) throw new ArgumentException("Duplicate group is empty", nameof(group));

        return group
            .OrderByDescending(e => BookAuthority(bookTypeBySourceBook.GetValueOrDefault(e.SourceBook, BookType.Unknown)))
            .ThenByDescending(e => AuthoritativeSources.Contains(e.DataSource) ? 1 : 0)
            .ThenByDescending(e => e.NeedsReview ? 0 : 1)
            .ThenByDescending(e => e.CanonicalText.Length)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .First();
    }

    // Higher = more authoritative. Core outranks all supplements/adventures/settings.
    private static int BookAuthority(BookType t) => t switch
    {
        BookType.Core => 4,
        BookType.Supplement => 3,
        BookType.Adventure => 2,
        BookType.Setting => 1,
        _ => 0, // Unknown
    };
}