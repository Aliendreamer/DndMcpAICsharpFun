using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

public sealed record FivetoolsFileEntry(
    string RelativePath,
    EntityType EntityType,
    string JsonArrayKey);

public static class FivetoolsSourceRegistry
{
    private const string Base = "5etools";

    public static IReadOnlyList<FivetoolsFileEntry> AllEntries { get; } = Build();

    private static IReadOnlyList<FivetoolsFileEntry> Build()
    {
        var entries = new List<FivetoolsFileEntry>();

        // ── Classes & subclasses (one file per class, contains both class[] and subclass[])
        var classDir = Path.Combine(Base, "class");
        if (Directory.Exists(classDir))
        {
            foreach (var file in Directory.GetFiles(classDir, "class-*.json")
                         .Where(f => !Path.GetFileName(f).StartsWith("fluff-")))
            {
                entries.Add(new(file, EntityType.Class, "class"));
                entries.Add(new(file, EntityType.Subclass, "subclass"));
            }
        }

        // ── Spells (one file per source)
        var spellDir = Path.Combine(Base, "spells");
        if (Directory.Exists(spellDir))
        {
            foreach (var file in Directory.GetFiles(spellDir, "spells-*.json")
                         .Where(f => !Path.GetFileName(f).StartsWith("fluff-") &&
                                     !Path.GetFileName(f).Contains("index") &&
                                     !Path.GetFileName(f).Contains("foundry")))
                entries.Add(new(file, EntityType.Spell, "spell"));
        }

        // ── Bestiary (one file per source)
        var bestiaryDir = Path.Combine(Base, "bestiary");
        if (Directory.Exists(bestiaryDir))
        {
            foreach (var file in Directory.GetFiles(bestiaryDir, "bestiary-*.json")
                         .Where(f => !Path.GetFileName(f).StartsWith("fluff-")))
                entries.Add(new(file, EntityType.Monster, "monster"));
        }

        // ── Global combined files
        void AddGlobal(string relPath, EntityType type, string key)
        {
            var full = Path.Combine(Base, relPath);
            if (File.Exists(full)) entries.Add(new(full, type, key));
        }

        AddGlobal("races.json", EntityType.Race, "race");
        AddGlobal("races.json", EntityType.Subrace, "subrace");
        AddGlobal("backgrounds.json", EntityType.Background, "background");
        AddGlobal("feats.json", EntityType.Feat, "feat");
        AddGlobal("items.json", EntityType.Item, "item");
        AddGlobal("items.json", EntityType.MagicItem, "item");
        AddGlobal("items-base.json", EntityType.Weapon, "baseitem");
        AddGlobal("items-base.json", EntityType.Armor, "baseitem");
        AddGlobal("deities.json", EntityType.God, "deity");
        AddGlobal("trapshazards.json", EntityType.Trap, "trap");
        AddGlobal("conditionsdiseases.json", EntityType.Condition, "condition");
        AddGlobal("conditionsdiseases.json", EntityType.DiseasePoison, "disease");
        AddGlobal("vehicles.json", EntityType.VehicleMount, "vehicle");
        AddGlobal("variantrules.json", EntityType.Rule, "variantrule");

        return entries;
    }
}