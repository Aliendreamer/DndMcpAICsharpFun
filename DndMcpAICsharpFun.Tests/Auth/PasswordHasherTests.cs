using DndMcpAICsharpFun.Features.Auth;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Auth;

public sealed class PasswordHasherTests
{
    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        var hash = PasswordHasher.Hash("correcthorse");
        PasswordHasher.Verify("correcthorse", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hash = PasswordHasher.Hash("correcthorse");
        PasswordHasher.Verify("wrongpassword", hash).Should().BeFalse();
    }

    [Fact]
    public void Hash_SamePassword_ProducesDifferentHashes()
    {
        var hash1 = PasswordHasher.Hash("password123");
        var hash2 = PasswordHasher.Hash("password123");
        hash1.Should().NotBe(hash2);
    }


    [Theory]
    [InlineData("")]
    [InlineData("not-a-valid-hash")]
    [InlineData("garbage:with:colons")]
    [InlineData("!!notbase64!!:@@alsonot@@")]
    public void Verify_MalformedOrEmptyStoredHash_ReturnsFalseWithoutThrowing(string storedHash)
    {
        var act = () => PasswordHasher.Verify("anything", storedHash);
        act.Should().NotThrow();
        act().Should().BeFalse();
    }
}
