using System.Text.Json;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Combat;

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
    public DbSet<Bm25TermStat> Bm25TermStats => Set<Bm25TermStat>();
    public DbSet<Bm25CorpusStat> Bm25CorpusStats => Set<Bm25CorpusStat>();
    public DbSet<Bm25BookStat> Bm25BookStats => Set<Bm25BookStat>();
    public DbSet<CampaignLogEntry> CampaignLogEntries => Set<CampaignLogEntry>();
    public DbSet<Combat> Combats => Set<Combat>();
    public DbSet<Combatant> Combatants => Set<Combatant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IngestionRecord>(e =>
        {
            e.HasIndex(r => r.FileHash);
            e.HasIndex(r => r.Status);
            e.Property(r => r.Status).HasConversion<string>();
            e.Property(r => r.BookType).HasConversion<string>();
            e.Property(r => r.FilePath).IsRequired().HasMaxLength(1024);
            e.Property(r => r.FileName).IsRequired().HasMaxLength(512);
            e.Property(r => r.FileHash).HasMaxLength(64);
            e.Property(r => r.Version).IsRequired().HasMaxLength(20);
            e.Property(r => r.DisplayName).IsRequired().HasMaxLength(200);
            e.Property(r => r.FivetoolsSourceKey).HasMaxLength(20);
        });

        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
        });

        modelBuilder.Entity<Campaign>(e =>
        {
            // FK-shaped column with no navigation property (plain record), so EF never auto-detects it
            // as a relationship/shadow-FK — the index must be declared explicitly (audit P3).
            e.HasIndex(c => c.UserId);
        });

        modelBuilder.Entity<Hero>(e =>
        {
            e.Ignore(h => h.LatestSnapshot);
            e.HasIndex(h => h.CampaignId);
        });

        modelBuilder.Entity<HeroSnapshot>(e =>
        {
            e.HasIndex(s => s.HeroId);

            // CharacterSheet is stored as a JSON string column (matches the legacy CharacterJson column).
            e.Property(s => s.Sheet)
                .HasColumnName("CharacterJson")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => DeserializeCharacterSheet(v));
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

        // The *Json columns below are read/written only as whole blobs (deserialized in the app), never
        // queried server-side, so plain `text` is the deliberate mapping (NET-12). If a future admin or
        // reporting path needs to query into the JSON, migrate these to `jsonb` and add a GIN index.
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

        // BM25 corpus-statistics store (COR-15). Bm25CorpusStat is a fixed singleton (Id = 1), so its
        // key is never auto-generated. Bm25BookStat holds each book's contribution keyed by FileHash,
        // and the global Bm25TermStat/Bm25CorpusStat aggregates are exactly the sum of those rows.
        modelBuilder.Entity<Bm25TermStat>(e =>
        {
            e.HasKey(t => t.Term);
            e.Property(t => t.Term).HasMaxLength(200);
        });

        modelBuilder.Entity<Bm25CorpusStat>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<Bm25BookStat>(e =>
        {
            e.HasKey(b => b.FileHash);
            e.Property(b => b.FileHash).HasMaxLength(64);
            e.Property(b => b.TermDfJson).HasColumnType("text");
        });

        modelBuilder.Entity<CampaignLogEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasConversion<string>();
            e.Property(x => x.PayloadJson).HasColumnType("text");
            e.Property(x => x.Label);
            e.HasIndex(x => new { x.CampaignId, x.UserId, x.CreatedAt });
        });

        modelBuilder.Entity<Combat>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Edition).HasConversion<string>();
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.Name).IsRequired();
            e.HasIndex(x => new { x.CampaignId, x.UserId, x.Status });

            // At most one Active combat per campaign, enforced at the DB (backstops StartAsync's
            // check-then-insert against a concurrent-start race). Status persists as a string via
            // HasConversion<string>(), so the partial-index filter matches the string literal.
            e.HasIndex(x => x.CampaignId)
                .IsUnique()
                .HasFilter("\"Status\" = 'Active'")
                .HasDatabaseName("IX_Combats_CampaignId_ActiveUnique");
        });

        modelBuilder.Entity<Combatant>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.ConditionsJson).HasColumnType("text");
            e.Ignore(x => x.Conditions);
            e.HasIndex(x => x.CombatId);
            e.HasOne<Combat>()
                .WithMany()
                .HasForeignKey(x => x.CombatId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    // Contains corrupted/truncated CharacterJson to the single affected row instead of throwing out of
    // EF's materialization — AttachLatestSnapshotsAsync batches ALL heroes' latest snapshots in one query,
    // so an unguarded throw here would take down an entire campaign's hero list for one bad row (audit P3,
    // persistence.md §7). A malformed row surfaces as an empty CharacterSheet rather than an exception.
    private static CharacterSheet DeserializeCharacterSheet(string v)
    {
        try
        {
            return JsonSerializer.Deserialize<CharacterSheet>(v, (JsonSerializerOptions?)null) ?? new CharacterSheet();
        }
        catch (JsonException)
        {
            return new CharacterSheet();
        }
    }
}