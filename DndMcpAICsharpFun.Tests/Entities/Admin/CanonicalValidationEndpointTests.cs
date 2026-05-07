using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

public class CanonicalValidationEndpointTests
{
    [Fact]
    public async Task Empty_directory_returns_empty_report()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var svc = new CanonicalValidationService(
                new CanonicalJsonLoader(),
                new EntityReferenceResolver(),
                Options.Create(new EntityExtractionOptions { CanonicalDirectory = dir }),
                NullLogger<CanonicalValidationService>.Instance);

            var report = await svc.ValidateAsync(CancellationToken.None);
            report.FilesScanned.Should().Be(0);
            report.TotalEntities.Should().Be(0);
            report.Failures.Should().BeEmpty();
            report.Warnings.Should().BeEmpty();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Schema_version_mismatch_is_reported_as_failure()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(dir, "broken.json"),
                """{"schemaVersion":"99","book":{"sourceBook":"x","edition":"e","fileHash":"h","displayName":"x"},"entities":[]}""");

            var svc = new CanonicalValidationService(
                new CanonicalJsonLoader(),
                new EntityReferenceResolver(),
                Options.Create(new EntityExtractionOptions { CanonicalDirectory = dir }),
                NullLogger<CanonicalValidationService>.Instance);

            var report = await svc.ValidateAsync(CancellationToken.None);
            report.Failures.Should().ContainSingle(f => f.Kind == "schema_validation_failure" && f.File == "broken.json");
        }
        finally { Directory.Delete(dir, true); }
    }
}
