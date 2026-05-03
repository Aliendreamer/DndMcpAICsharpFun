using System.Text;
using DndMcpAICsharpFun.Domain.Entities.Fields;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public sealed class SpellCanonicalTextRenderer : IEntityCanonicalTextRenderer<SpellFields>
{
    public string Render(string name, SpellFields f)
    {
        var sb = new StringBuilder();
        var levelText = f.Level == 0 ? $"{f.School} cantrip" : $"{Ordinal(f.Level)}-level {f.School}";
        sb.AppendLine($"{name} — {levelText}");
        sb.AppendLine($"Casting Time: {f.CastingTime}");
        sb.AppendLine($"Range: {f.Range}");
        var comps = new List<string>();
        if (f.Components.V) comps.Add("V");
        if (f.Components.S) comps.Add("S");
        if (f.Components.M) comps.Add(f.Components.Material is null ? "M" : $"M ({f.Components.Material})");
        sb.AppendLine($"Components: {string.Join(", ", comps)}");
        sb.AppendLine($"Duration: {(f.Concentration ? "Concentration, up to " : "")}{f.Duration}");
        if (f.Classes.Count > 0) sb.AppendLine($"Classes: {string.Join(", ", f.Classes)}");
        sb.AppendLine();
        sb.AppendLine(f.Description);
        if (!string.IsNullOrEmpty(f.AtHigherLevels))
        {
            sb.AppendLine();
            sb.AppendLine("At Higher Levels: " + f.AtHigherLevels);
        }
        return sb.ToString();
    }

    private static string Ordinal(int n) => n switch
    {
        1 => "1st", 2 => "2nd", 3 => "3rd",
        _ => $"{n}th"
    };
}
