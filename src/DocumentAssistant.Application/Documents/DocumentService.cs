using DocumentAssistant.Application.Abstractions;
using DocumentAssistant.Application.Common;
using DocumentAssistant.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentAssistant.Application.Documents;

public class DocumentService(
    IDocumentRepository repository,
    IFileStorage fileStorage,
    IOptions<DocumentUploadOptions> options,
    ILogger<DocumentService> logger)
{
    public const string PdfContentType = "application/pdf";
    public const int MaxPageSize = 100;

    // Every PDF starts with "%PDF-".
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();

    public static string StorageKeyFor(Guid documentId) => $"{documentId}.pdf";

    /// <summary>
    /// Validates the file, stores it and records a new document in the Uploaded state.
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
        return DocumentDto.FromEntity(document);
    }

    public async Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await repository.GetByIdAsync(id, cancellationToken);
        return document is null ? null : DocumentDto.FromEntity(document);
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
