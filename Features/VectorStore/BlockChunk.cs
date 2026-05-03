using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.VectorStore;

public sealed record BlockChunk(string Text, BlockMetadata Metadata);

public sealed record BlockMetadata(
    string SourceBook,
    DndVersion Version,
    ContentCategory Category,
    string SectionTitle,
    int SectionStart,
    int SectionEnd,
    int PageNumber,
    int BlockOrder,
    int GlobalIndex,
    BookType BookType = BookType.Unknown);
