using FluentAssertions;

using DndMcpAICsharpFun.Tools.ModelEval.Full;

namespace DndMcpAICsharpFun.Tests.ModelEval;

public class EvalReportTests
{
    internal static EvalReport Report(string model, DateTime ts, params CaseReport[] cases) =>
        new("full", model, ts, 42, new[] { "phb14" }, "hash", cases,
            cases.GroupBy(c => c.Category).ToDictionary(g => g.Key, g => g.Count(c => c.Passed)),
            cases.GroupBy(c => c.Category).ToDictionary(g => g.Key, g => g.Count()));

    [Theory]
    [InlineData("qwen3:8b", "qwen3_8b")]
    [InlineData("hf.co/A/b", "hf.co_a_b")]
    public void SanitizeModel_MapsUnsafeChars(string input, string expected) =>
        EvalReportStore.SanitizeModel(input).Should().Be(expected);

    [Fact]
    public void Write_ThenLatestBefore_FindsPrevious()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rep-{Guid.NewGuid():N}");
        try
        {
            var first = EvalReportStore.Write(Report("qwen3:8b", new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc)), dir);
            var second = EvalReportStore.Write(Report("qwen3:8b", new DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc)), dir);

            Path.GetFileName(first).Should().Be("20260101-100000-qwen3_8b-full.json");
            var prev = EvalReportStore.LatestBefore(dir, second);
            prev.Should().NotBeNull();
            prev!.UtcTimestamp.Should().Be(new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void LatestBefore_MissingDir_ReturnsNull() =>
        EvalReportStore.LatestBefore(Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}"), "x").Should().BeNull();
}
