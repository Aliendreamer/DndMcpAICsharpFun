using DndMcpAICsharpFun.Features.Auth;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;

namespace DndMcpAICsharpFun.Extensions;

internal static class AuthExtensions
{
    /// <summary>
    /// Keeps the Data Protection keys (which encrypt the login cookie) in <c>DataProtection:KeysPath</c>.
    /// Without it the keys lived inside the container, so every rebuild minted new keys and logged everyone out.
    /// </summary>
    internal static IServiceCollection AddDndDataProtection(this IServiceCollection services, IConfiguration config)
    {
        var keysPath = config["DataProtection:KeysPath"];
        var builder = services.AddDataProtection().SetApplicationName("DndMcpAICsharpFun");
        if (!string.IsNullOrWhiteSpace(keysPath))
            builder.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        return services;
    }

    internal static IServiceCollection AddDndAuthentication(this IServiceCollection services, IConfiguration config)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.LoginPath = "/login";
                o.LogoutPath = "/logout";
                // Session cookie hardening: always Secure (TLS terminated at the reverse proxy —
                // pair with UseForwardedHeaders so the framework treats requests as HTTPS), and an
                // explicit SameSite to limit cross-site submission.
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.HttpOnly = true;
            });
        services.AddScoped<IAuthorizationHandler, AdminAuthorizationHandler>();
        services.AddDndDataProtection(config);
        services.AddAuthorization(o => o.AddPolicy(AuthPolicies.Admin, p => p.RequireAuthenticatedUser().AddRequirements(new AdminRequirement())));
        return services;
    }
}