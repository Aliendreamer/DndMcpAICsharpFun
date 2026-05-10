using System.Text;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class ExtractionPromptBuilder
{
    private const string SizeCodes =
        "Size codes: T=Tiny, S=Small, M=Medium, L=Large, H=Huge, G=Gargantuan. " +
        "Use an array: \"size\": [\"M\"]";

    private const string AlignmentCodes =
        "Alignment codes: L=Lawful, C=Chaotic, G=Good, E=Evil, N=Neutral, U=Unaligned, A=Any. " +
        "Use an array: \"alignment\": [\"L\", \"G\"] for Lawful Good.";

    private const string SchoolCodes =
        "Spell school codes: A=Abjuration, C=Conjuration, D=Divination, E=Enchantment, " +
        "V=Evocation, I=Illusion, N=Necromancy, T=Transmutation. " +
        "Example: \"school\": \"V\"";

    private const string RuleTypeCodes =
        "Rule type codes: C=Core rule, O=Optional rule, V=Variant rule. " +
        "Example: \"ruleType\": \"O\"";

    private const string EntriesGuidance =
        "Produce descriptive text as a JSON `entries` array. " +
        "Plain paragraphs are strings. Named subsections use: " +
        "{\"type\":\"entries\",\"name\":\"Section Name\",\"entries\":[\"...\"]}. " +
        "Lists use: {\"type\":\"list\",\"items\":[\"item1\",\"item2\"]}. " +
        "Use inline tags where appropriate: {@damage 2d8} for damage rolls, " +
        "{@dc 15} for DCs, {@condition prone} for conditions, " +
        "{@item Javelin|PHB} for item references.";

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
        sb.AppendLine("The source text may contain OCR artifacts (e.g. 'gons' → 'gods', 'lhe' → 'the'). Use surrounding context to infer the correct meaning.");
        sb.AppendLine("Cross-entity references must use existing slug-style IDs of form `<book-slug>.<type-slug>.<entity-slug>`.");
        sb.AppendLine();
        sb.AppendLine("Do not extract chapter titles, section headings, or table headers as entities. Only extract named, discrete game elements.");
        sb.AppendLine();
        sb.AppendLine(EntriesGuidance);
        sb.AppendLine();
        sb.AppendLine("Type routing guidance:");
        sb.AppendLine("- Use `Lore` for named worldbuilding concepts, cosmology, pantheon overviews, philosophies, and cultural flavour that is not a discrete game entity.");
        sb.AppendLine("- Use `Rule` for mechanical procedures, encounter tables, adventure design guidelines, random tables, and DMing system explanations.");
        sb.AppendLine("- Use `God` only when the entity is a named deity with known alignment and at least one domain.");
        sb.AppendLine("- Use `Plane` only when the entity is a named plane of existence with a defined category (Inner, Outer, Transitive, Material, etc.).");
        sb.AppendLine("- Use `Monster` only when the entity has a stat block with a challenge rating.");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT — EntityType classification rules:");
        sb.AppendLine("Valid types: Class, Subclass, Spell, Monster, Race, Subrace, Background, Feat, Item, MagicItem, Weapon, Armor, God, Trap, Condition, DiseasePoison, VehicleMount, Rule, Lore, Plane, Faction, Location.");
        sb.AppendLine("- Use Subclass for named subclass entries (e.g. Path of Wild Magic, Circle of Spores, Oath of Glory).");
        sb.AppendLine("- Use Spell for any named spell entry with casting time, range, duration, and components.");
        sb.AppendLine("- Use Feat for player feats with prerequisites.");
        sb.AppendLine("- Use Rule for metamagic options, fighting styles, eldritch invocations, maneuvers, and other optional mechanical add-ons.");
        sb.AppendLine("- Use Class ONLY for a full base class entry (e.g. Barbarian, Fighter). Class is a last resort — prefer a more specific type.");
        sb.AppendLine("- Use Faction for named political, religious, or social organisations with membership or hierarchy details (e.g. The Harpers, The Lords' Alliance).");
        sb.AppendLine("- Use Location for named geographic places, settlements, dungeons, planes of existence, or dungeon sites.");
        sb.AppendLine("If uncertain, pick the most specific applicable type over Class.");
        sb.AppendLine();

        switch (type)
        {
            case EntityType.Spell:
                sb.AppendLine(SchoolCodes);
                sb.AppendLine("Casting time example: \"time\": [{\"number\": 1, \"unit\": \"action\"}]");
                sb.AppendLine("Range example: \"range\": {\"type\": \"point\", \"distance\": {\"type\": \"feet\", \"amount\": 150}}");
                sb.AppendLine("Duration example: \"duration\": [{\"type\": \"instant\"}] or [{\"type\": \"timed\", \"duration\": {\"type\": \"minute\", \"amount\": 1}, \"concentration\": true}]");
                sb.AppendLine("Components: v/s are booleans; m is the material text string, e.g. \"m\": \"a tiny ball of bat guano and sulfur\"");
                break;

            case EntityType.Monster:
                sb.AppendLine(SizeCodes);
                sb.AppendLine(AlignmentCodes);
                sb.AppendLine("Type example: \"type\": {\"type\": \"humanoid\", \"tags\": [\"bullywug\"]}");
                sb.AppendLine("AC example: \"ac\": [15] or \"ac\": [{\"ac\": 17, \"from\": [\"natural armor\"]}]");
                sb.AppendLine("HP example: \"hp\": {\"average\": 11, \"formula\": \"2d8+2\"}");
                sb.AppendLine("Ability scores are flat fields: \"str\": 12, \"dex\": 14, \"con\": 10, \"int\": 8, \"wis\": 10, \"cha\": 8");
                sb.AppendLine("Skills example: \"skill\": {\"perception\": \"+5\", \"stealth\": \"+3\"}");
                sb.AppendLine("CR example: \"cr\": \"1/4\"");
                sb.AppendLine("Traits/actions use entries: \"trait\": [{\"name\": \"Amphibious\", \"entries\": [\"Can breathe air and water.\"]}]");
                sb.AppendLine("keywords: collect the name of each named trait or special ability visible in the stat block (e.g. [\"Pack Tactics\", \"Keen Senses\"]). Use names exactly as they appear in the text. Omit if none.");
                break;

            case EntityType.Race:
            case EntityType.Subrace:
                sb.AppendLine(SizeCodes);
                sb.AppendLine("Speed example: \"speed\": {\"walk\": 30, \"fly\": 30}");
                sb.AppendLine("Ability bonuses example: \"ability\": [{\"str\": 2, \"dex\": 1}]");
                sb.AppendLine("Language proficiencies example: \"languageProficiencies\": [{\"common\": true, \"anyStandard\": 1}]");
                break;

            case EntityType.God:
                sb.AppendLine(AlignmentCodes);
                sb.AppendLine("Example: \"alignment\": [\"L\", \"G\"] for Lawful Good");
                break;

            case EntityType.Rule:
                sb.AppendLine(RuleTypeCodes);
                break;

            case EntityType.MagicItem:
                sb.AppendLine("If the source text describes multiple tiers of the same item (e.g. +1/+2/+3), extract them as a single entity with a `variants` array.");
                break;

            case EntityType.Trap:
                sb.AppendLine("If the source text describes a group of trap variants (e.g. Simple Pit / Spiked Pit), extract them as a single entity with a `variants` array.");
                sb.AppendLine("trapHazType codes: MECH=Mechanical, MAG=Magical, SMPL=Simple, CMPX=Complex");
                break;
        }

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
