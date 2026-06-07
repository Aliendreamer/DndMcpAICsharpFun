using Microsoft.Data.Sqlite;

using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Campaigns;




public sealed class CampaignRepository(string connectionString)
{
    public async Task InitializeAsync()
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Campaigns (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId      INTEGER NOT NULL,
                Name        TEXT    NOT NULL,
                Description TEXT    NOT NULL DEFAULT '',
                CreatedAt   TEXT    NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Heroes (
                Id         INTEGER PRIMARY KEY AUTOINCREMENT,
                CampaignId INTEGER NOT NULL,
                Name       TEXT    NOT NULL,
                CreatedAt  TEXT    NOT NULL
            );
            CREATE TABLE IF NOT EXISTS HeroSnapshots (
                Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                HeroId        INTEGER NOT NULL,
                SessionNumber INTEGER NOT NULL,
                SessionLabel  TEXT    NOT NULL DEFAULT '',
                Level         INTEGER NOT NULL DEFAULT 0,
                CreatedAt     TEXT    NOT NULL,
                CharacterJson TEXT    NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<CampaignSummary>> GetAllAsync(long userId)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT c.Id, c.UserId, c.Name, c.Description, c.CreatedAt,
                   (SELECT COUNT(*) FROM Heroes WHERE CampaignId = c.Id) AS HeroCount
            FROM Campaigns c
            WHERE c.UserId = @u
            ORDER BY c.CreatedAt DESC
            """;
        cmd.Parameters.AddWithValue("@u", userId);
        var results = new List<CampaignSummary>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(new CampaignSummary(
                reader.GetInt64(0), reader.GetInt64(1),
                reader.GetString(2), reader.GetString(3),
                DateTime.Parse(reader.GetString(4)), (int)reader.GetInt64(5)));
        return results;
    }

    public async Task<Campaign?> GetByIdAsync(long id, long userId)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, UserId, Name, Description, CreatedAt FROM Campaigns WHERE Id = @id AND UserId = @u LIMIT 1";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@u", userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new Campaign(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), DateTime.Parse(reader.GetString(4)));
    }

    public async Task<long> CreateAsync(long userId, string name, string description)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Campaigns (UserId, Name, Description, CreatedAt) VALUES (@u, @n, @d, @c); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@u", userId);
        cmd.Parameters.AddWithValue("@n", name);
        cmd.Parameters.AddWithValue("@d", description);
        cmd.Parameters.AddWithValue("@c", DateTime.UtcNow.ToString("O"));
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task DeleteAsync(long id, long userId)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM HeroSnapshots WHERE HeroId IN (SELECT Id FROM Heroes WHERE CampaignId = @id);
            DELETE FROM Heroes WHERE CampaignId = @id;
            DELETE FROM Campaigns WHERE Id = @id AND UserId = @u;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@u", userId);
        await cmd.ExecuteNonQueryAsync();
    }
}
