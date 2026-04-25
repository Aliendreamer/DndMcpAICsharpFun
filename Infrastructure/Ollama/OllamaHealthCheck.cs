using Microsoft.Extensions.Diagnostics.HealthChecks;
using OllamaSharp;

namespace DndMcpAICsharpFun.Infrastructure.Ollama;

public sealed class OllamaHealthCheck(OllamaApiClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await client.ListLocalModelsAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Ollama is unreachable", ex);
        }
    }
}
