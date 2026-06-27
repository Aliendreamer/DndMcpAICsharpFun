using DndMcpAICsharpFun.Features.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DndMcpAICsharpFun.Extensions;

internal static class AppExtensions
{
    internal static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        // Schema is created by EF migrations (MigrateDatabaseAsync). Seed a dev login for convenience.
        var userRepo = app.Services.GetRequiredService<UserRepository>();
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

    internal static WebApplication MapDndEndpoints(this WebApplication app)
    {
        app.MapGet("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        }).RequireRateLimiting("global");

        app.MapRazorComponents<DndMcpAICsharpFun.CompanionUI.Components.App>()
            .AddInteractiveServerRenderMode()
            .RequireRateLimiting("global");

        return app;
    }
}
