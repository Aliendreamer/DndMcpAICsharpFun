using DndMcpAICsharpFun.Features.Chat;

using FluentAssertions;

using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Tests.Chat;

public sealed class PersonaProviderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public PersonaProviderTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private PersonaProvider CreateProvider(string? persona = null, string? dir = null)
    {
        var opts = Options.Create(new ChatPersonaOptions
        {
            Persona = persona ?? "companion",
            PersonasDirectory = dir ?? _tempDir,
        });
        return new PersonaProvider(opts);
    }

    [Fact]
    public void GetPersonaText_returns_companion_text_when_companion_file_exists()
    {
        const string expected = "You are a companion persona.";
        File.WriteAllText(Path.Combine(_tempDir, "companion.md"), expected);
        var provider = CreateProvider();

        var text = provider.GetPersonaText();

        text.Should().Be(expected);
    }

    [Fact]
    public void GetPersonaText_loads_named_persona_file_when_configured()
    {
        const string expected = "You are a dungeon master persona.";
        File.WriteAllText(Path.Combine(_tempDir, "dm.md"), expected);
        var provider = CreateProvider(persona: "dm");

        var text = provider.GetPersonaText();

        text.Should().Be(expected);
    }

    [Fact]
    public void GetPersonaText_falls_back_to_default_when_file_is_missing()
    {
        // No files written — "missing-persona.md" does not exist
        var provider = CreateProvider(persona: "missing-persona");

        var text = provider.GetPersonaText();

        text.Should().Be(PersonaProvider.DefaultPersonaText);
    }

    [Fact]
    public void GetPersonaText_falls_back_to_default_when_file_is_empty()
    {
        File.WriteAllText(Path.Combine(_tempDir, "empty.md"), "   \n  ");
        var provider = CreateProvider(persona: "empty");

        var text = provider.GetPersonaText();

        text.Should().Be(PersonaProvider.DefaultPersonaText);
    }

    [Fact]
    public void GetPersonaText_returns_cached_value_on_second_call()
    {
        const string expected = "Cached persona text.";
        var filePath = Path.Combine(_tempDir, "companion.md");
        File.WriteAllText(filePath, expected);
        var provider = CreateProvider();

        var first = provider.GetPersonaText();
        // Overwrite the file after first read — cached value should still be returned
        File.WriteAllText(filePath, "Changed after caching.");
        var second = provider.GetPersonaText();

        first.Should().Be(expected);
        second.Should().Be(expected);
    }
}