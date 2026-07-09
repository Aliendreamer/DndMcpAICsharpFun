using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;

public sealed class FivetoolsFeatMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.Feat;
}