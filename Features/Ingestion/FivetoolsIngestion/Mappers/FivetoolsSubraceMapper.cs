using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;

public sealed class FivetoolsSubraceMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.Subrace;
}
