using Qdrant.Client.Grpc;

namespace DndMcpAICsharpFun.Infrastructure.Qdrant;

/// <summary>
/// Pure builders that map <see cref="QdrantQuantizationOptions"/> onto Qdrant gRPC types:
/// the scalar int8 <see cref="QuantizationConfig"/> for collection setup, and the
/// rescoring <see cref="SearchParams"/> for queries. Kept side-effect-free so the mapping is
/// unit-tested independently of the (coverage-excluded) client wiring.
/// </summary>
public static class QdrantQuantization
{
    /// <summary>The scalar int8 quantization config, or <c>null</c> when quantization is disabled.</summary>
    public static QuantizationConfig? ConfigFor(QdrantQuantizationOptions o) =>
        o.Enabled
            ? new QuantizationConfig
            {
                Scalar = new ScalarQuantization
                {
                    Type = QuantizationType.Int8,
                    Quantile = o.Quantile,
                    AlwaysRam = o.AlwaysRam,
                },
            }
            : null;

    /// <summary>The update-collection diff that adds scalar int8 quantization in place.</summary>
    public static QuantizationConfigDiff? DiffFor(QdrantQuantizationOptions o) =>
        o.Enabled
            ? new QuantizationConfigDiff
            {
                Scalar = new ScalarQuantization
                {
                    Type = QuantizationType.Int8,
                    Quantile = o.Quantile,
                    AlwaysRam = o.AlwaysRam,
                },
            }
            : null;

    /// <summary>Search params that rescore an oversampled candidate set against the original
    /// vectors, or <c>null</c> when quantization/rescoring is off (plain search).</summary>
    public static SearchParams? SearchParamsFor(QdrantQuantizationOptions o) =>
        o.Enabled && o.Rescore
            ? new SearchParams
            {
                Quantization = new QuantizationSearchParams
                {
                    Rescore = true,
                    Oversampling = o.Oversampling,
                },
            }
            : null;
}
