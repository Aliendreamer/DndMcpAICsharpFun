using System.Collections.Frozen;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>
/// Single shared EntityType -> IFivetoolsEntityMapper registry, used by both
/// <see cref="FivetoolsIngestionService"/> (Qdrant ingestion) and <see cref="FivetoolsRecordIndex"/>
/// (in-memory enrichment index) so the mapper set is defined exactly once. All mappers derive from
/// <see cref="FivetoolsMapperBase"/> and hold no per-instance mutable state, so sharing a single
/// static instance across both call sites is safe.
/// </summary>
internal static class FivetoolsMapperRegistry
{
    public static readonly IReadOnlyDictionary<EntityType, IFivetoolsEntityMapper> Mappers = new Dictionary<EntityType, IFivetoolsEntityMapper>
    {
        [EntityType.Class] = new FivetoolsClassMapper(),
        [EntityType.Subclass] = new FivetoolsSubclassMapper(),
        [EntityType.Spell] = new FivetoolsSpellMapper(),
        [EntityType.Monster] = new FivetoolsMonsterMapper(),
        [EntityType.Race] = new FivetoolsRaceMapper(),
        [EntityType.Subrace] = new FivetoolsSubraceMapper(),
        [EntityType.Background] = new FivetoolsBackgroundMapper(),
        [EntityType.Feat] = new FivetoolsFeatMapper(),
        [EntityType.Item] = new FivetoolsItemMapper(),
        [EntityType.MagicItem] = new FivetoolsMagicItemMapper(),
        [EntityType.Weapon] = new FivetoolsWeaponMapper(),
        [EntityType.Armor] = new FivetoolsArmorMapper(),
        [EntityType.God] = new FivetoolsGodMapper(),
        [EntityType.Trap] = new FivetoolsTrapMapper(),
        [EntityType.Condition] = new FivetoolsConditionMapper(),
        [EntityType.DiseasePoison] = new FivetoolsDiseasePoisonMapper(),
        [EntityType.VehicleMount] = new FivetoolsVehicleMapper(),
        [EntityType.Rule] = new FivetoolsRuleMapper(),
    }.ToFrozenDictionary();
}