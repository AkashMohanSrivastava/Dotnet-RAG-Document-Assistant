using DocumentAssistant.Domain.Entities;
using DocumentAssistant.Domain.Enums;

namespace DocumentAssistant.Application.Abstractions;

public interface IDocumentRepository
{
    void Add(Document document);

    /// <summary>Deletes the document; its chunks are deleted with it (cascade).</summary>
    void Remove(Document document);

    /// <summary>Loads the document without its chunks.</summary>
    Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Loads the document with its chunks, for changes that replace them.</summary>
    Task<Document?> GetByIdWithChunksAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Tracked documents (without chunks) in the given status, oldest first.</summary>
    Task<IReadOnlyList<Document>> GetByStatusAsync(DocumentStatus status, CancellationToken cancellationToken = default);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<Document>> ListAsync(int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>Read-only chunks of a document, in order.</summary>
    Task<IReadOnlyList<DocumentChunk>> GetChunksAsync(Guid documentId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
