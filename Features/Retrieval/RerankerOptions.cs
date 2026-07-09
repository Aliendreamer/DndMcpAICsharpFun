namespace DndMcpAICsharpFun.Features.Retrieval;

public sealed class RerankerOptions
{
    public bool Enabled { get; init; } = true;
    public string ModelPath { get; init; } = "models";
    public string ModelUrl { get; init; } = "https://huggingface.co/cross-encoder/ms-marco-MiniLM-L-6-v2/resolve/main/onnx/model.onnx";
    public string VocabUrl { get; init; } = "https://huggingface.co/cross-encoder/ms-marco-MiniLM-L-6-v2/resolve/main/vocab.txt";
    public bool RerankBlocks { get; init; } = true;
    public bool RerankEntities { get; init; } = true;
    public int CandidatePoolSize { get; init; } = 20;
}