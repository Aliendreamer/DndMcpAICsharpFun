using System.Text.RegularExpressions;
using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Chunking;

public sealed partial class ChapterContextTracker
{
    private static readonly (Regex Pattern, ContentCategory Category)[] ChapterMappings =
    [
        (ChapterSpell(),      ContentCategory.Spell),
        (ChapterMonster(),    ContentCategory.Monster),
        (ChapterClass(),      ContentCategory.Class),
        (ChapterBackground(), ContentCategory.Background),
        (ChapterItem(),       ContentCategory.Item),
        (ChapterCondition(),  ContentCategory.Rule),
    ];

    public ContentCategory CurrentCategory { get; private set; } = ContentCategory.Rule;
    public string CurrentChapter { get; private set; } = string.Empty;

    public void Reset()
    {
        CurrentCategory = ContentCategory.Rule;
        CurrentChapter = string.Empty;
    }

    public void ProcessLine(string line)
    {
        if (!line.StartsWith("Chapter", StringComparison.OrdinalIgnoreCase) &&
            !line.StartsWith("Appendix", StringComparison.OrdinalIgnoreCase))
            return;

        // Ignore TOC lines: real chapter headings are short; TOC entries have
        // trailing dots and page numbers (e.g. "Chapter 3: Classes ........ 45")
        if (line.Length > 80 || ContainsPageReference().IsMatch(line))
            return;

        foreach (var (pattern, category) in ChapterMappings)
        {
            if (pattern.IsMatch(line))
            {
                CurrentCategory = category;
                CurrentChapter = line.Trim();
                return;
            }
        }
    }

    [GeneratedRegex(@"spell|magic|cantrip", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterSpell();

    [GeneratedRegex(@"monster|bestiary|creature", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterMonster();

    [GeneratedRegex(@"\bclass(es)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterClass();

    [GeneratedRegex(@"background", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterBackground();

    [GeneratedRegex(@"equipment|item|weapon|armor|gear", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterItem();

    [GeneratedRegex(@"condition|appendix", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterCondition();

    [GeneratedRegex(@"\.{3,}|\d+\s*$")]
    private static partial Regex ContainsPageReference();
}
