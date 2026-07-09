using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;

public sealed class FivetoolsClassMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.Class;
}