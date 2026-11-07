using DocumentAssistant.Application.Abstractions;
using DocumentAssistant.Application.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace DocumentAssistant.Infrastructure.Extraction;

/// <summary>
/// Extracts text with PdfPig (pure .NET, Apache-2.0). Text-based PDFs only; scanned pages would need OCR.
/// </summary>
public class PdfPigTextExtractor : IPdfTextExtractor
{
    public async Task<ExtractedText> ExtractAsync(Stream pdf, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        // PdfPig reads synchronously and seeks around the file, so load it into memory first
        // (uploads are size-limited).
        using var buffer = new MemoryStream();
        await pdf.CopyToAsync(buffer, cancellationToken);

        var pages = new List<ExtractedPage>();
        try
        {
            using var document = PdfDocument.Open(buffer.ToArray(), new ParsingOptions { UseLenientParsing = true });

            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Content-order extraction inserts spaces and line breaks based on glyph positions,
                // unlike page.Text, which concatenates letters.
                var text = ContentOrderTextExtractor.GetText(page);
                pages.Add(new ExtractedPage(page.Number, TextNormalizer.Normalize(text)));
            }
        }
        catch (PdfDocumentEncryptedException ex)
        {
            throw new TextExtractionException("The PDF is password-protected.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new TextExtractionException("The PDF could not be read. It may be corrupt.", ex);
        }

        if (pages.Count == 0)
            throw new TextExtractionException("The PDF has no pages.");
        if (pages.All(p => p.Text.Length == 0))
            throw new TextExtractionException(
                "The PDF contains no extractable text. It may be a scanned document, which needs OCR (not supported yet).");

        return new ExtractedText(pages);
    }
}
