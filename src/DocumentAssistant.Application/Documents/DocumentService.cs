using DocumentAssistant.Application.Abstractions;
using DocumentAssistant.Application.Common;
using DocumentAssistant.Application.Text;
using DocumentAssistant.Domain.Entities;
using DocumentAssistant.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentAssistant.Application.Documents;

public class DocumentService(
    IDocumentRepository repository,
    IFileStorage fileStorage,
    IPdfTextExtractor textExtractor,
    IDocumentProcessingQueue processingQueue,
    IOptions<DocumentUploadOptions> options,
    ILogger<DocumentService> logger)
{
    public const string PdfContentType = "application/pdf";
    public const int MaxPageSize = 100;

    // Every PDF starts with "%PDF-".
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();

    public static string StorageKeyFor(Guid documentId) => $"{documentId}.pdf";

    /// <summary>
    /// Validates the file, stores it, records a new document in the Uploaded state and queues it
    /// for background processing.
    /// </summary>
    /// <exception cref="ValidationFailedException">The file is empty, too large or not a PDF.</exception>
    public async Task<DocumentDto> UploadAsync(
        string fileName, long length, Stream content, CancellationToken cancellationToken = default)
    {
        var maxSize = options.Value.MaxFileSizeBytes;

        if (length <= 0)
            throw new ValidationFailedException("The file is empty.");
        if (length > maxSize)
            throw new ValidationFailedException($"The file exceeds the maximum size of {maxSize / (1024 * 1024)} MB.");

        // Browsers may send a full client path; keep only the file name.
        fileName = Path.GetFileName(fileName ?? string.Empty);
        if (!string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new ValidationFailedException("Only PDF files are supported.");

        // The extension and the client's Content-Type header can't be trusted; check the bytes.
        if (!content.CanSeek)
        {
            var buffered = new MemoryStream();
            await content.CopyToAsync(buffered, cancellationToken);
            buffered.Position = 0;
            content = buffered;
        }
        if (!await HasPdfSignatureAsync(content, cancellationToken))
            throw new ValidationFailedException("The file is not a valid PDF.");

        Document document;
        try
        {
            document = Document.Create(fileName, PdfContentType, length);
        }
        catch (ArgumentException ex)
        {
            throw new ValidationFailedException(ex.Message);
        }

        var storageKey = StorageKeyFor(document.Id);
        await fileStorage.SaveAsync(storageKey, content, cancellationToken);

        try
        {
            repository.Add(document);
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Don't leave a file behind that no document points to.
            await TryDeleteFileAsync(storageKey);
            throw;
        }

        logger.LogInformation("Uploaded document {DocumentId} ({FileName}, {Size} bytes)", document.Id, fileName, length);

        // If the app stops before the worker picks this up, startup recovery re-queues it.
        await processingQueue.EnqueueAsync(document.Id, cancellationToken);

        return DocumentDto.FromEntity(document);
    }

    /// <summary>
    /// Queues a document for (re)processing. Returns null if the document does not exist.
    /// </summary>
    /// <exception cref="ConflictException">The document is already processing or ready.</exception>
    public async Task<DocumentDto?> RequestProcessingAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await repository.GetByIdAsync(id, cancellationToken);
        if (document is null)
            return null;

        if (document.Status is not (DocumentStatus.Uploaded or DocumentStatus.Failed))
            throw new ConflictException($"The document cannot be processed while its status is {document.Status}.");

        await processingQueue.EnqueueAsync(document.Id, cancellationToken);
        return DocumentDto.FromEntity(document);
    }

    /// <summary>Returns null if the document does not exist.</summary>
    public async Task<IReadOnlyList<ChunkDto>?> GetChunksAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await repository.GetByIdAsync(id, cancellationToken) is null)
            return null;

        var chunks = await repository.GetChunksAsync(id, cancellationToken);
        return chunks.Select(ChunkDto.FromEntity).ToList();
    }

    public async Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await repository.GetByIdAsync(id, cancellationToken);
        return document is null ? null : DocumentDto.FromEntity(document);
    }

    /// <summary>
    /// Extracts the stored file's text on demand, without changing the document. Returns null if
    /// the document does not exist.
    /// </summary>
    /// <exception cref="TextExtractionException">The PDF can't be read or has no text.</exception>
    public async Task<DocumentTextDto?> ExtractTextAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await repository.GetByIdAsync(id, cancellationToken);
        if (document is null)
            return null;

        await using var pdf = await fileStorage.OpenReadAsync(StorageKeyFor(document.Id), cancellationToken);
        var text = await textExtractor.ExtractAsync(pdf, cancellationToken);

        return new DocumentTextDto(document.Id, document.FileName, text.Pages.Count, text.TotalCharacters, text.Pages);
    }

    public async Task<PagedResult<DocumentDto>> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var documents = await repository.ListAsync((page - 1) * pageSize, pageSize, cancellationToken);
        var total = await repository.CountAsync(cancellationToken);

        return new PagedResult<DocumentDto>(documents.Select(DocumentDto.FromEntity).ToList(), page, pageSize, total);
    }

    private static async Task<bool> HasPdfSignatureAsync(Stream content, CancellationToken cancellationToken)
    {
        var header = new byte[PdfSignature.Length];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        content.Position = 0;

        return read == header.Length && header.AsSpan().SequenceEqual(PdfSignature);
    }

    private async Task TryDeleteFileAsync(string storageKey)
    {
        try
        {
            await fileStorage.DeleteAsync(storageKey);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to clean up stored file {StorageKey}", storageKey);
        }
    }
}
