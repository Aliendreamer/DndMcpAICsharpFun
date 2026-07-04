using DndMcpAICsharpFun.Infrastructure.Qdrant;
using FluentAssertions;
using Qdrant.Client.Grpc;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Infrastructure;

public sealed class QdrantQuantizationTests
{
    [Fact]
    public void ConfigFor_enabled_builds_scalar_int8_config()
    {
        var o = new QdrantQuantizationOptions { Enabled = true, Quantile = 0.99f, AlwaysRam = true };

        var config = QdrantQuantization.ConfigFor(o);

        config.Should().NotBeNull();
        config!.Scalar.Type.Should().Be(QuantizationType.Int8);
        config.Scalar.Quantile.Should().BeApproximately(0.99f, 1e-6f);
        config.Scalar.AlwaysRam.Should().BeTrue();
    }

    [Fact]
    public void ConfigFor_disabled_is_null_preserving_current_behaviour()
    {
        QdrantQuantization.ConfigFor(new QdrantQuantizationOptions { Enabled = false }).Should().BeNull();
    }

    [Fact]
    public void DiffFor_enabled_builds_scalar_int8_update_diff()
    {
        var diff = QdrantQuantization.DiffFor(new QdrantQuantizationOptions { Enabled = true, AlwaysRam = false, Quantile = 0.95f });

        diff.Should().NotBeNull();
        diff!.Scalar.Type.Should().Be(QuantizationType.Int8);
        diff.Scalar.AlwaysRam.Should().BeFalse();
        diff.Scalar.Quantile.Should().BeApproximately(0.95f, 1e-6f);
    }

    [Fact]
    public void SearchParamsFor_enabled_with_rescore_sets_rescore_and_oversampling()
    {
        var sp = QdrantQuantization.SearchParamsFor(new QdrantQuantizationOptions { Enabled = true, Rescore = true, Oversampling = 3.0 });

        sp.Should().NotBeNull();
        sp!.Quantization.Rescore.Should().BeTrue();
        sp.Quantization.Oversampling.Should().BeApproximately(3.0, 1e-9);
    }

    [Fact]
    public void SearchParamsFor_is_null_when_disabled_or_rescore_off()
    {
        QdrantQuantization.SearchParamsFor(new QdrantQuantizationOptions { Enabled = false }).Should().BeNull();
        QdrantQuantization.SearchParamsFor(new QdrantQuantizationOptions { Enabled = true, Rescore = false }).Should().BeNull();
    }
}
