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
    /// <summary>Pages with less text than this (page numbers, stray marks) don't count as having text.</summary>
    public const int MinCharactersPerTextPage = 20;

    /// <summary>
    /// The "mostly images" rule needs enough pages to be meaningful; in a 2-page document one cover
    /// page would already make it half empty.
    /// </summary>
    public const int MinPagesForImageCheck = 5;

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

        // A PDF whose pages are mostly images (scans, or exports such as similarity reports) has a little
        // text on a few pages and nothing elsewhere. Indexing it would answer from the scraps, so reject it.
        var textPages = pages.Count(p => p.Text.Length >= MinCharactersPerTextPage);
        if (pages.Count >= MinPagesForImageCheck && textPages * 2 < pages.Count)
            throw new TextExtractionException(
                $"Only {textPages} of {pages.Count} pages contain extractable text. This looks like a scanned or " +
                "image-based PDF, which needs OCR (not supported yet). Upload a PDF exported directly from the original document.");

        return new ExtractedText(pages);
    }
}
