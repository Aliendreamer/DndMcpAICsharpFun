using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DndMcpAICsharpFun.Infrastructure.Marker;

public sealed class MarkerHealthCheck : IHealthCheck, IDisposable
{
    private readonly HttpClient _http;

    public MarkerHealthCheck(IOptions<MarkerOptions> options)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(options.Value.Url),
            Timeout = TimeSpan.FromSeconds(5),
        };
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync("/health", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return HealthCheckResult.Unhealthy($"marker returned {(int)response.StatusCode}");

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            var modelsLoaded = doc.RootElement.TryGetProperty("models_loaded", out var ml)
                && ml.ValueKind == JsonValueKind.True;

            return modelsLoaded
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Degraded("marker is reachable but models are not loaded yet");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"marker unreachable: {ex.Message}", ex);
        }
    }

    public void Dispose() => _http.Dispose();
}
