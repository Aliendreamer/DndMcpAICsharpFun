using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;

public sealed class FivetoolsDiseasePoisonMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.DiseasePoison;
}
