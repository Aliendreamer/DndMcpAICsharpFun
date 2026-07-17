using DndMcpAICsharpFun.Features.Embedding;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Chat.Routing;

/// <summary>The embedding backstop: argmax-cosine group for a query vector, with the cosine as confidence.</summary>
public interface IExemplarIndex
{
    Task<(string? Group, double Confidence)> ClassifyAsync(float[] queryVector, CancellationToken ct);
}

/// <summary>
/// The embedding backstop for the query router: per-group exemplar centroids (mean of the group's
/// seed-phrase vectors, L2-normalized), computed once on first use and cached. Classify returns the
/// argmax-cosine group and that cosine as confidence. Registered as a singleton so the exemplars are
/// embedded exactly once for the process.
/// </summary>
public sealed class ExemplarIndex(IServiceScopeFactory scopeFactory, IOptions<QueryRouterOptions> options)
    : IExemplarIndex
{
    private readonly QueryRouterOptions _opts = options.Value;
    private readonly SemaphoreSlim _buildLock = new(1, 1);
    private (string Group, float[] Centroid)[]? _centroids;

    public async Task<(string? Group, double Confidence)> ClassifyAsync(float[] queryVector, CancellationToken ct)
    {
        await EnsureBuiltAsync(ct);
        if (_centroids is null || _centroids.Length == 0) return (null, 0);

        var q = Normalized(queryVector);
        string? best = null;
        var bestCos = double.NegativeInfinity;
        foreach (var (group, centroid) in _centroids)
        {
            var cos = Dot(q, centroid);
            if (cos > bestCos)
            {
                bestCos = cos;
                best = group;
            }
        }
        return (best, bestCos);
    }

    private async Task EnsureBuiltAsync(CancellationToken ct)
    {
        if (_centroids is not null) return;
        await _buildLock.WaitAsync(ct);
        try
        {
            if (_centroids is not null) return;
            // Resolve the scoped embedding service in a temporary scope: this index is a singleton
            // (centroids computed once for the process) and must not capture a scoped dependency.
            using var scope = scopeFactory.CreateScope();
            var embeddings = scope.ServiceProvider.GetRequiredService<IEmbeddingService>();
            var built = new List<(string, float[])>();
            foreach (var (group, phrases) in _opts.Exemplars)
            {
                if (phrases.Length == 0) continue;
                var vecs = await embeddings.EmbedAsync(phrases, ct);
                if (vecs.Count == 0) continue;
                built.Add((group, Normalized(Mean(vecs))));
            }
            _centroids = built.ToArray();
        }
        finally
        {
            _buildLock.Release();
        }
    }

    private static float[] Mean(IList<float[]> vecs)
    {
        var dim = vecs[0].Length;
        var sum = new float[dim];
        foreach (var v in vecs)
            for (var i = 0; i < dim && i < v.Length; i++)
                sum[i] += v[i];
        for (var i = 0; i < dim; i++) sum[i] /= vecs.Count;
        return sum;
    }

    private static float[] Normalized(float[] v)
    {
        double norm = 0;
        foreach (var x in v) norm += (double)x * x;
        norm = Math.Sqrt(norm);
        if (norm <= 0) return (float[])v.Clone();
        var outv = new float[v.Length];
        for (var i = 0; i < v.Length; i++) outv[i] = (float)(v[i] / norm);
        return outv;
    }

    private static double Dot(float[] a, float[] b)
    {
        double d = 0;
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++) d += (double)a[i] * b[i];
        return d;
    }
}
