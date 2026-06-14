namespace DndMcpAICsharpFun.Features.Chat;

public sealed class ChatPersonaOptions
{
    /// <summary>
    /// Name of the active persona file (without extension). Corresponds to
    /// <c>&lt;PersonasDirectory&gt;/&lt;Persona&gt;.md</c>. Defaults to "companion".
    /// </summary>
    public string Persona { get; init; } = "companion";

    /// <summary>
    /// Directory (relative to the working directory) that contains the persona
    /// Markdown files. Defaults to "Config/personas".
    /// </summary>
    public string PersonasDirectory { get; init; } = "Config/personas";
}
