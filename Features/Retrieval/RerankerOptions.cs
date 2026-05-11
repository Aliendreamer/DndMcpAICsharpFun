namespace DndMcpAICsharpFun.Features.Retrieval;

public sealed class RerankerOptions
{
    public bool Enabled { get; init; } = true;
    public string ModelPath { get; init; } = "models";
    public string ModelUrl { get; init; } = "https://huggingface.co/cross-encoder/ms-marco-MiniLM-L-6-v2/resolve/main/onnx/model.onnx";
    public string VocabUrl { get; init; } = "https://huggingface.co/cross-encoder/ms-marco-MiniLM-L-6-v2/resolve/main/vocab.txt";
    public int TopK { get; init; } = 20;
    public int TopN { get; init; } = 5;
}
