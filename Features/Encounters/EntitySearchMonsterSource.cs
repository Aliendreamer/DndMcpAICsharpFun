using System.Globalization;
using System.Text.Json;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Retrieval.Entities;

namespace DndMcpAICsharpFun.Features.Encounters;

/// <summary>
/// Production <see cref="IEncounterMonsterSource"/>: retrieves monster candidates from the
/// structured entity store via <see cref="IEntityRetrievalService"/>.
/// </summary>
/// <remarks>
/// Uses <see cref="IEntityRetrievalService.SearchDiagnosticAsync"/> rather than
/// <c>SearchAsync</c>: <see cref="EntitySearchResult"/> (the plain search result) carries no CR —
/// only <see cref="EntityDiagnosticResult"/> exposes the entity's raw <c>Fields</c> JSON, which is
/// where CR lives. Results with no usable CR (missing/unparseable) are skipped rather than thrown.
/// </remarks>
public sealed class EntitySearchMonsterSource(IEntityRetrievalService search) : IEncounterMonsterSource
{
    public async Task<IReadOnlyList<MonsterRef>> FindAsync(
        DndVersion ed,
        double crGte,
        double crLte,
        string? theme,
        bool srdOnly,
        int limit,
        CancellationToken ct)
    {
        var query = new EntitySearchQuery(
            QueryText: theme ?? "monster",
            Type: EntityType.Monster,
            SourceBook: null,
            Edition: ed.ToString(),
            BookType: null,
            SettingTag: null,
            Keyword: theme,
            CrNumericLte: crLte,
            CrNumericGte: crGte,
            SpellLevel: null,
            DamageType: null,
            TopK: limit,
            Srd: srdOnly ? true : null);

        var hits = await search.SearchDiagnosticAsync(query, ct);

        var monsters = new List<MonsterRef>();
        foreach (var hit in hits)
        {
            if (!MonsterCr.TryRead(hit.Fields, out var cr))
                continue; // no usable CR on this entity — skip rather than fail the whole search

            int xp;
            try
            {
                xp = EncounterMath.CrToXp(cr);
            }
            catch (ArgumentOutOfRangeException)
            {
                continue; // CR outside the standard 0..30 table — skip rather than throw mid-search
            }

            var initMod = MonsterDex.TryReadModifier(hit.Fields, out var m) ? m : 0;
            MonsterHp.TryRead(hit.Fields, out var avgHp, out var hpFormula);
            monsters.Add(new MonsterRef(hit.Id, hit.Name, cr, xp, initMod, avgHp, hpFormula));
        }

        return monsters;
    }
}

/// <summary>
/// Shared CR-from-entity-fields parsing, mirroring how <c>QdrantEntityVectorStore</c> flattens the
/// same "cr" field into the indexed <c>cr_numeric</c> payload at ingestion time, so the encounter
/// surface reads CR the same way the index itself does.
/// </summary>
internal static class MonsterCr
{
    public static bool TryRead(JsonElement fields, out double cr)
    {
        cr = 0;
        if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty("cr", out var crEl))
            return false;

        if (crEl.ValueKind == JsonValueKind.String)
            return TryParse(crEl.GetString(), out cr);

        if (crEl.ValueKind == JsonValueKind.Object && crEl.TryGetProperty("cr", out var inner))
            return TryParse(inner.GetString(), out cr);

        return false;
    }

    private static bool TryParse(string? cr, out double value)
    {
        switch (cr)
        {
            case "1/8": value = 0.125; return true;
            case "1/4": value = 0.25; return true;
            case "1/2": value = 0.5; return true;
        }

        return double.TryParse(cr, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }
}


/// <summary>
/// Reads a monster's Dexterity ability score from its entity fields (<c>MonsterFields.dex</c>) and
/// derives the D&D initiative modifier <c>floor((dex-10)/2)</c>. Returns false when no usable dex is
/// present (caller defaults the modifier to 0).
/// </summary>
internal static class MonsterDex
{
    public static bool TryReadModifier(JsonElement fields, out int modifier)
    {
        modifier = 0;
        if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty("dex", out var dexEl))
            return false;

        int dex;
        if (dexEl.ValueKind == JsonValueKind.Number && dexEl.TryGetInt32(out dex))
        {
            // ok
        }
        else if (dexEl.ValueKind == JsonValueKind.String && int.TryParse(dexEl.GetString(), out dex))
        {
            // ok
        }
        else
        {
            return false;
        }

        modifier = (int)Math.Floor((dex - 10) / 2.0);
        return true;
    }
}

/// <summary>Reads a monster's HP from its entity fields (<c>MonsterFields.hp.average</c> + optional
/// <c>hp.formula</c>). Returns false when no usable average is present (caller defaults to 0).</summary>
internal static class MonsterHp
{
    public static bool TryRead(JsonElement fields, out int average, out string? formula)
    {
        average = 0;
        formula = null;
        if (fields.ValueKind != JsonValueKind.Object
            || !fields.TryGetProperty("hp", out var hpEl) || hpEl.ValueKind != JsonValueKind.Object)
            return false;
        if (!hpEl.TryGetProperty("average", out var avgEl)
            || avgEl.ValueKind != JsonValueKind.Number || !avgEl.TryGetInt32(out average))
            return false;
        if (hpEl.TryGetProperty("formula", out var fEl) && fEl.ValueKind == JsonValueKind.String)
            formula = fEl.GetString();
        return true;
    }
}
