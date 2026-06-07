using Microsoft.Data.Sqlite;

namespace DndMcpAICsharpFun.Features.Auth;

public sealed class UserRepository(string connectionString)
{
    public async Task InitializeAsync()
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Users (
                Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                Username     TEXT    NOT NULL UNIQUE,
                PasswordHash TEXT    NOT NULL
            )
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<User?> FindByUsernameAsync(string username)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Username, PasswordHash FROM Users WHERE Username = @u LIMIT 1";
        cmd.Parameters.AddWithValue("@u", username);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new User(reader.GetInt64(0), reader.GetString(1), reader.GetString(2));
    }

    public async Task<bool> ExistsAsync(string username)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM Users WHERE Username = @u";
        cmd.Parameters.AddWithValue("@u", username);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    public async Task<long> CreateAsync(string username, string passwordHash)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Users (Username, PasswordHash) VALUES (@u, @h) RETURNING Id";
        cmd.Parameters.AddWithValue("@u", username);
        cmd.Parameters.AddWithValue("@h", passwordHash);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}

public sealed record User(long Id, string Username, string PasswordHash);
