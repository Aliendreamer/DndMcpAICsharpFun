using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;

public sealed class FivetoolsTrapMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.Trap;
}