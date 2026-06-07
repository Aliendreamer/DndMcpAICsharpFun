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
            });
        services.AddAuthorization();
        return services;
    }
}
