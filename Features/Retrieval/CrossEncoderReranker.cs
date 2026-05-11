using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace DndMcpAICsharpFun.Features.Retrieval;

public sealed class CrossEncoderReranker(
    RerankerOptions opts,
    ILogger<CrossEncoderReranker> logger) : IDisposable
{
    private const int MaxTokens = 512;

    private InferenceSession? _session;
    private BertTokenizer? _tokenizer;
    private bool _enabled;

    public bool Enabled => _enabled;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (!opts.Enabled)
        {
            logger.LogInformation("Reranker disabled via configuration.");
            return;
        }

        var ready = await ModelDownloader.EnsureModelAsync(opts, logger, ct);
        if (!ready)
        {
            _enabled = false;
            return;
        }

        var modelPath = Path.Combine(opts.ModelPath, "model.onnx");
        var vocabPath = Path.Combine(opts.ModelPath, "vocab.txt");

        _session = new InferenceSession(modelPath);
        _tokenizer = BertTokenizer.Create(vocabPath, new BertOptions
        {
            LowerCaseBeforeTokenization = true,
            ApplyBasicTokenization = true
        });

        _enabled = true;
        logger.LogInformation("Reranker initialised from {Path}", modelPath);
    }

    public Task<float[]> RerankAsync(
        string query, IReadOnlyList<string> passages, CancellationToken ct)
    {
        if (!_enabled || _session is null || _tokenizer is null)
            return Task.FromResult(new float[passages.Count]);

        var scores = new float[passages.Count];
        for (var i = 0; i < passages.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            scores[i] = ScorePair(query, passages[i]);
        }
        return Task.FromResult(scores);
    }

    public IList<RetrievalResult> SelectTopN(
        IEnumerable<RetrievalResult> candidates, float[] scores, int topN)
    {
        return candidates
            .Zip(scores, static (c, s) => (Candidate: c, Score: s))
            .OrderByDescending(static t => t.Score)
            .Take(topN)
            .Select(static t => t.Candidate)
            .ToList();
    }

    public void Dispose() => _session?.Dispose();

    private float ScorePair(string query, string passage)
    {
        var queryIds = _tokenizer!.EncodeToIds(query, addSpecialTokens: false,
            considerPreTokenization: true, considerNormalization: true);
        var passageIds = _tokenizer.EncodeToIds(passage, addSpecialTokens: false,
            considerPreTokenization: true, considerNormalization: true);

        var combined = _tokenizer.BuildInputsWithSpecialTokens(queryIds, passageIds).ToArray();
        var typeIds = _tokenizer.CreateTokenTypeIdsFromSequences(queryIds, passageIds).ToArray();

        if (combined.Length > MaxTokens)
        {
            combined = combined[..MaxTokens];
            typeIds = typeIds[..MaxTokens];
        }

        var seq = combined.Length;
        var inputIds = new DenseTensor<long>(seq);
        var attentionMask = new DenseTensor<long>(seq);
        var tokenTypeIds = new DenseTensor<long>(seq);

        for (var j = 0; j < seq; j++)
        {
            inputIds[j] = combined[j];
            attentionMask[j] = 1;
            tokenTypeIds[j] = typeIds[j];
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids",
                inputIds.Reshape([1, seq])),
            NamedOnnxValue.CreateFromTensor("attention_mask",
                attentionMask.Reshape([1, seq])),
            NamedOnnxValue.CreateFromTensor("token_type_ids",
                tokenTypeIds.Reshape([1, seq]))
        };

        using var results = _session!.Run(inputs);
        return results.First().AsTensor<float>().First();
    }
}
