using System.Globalization;
using System.Text;

namespace DndMcpAICsharpFun.Domain.Entities;

public static class EntityIdSlug
{
    private static readonly Dictionary<string, string> BookOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Player's Handbook 2014"] = "phb14",
        ["Player's Handbook 2024"] = "phb24",
        ["Monster Manual 2014"]    = "mm14",
        ["Monster Manual 2024"]    = "mm24",
        ["Dungeon Master's Guide 2014"] = "dmg14",
        ["Dungeon Master's Guide 2024"] = "dmg24",
        ["Tasha's Cauldron of Everything"] = "tasha",
        ["Xanathar's Guide to Everything"] = "xanathar",
        ["Volo's Guide to Monsters"] = "volo",
        ["Mordenkainen Presents: Monsters of the Multiverse"] = "motm",
        ["Eberron: Rising from the Last War"] = "eberron",
    };

    public static string For(string book, EntityType type, string name)
    {
        var bookSlug = BookOverrides.TryGetValue(book, out var s) ? s : SlugifyBook(book);
        var typeSlug = type.ToString().ToLowerInvariant();
        var nameSlug = SlugifyName(name);
        return $"{bookSlug}.{typeSlug}.{nameSlug}";
    }

    private static string SlugifyBook(string book) => SlugifyName(book);

    private static string SlugifyName(string text)
    {
        var folded = FoldToAscii(text);
        var sb = new StringBuilder(folded.Length);
        var lastWasHyphen = true;
        foreach (var ch in folded.ToLowerInvariant())
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
            {
                sb.Append(ch);
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen)
            {
                sb.Append('-');
                lastWasHyphen = true;
            }
        }
        return sb.ToString().Trim('-');
    }

    private static string FoldToAscii(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
