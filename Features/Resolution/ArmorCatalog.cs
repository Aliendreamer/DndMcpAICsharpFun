namespace DndMcpAICsharpFun.Features.Resolution;

public enum ArmorCategory { Light, Medium, Heavy }

/// <summary>
/// Static PHB armor data: name → (base AC, category). Single source of truth for armor base AC — a
/// character stores only the armor NAME (see <see cref="Domain.WornArmor"/>); the resolver looks up
/// base AC + category here. Same static-data pattern as SavingThrowProficiencies.
/// </summary>
public static class ArmorCatalog
{
    private static readonly Dictionary<string, (int BaseAc, ArmorCategory Category)> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Padded"] = (11, ArmorCategory.Light),
        ["Leather"] = (11, ArmorCategory.Light),
        ["Studded Leather"] = (12, ArmorCategory.Light),
        ["Hide"] = (12, ArmorCategory.Medium),
        ["Chain Shirt"] = (13, ArmorCategory.Medium),
        ["Scale Mail"] = (14, ArmorCategory.Medium),
        ["Breastplate"] = (14, ArmorCategory.Medium),
        ["Half Plate"] = (15, ArmorCategory.Medium),
        ["Ring Mail"] = (14, ArmorCategory.Heavy),
        ["Chain Mail"] = (16, ArmorCategory.Heavy),
        ["Splint"] = (17, ArmorCategory.Heavy),
        ["Plate"] = (18, ArmorCategory.Heavy),
    };

    // Explicit dropdown order (light → medium → heavy), independent of dictionary iteration order.
    public static IReadOnlyList<string> Names { get; } =
    [
        "Padded", "Leather", "Studded Leather",
        "Hide", "Chain Shirt", "Scale Mail", "Breastplate", "Half Plate",
        "Ring Mail", "Chain Mail", "Splint", "Plate",
    ];

    public static (int BaseAc, ArmorCategory Category)? Lookup(string name) =>
        Map.TryGetValue(name, out var v) ? v : null;
}