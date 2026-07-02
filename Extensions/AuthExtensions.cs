using Microsoft.AspNetCore.Authentication.Cookies;

namespace DndMcpAICsharpFun.Extensions;

internal static class AuthExtensions
{
    internal static IServiceCollection AddDndAuthentication(this IServiceCollection services)
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
        services.AddAuthorization();
        return services;
    }
}
