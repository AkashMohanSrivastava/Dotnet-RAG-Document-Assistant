using DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocumentAssistant.Infrastructure.Persistence;

public class DocumentAssistantDbContext(DbContextOptions<DocumentAssistantDbContext> options)
    : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DocumentAssistantDbContext).Assembly);
    }
}
