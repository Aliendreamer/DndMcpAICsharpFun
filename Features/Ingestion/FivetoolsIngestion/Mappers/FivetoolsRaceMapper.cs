using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;

public sealed class FivetoolsRaceMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.Race;
}