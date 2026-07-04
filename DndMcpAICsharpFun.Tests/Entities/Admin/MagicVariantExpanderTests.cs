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
    public void Expand_ArrayValuedExcludes_SuppressesAnyMatchingElement()
    {
        var root = CreateFivetoolsDir(
            """
            { "magicvariant": [
              { "name": "+1 Imbued Wood",
                "requires": [ { "imbuable": true } ],
                "excludes": { "name": ["Crystal", "Orb"] },
                "inherits": {
                  "namePrefix": "+1 ",
                  "source": "ERLW",
                  "rarity": "uncommon",
                  "entries": [ "Bonus to attack and damage rolls." ]
                }
              },
              { "name": "+1 Armblade",
                "requires": [ { "armbladeable": true } ],
                "excludes": { "property": ["2H", "2H|XPHB"] },
                "inherits": {
                  "namePrefix": "+1 ",
                  "source": "ERLW",
                  "rarity": "uncommon",
                  "entries": [ "Bonus to attack and damage rolls." ]
                }
              }
            ] }
            """,
            """
            { "baseitem": [
              { "name": "Wand", "source": "PHB", "imbuable": true, "type": "M|PHB" },
              { "name": "Crystal", "source": "PHB", "imbuable": true, "type": "M|PHB" },
              { "name": "Orb", "source": "PHB", "imbuable": true, "type": "M|PHB" },
              { "name": "Greatsword", "source": "PHB", "armbladeable": true, "property": ["2H"], "type": "M|PHB" },
              { "name": "Shortsword", "source": "PHB", "armbladeable": true, "property": ["F"], "type": "M|PHB" }
            ] }
            """);
        try
        {
            var roster = MagicVariantExpander.Expand(root).ToList();
            var names = roster.Select(e => e.GetProperty("name").GetString()).ToList();

            // Array-valued excludes on a scalar actual field (name) suppresses matching elements
            // but lets everything else through.
            Assert.Contains("+1 Wand", names);
            Assert.DoesNotContain("+1 Crystal", names);
            Assert.DoesNotContain("+1 Orb", names);

            // Array-valued excludes on an array actual field (property) suppresses on non-empty
            // intersection but lets everything else through.
            Assert.DoesNotContain("+1 Greatsword", names);
            Assert.Contains("+1 Shortsword", names);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }


    [Fact]
    public void Expand_ScalarRequiresPredicateAgainstArrayBaseItemField_MatchesAnyArrayElement()
    {
        // Mirrors the real "Repeating Shot" / "Returning Weapon" shape: the variant's `requires`
        // predicate has a SCALAR "property" value ("A|XPHB"), while every real base item's
        // "property" field in items-base.json is an ARRAY (e.g. ["A|XPHB", "LD|XPHB"]). The
        // matcher must treat this as "the scalar equals any element of the array".
        var root = CreateFivetoolsDir(
            """
            { "magicvariant": [
              { "name": "+1 Repeating Weapon",
                "requires": [ { "weaponCategory": "simple", "property": "A|XPHB" } ],
                "inherits": {
                  "namePrefix": "+1 ",
                  "source": "DMG",
                  "rarity": "uncommon",
                  "entries": [ "This weapon can fire an extra time." ]
                }
              }
            ] }
            """,
            """
            { "baseitem": [
              { "name": "Shortbow", "source": "PHB", "weaponCategory": "simple", "property": ["A|XPHB", "LD|XPHB"], "type": "R|PHB" },
              { "name": "Sling", "source": "PHB", "weaponCategory": "simple", "property": ["S|XPHB"], "type": "R|PHB" }
            ] }
            """);
        try
        {
            var roster = MagicVariantExpander.Expand(root).ToList();
            var names = roster.Select(e => e.GetProperty("name").GetString()).ToList();

            // Base item whose property array CONTAINS the scalar predicate value matches.
            Assert.Contains("+1 Shortbow", names);

            // Base item whose property array does NOT contain the scalar predicate value
            // must not match.
            Assert.DoesNotContain("+1 Sling", names);
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
