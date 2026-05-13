using DndMcpAICompanion.Features.Auth;
using Microsoft.AspNetCore.Authentication;
using DndMcpAICompanion.Features.Campaign;
using Microsoft.AspNetCore.Authentication.Cookies;
using ModelContextProtocol.Client;
using AppMcpClientOptions = DndMcpAICompanion.Features.Chat.McpClientOptions;

namespace DndMcpAICompanion.Extensions;

internal static class AppExtensions
{
    internal static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        var userRepo = app.Services.GetRequiredService<UserRepository>();
        await userRepo.InitializeAsync();

        var campaignRepo = app.Services.GetRequiredService<CampaignRepository>();
        await campaignRepo.InitializeAsync();

        if (!await userRepo.ExistsAsync("test"))
            await userRepo.CreateAsync("test", PasswordHasher.Hash("test"));
    }

    internal static WebApplication UseDndMiddleware(this WebApplication app)
    {
        app.UseStaticFiles();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        return app;
    }

    internal static WebApplication MapDndEndpoints(this WebApplication app, AppMcpClientOptions mcpOpts, McpClient mcpClient)
    {
        if (string.IsNullOrWhiteSpace(mcpOpts.ApiKey))
            app.Logger.LogWarning("Mcp:ApiKey is not configured — MCP requests will be sent without authentication and will likely be rejected by the server.");

        app.Lifetime.ApplicationStopping.Register(() => mcpClient.DisposeAsync().AsTask().GetAwaiter().GetResult());

        app.MapGet("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        }).RequireRateLimiting("global");

        app.MapRazorComponents<DndMcpAICompanion.Components.App>()
            .AddInteractiveServerRenderMode()
            .RequireRateLimiting("global");

        return app;
    }
}
