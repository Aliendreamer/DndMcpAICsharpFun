namespace DndMcpAICsharpFun.Domain;

public sealed record User(long Id, string Username, string PasswordHash, bool IsAdmin = false);
