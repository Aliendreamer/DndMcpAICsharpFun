using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

namespace DndMcpAICsharpFun.Tools.ProjectTables;

/// <summary>
/// Thin CLI wrapper around <see cref="ProjectTablesRunner"/>: for each requested canonical book
/// (a slug or <c>--all</c>), replaces its <c>tables[]</c> from local 5etools data (official books
/// only) and reports a per-book summary line.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: ProjectTables <slug|--all> [canonicalDir] [fivetoolsDir]");
            return 1;
        }

        var canonicalDir = args.Length > 1 ? args[1] : Path.Combine("books", "canonical");
        var fivetoolsDir = args.Length > 2 ? args[2] : "5etools";
        var loader = new CanonicalJsonLoader();
        var writer = new CanonicalJsonWriter();

        var slugs = args[0] == "--all"
            ? Directory.EnumerateFiles(canonicalDir, "*.json")
                .Where(p => !p.Contains(".errors.") && !p.Contains(".declined.") && !p.Contains(".warnings.") && !p.Contains(".progress")
                            // dragonborn-slice.json is a hand-authored resolution TEST fixture (sourceBook "PHB"), not a corpus book.
                            && !Path.GetFileNameWithoutExtension(p)!.Equals("dragonborn-slice", StringComparison.Ordinal))
                .Select(p => Path.GetFileNameWithoutExtension(p)!).ToList()
            : new List<string> { args[0] };

        var failed = 0;
        foreach (var slug in slugs)
        {
            var path = Path.Combine(canonicalDir, slug + ".json");
            try
            {
                var r = await ProjectTablesRunner.RunOneAsync(path, fivetoolsDir, loader, writer, CancellationToken.None);
                Console.WriteLine(r.Skipped ? $"{slug}: SKIP ({r.SkipReason})" : $"{slug}: {r.TableCount} tables");
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"{slug}: FAIL {ex.Message}");
            }
        }

        return failed == 0 ? 0 : 1;
    }
}