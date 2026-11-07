using DocumentAssistant.Application.Abstractions;
using DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocumentAssistant.Infrastructure.Persistence;

public class DocumentRepository(DocumentAssistantDbContext context) : IDocumentRepository
{
    public void Add(Document document) => context.Documents.Add(document);

    public Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Document>> ListAsync(int skip, int take, CancellationToken cancellationToken = default) =>
        await context.Documents
            .AsNoTracking()
            .OrderByDescending(d => d.UploadedAt)
            .ThenBy(d => d.Id) // stable order when timestamps tie
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        context.Documents.CountAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
