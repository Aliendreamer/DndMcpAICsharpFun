using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Entities;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public sealed class EntityCanonicalTextDispatcher
{
    private readonly ClassCanonicalTextRenderer _classR = new();
    private readonly MonsterCanonicalTextRenderer _monsterR = new();
    private readonly SpellCanonicalTextRenderer _spellR = new();
    private readonly CanonicalJsonLoader _loader = new();

    public string Render(EntityEnvelope envelope)
    {
        // For Plan 1 we only render Class/Monster/Spell. Other types embed their author-provided canonicalText
        // unchanged (it's already deterministic from the LLM/hand-written JSON).
        return envelope.Type switch
        {
            EntityType.Class   => _classR.Render(envelope.Name, _loader.DeserialiseFields<ClassFields>(envelope)),
            EntityType.Monster => _monsterR.Render(envelope.Name, _loader.DeserialiseFields<MonsterFields>(envelope)),
            EntityType.Spell   => _spellR.Render(envelope.Name, _loader.DeserialiseFields<SpellFields>(envelope)),
            _ => envelope.CanonicalText,
        };
    }
}
