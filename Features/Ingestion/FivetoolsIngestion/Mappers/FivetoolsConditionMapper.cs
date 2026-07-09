using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;

public sealed class FivetoolsConditionMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.Condition;
}