using DocumentAssistant.Domain.Entities;
using DocumentAssistant.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace DocumentAssistant.IntegrationTests.Persistence;

[Collection(PostgresCollection.Name)]
public class DocumentPersistenceTests(PostgresFixture fixture)
{
    private const int Dimensions = DocumentChunk.EmbeddingDimensions;

    /// <summary>A unit vector pointing along a single axis, so similarity is easy to reason about.</summary>
    private static float[] AxisVector(int axis)
    {
        var vector = new float[Dimensions];
        vector[axis] = 1f;
        return vector;
    }

    [Fact]
    public async Task Document_WithChunksAndEmbeddings_RoundTrips()
    {
        var document = Document.Create("manual.pdf", "application/pdf", 4096);
        document.StartProcessing();
        document.AddChunk(0, "First chunk", pageNumber: 1).SetEmbedding(AxisVector(0));
        document.AddChunk(1, "Second chunk", pageNumber: 2);
        document.MarkReady();

        await using (var context = fixture.CreateContext())
        {
            context.Documents.Add(document);
            await context.SaveChangesAsync();
        }

        await using (var context = fixture.CreateContext())
        {
            var loaded = await context.Documents
                .Include(d => d.Chunks)
                .SingleAsync(d => d.Id == document.Id);

            Assert.Equal("manual.pdf", loaded.FileName);
            Assert.Equal(DocumentStatus.Ready, loaded.Status);
            Assert.NotNull(loaded.ProcessedAt);
            Assert.Equal(2, loaded.Chunks.Count);

            var first = loaded.Chunks.Single(c => c.ChunkIndex == 0);
            Assert.Equal(1, first.PageNumber);
            Assert.Equal(AxisVector(0), first.Embedding);
            Assert.False(loaded.Chunks.Single(c => c.ChunkIndex == 1).HasEmbedding);
        }
    }

    [Fact]
    public async Task RetryingFailedDocument_DeletesChunksFromPreviousAttempt()
    {
        var document = Document.Create("report.pdf", "application/pdf", 100);
        document.StartProcessing();
        document.AddChunk(0, "Partial");
        document.MarkFailed("Timeout");

        await using (var context = fixture.CreateContext())
        {
            context.Documents.Add(document);
            await context.SaveChangesAsync();
        }

        await using (var context = fixture.CreateContext())
        {
            var loaded = await context.Documents.Include(d => d.Chunks).SingleAsync(d => d.Id == document.Id);
            loaded.StartProcessing();
            await context.SaveChangesAsync();
        }

        await using (var context = fixture.CreateContext())
        {
            var remaining = await context.DocumentChunks.CountAsync(c => c.DocumentId == document.Id);
            var status = await context.Documents.Where(d => d.Id == document.Id).Select(d => d.Status).SingleAsync();

            Assert.Equal(0, remaining);
            Assert.Equal(DocumentStatus.Processing, status);
        }
    }

    [Fact]
    public async Task CosineDistanceQuery_ReturnsNearestChunkFirst()
    {
        var document = Document.Create("vectors.pdf", "application/pdf", 100);
        document.StartProcessing();
        document.AddChunk(0, "About axis 0").SetEmbedding(AxisVector(0));
        document.AddChunk(1, "About axis 1").SetEmbedding(AxisVector(1));
        document.MarkReady();

        await using (var context = fixture.CreateContext())
        {
            context.Documents.Add(document);
            await context.SaveChangesAsync();
        }

        // Query vector mostly along axis 1, slightly along axis 0.
        var query = new float[Dimensions];
        query[0] = 0.2f;
        query[1] = 0.9f;
        var queryVector = new Vector(query);

        await using (var context = fixture.CreateContext())
        {
            // <=> is pgvector's cosine distance operator (served by the HNSW index).
            var nearest = await context.DocumentChunks
                .FromSql($"SELECT * FROM document_chunks WHERE document_id = {document.Id} ORDER BY embedding <=> {queryVector}")
                .AsNoTracking()
                .ToListAsync();

            Assert.Equal(["About axis 1", "About axis 0"], nearest.Select(c => c.Content));
        }
    }
}
