using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;

public sealed class FivetoolsSubclassMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.Subclass;
}
