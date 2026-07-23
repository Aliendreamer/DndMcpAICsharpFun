using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

/// <summary>
/// Covers ItemBackfillProvider, WeaponBackfillProvider, and ArmorBackfillProvider — the
/// base-item PARTITION set (design D2). Each mundane items-base.json/items.json element must be
/// claimed by exactly one of the three, and magic items (rarity present, handled by
/// MagicItemBackfillProvider) must be claimed by none of them. See
/// <see cref="CatalogBackfillProviderTests"/> for the sibling prose-catalog providers.
/// </summary>
public sealed class BaseItemBackfillProviderTests
{
    private readonly CanonicalJsonLoader _loader = new();

    [Fact]
    public void WeaponBackfillProvider_Type_IsWeapon()
    {
        var provider = new WeaponBackfillProvider();

        Assert.Equal(EntityType.Weapon, provider.Type);
    }

    [Fact]
    public void ArmorBackfillProvider_Type_IsArmor()
    {
        var provider = new ArmorBackfillProvider();

        Assert.Equal(EntityType.Armor, provider.Type);
    }

    [Fact]
    public void ItemBackfillProvider_Type_IsItem()
    {
        var provider = new ItemBackfillProvider();

        Assert.Equal(EntityType.Item, provider.Type);
    }

    [Fact]
    public void WeaponBackfillProvider_BuildEntity_ProjectsCuratedFieldsForDagger()
    {
        var provider = new WeaponBackfillProvider();
        using var doc = JsonDocument.Parse("""
            { "name": "Dagger", "source": "PHB", "page": 149, "srd": true,
              "type": "M", "rarity": "none", "weight": 1, "value": 200,
              "weaponCategory": "simple", "property": [ "F", "L", "T" ],
              "range": "20/60", "dmg1": "1d4", "dmgType": "P" }
            """);

        var entity = provider.BuildEntity("PHB", "Edition2014", "Dagger", doc.RootElement);

        Assert.Equal("phb14.weapon.dagger", entity.Id);
        Assert.Equal(EntityType.Weapon, entity.Type);
        Assert.Equal("5etools-backfill", entity.DataSource);
        Assert.Equal(EntityDisposition.Accepted, entity.Disposition);

        var fields = _loader.DeserialiseFields<WeaponFields>(entity);
        Assert.Equal("Simple", fields.Category);
        Assert.Equal("Melee", fields.WeaponType);
        Assert.Equal(200, fields.CostCp);
        Assert.Equal(1, fields.WeightLb);
        Assert.Equal("1d4", fields.Damage.Dice);
        Assert.Equal(2, fields.Damage.Average);
        Assert.Equal("piercing", fields.Damage.Type);
        Assert.NotNull(fields.Range);
        Assert.Equal(20, fields.Range!.Normal);
        Assert.Equal(60, fields.Range!.Long);
        Assert.Contains("Finesse", fields.Properties);
        Assert.Contains("Light", fields.Properties);
        Assert.Contains("Thrown", fields.Properties);
    }

    [Fact]
    public void ArmorBackfillProvider_BuildEntity_ProjectsCuratedFieldsForChainMail()
    {
        var provider = new ArmorBackfillProvider();
        using var doc = JsonDocument.Parse("""
            { "name": "Chain Mail", "source": "PHB", "page": 145, "srd": true,
              "type": "HA", "rarity": "none", "weight": 55, "value": 7500,
              "ac": 16, "strength": "13", "stealth": true }
            """);

        var entity = provider.BuildEntity("PHB", "Edition2014", "Chain Mail", doc.RootElement);

        Assert.Equal("phb14.armor.chain-mail", entity.Id);
        Assert.Equal(EntityType.Armor, entity.Type);
        Assert.Equal("5etools-backfill", entity.DataSource);
        Assert.Equal(EntityDisposition.Accepted, entity.Disposition);

        var fields = _loader.DeserialiseFields<ArmorFields>(entity);
        Assert.Equal("Heavy", fields.Category);
        Assert.Equal(75, fields.CostGp);
        Assert.Equal(55, fields.WeightLb);
        Assert.Equal("16", fields.AcFormula);
        Assert.Equal(13, fields.StrengthRequirement);
        Assert.True(fields.StealthDisadvantage);
    }

