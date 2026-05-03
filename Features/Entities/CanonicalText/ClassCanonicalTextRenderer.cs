using System.Text;
using DndMcpAICsharpFun.Domain.Entities.Fields;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public sealed class ClassCanonicalTextRenderer : IEntityCanonicalTextRenderer<ClassFields>
{
    public string Render(string name, ClassFields f)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{name} — Hit Die: {f.HitDie}");
        sb.AppendLine($"Primary abilities: {string.Join(", ", f.PrimaryAbilities)}");
        sb.AppendLine($"Saving throws: {string.Join(", ", f.SavingThrowProficiencies)}");
        sb.AppendLine($"Armor: {string.Join(", ", f.ArmorProficiencies)}");
        sb.AppendLine($"Weapons: {string.Join(", ", f.WeaponProficiencies)}");
        if (f.Spellcasting is { } sc) sb.AppendLine($"Spellcasting: {sc.Type} ({sc.Ability})");
        sb.AppendLine($"Subclass at level {f.SubclassSelectionLevel}");
        sb.AppendLine("Features by level:");
        foreach (var entry in f.FeaturesByLevel)
        {
            sb.AppendLine($"  L{entry.Level} (PB +{entry.ProficiencyBonus}):");
            foreach (var feat in entry.Features)
                sb.AppendLine($"    - {feat.Name}: {feat.Summary}");
        }
        return sb.ToString();
    }
}
