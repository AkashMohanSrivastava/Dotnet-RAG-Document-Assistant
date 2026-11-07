using DocumentAssistant.Application.Abstractions;
using DocumentAssistant.Domain.Entities;
using DocumentAssistant.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DocumentAssistant.Infrastructure.Persistence;

public class DocumentRepository(DocumentAssistantDbContext context) : IDocumentRepository
{
    public void Add(Document document) => context.Documents.Add(document);

    public void Remove(Document document) => context.Documents.Remove(document);

    public Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public Task<Document?> GetByIdWithChunksAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Documents.Include(d => d.Chunks).FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Document>> GetByStatusAsync(DocumentStatus status, CancellationToken cancellationToken = default) =>
        await context.Documents
            .Where(d => d.Status == status)
            .OrderBy(d => d.UploadedAt)
            .ToListAsync(cancellationToken);

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

    public async Task<IReadOnlyList<DocumentChunk>> GetChunksAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        await context.DocumentChunks
            .AsNoTracking()
            .Where(c => c.DocumentId == documentId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
