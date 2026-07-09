using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;

public sealed class FivetoolsSpellMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.Spell;
}