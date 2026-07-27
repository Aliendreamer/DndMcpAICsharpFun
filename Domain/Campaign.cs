namespace DndMcpAICsharpFun.Domain;

/// <summary>
/// A table's campaign. Two properties govern retrieval scope and are deliberately SEPARATE concerns:
/// <see cref="Edition"/> is the RULES CANON (which grappling rule?), while <see cref="AllowedBookKeys"/>
/// is AVAILABILITY (is Ravnica in play?). Collapsing them breaks the common case — a 2024 table playing
/// in a 2014-era setting needs a 2024 edition and 2014-canon books at once, and thirteen of the sixteen
/// books in the corpus will never get a 2024 equivalent.
/// </summary>
public sealed record Campaign(
    long Id,
    long UserId,
    string Name,
    string Description,
    DateTime CreatedAt,
    string? Setting = null,
    DndVersion Edition = DndVersion.Edition2014,
    IReadOnlyList<string>? AllowedBookKeys = null)
{
    /// <summary>
    /// The source books available to this campaign, as <c>BookCatalog</c> KEYS — never display names,
    /// which are the fragile identifier this codebase has already been burned by (a display-name
    /// mismatch silently matched zero blocks). Keys are validated against the catalog on write, so an
    /// unknown entry fails loud instead of matching nothing. Empty means no books are in scope.
    /// </summary>
    public IReadOnlyList<string> AllowedBookKeys { get; init; } = AllowedBookKeys ?? [];
}

public sealed record CampaignSummary(long Id, long UserId, string Name, string Description, DateTime CreatedAt, int HeroCount);