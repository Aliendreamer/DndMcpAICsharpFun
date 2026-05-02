using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public sealed class PdfPigBlockExtractor : IPdfBlockExtractor
{
    public IEnumerable<PdfBlock> ExtractBlocks(string filePath)
    {
        using var document = PdfDocument.Open(filePath);
        foreach (var page in document.GetPages())
            foreach (var block in ExtractFromPage(page))
                yield return block;
    }

    private static IEnumerable<PdfBlock> ExtractFromPage(Page page)
    {
        var words = page.GetWords().ToList();
        if (words.Count == 0) yield break;

        var textBlocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
        var ordered = UnsupervisedReadingOrderDetector.Instance.Get(textBlocks).ToList();

        var order = 0;
        foreach (var block in ordered)
        {
            var text = block.Text?.Trim();
            if (string.IsNullOrEmpty(text)) continue;
            yield return new PdfBlock(text, page.Number, order++, block.BoundingBox);
        }
    }
}
