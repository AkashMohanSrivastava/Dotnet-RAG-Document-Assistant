namespace DocumentAssistant.Domain.Entities;

/// <summary>
/// A contiguous piece of text from a document, with its embedding once generated.
/// Created only through <see cref="Document.AddChunk"/> so it always belongs to a document.
/// </summary>
public class DocumentChunk
{
    /// <summary>
    /// Size of every embedding vector. 768 works for both OpenAI text-embedding-3-small (which can
    /// shorten its output on request) and common local models such as nomic-embed-text, so the
    /// provider can be switched without a schema change.
    /// </summary>
    public const int EmbeddingDimensions = 768;

    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public int ChunkIndex { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public int? PageNumber { get; private set; }

    /// <summary>
    /// Embedding vector. Kept as a plain float array so the domain has no dependency on
    /// pgvector; Infrastructure maps it to a vector column.
    /// </summary>
    public float[]? Embedding { get; private set; }

    public bool HasEmbedding => Embedding is { Length: > 0 };

    // Required by EF Core.
    private DocumentChunk() { }

    internal static DocumentChunk Create(Guid documentId, int chunkIndex, string content, int? pageNumber)
    {
        if (documentId == Guid.Empty)
            throw new ArgumentException("Document id is required.", nameof(documentId));
        if (chunkIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(chunkIndex), "Chunk index cannot be negative.");
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Chunk content is required.", nameof(content));
        if (pageNumber is < 1)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "Page number must be 1 or greater.");

        return new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            ChunkIndex = chunkIndex,
            Content = content,
            PageNumber = pageNumber
        };
    }

    public void SetEmbedding(float[] embedding)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        if (embedding.Length != EmbeddingDimensions)
            throw new ArgumentException(
                $"Embedding must have {EmbeddingDimensions} dimensions but has {embedding.Length}.", nameof(embedding));

        // Copy so callers can't mutate the stored vector afterwards.
        Embedding = (float[])embedding.Clone();
    }
}
