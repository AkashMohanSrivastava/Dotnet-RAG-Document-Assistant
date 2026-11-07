using DocumentAssistant.Application.Text;

namespace DocumentAssistant.Application.Abstractions;

public interface IPdfTextExtractor
{
    /// <summary>
    /// Extracts normalized text from every page, in page order.
    /// </summary>
    /// <exception cref="TextExtractionException">
    /// The PDF is corrupt, password-protected, or has no extractable text (e.g. scanned images).
    /// </exception>
    Task<ExtractedText> ExtractAsync(Stream pdf, CancellationToken cancellationToken = default);
}
