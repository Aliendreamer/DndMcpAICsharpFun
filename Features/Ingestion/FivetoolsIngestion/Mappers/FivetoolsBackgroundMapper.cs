using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;

public sealed class FivetoolsBackgroundMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.Background;
}
