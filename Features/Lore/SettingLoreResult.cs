namespace DndMcpAICsharpFun.Features.Lore;

public sealed record CitedPassage(string Text, string SourceBook, string? Section, double Score);

public sealed record SettingLoreResult(
    string? Setting,
    IReadOnlyList<string> ScopedBooks,
    IReadOnlyList<CitedPassage> Passages);
