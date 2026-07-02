using System.Text.Json;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DndMcpAICsharpFun.Features.Resolution;

/// <summary>
/// A resolved sub-value for one dimension of a character feature, including its provenance citation.
/// </summary>
public sealed record ResolvedComponent(string Label, string Value, ProvenanceRef? Provenance);

/// <summary>
/// The fully resolved value for a character feature, with all contributing components and a
/// confidence indicator ("ok" or "needsReview").
/// </summary>
public sealed record ResolvedFact(
    string Feature,
    string Value,
    IReadOnlyList<ResolvedComponent> Components,
    string Confidence);

/// <summary>
/// Deterministic resolution engine: resolves character features (e.g. breath weapon stats) from
/// structured facts stored in Postgres, citing provenance for every component.
/// </summary>
public sealed class CharacterResolutionService(
    IDbContextFactory<AppDbContext> dbf,
    HeroRepository heroes)
{
    private static readonly JsonSerializerOptions JsonOpts = new();

    public async Task<ResolvedFact> ResolveAsync(
        long heroSnapshotId,
        string feature,
        CancellationToken ct = default)
    {
        var snapshot = await heroes.GetSnapshotAsync(heroSnapshotId);
        if (snapshot is null)
            throw new InvalidOperationException("snapshot not found");

        if (feature.Equals("breath weapon", StringComparison.OrdinalIgnoreCase))
            return await ResolveBreathWeaponAsync(snapshot.Sheet, ct);

        throw new NotSupportedException($"feature not supported: {feature}");
    }

    private async Task<ResolvedFact> ResolveBreathWeaponAsync(CharacterSheet sheet, CancellationToken ct)
    {
        // Step a: parse ancestry choice
        if (!sheet.ResolvedChoices.TryGetValue("ancestry", out var ancestryValue)
            || string.IsNullOrWhiteSpace(ancestryValue))
        {
            return new ResolvedFact("breath weapon", "unknown", [], "needsReview");
        }

        var lastColon = ancestryValue.LastIndexOf(':');
        if (lastColon < 0)
            return new ResolvedFact("breath weapon", "unknown", [], "needsReview");

        var choicesetId = ancestryValue[..lastColon];
        var optionKey   = ancestryValue[(lastColon + 1)..];

        await using var db = await dbf.CreateDbContextAsync(ct);

        // Step b: load choice-set and find option
        var choiceSetRow = await db.ChoiceSetRows
            .FirstOrDefaultAsync(c => c.CanonicalId == choicesetId, ct);
        if (choiceSetRow is null)
            return new ResolvedFact("breath weapon", "unknown", [], "needsReview");

        var options = JsonSerializer.Deserialize<List<CanonicalChoiceOption>>(
            choiceSetRow.OptionsJson, JsonOpts);
        var option = options?.Find(o => o.Key == optionKey);
        if (option is null)
            return new ResolvedFact("breath weapon", "unknown", [], "needsReview");

        // Step c: load the ancestry table row
        var structuredTable = await db.StructuredTables
            .FirstOrDefaultAsync(t => t.CanonicalId == option.TableId, ct);
        if (structuredTable is null)
            return new ResolvedFact("breath weapon", "unknown", [], "needsReview");

        var ancestryRow = await db.StructuredTableRows
            .FirstOrDefaultAsync(r => r.TableId == structuredTable.Id && r.RowIndex == option.RowIndex, ct);
        if (ancestryRow is null)
            return new ResolvedFact("breath weapon", "unknown", [], "needsReview");

        var columns = JsonSerializer.Deserialize<List<string>>(structuredTable.ColumnsJson, JsonOpts)
                      ?? [];
        var cells = JsonSerializer.Deserialize<List<CanonicalCell>>(ancestryRow.CellsJson, JsonOpts)
                    ?? [];

        // Map column name → cell
        var cellMap = new Dictionary<string, CanonicalCell>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < columns.Count && i < cells.Count; i++)
            cellMap[columns[i]] = cells[i];

        var damageTypeCell  = cellMap.GetValueOrDefault("damageType");
        var breathAreaCell  = cellMap.GetValueOrDefault("breathArea");
        var saveAbilityCell = cellMap.GetValueOrDefault("saveAbility");

        // Step d: dice from tier table (best-effort provenance)
        var dice = BreathWeaponRules.DiceForLevel(sheet.Level);
        var tier = sheet.Level switch
        {
            <= 5  => 1,
            <= 10 => 2,
            <= 15 => 3,
            _     => 4,
        };

        ProvenanceRef? tierProv = null;
        var tierTable = await db.StructuredTables
            .FirstOrDefaultAsync(t => t.CanonicalId == "phb14.table.breath-damage-by-tier", ct);
        if (tierTable is not null)
        {
            var tierRow = await db.StructuredTableRows
                .FirstOrDefaultAsync(r => r.TableId == tierTable.Id && r.RowIndex == tier - 1, ct);
            if (tierRow is not null)
            {
                var tierCols  = JsonSerializer.Deserialize<List<string>>(tierTable.ColumnsJson, JsonOpts) ?? [];
                var tierCells = JsonSerializer.Deserialize<List<CanonicalCell>>(tierRow.CellsJson, JsonOpts) ?? [];
                var tierCellMap = new Dictionary<string, CanonicalCell>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < tierCols.Count && i < tierCells.Count; i++)
                    tierCellMap[tierCols[i]] = tierCells[i];
                tierProv = tierCellMap.GetValueOrDefault("dice")?.Provenance;
            }
        }

        // Step e: conMod and save DC
        var conMod = CharacterSheet.Modifier(sheet.Constitution);
        var dc     = BreathWeaponRules.SaveDc(sheet.Level, conMod);

        // Step f+h: build components, detect missing/needsReview
        var confidence = "ok";

        string CellValue(CanonicalCell? cell, string label)
        {
            if (cell is null || string.IsNullOrWhiteSpace(cell.Value))
            {
                confidence = "needsReview";
                var blockId = cell?.Provenance?.BlockId ?? $"unknown-{label}";
                return $"[see {blockId}]";
            }
            return cell.Value;
        }

        var damageTypeVal  = CellValue(damageTypeCell, "damageType");
        var breathAreaVal  = CellValue(breathAreaCell, "breathArea");
        var saveAbilityVal = CellValue(saveAbilityCell, "saveAbility");

        var components = (IReadOnlyList<ResolvedComponent>)new List<ResolvedComponent>
        {
            new("damageType",  damageTypeVal,  damageTypeCell?.Provenance),
            new("breathArea",  breathAreaVal,  breathAreaCell?.Provenance),
            new("saveAbility", saveAbilityVal, saveAbilityCell?.Provenance),
            new("dice",        dice,           tierProv),
            // saveDC is COMPUTED (8 + proficiency bonus + CON modifier, via BreathWeaponRules) — it is
            // not sourced from any cell, so it carries no provenance rather than mis-citing the
            // saveAbility cell's source (COR-20).
            new("saveDC",      dc.ToString(),  null),
        };

        var value = $"{breathAreaVal} of {damageTypeVal}, {saveAbilityVal} save DC {dc}, {dice}";

        return new ResolvedFact("breath weapon", value, components, confidence);
    }
}
