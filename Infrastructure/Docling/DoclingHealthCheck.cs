using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Infrastructure.Docling;

public sealed class DoclingHealthCheck : IHealthCheck, IDisposable
{
    private readonly HttpClient _http;

    public DoclingHealthCheck(IOptions<DoclingOptions> options)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(options.Value.BaseUrl),
            Timeout = TimeSpan.FromSeconds(5),
        };
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync("/health", cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"docling-serve returned {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"docling-serve unreachable: {ex.Message}", ex);
        }
    }

    public void Dispose() => _http.Dispose();
}
