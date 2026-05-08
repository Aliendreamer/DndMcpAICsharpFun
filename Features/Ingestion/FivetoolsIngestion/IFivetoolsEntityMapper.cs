using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

public interface IFivetoolsEntityMapper
{
    EntityEnvelope? Map(JsonElement entry);
}
