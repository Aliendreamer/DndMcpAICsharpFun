using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Encounters;      // MonsterCr
using DndMcpAICsharpFun.Features.Retrieval.Entities;

namespace DndMcpAICsharpFun.Features.Npc;

/// <summary>
/// Grounds an NPC to a real corpus stat block: the caller (chat LLM) picks the archetype fitting a
/// concept, this validates it exists as a Monster entity by exact name and returns its real stats;
/// a miss (or a CR over maxCr) yields the not-in-corpus flag + the archetype menu. Not ownership-gated;
/// does NOT call an LLM — the persona invents the flavour around the returned numbers.
/// </summary>
public sealed class NpcGenerationService(IEntityRetrievalService retrieval)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<GeneratedNpc> GenerateAsync(
        string concept, string archetype, double? maxCr, CancellationToken ct)
    {
        var hits = await retrieval.SearchDiagnosticAsync(
            new EntitySearchQuery(
                QueryText: archetype, Type: EntityType.Monster, SourceBook: null, Edition: null,
                BookType: null, SettingTag: null, Keyword: null, CrNumericLte: null, CrNumericGte: null,
                SpellLevel: null, DamageType: null, TopK: 1),
            ct);

        var hit = hits.FirstOrDefault(
            r => string.Equals(r.Name, archetype, StringComparison.OrdinalIgnoreCase));

        // Not found, or the resolved CR exceeds the caller's cap → suggest the roster.
        if (hit is null
            || (maxCr is { } cap && MonsterCr.TryRead(hit.Fields, out var hitCr) && hitCr > cap))
        {
            return new GeneratedNpc(concept, archetype, StatBlock: null,
                ArchetypeInCorpus: false, AvailableArchetypes: NpcArchetypes.Common);
        }

        // Fetch the full envelope for the rendered stat-block text.
        var full = await retrieval.GetByIdAsync(hit.Id, ct);
        var canonicalText = full?.Envelope.CanonicalText ?? string.Empty;
        var fields = full?.Envelope.Fields ?? hit.Fields;

        double? cr = MonsterCr.TryRead(hit.Fields, out var crVal) ? crVal : null;
        MonsterFields? mf = TryDeserialize(fields);

        var block = new NpcStatBlock(
            hit.Name, hit.SourceBook, cr, mf?.Hp?.Average,
            mf?.Str, mf?.Dex, mf?.Con, mf?.Int, mf?.Wis, mf?.Cha, canonicalText);

        return new GeneratedNpc(concept, archetype, block,
            ArchetypeInCorpus: true, AvailableArchetypes: []);
    }


    public async Task<GeneratedNpcParty> GeneratePartyAsync(string theme, CancellationToken ct)
    {
        var (templateName, roster) = NpcPartyTemplates.Resolve(theme);
        var members = new List<NpcPartyMember>(roster.Count);
        foreach (var (role, archetype) in roster)
        {
            var npc = await GenerateAsync(concept: role, archetype: archetype, maxCr: null, ct);
            members.Add(new NpcPartyMember(role, npc));
        }
        return new GeneratedNpcParty(theme, templateName, members);
    }

    private static MonsterFields? TryDeserialize(JsonElement fields)
    {
        try { return fields.Deserialize<MonsterFields>(JsonOpts); }
        catch (JsonException) { return null; } // structured fields are a convenience; CanonicalText is the base
    }
}
