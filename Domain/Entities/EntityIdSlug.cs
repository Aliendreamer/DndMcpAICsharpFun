using System.Globalization;
using System.Text;

namespace DndMcpAICsharpFun.Domain.Entities;

public static class EntityIdSlug
{
    private static readonly Dictionary<string, string> BookOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        // Display name → slug
        ["Player's Handbook 2014"]                            = "phb14",
        ["Player's Handbook 2024"]                            = "phb24",
        ["Monster Manual 2014"]                               = "mm14",
        ["Monster Manual 2024"]                               = "mm24",
        ["Dungeon Master's Guide 2014"]                       = "dmg14",
        ["Dungeon Master's Guide 2024"]                       = "dmg24",
        ["Dungeon Master's Guide"]                            = "dmg14",
        ["Tasha's Cauldron of Everything"]                    = "tce",
        ["Xanathar's Guide to Everything"]                    = "xgte",
        ["Volo's Guide to Monsters"]                          = "vgm",
        ["Mordenkainen Presents: Monsters of the Multiverse"] = "mpmm",
        ["Eberron: Rising from the Last War"]                 = "erlw",
        // Source key → slug (aligns 5etools pipeline with canonical pipeline)
        ["PHB"]  = "phb14",
        ["XPHB"] = "phb24",
        ["DMG"]  = "dmg14",
        ["XDMG"] = "dmg24",
        ["MM"]   = "mm14",
        ["MM25"] = "mm24",
        ["TCE"]  = "tce",
        ["XGTE"] = "xgte",
        ["MPMM"] = "mpmm",
        ["VGM"]  = "vgm",
        ["ERLW"] = "erlw",
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
