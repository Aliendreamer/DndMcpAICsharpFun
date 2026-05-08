using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;

public sealed class FivetoolsVehicleMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.VehicleMount;
}
