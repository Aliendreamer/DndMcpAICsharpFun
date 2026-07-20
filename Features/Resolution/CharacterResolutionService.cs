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

        return await ResolveForSheetAsync(snapshot.Sheet, feature, ct);
    }

    /// <summary>
    /// User-scoped resolution: loads the snapshot only if it belongs to a campaign owned by
    /// <paramref name="userId"/>, throwing <see cref="UnauthorizedAccessException"/> otherwise.
    /// This is the security boundary for character-scoped resolution (SEC-08) — the ownership
    /// check is enforced here, server-side, so a caller can never resolve another user's snapshot.
    /// </summary>
    public async Task<ResolvedFact> ResolveForUserAsync(
        long heroSnapshotId, long userId, string feature, CancellationToken ct = default)
    {
        var snapshot = await heroes.GetSnapshotForUserAsync(heroSnapshotId, userId);
        if (snapshot is null)
            throw new UnauthorizedAccessException(
                "Hero snapshot not found or not owned by the caller.");
        return await ResolveForSheetAsync(snapshot.Sheet, feature, ct);
    }

    private Task<ResolvedFact> ResolveForSheetAsync(CharacterSheet sheet, string feature, CancellationToken ct)
    {
        if (feature.Equals("breath weapon", StringComparison.OrdinalIgnoreCase))
            return ResolveBreathWeaponAsync(sheet, ct);
        if (feature.Equals("spell slots", StringComparison.OrdinalIgnoreCase))
            return ResolveSpellSlotsAsync(sheet, ct);
        if (feature.Equals("spell save dc", StringComparison.OrdinalIgnoreCase))
            return ResolveSpellSaveDcAsync(sheet, ct);
        if (feature.Equals("spell attack", StringComparison.OrdinalIgnoreCase))
            return ResolveSpellAttackAsync(sheet, ct);
        if (feature.Equals("class features", StringComparison.OrdinalIgnoreCase))
            return ResolveClassFeaturesAsync(sheet, ct);

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
        var optionKey = ancestryValue[(lastColon + 1)..];

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

        var damageTypeCell = cellMap.GetValueOrDefault("damageType");
        var breathAreaCell = cellMap.GetValueOrDefault("breathArea");
        var saveAbilityCell = cellMap.GetValueOrDefault("saveAbility");

        // Step d: dice from tier table (best-effort provenance)
        var dice = BreathWeaponRules.DiceForLevel(sheet.Level);
        var tier = sheet.Level switch
        {
            <= 5 => 1,
            <= 10 => 2,
            <= 15 => 3,
            _ => 4,
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
                var tierCols = JsonSerializer.Deserialize<List<string>>(tierTable.ColumnsJson, JsonOpts) ?? [];
                var tierCells = JsonSerializer.Deserialize<List<CanonicalCell>>(tierRow.CellsJson, JsonOpts) ?? [];
                var tierCellMap = new Dictionary<string, CanonicalCell>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < tierCols.Count && i < tierCells.Count; i++)
                    tierCellMap[tierCols[i]] = tierCells[i];
                tierProv = tierCellMap.GetValueOrDefault("dice")?.Provenance;
            }
        }

        // Step e: conMod and save DC
        var conMod = CharacterSheet.Modifier(sheet.Constitution);
        var dc = BreathWeaponRules.SaveDc(sheet.Level, conMod);

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

        var damageTypeVal = CellValue(damageTypeCell, "damageType");
        var breathAreaVal = CellValue(breathAreaCell, "breathArea");
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

    private async Task<ResolvedFact> ResolveSpellSlotsAsync(CharacterSheet sheet, CancellationToken ct)
    {
        var source = MulticlassSpellcasting.ResolveSlotSource(sheet.Classes);
        var pact = MulticlassSpellcasting.WarlockPact(sheet.Classes);

        if (source.Kind == "none" && pact is null)
            return new ResolvedFact("spell slots", "no spellcasting", [], "needsReview");

        var components = new List<ResolvedComponent>();
        var rendered = new List<string>();

        if (source.Kind != "none" && source.Level >= 1)
        {
            var tableId = source.Kind switch
            {
                "half" => MulticlassSlotTableSeeder.HalfCasterTableId,
                "third" => MulticlassSlotTableSeeder.ThirdCasterTableId,
                _ => MulticlassSlotTableSeeder.TableId,
            };

            await using var db = await dbf.CreateDbContextAsync(ct);
            var table = await db.StructuredTables.FirstOrDefaultAsync(t => t.CanonicalId == tableId, ct);
            var row = table is null ? null : await db.StructuredTableRows
                .FirstOrDefaultAsync(r => r.TableId == table.Id && r.RowIndex == source.Level - 1, ct);

            if (table is null || row is null)
            {
                // Table/row missing: still surface the (table-independent) pact component if present.
                if (pact is not null)
                    return new ResolvedFact("spell slots",
                        $"pact {pact.SlotCount}@L{pact.SlotLevel}",
                        [new ResolvedComponent("pact magic", $"{pact.SlotCount} slots at level {pact.SlotLevel}", null)],
                        "needsReview");
                return new ResolvedFact("spell slots", "caster level " + source.Level, [], "needsReview");
            }

            var columns = JsonSerializer.Deserialize<List<string>>(table.ColumnsJson, JsonOpts) ?? [];
            var cells = JsonSerializer.Deserialize<List<CanonicalCell>>(row.CellsJson, JsonOpts) ?? [];
            for (var lvl = 1; lvl <= 9 && lvl < columns.Count && lvl < cells.Count; lvl++)
            {
                var count = cells[lvl].Value;
                if (count == "0" || string.IsNullOrWhiteSpace(count)) continue;
                components.Add(new ResolvedComponent($"level {lvl} slots", count, cells[lvl].Provenance));
                rendered.Add($"L{lvl}:{count}");
            }
        }

        if (pact is not null)
        {
            components.Add(new ResolvedComponent(
                "pact magic", $"{pact.SlotCount} slots at level {pact.SlotLevel}", null));
            rendered.Add($"pact {pact.SlotCount}@L{pact.SlotLevel}");
        }

        var value = rendered.Count > 0 ? string.Join(", ", rendered) : "no spellcasting";
        var confidence = components.Count > 0 ? "ok" : "needsReview";
        return new ResolvedFact("spell slots", value, components, confidence);
    }

    // Shared per-caster-class resolution for computed spellcasting facts (save DC, attack). Each caster
    // class contributes one component using its own spellcasting ability; computed values carry no
    // provenance (COR-20). No caster classes => needsReview.
    private static ResolvedFact PerCasterClass(
        CharacterSheet sheet, string feature, string labelSuffix,
        Func<int, int, int> valueFn, Func<int, string> render)
    {
        var pb = sheet.ProficiencyBonus;
        var components = new List<ResolvedComponent>();
        foreach (var c in sheet.Classes)
        {
            var ability = MulticlassSpellcasting.SpellcastingAbility(c.Class);
            if (ability is null) continue;
            var score = ability switch
            {
                "Strength" => sheet.Strength,
                "Dexterity" => sheet.Dexterity,
                "Constitution" => sheet.Constitution,
                "Intelligence" => sheet.Intelligence,
                "Wisdom" => sheet.Wisdom,
                "Charisma" => sheet.Charisma,
                _ => 10,
            };
            var v = valueFn(pb, CharacterSheet.Modifier(score));
            components.Add(new ResolvedComponent($"{c.Class} {labelSuffix}", render(v), null));
        }
        if (components.Count == 0)
            return new ResolvedFact(feature, "no spellcasting", [], "needsReview");
        var value = string.Join(", ", components.Select(x => $"{x.Label} {x.Value}"));
        return new ResolvedFact(feature, value, components, "ok");
    }

    private Task<ResolvedFact> ResolveSpellSaveDcAsync(CharacterSheet sheet, CancellationToken ct)
        => Task.FromResult(PerCasterClass(
            sheet, "spell save dc", "save DC", (pb, mod) => 8 + pb + mod, v => v.ToString()));

    private Task<ResolvedFact> ResolveSpellAttackAsync(CharacterSheet sheet, CancellationToken ct)
        => Task.FromResult(PerCasterClass(
            sheet, "spell attack", "spell attack", (pb, mod) => pb + mod, v => $"+{v}"));


    private async Task<ResolvedFact> ResolveClassFeaturesAsync(CharacterSheet sheet, CancellationToken ct)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        var components = new List<ResolvedComponent>();
        var rendered = new List<string>();
        var confidence = "ok";

        foreach (var c in sheet.Classes)
        {
            if (string.IsNullOrWhiteSpace(c.Class)) continue;
            var suffix = $".table.{EntityIdSlug.Slug(c.Class)}";
            var table = await db.StructuredTables.FirstOrDefaultAsync(t => t.CanonicalId.EndsWith(suffix), ct);
            if (table is null)
            {
                confidence = "needsReview";
                components.Add(new ResolvedComponent(c.Class, "[class table not found]", null));
                continue;
            }
            var rows = await db.StructuredTableRows
                .Where(r => r.TableId == table.Id && r.RowIndex < c.Level)
                .OrderBy(r => r.RowIndex).ToListAsync(ct);
            ProvenanceRef? prov = null;
            var byLevel = new List<string>();
            string prof = "";
            foreach (var r in rows)
            {
                var cells = JsonSerializer.Deserialize<List<CanonicalCell>>(r.CellsJson, JsonOpts) ?? [];
                prov ??= cells.Count > 0 ? cells[0].Provenance : null;
                var feats = cells.Count > 2 ? cells[2].Value : "";
                if (!string.IsNullOrWhiteSpace(feats)) byLevel.Add($"L{r.RowIndex + 1}: {feats}");
                if (r.RowIndex == c.Level - 1 && cells.Count > 1) prof = cells[1].Value;
            }
            var summary = byLevel.Count > 0 ? string.Join("; ", byLevel) : "no features";
            var val = string.IsNullOrWhiteSpace(prof) ? summary : $"{summary} (proficiency bonus {prof})";
            components.Add(new ResolvedComponent(c.Class, val, prov));
            rendered.Add($"{c.Class}: {val}");
        }

        if (components.Count == 0)
            return new ResolvedFact("class features", "no classes", [], "needsReview");
        return new ResolvedFact("class features", string.Join(" | ", rendered), components, confidence);
    }

    /// <summary>
    /// Deterministic multiclass-validity answer for a target class: prerequisite check + reduced
    /// proficiency subset. Pure (no DB) so it serves non-caster combos with zero spellcasting.
    /// </summary>
    public static ResolvedFact ResolveMulticlassValidity(CharacterSheet sheet, string targetClass)
    {
        var prereq = MulticlassRules.CanMulticlassInto(targetClass, sheet);
        var profs = MulticlassRules.MulticlassProficiencies(targetClass);
        var components = new List<ResolvedComponent>
        {
            new("prerequisite", prereq.Allowed ? "met" : prereq.Reason, null),
            new("proficiencies", string.Join(", ", profs), null),
        };
        var value = prereq.Allowed ? "allowed" : $"not allowed: {prereq.Reason}";
        return new ResolvedFact($"multiclass into {targetClass}", value, components, "ok");
    }

    /// <summary>User-scoped wrapper: enforces snapshot ownership (SEC-08) then runs the pure check.</summary>
    public async Task<ResolvedFact> ResolveMulticlassValidityForUserAsync(
        long heroSnapshotId, long userId, string targetClass, CancellationToken ct = default)
    {
        var snapshot = await heroes.GetSnapshotForUserAsync(heroSnapshotId, userId);
        if (snapshot is null)
            throw new UnauthorizedAccessException("Hero snapshot not found or not owned by the caller.");
        return ResolveMulticlassValidity(snapshot.Sheet, targetClass);
    }
}