using System.Text.Json;
using DndMcpAICsharpFun.Domain;
using Microsoft.EntityFrameworkCore;

namespace DndMcpAICsharpFun.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for the application: ingestion records plus the
/// user-facing companion data (users, campaigns, heroes, snapshots, chat).
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<IngestionRecord> IngestionRecords => Set<IngestionRecord>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<Hero> Heroes => Set<Hero>();
    public DbSet<HeroSnapshot> HeroSnapshots => Set<HeroSnapshot>();
    public DbSet<ChatTurn> ChatTurns => Set<ChatTurn>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<StructuredTable> StructuredTables => Set<StructuredTable>();
    public DbSet<StructuredTableRow> StructuredTableRows => Set<StructuredTableRow>();
    public DbSet<ChoiceSetRow> ChoiceSetRows => Set<ChoiceSetRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IngestionRecord>(e =>
        {
            e.HasIndex(r => r.FileHash);
            e.HasIndex(r => r.Status);
            e.Property(r => r.Status).HasConversion<string>();
            e.Property(r => r.BookType).HasConversion<string>();
        });

        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
        });

        modelBuilder.Entity<Campaign>();

        modelBuilder.Entity<Hero>();

        modelBuilder.Entity<HeroSnapshot>(e =>
        {
            // CharacterSheet is stored as a JSON string column (matches the legacy CharacterJson column).
            e.Property(s => s.Sheet)
                .HasColumnName("CharacterJson")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<CharacterSheet>(v, (JsonSerializerOptions?)null)!);
        });

        modelBuilder.Entity<ChatTurn>(e =>
        {
            e.HasIndex(m => new { m.UserId, m.CampaignId, m.HeroId });
            e.HasIndex(m => m.CreatedAt);
        });

        modelBuilder.Entity<Note>(e =>
        {
            e.HasIndex(n => n.CampaignId);
        });

        modelBuilder.Entity<StructuredTable>(e =>
        {
            e.HasIndex(t => t.CanonicalId).IsUnique();
            e.Property(t => t.ColumnsJson).HasColumnType("text");
        });

        modelBuilder.Entity<StructuredTableRow>(e =>
        {
            e.HasIndex(r => new { r.TableId, r.RowIndex }).IsUnique();
            e.Property(r => r.CellsJson).HasColumnType("text");
        });

        modelBuilder.Entity<ChoiceSetRow>(e =>
        {
            e.HasIndex(c => c.CanonicalId).IsUnique();
            e.Property(c => c.OptionsJson).HasColumnType("text");
        });
    }
}
