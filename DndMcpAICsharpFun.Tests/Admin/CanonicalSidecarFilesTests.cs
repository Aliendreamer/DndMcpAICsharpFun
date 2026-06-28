using DndMcpAICsharpFun.Features.Admin;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Admin;

public class CanonicalSidecarFilesTests
{
    [Theory]
    [InlineData("phb14.errors.json")]
    [InlineData("phb14.warnings.json")]
    [InlineData("phb14.declined.json")]          // the regression: declined.json must be excluded
    [InlineData("phb14.progress.json")]
    [InlineData("phb14.progress.errors.json")]
    [InlineData("/books/canonical/mm.declined.json")]
    public void Sidecar_files_are_recognised(string path) =>
        CanonicalSidecarFiles.IsSidecar(path).Should().BeTrue();

    [Theory]
    [InlineData("phb14.json")]
    [InlineData("/books/canonical/mm.json")]
    [InlineData("system-reference-document.json")]
    public void Canonical_entity_files_are_not_sidecars(string path) =>
        CanonicalSidecarFiles.IsSidecar(path).Should().BeFalse();
}
