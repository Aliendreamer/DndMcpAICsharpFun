using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;

public sealed class FivetoolsRuleMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.Rule;
}
