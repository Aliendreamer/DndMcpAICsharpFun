using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;

public sealed class FivetoolsGodMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.God;
}