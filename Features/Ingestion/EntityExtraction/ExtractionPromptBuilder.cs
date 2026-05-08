using System.Text;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class ExtractionPromptBuilder
{
    public string BuildSystemPrompt(string sourceBook, string edition, EntityType type)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are extracting structured D&D rules data from official rulebook text.");
        sb.AppendLine($"Source book: {sourceBook} ({edition}).");
        sb.AppendLine($"Entity type: {type}.");
        sb.AppendLine();
        sb.AppendLine($"Call the tool `{ToolName(type)}` with a JSON object that conforms exactly to its input_schema.");
        sb.AppendLine("Do not include any prose. The tool's input is the only output we read.");
        sb.AppendLine("If the source text is incomplete or ambiguous, leave optional fields null/absent rather than guessing.");
        sb.AppendLine("The source text may contain OCR artifacts (e.g. 'gons' → 'gods', 'lhe' → 'the', 'encaunters' → 'encounters'). Use surrounding context to infer the correct meaning.");
        sb.AppendLine("Cross-entity references must use existing slug-style IDs of form `<book-slug>.<type-slug>.<entity-slug>`.");
        sb.AppendLine();
        sb.AppendLine("Do not extract chapter titles, section headings, or table headers as entities. Only extract named, discrete game elements.");
        sb.AppendLine();
        sb.AppendLine("Type routing guidance:");
        sb.AppendLine("- Use `Lore` for named worldbuilding concepts, cosmology descriptions, pantheon overviews, religious philosophies, and cultural/setting flavour that is not a discrete game entity.");
        sb.AppendLine("- Use `Rule` for mechanical procedures, encounter tables, adventure design guidelines, random tables, and DMing system explanations.");
        sb.AppendLine("- Use `God` only when the entity is a named deity with known alignment and at least one domain.");
        sb.AppendLine("- Use `Plane` only when the entity is a named plane of existence with a defined category (Inner, Outer, Transitive, Material, etc.).");
        sb.AppendLine("- Use `Monster` only when the entity has a stat block with a challenge rating.");
        if (type == EntityType.MagicItem)
            sb.AppendLine("If the source text describes multiple tiers or variants of the same item (e.g. +1/+2/+3), extract them as a single entity with a `variants` array rather than separate entities.");
        if (type == EntityType.Trap)
            sb.AppendLine("If the source text describes a group of trap variants (e.g. Simple Pit / Spiked Pit / Locking Pit), extract them as a single entity with a `variants` array. Each variant gets its own name, detectDc, disarmDc, difficulty, and description.");
        return sb.ToString();
    }

    public string BuildUserPrompt(EntityCandidate candidate)
    {
        var pageNote = candidate.Page is { } p ? $" (page {p})" : "";
        var sb = new StringBuilder();
        sb.AppendLine($"Entity: {candidate.DisplayName}{pageNote}");
        sb.AppendLine();
        sb.AppendLine("Source text:");
        sb.AppendLine("```");
        sb.Append(candidate.Text);
        sb.AppendLine();
        sb.AppendLine("```");
        return sb.ToString();
    }

    public string ToolName(EntityType type)
    {
        var camel = type.ToString();
        var sb = new StringBuilder("emit_");
        for (int i = 0; i < camel.Length; i++)
        {
            var c = camel[i];
            if (i > 0 && char.IsUpper(c)) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        sb.Append("_fields");
        return sb.ToString();
    }

    public string ToolDescription(EntityType type) =>
        $"Emit a structured {type} entity's `fields` object. The input MUST validate against the provided schema.";
}
