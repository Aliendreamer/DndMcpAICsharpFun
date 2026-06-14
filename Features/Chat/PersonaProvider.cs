using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Chat;

/// <summary>
/// Resolves and caches the active persona text from the configured personas directory.
/// Falls back to a built-in default when the configured persona file is absent or empty,
/// so chat never runs without a system prompt.
/// </summary>
public sealed class PersonaProvider
{
    /// <summary>Built-in fallback used when the persona file is missing or empty.</summary>
    public const string DefaultPersonaText =
        "You are a knowledgeable D&D 5e companion. Help the user with rules, lore, " +
        "character building, and encounter advice. Always base rules answers on the " +
        "official source books when possible, and clearly state when you are unsure.";

    private readonly ChatPersonaOptions _options;
    private string? _cachedText;

    public PersonaProvider(IOptions<ChatPersonaOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Returns the active persona text. The result is cached after the first call.
    /// </summary>
    public string GetPersonaText()
    {
        if (_cachedText is not null)
            return _cachedText;

        var path = Path.Combine(_options.PersonasDirectory, $"{_options.Persona}.md");
        if (File.Exists(path))
        {
            var text = File.ReadAllText(path).Trim();
            if (!string.IsNullOrEmpty(text))
            {
                _cachedText = text;
                return _cachedText;
            }
        }

        _cachedText = DefaultPersonaText;
        return _cachedText;
    }
}
