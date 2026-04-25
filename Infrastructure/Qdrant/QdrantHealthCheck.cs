using Microsoft.Extensions.Diagnostics.HealthChecks;
using Qdrant.Client;

namespace DndMcpAICsharpFun.Infrastructure.Qdrant;

public sealed class QdrantHealthCheck(QdrantClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await client.ListCollectionsAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Qdrant is unreachable", ex);
        }
    }
}
