using System.Text.Json;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

/// <summary>
/// Covers <see cref="MagicVariantExpander"/>: expanding 5etools <c>magicvariant</c> templates
/// (e.g. "+1 Weapon") against the <c>items-base.json</c> base-item pool into concrete synthetic
/// magic items (e.g. "+1 Longsword"), so recall/backfill see the templated <c>+N</c> items that
/// 5etools never materialises as concrete <c>items.json</c> records.
/// </summary>
public sealed class MagicVariantExpanderTests
{
    private static string CreateFivetoolsDir(string magicVariantsJson, string itemsBaseJson)
    {
        var root = Path.Combine(Path.GetTempPath(), "magicvariant-expander-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "magicvariants.json"), magicVariantsJson);
        File.WriteAllText(Path.Combine(root, "items-base.json"), itemsBaseJson);
        return root;
    }

    [Fact]
    public void Expand_WeaponRequirement_ProducesLongswordVariantWithSubstitutedPlaceholderAndSkipsNonWeapon()
    {
        var root = CreateFivetoolsDir(
            """
            { "magicvariant": [
              { "name": "+1 Weapon",
                "requires": [ { "weapon": true } ],
                "inherits": {
                  "namePrefix": "+1 ",
                  "source": "DMG",
                  "rarity": "uncommon",
                  "bonusWeapon": "+1",
                  "entries": [ "You have a {=bonusWeapon} bonus to attack and damage rolls made with this magic weapon." ]
                }
              }
            ] }
            """,
            """
            { "baseitem": [
              { "name": "Longsword", "source": "PHB", "weapon": true, "type": "M|PHB" },
              { "name": "Torch", "source": "PHB", "type": "GS|PHB" }
            ] }
            """);
        try
        {
            var roster = MagicVariantExpander.Expand(root).ToList();

            var longsword = Assert.Single(roster);
            Assert.Equal("+1 Longsword", longsword.GetProperty("name").GetString());
            Assert.Equal("DMG", longsword.GetProperty("source").GetString());
            Assert.Equal("uncommon", longsword.GetProperty("rarity").GetString());
            Assert.Contains("+1 bonus to attack", longsword.GetProperty("entries")[0].GetString());
            Assert.DoesNotContain(roster, e => e.GetProperty("name").GetString() == "+1 Torch");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Expand_ExcludesPredicate_SuppressesMatchingBaseItem()
    {
        var root = CreateFivetoolsDir(
            """
            { "magicvariant": [
              { "name": "+1 Weapon",
                "requires": [ { "weapon": true } ],
                "excludes": { "net": true },
                "inherits": {
                  "namePrefix": "+1 ",
                  "source": "DMG",
                  "rarity": "uncommon",
                  "entries": [ "Bonus to attack and damage rolls." ]
                }
              }
            ] }
            """,
            """
            { "baseitem": [
              { "name": "Longsword", "source": "PHB", "weapon": true, "type": "M|PHB" },
              { "name": "Net", "source": "PHB", "weapon": true, "net": true, "type": "NET|PHB" }
            ] }
            """);
        try
        {
            var roster = MagicVariantExpander.Expand(root).ToList();

            var only = Assert.Single(roster);
            Assert.Equal("+1 Longsword", only.GetProperty("name").GetString());
            Assert.DoesNotContain(roster, e => e.GetProperty("name").GetString() == "+1 Net");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Expand_TagsSyntheticItemWithVariantsOwnInheritsSource()
    {
        var root = CreateFivetoolsDir(
            """
            { "magicvariant": [
              { "name": "+1 Weapon",
                "requires": [ { "weapon": true } ],
                "inherits": { "namePrefix": "+1 ", "source": "DMG", "rarity": "uncommon",
                  "entries": [ "DMG variant text." ] }
              },
              { "name": "+1 Weapon (2024)",
                "requires": [ { "weapon": true } ],
                "inherits": { "namePrefix": "+1 ", "source": "XDMG", "rarity": "uncommon",
                  "entries": [ "XDMG variant text." ] }
              }
            ] }
            """,
            """
            { "baseitem": [
              { "name": "Longsword", "source": "PHB", "weapon": true, "type": "M|PHB" }
            ] }
            """);
        try
        {
            var roster = MagicVariantExpander.Expand(root).ToList();

            Assert.Equal(2, roster.Count);
            Assert.Contains(roster, e => e.GetProperty("source").GetString() == "DMG");
            Assert.Contains(roster, e => e.GetProperty("source").GetString() == "XDMG");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Expand_MissingFiles_ReturnsEmpty()
    {
        var root = Path.Combine(Path.GetTempPath(), "magicvariant-expander-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var roster = MagicVariantExpander.Expand(root).ToList();
            Assert.Empty(roster);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
