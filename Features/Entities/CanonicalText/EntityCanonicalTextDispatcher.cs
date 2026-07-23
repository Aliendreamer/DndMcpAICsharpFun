using System.Text;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Entities;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public sealed class EntityCanonicalTextDispatcher(ILogger<EntityCanonicalTextDispatcher>? logger = null)
{
    private readonly ILogger<EntityCanonicalTextDispatcher> _logger = logger ?? NullLogger<EntityCanonicalTextDispatcher>.Instance;
    private readonly MonsterCanonicalTextRenderer _monsterR = new();
    private readonly SpellCanonicalTextRenderer _spellR = new();
    private readonly ClassCanonicalTextRenderer _classR = new();
    private readonly SubclassCanonicalTextRenderer _subclassR = new();
    private readonly ObjectCanonicalTextRenderer _objectR = new();
    private readonly CanonicalJsonLoader _loader = new();

    private readonly Dictionary<EntityType, ISimpleEntityRenderer> _simpleRenderers = new()
    {
        [EntityType.Race] = new RaceCanonicalTextRenderer(),
        [EntityType.Subrace] = new SubraceCanonicalTextRenderer(),
        [EntityType.Background] = new BackgroundCanonicalTextRenderer(),
        [EntityType.Feat] = new FeatCanonicalTextRenderer(),
        [EntityType.Item] = new ItemCanonicalTextRenderer(),
        [EntityType.MagicItem] = new MagicItemCanonicalTextRenderer(),
        [EntityType.Weapon] = new WeaponCanonicalTextRenderer(),
        [EntityType.Armor] = new ArmorCanonicalTextRenderer(),
        [EntityType.God] = new GodCanonicalTextRenderer(),
        [EntityType.Trap] = new TrapCanonicalTextRenderer(),
        [EntityType.Condition] = new ConditionCanonicalTextRenderer(),
        [EntityType.DiseasePoison] = new DiseasePoisonCanonicalTextRenderer(),
        [EntityType.VehicleMount] = new VehicleMountCanonicalTextRenderer(),
        [EntityType.Plane] = new PlaneCanonicalTextRenderer(),
        [EntityType.Faction] = new FactionCanonicalTextRenderer(),
        [EntityType.Location] = new LocationCanonicalTextRenderer(),
        [EntityType.Lore] = new LoreCanonicalTextRenderer(),
        [EntityType.Rule] = new RuleCanonicalTextRenderer(),
    };

    public string Render(EntityEnvelope envelope)
    {
        try
        {
            return envelope.Type switch
            {
                EntityType.Monster => _monsterR.Render(envelope.Name, _loader.DeserialiseFields<MonsterFields>(envelope)),
                EntityType.Spell => _spellR.Render(envelope.Name, _loader.DeserialiseFields<SpellFields>(envelope)),
                EntityType.Class => _classR.Render(envelope.Name, _loader.DeserialiseFields<ClassFields>(envelope)),
                EntityType.Subclass => _subclassR.Render(envelope.Name, _loader.DeserialiseFields<SubclassFields>(envelope)),
                EntityType.Object => _objectR.Render(envelope.Name, _loader.DeserialiseFields<ObjectFields>(envelope)),
                _ when _simpleRenderers.TryGetValue(envelope.Type, out var r) => r.Render(envelope.Name, envelope.Fields),
                _ => envelope.CanonicalText,
            };
        }
        catch (Exception ex)
        {
            // Invariant: Render() NEVER drops an entity. Both failure modes land here — the
            // UNGUARDED envelope.Fields.Deserialize<TFields>(JsonOptions) inside
            // CanonicalJsonLoader.DeserialiseFields<TFields> throwing a JsonException (or
            // CanonicalJsonSchemaException) at the type boundary BEFORE any renderer body runs
            // (e.g. a JSON null or wrong-kind value bound to a non-nullable value-type property —
            // the actual mechanism that dropped "Sacred Statue" at ingest), and any exception a
            // renderer body itself throws. EntityIngestionOrchestrator.IngestEntitiesAsync catches
            // around this call and SKIPS the entity on any exception — so Render must degrade to
            // a safe, non-empty, name-bearing canonicalText instead of ever rethrowing.
            _logger.LogWarning(ex, "Degraded render for {Id}: {Reason} — falling back to name-only canonical text", envelope.Id, ex.Message);
            return DegradedFallback(envelope);
        }
    }

    /// <summary>
    /// Best-effort minimal canonical text used when the typed/simple render path above throws.
    /// Always includes the entity name (never empty) plus type, and — only if it can be read
    /// without itself throwing — a first prose snippet from the raw Fields.
    /// </summary>
    private static string DegradedFallback(EntityEnvelope envelope)
    {
        var sb = new StringBuilder(envelope.Name);
        sb.Append(" (").Append(envelope.Type).Append(')');
        try
        {
            var firstEntry = RendererHelpers.FirstEntryText(envelope.Fields);
            if (!string.IsNullOrWhiteSpace(firstEntry))
                sb.Append(". ").Append(firstEntry);
        }
        catch
        {
            // Best-effort only — the fallback itself must never throw.
        }
        return sb.ToString();
    }
}