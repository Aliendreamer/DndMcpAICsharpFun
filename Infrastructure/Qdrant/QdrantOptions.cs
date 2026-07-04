using System.ComponentModel.DataAnnotations;

namespace DndMcpAICsharpFun.Infrastructure.Qdrant;

public sealed class QdrantOptions
{
    [Required]
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6334;
    public int VectorSize { get; set; } = 768;
    public string BlocksCollectionName { get; set; } = "dnd_blocks";
    public string EntitiesCollectionName { get; set; } = "dnd_entities";
    public float HybridAlpha { get; set; } = 0.5f;

    /// <summary>Scalar int8 quantization of the dense vectors (memory ~4x smaller, faster search;
    /// rescoring preserves recall). See the <c>qdrant-scalar-quantization</c> change.</summary>
    public QdrantQuantizationOptions Quantization { get; set; } = new();
}

public sealed class QdrantQuantizationOptions
{
    /// <summary>When false, collections are created/left without quantization (prior behaviour).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Keep the quantized vectors in RAM for fast traversal (originals stay for rescoring).</summary>
    public bool AlwaysRam { get; set; } = true;

    /// <summary>Quantile used to clip outliers when computing the int8 scale.</summary>
    public float Quantile { get; set; } = 0.99f;

    /// <summary>Rescore an oversampled candidate set against the original vectors to preserve recall.</summary>
    public bool Rescore { get; set; } = true;

    /// <summary>Candidate oversampling factor for rescoring (higher = better recall, slower).</summary>
    public double Oversampling { get; set; } = 2.0;
}
