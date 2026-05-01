using System.Diagnostics.CodeAnalysis;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Infrastructure;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Extensions;

[ExcludeFromCodeCoverage]
internal static class WebApplicationExtensions
{
    internal static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IngestionDbContext>();
        await db.Database.MigrateAsync();
        // WAL mode allows concurrent reads while the background worker holds a write lock.
        // The pragma is persistent — it only needs to run once after the DB is created.
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    }

    internal static void ValidateStartupConfiguration(this WebApplication app)
    {
        var adminOpts = app.Services.GetRequiredService<IOptions<AdminOptions>>().Value;
        if (string.IsNullOrWhiteSpace(adminOpts.ApiKey))
            throw new InvalidOperationException("Admin:ApiKey must be configured. Set the Admin__ApiKey environment variable.");
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
