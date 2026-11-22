namespace DndMcpAICsharpFun.Domain.Entities;

/// <summary>
/// A writer's margin note printed on the entity's page (a Bigby or Diancastra aside, a Mordenkainen note), kept
/// beside the entity rather than in its <see cref="EntityEnvelope.CanonicalText"/>. <paramref name="By"/> is the
/// signature line ("Diancastra"), null when the note is unsigned.
/// </summary>
public sealed record EntityNote(string? By, string Text, int Page);
