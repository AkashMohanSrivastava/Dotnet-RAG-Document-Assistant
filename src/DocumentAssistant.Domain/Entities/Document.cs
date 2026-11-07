using DocumentAssistant.Domain.Enums;

namespace DocumentAssistant.Domain.Entities;

/// <summary>
/// An uploaded file and the chunks extracted from it.
/// State changes go through methods so the status lifecycle cannot be bypassed:
/// Uploaded -> Processing -> Ready, or Processing -> Failed -> Processing (retry).
/// </summary>
public class Document
{
    public const int MaxFileNameLength = 255;
    public const int MaxContentTypeLength = 100;
    public const int MaxFailureReasonLength = 2000;

    private readonly List<DocumentChunk> _chunks = [];

    public Guid Id { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long FileSizeBytes { get; private set; }
    public DocumentStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    public IReadOnlyCollection<DocumentChunk> Chunks => _chunks.AsReadOnly();

    // Required by EF Core.
    private Document() { }

    public static Document Create(string fileName, string contentType, long fileSizeBytes)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name is required.", nameof(fileName));
        if (fileName.Length > MaxFileNameLength)
            throw new ArgumentException($"File name cannot exceed {MaxFileNameLength} characters.", nameof(fileName));
        if (string.IsNullOrWhiteSpace(contentType))
            throw new ArgumentException("Content type is required.", nameof(contentType));
        if (contentType.Length > MaxContentTypeLength)
            throw new ArgumentException($"Content type cannot exceed {MaxContentTypeLength} characters.", nameof(contentType));
        if (fileSizeBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(fileSizeBytes), "File size must be greater than zero.");

        return new Document
        {
            Id = Guid.NewGuid(),
            FileName = fileName.Trim(),
            ContentType = contentType.Trim(),
            FileSizeBytes = fileSizeBytes,
            Status = DocumentStatus.Uploaded,
            UploadedAt = UtcNow()
        };
    }

    /// <summary>
    /// Begins (or retries) processing. Any chunks from a previous failed attempt are discarded.
    /// </summary>
    public void StartProcessing()
    {
        if (Status is not (DocumentStatus.Uploaded or DocumentStatus.Failed))
            throw InvalidTransition(DocumentStatus.Processing);

        _chunks.Clear();
        FailureReason = null;
        Status = DocumentStatus.Processing;
    }

    public DocumentChunk AddChunk(int chunkIndex, string content, int? pageNumber = null)
    {
        if (Status != DocumentStatus.Processing)
            throw new InvalidOperationException("Chunks can only be added while the document is processing.");
        if (_chunks.Any(c => c.ChunkIndex == chunkIndex))
            throw new InvalidOperationException($"A chunk with index {chunkIndex} already exists.");

        var chunk = DocumentChunk.Create(Id, chunkIndex, content, pageNumber);
        _chunks.Add(chunk);
        return chunk;
    }

    public void MarkReady()
    {
        if (Status != DocumentStatus.Processing)
            throw InvalidTransition(DocumentStatus.Ready);
        if (_chunks.Count == 0)
            throw new InvalidOperationException("A document cannot be marked ready without any chunks.");

        Status = DocumentStatus.Ready;
        ProcessedAt = UtcNow();
    }

    public void MarkFailed(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A failure reason is required.", nameof(reason));
        if (Status != DocumentStatus.Processing)
            throw InvalidTransition(DocumentStatus.Failed);

        reason = reason.Trim();
        FailureReason = reason.Length > MaxFailureReasonLength ? reason[..MaxFailureReasonLength] : reason;
        Status = DocumentStatus.Failed;
        ProcessedAt = UtcNow();
    }

    // Rounded down to whole microseconds (what PostgreSQL stores), so a timestamp is the same
    // before and after a round trip to the database.
    private static DateTimeOffset UtcNow()
    {
        var now = DateTimeOffset.UtcNow;
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
    }

    private InvalidOperationException InvalidTransition(DocumentStatus target) =>
        new($"Cannot change document status from {Status} to {target}.");
}
