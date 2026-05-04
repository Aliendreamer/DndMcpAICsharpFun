using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DndMcpAICsharpFun.Infrastructure.Sqlite;

internal sealed class IngestionDbContextDesignTimeFactory : IDesignTimeDbContextFactory<IngestionDbContext>
{
    public IngestionDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IngestionDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;
        return new IngestionDbContext(options);
    }
}
