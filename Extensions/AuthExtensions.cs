using DndMcpAICsharpFun.Features.Auth;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

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
        services.AddScoped<IAuthorizationHandler, AdminAuthorizationHandler>();
        services.AddAuthorization(o => o.AddPolicy(AuthPolicies.Admin, p => p.AddRequirements(new AdminRequirement())));
        return services;
    }
}