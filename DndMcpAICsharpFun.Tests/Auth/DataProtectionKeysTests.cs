using DndMcpAICsharpFun.Extensions;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Tests.Auth;

public sealed class DataProtectionKeysTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dp-keys-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private IDataProtector Protector()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DataProtection:KeysPath"] = _dir })
            .Build();
        var services = new ServiceCollection();
        services.AddDndDataProtection(config);
        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>().CreateProtector("login");
    }

    [Fact]
    public void A_cookie_protected_before_a_restart_still_opens_after_it()
    {
        // Two separate containers = two app starts sharing only the keys directory (the volume).
        var cookie = Protector().Protect("signed-in");

        Protector().Unprotect(cookie).Should().Be("signed-in");
        Directory.EnumerateFiles(_dir, "*.xml").Should().NotBeEmpty();
    }
}
