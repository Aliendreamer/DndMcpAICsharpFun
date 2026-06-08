using System.Diagnostics.CodeAnalysis;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Mcp;
using DndMcpAICsharpFun.Infrastructure;
using DndMcpAICsharpFun.Infrastructure.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using DndMcpAICsharpFun.Infrastructure.Persistence;

namespace DndMcpAICsharpFun.Extensions;

[ExcludeFromCodeCoverage]
internal static class WebApplicationExtensions
{
    internal static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    internal static void ValidateStartupConfiguration(this WebApplication app)
    {
        var adminOpts = app.Services.GetRequiredService<IOptions<AdminOptions>>().Value;
        if (string.IsNullOrWhiteSpace(adminOpts.ApiKey))
            throw new InvalidOperationException("Admin:ApiKey must be configured. Set the Admin__ApiKey environment variable.");

        var mcpOpts = app.Services.GetRequiredService<IOptions<McpOptions>>().Value;
        if (string.IsNullOrWhiteSpace(mcpOpts.ApiKey))
            app.Logger.LogWarning("Mcp:ApiKey is not configured — all /mcp requests will return 401. Set Mcp__ApiKey to enable the MCP endpoint.");
    }

    internal static void MapAdminMiddleware(this WebApplication app)
    {
        app.UseWhen(
            static ctx => ctx.Request.Path.StartsWithSegments("/admin"),
            static adminApp => adminApp.UseMiddleware<AdminApiKeyMiddleware>()
        );
    }

    internal static void MapObservabilityEndpoints(this WebApplication app)
    {
        var otelOptions = app.Configuration.GetSection("OpenTelemetry").Get<OpenTelemetryOptions>() ?? new OpenTelemetryOptions();
        if (otelOptions.Enabled)
            app.MapPrometheusScrapingEndpoint();
    }
}