    [Fact]
    public void ItemBackfillProvider_BuildEntity_ProjectsCuratedFieldsForCrowbar()
    {
        var provider = new ItemBackfillProvider();
        using var doc = JsonDocument.Parse("""
            { "name": "Crowbar", "source": "PHB", "page": 153, "srd": true,
              "type": "AT", "rarity": "none", "weight": 5, "value": 200,
              "entries": [ "Using a crowbar grants advantage to Strength checks where the crowbar's leverage can be applied." ] }
            """);

        var entity = provider.BuildEntity("PHB", "Edition2014", "Crowbar", doc.RootElement);

        Assert.Equal("phb14.item.crowbar", entity.Id);
        Assert.Equal(EntityType.Item, entity.Type);
        Assert.Equal("5etools-backfill", entity.DataSource);
        Assert.Equal(EntityDisposition.Accepted, entity.Disposition);

        var fields = _loader.DeserialiseFields<ItemFields>(entity);
        Assert.Equal(200, fields.CostCp);
        Assert.Equal(5, fields.WeightLb);
        Assert.Contains("advantage to Strength checks", fields.Description);
    }

    [Fact]
    public void BaseItemProviders_Partition_ClaimsEachMundaneItemExactlyOnceAndExcludesMagicItems()
    {
        var root = Path.Combine(Path.GetTempPath(), "baseitem-backfill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "items-base.json"), """
                { "baseitem": [
                    { "name": "Dagger", "source": "PHB", "type": "M", "rarity": "none",
                      "weaponCategory": "simple", "weight": 1, "value": 200,
                      "property": [ "F", "L", "T" ], "range": "20/60", "dmg1": "1d4", "dmgType": "P" },
                    { "name": "Leather Armor", "source": "PHB", "type": "LA", "rarity": "none",
                      "weight": 10, "value": 1000, "ac": 11 },
                    { "name": "Crowbar", "source": "PHB", "type": "AT", "rarity": "none",
                      "weight": 5, "value": 200,
                      "entries": [ "Using a crowbar grants advantage to Strength checks." ] }
                  ]
                }
                """);

            File.WriteAllText(Path.Combine(root, "items.json"), """
                { "item": [
                    { "name": "Bag of Holding", "source": "DMG", "type": "AT", "rarity": "uncommon",
                      "reqAttune": false,
                      "entries": [ "This bag has an interior space considerably larger than its outside dimensions." ] }
                  ]
                }
                """);

            var weaponNames = new WeaponBackfillProvider().EnumerateRoster(root)
                .Select(e => e.GetProperty("name").GetString()).ToList();
            var armorNames = new ArmorBackfillProvider().EnumerateRoster(root)
                .Select(e => e.GetProperty("name").GetString()).ToList();
            var itemNames = new ItemBackfillProvider().EnumerateRoster(root)
                .Select(e => e.GetProperty("name").GetString()).ToList();

            Assert.Equal(new[] { "Dagger" }, weaponNames);
            Assert.Equal(new[] { "Leather Armor" }, armorNames);
            Assert.Equal(new[] { "Crowbar" }, itemNames);

            Assert.DoesNotContain("Bag of Holding", weaponNames);
            Assert.DoesNotContain("Bag of Holding", armorNames);
            Assert.DoesNotContain("Bag of Holding", itemNames);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Review finding (design D2): a magic WEAPON — a base item carrying BOTH a
    /// <c>"weaponCategory"</c> (so <see cref="BaseItemPartition.Classify"/> would otherwise call
    /// it a weapon) AND a present, non-"none" <c>"rarity"</c> (so it's a magic item, not mundane
    /// gear) — must be excluded from all three mundane partitions by
    /// <see cref="BaseItemPartition.IsMundane"/>. The existing partition test above only seeds a
    /// magic ITEM (Bag of Holding, no <c>weaponCategory</c>); this closes the double-count risk
    /// specific to a magic item that ALSO looks like a weapon by shape.
    /// </summary>
    [Fact]
    public void BaseItemProviders_Partition_ExcludesMagicWeapon_FromAllThreeMundaneProviders()
    {
        var root = Path.Combine(Path.GetTempPath(), "baseitem-backfill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "items.json"), """
                { "item": [
                    { "name": "+1 Longsword", "source": "DMG", "type": "M", "rarity": "rare",
                      "weaponCategory": "martial", "reqAttune": false, "weight": 3, "value": 1500,
                      "property": [ "V" ], "dmg1": "1d8", "dmg2": "1d10", "dmgType": "S", "bonusWeapon": "+1" }
                  ]
                }
                """);

            var weaponNames = new WeaponBackfillProvider().EnumerateRoster(root)
                .Select(e => e.GetProperty("name").GetString()).ToList();
            var armorNames = new ArmorBackfillProvider().EnumerateRoster(root)
                .Select(e => e.GetProperty("name").GetString()).ToList();
            var itemNames = new ItemBackfillProvider().EnumerateRoster(root)
                .Select(e => e.GetProperty("name").GetString()).ToList();

            Assert.DoesNotContain("+1 Longsword", weaponNames);
            Assert.DoesNotContain("+1 Longsword", armorNames);
            Assert.DoesNotContain("+1 Longsword", itemNames);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}