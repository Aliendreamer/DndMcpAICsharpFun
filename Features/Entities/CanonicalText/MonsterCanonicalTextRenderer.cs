using System.Text;
using DndMcpAICsharpFun.Domain.Entities.Fields;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public sealed class MonsterCanonicalTextRenderer : IEntityCanonicalTextRenderer<MonsterFields>
{
    public string Render(string name, MonsterFields f)
    {
        var sb = new StringBuilder();
        var subtypes = f.Subtypes.Count == 0 ? "" : $" ({string.Join(", ", f.Subtypes)})";
        sb.AppendLine($"{name} — {f.Size} {f.Type}{subtypes}, {f.Alignment}");
        sb.AppendLine($"AC {f.ArmorClass.Value}{(f.ArmorClass.Source is null ? "" : $" ({f.ArmorClass.Source})")}");
        sb.AppendLine($"HP {f.HitPoints.Average} ({f.HitPoints.Dice})");
        sb.AppendLine($"Speed {string.Join(", ", f.Speed.Select(kv => $"{kv.Key} {kv.Value} ft."))}");
        sb.AppendLine($"STR {f.AbilityScores.Strength} DEX {f.AbilityScores.Dexterity} CON {f.AbilityScores.Constitution} INT {f.AbilityScores.Intelligence} WIS {f.AbilityScores.Wisdom} CHA {f.AbilityScores.Charisma}");
        sb.AppendLine($"Challenge {f.ChallengeRating.Cr} ({f.ChallengeRating.Xp} XP), proficiency +{f.ChallengeRating.ProficiencyBonus}");
        if (f.Keywords.Count > 0) sb.AppendLine($"Keywords: {string.Join(", ", f.Keywords)}");
        if (f.Environment.Count > 0) sb.AppendLine($"Environment: {string.Join(", ", f.Environment)}");
        foreach (var t in f.Traits) sb.AppendLine($"Trait — {t.Name}: {t.Summary}");
        foreach (var a in f.Actions) sb.AppendLine($"Action — {a.Name}: {a.Summary}");
        return sb.ToString();
    }
}
