using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;

namespace DndMcpAICsharpFun.Features.Auth;

public static class AuthPolicies
{
    public const string Admin = "Admin";
}

public sealed class AdminRequirement : IAuthorizationRequirement;

/// <summary>
/// Succeeds only for users whose <c>IsAdmin</c> flag is currently true in the database. The flag is
/// read on every evaluation (never cached in a claim) so revoking admin takes effect immediately.
/// </summary>
public sealed class AdminAuthorizationHandler(UserRepository users) : AuthorizationHandler<AdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        var raw = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(raw, out var id)) return;
        if (await users.IsAdminAsync(id)) context.Succeed(requirement);
    }
}
