using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace DndMcpAICompanion.Features.Campaign;

public sealed record Hero(long Id, long CampaignId, string Name, DateTime CreatedAt, HeroSnapshot? LatestSnapshot);
public sealed record HeroSnapshot(long Id, long HeroId, int SessionNumber, string SessionLabel, int Level, DateTime CreatedAt, CharacterSheet Sheet);
public sealed record HeroSnapshotMeta(long Id, long HeroId, int SessionNumber, string SessionLabel, int Level, DateTime CreatedAt);
public sealed record HeroWithCampaign(Hero Hero, string CampaignName);

public sealed class HeroRepository(string connectionString)
{
    public async Task<List<Hero>> GetByCampaignAsync(long campaignId)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT h.Id, h.CampaignId, h.Name, h.CreatedAt,
                   s.Id, s.SessionNumber, s.SessionLabel, s.Level, s.CreatedAt, s.CharacterJson
            FROM Heroes h
            LEFT JOIN HeroSnapshots s ON s.Id = (
                SELECT Id FROM HeroSnapshots WHERE HeroId = h.Id ORDER BY CreatedAt DESC LIMIT 1
            )
            WHERE h.CampaignId = @cid
            ORDER BY h.CreatedAt ASC
            """;
        cmd.Parameters.AddWithValue("@cid", campaignId);
        var results = new List<Hero>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(ReadHero(reader));
        return results;
    }

    public async Task<List<HeroWithCampaign>> GetAllByUserAsync(long userId)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT h.Id, h.CampaignId, h.Name, h.CreatedAt,
                   s.Id, s.SessionNumber, s.SessionLabel, s.Level, s.CreatedAt, s.CharacterJson,
                   c.Name as CampaignName
            FROM Heroes h
            JOIN Campaigns c ON c.Id = h.CampaignId
            LEFT JOIN HeroSnapshots s ON s.Id = (
                SELECT Id FROM HeroSnapshots WHERE HeroId = h.Id ORDER BY CreatedAt DESC LIMIT 1
            )
            WHERE c.UserId = @uid
            ORDER BY c.Name, h.Name
            """;
        cmd.Parameters.AddWithValue("@uid", userId);
        var results = new List<HeroWithCampaign>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var hero = ReadHero(reader);
            var campaignName = reader.GetString(10);
            results.Add(new HeroWithCampaign(hero, campaignName));
        }
        return results;
    }

    public async Task<Hero?> GetByIdAsync(long id)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT h.Id, h.CampaignId, h.Name, h.CreatedAt,
                   s.Id, s.SessionNumber, s.SessionLabel, s.Level, s.CreatedAt, s.CharacterJson
            FROM Heroes h
            LEFT JOIN HeroSnapshots s ON s.Id = (
                SELECT Id FROM HeroSnapshots WHERE HeroId = h.Id ORDER BY CreatedAt DESC LIMIT 1
            )
            WHERE h.Id = @id
            """;
        cmd.Parameters.AddWithValue("@id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return ReadHero(reader);
    }

    public async Task<List<HeroSnapshotMeta>> GetSnapshotsAsync(long heroId)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, HeroId, SessionNumber, SessionLabel, Level, CreatedAt FROM HeroSnapshots WHERE HeroId = @id ORDER BY CreatedAt DESC";
        cmd.Parameters.AddWithValue("@id", heroId);
        var results = new List<HeroSnapshotMeta>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(new HeroSnapshotMeta(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetString(3), reader.GetInt32(4), DateTime.Parse(reader.GetString(5))));
        return results;
    }

    public async Task<HeroSnapshot?> GetSnapshotAsync(long snapshotId)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, HeroId, SessionNumber, SessionLabel, Level, CreatedAt, CharacterJson FROM HeroSnapshots WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", snapshotId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new HeroSnapshot(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetString(3), reader.GetInt32(4), DateTime.Parse(reader.GetString(5)),
            JsonSerializer.Deserialize<CharacterSheet>(reader.GetString(6))!);
    }

    public async Task<long> CreateAsync(long campaignId, string name)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Heroes (CampaignId, Name, CreatedAt) VALUES (@c, @n, @t); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@c", campaignId);
        cmd.Parameters.AddWithValue("@n", name);
        cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O"));
        var heroId = (long)(await cmd.ExecuteScalarAsync())!;

        await using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "INSERT INTO HeroSnapshots (HeroId, SessionNumber, SessionLabel, Level, CreatedAt, CharacterJson) VALUES (@h, 0, 'Created', 0, @t, @j)";
        cmd2.Parameters.AddWithValue("@h", heroId);
        cmd2.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O"));
        cmd2.Parameters.AddWithValue("@j", JsonSerializer.Serialize(new CharacterSheet()));
        await cmd2.ExecuteNonQueryAsync();
        return heroId;
    }

    public async Task SaveSnapshotAsync(long heroId, int sessionNumber, string sessionLabel, CharacterSheet sheet)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO HeroSnapshots (HeroId, SessionNumber, SessionLabel, Level, CreatedAt, CharacterJson) VALUES (@h, @sn, @sl, @lv, @t, @j)";
        cmd.Parameters.AddWithValue("@h", heroId);
        cmd.Parameters.AddWithValue("@sn", sessionNumber);
        cmd.Parameters.AddWithValue("@sl", sessionLabel);
        cmd.Parameters.AddWithValue("@lv", sheet.Level);
        cmd.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@j", JsonSerializer.Serialize(sheet));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(long id)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM HeroSnapshots WHERE HeroId = @id; DELETE FROM Heroes WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    private static Hero ReadHero(SqliteDataReader reader)
    {
        HeroSnapshot? snapshot = null;
        if (!reader.IsDBNull(4))
        {
            snapshot = new HeroSnapshot(
                reader.GetInt64(4), reader.GetInt64(0),
                reader.GetInt32(5), reader.GetString(6), reader.GetInt32(7),
                DateTime.Parse(reader.GetString(8)),
                JsonSerializer.Deserialize<CharacterSheet>(reader.GetString(9))!);
        }
        return new Hero(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), DateTime.Parse(reader.GetString(3)), snapshot);
    }
}
