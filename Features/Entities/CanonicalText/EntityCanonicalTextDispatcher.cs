using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Entities;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public sealed class EntityCanonicalTextDispatcher
{
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
}