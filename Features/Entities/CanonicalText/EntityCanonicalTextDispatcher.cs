using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Entities;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public sealed class EntityCanonicalTextDispatcher
{
    private readonly MonsterCanonicalTextRenderer _monsterR = new();
    private readonly SpellCanonicalTextRenderer _spellR = new();
    private readonly CanonicalJsonLoader _loader = new();

    public string Render(EntityEnvelope envelope)
    {
        // Monster and Spell use dedicated renderers that build canonicalText from typed fields.
        // All other entity types use the author-provided canonicalText field unchanged
        // (already deterministic from LLM extraction or hand-written canonical JSON).
        return envelope.Type switch
        {
            EntityType.Monster => _monsterR.Render(envelope.Name, _loader.DeserialiseFields<MonsterFields>(envelope)),
            EntityType.Spell   => _spellR.Render(envelope.Name, _loader.DeserialiseFields<SpellFields>(envelope)),
            _ => envelope.CanonicalText,
        };
    }
}
