using DocumentAssistant.Domain.Entities;

namespace DocumentAssistant.Application.Abstractions;

public interface IDocumentRepository
{
    void Add(Document document);

    /// <summary>Loads the document without its chunks.</summary>
    Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<Document>> ListAsync(int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
