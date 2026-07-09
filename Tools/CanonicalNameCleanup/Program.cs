using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

if (args.Length < 1)
{
    Console.Error.WriteLine(
        "Usage: CanonicalNameCleanup <canonical-slug> [canonicalDir] [fivetoolsDir]  (e.g. mm14)");
    return 1;
}

var slug = args[0];
var canonicalDir = args.Length > 1 ? args[1] : Path.Combine("books", "canonical");
var fivetoolsDir = args.Length > 2 ? args[2] : "5etools";
var canonicalPath = Path.Combine(canonicalDir, slug + ".json");

if (!File.Exists(canonicalPath))
{
    Console.Error.WriteLine($"Canonical file not found: {canonicalPath}");
    return 1;
}
if (!Directory.Exists(fivetoolsDir))
{
    Console.Error.WriteLine($"5etools directory not found: {fivetoolsDir}");
    return 1;
}

var matcher = new EntityNameMatcher(new EntityNameIndex(fivetoolsDir));
var loader = new CanonicalJsonLoader();
var writer = new CanonicalJsonWriter();

var file = await loader.LoadAsync(canonicalPath, CancellationToken.None);
var (entities, counts) = MonsterNameCleanup.Clean(file.Entities, matcher, slug);
await writer.WriteAsync(canonicalPath, file with { Entities = entities }, CancellationToken.None);

Console.WriteLine(
    $"cleaned: {counts.Cleaned}  deduped: {counts.Deduped}  " +
    $"groundedCollisionsFlagged: {counts.GroundedCollisionsFlagged}");
Console.WriteLine($"grounded: {counts.Grounded}  backfilled: {counts.Backfilled}");
return 0;