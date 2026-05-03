using Microsoft.EntityFrameworkCore;

namespace DndMcpAICsharpFun.Infrastructure.Sqlite;

public sealed class IngestionDbContext(DbContextOptions<IngestionDbContext> options) : DbContext(options)
{
    public DbSet<IngestionRecord> IngestionRecords => Set<IngestionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IngestionRecord>(e =>
        {
            e.HasIndex(r => r.FileHash);
            e.HasIndex(r => r.Status);
            e.Property(r => r.Status).HasConversion<string>();
            e.Property(r => r.BookType).HasConversion<string>();
        });
    }
}
