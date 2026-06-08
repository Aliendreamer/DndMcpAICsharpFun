namespace DndMcpAICsharpFun.Infrastructure.Postgres;

/// <summary>PostgreSQL connection settings, bound from the "Postgres" configuration section.</summary>
public sealed class PostgresOptions
{
    public string Host { get; set; } = "postgres";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "dnd";
    public string Username { get; set; } = "dnd";
    public string Password { get; set; } = "dnd";

    public string ConnectionString() =>
        $"Host={Host};Port={Port};Database={Database};Username={Username};Password={Password}";
}
