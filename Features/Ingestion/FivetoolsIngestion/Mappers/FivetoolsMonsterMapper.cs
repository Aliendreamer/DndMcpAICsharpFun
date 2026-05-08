using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;

public sealed class FivetoolsMonsterMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.Monster;
}
