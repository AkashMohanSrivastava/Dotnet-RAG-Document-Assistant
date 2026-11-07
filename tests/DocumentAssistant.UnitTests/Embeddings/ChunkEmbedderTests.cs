using DocumentAssistant.Application.Documents;
using DocumentAssistant.Application.Embeddings;
using DocumentAssistant.Domain.Entities;
using DocumentAssistant.UnitTests.Fakes;
using Microsoft.Extensions.Options;

namespace DocumentAssistant.UnitTests.Embeddings;

public class ChunkEmbedderTests
{
    private readonly FakeEmbeddingGenerator _generator = new();

    private ChunkEmbedder Embedder(int batchSize = 64, decimal costPerMillion = 0.02m) =>
        new(_generator, Options.Create(new EmbeddingOptions { BatchSize = batchSize, CostPerMillionTokens = costPerMillion }));

    private static List<DocumentChunk> Chunks(int count)
    {
        var document = Document.Create("doc.pdf", DocumentService.PdfContentType, 10);
        document.StartProcessing();
        for (var i = 0; i < count; i++)
            document.AddChunk(i, new string('x', i + 1));
        return document.Chunks.ToList();
    }

    [Fact]
    public async Task EmbedsEveryChunk_MatchingEmbeddingsToChunksInOrder()
    {
        var chunks = Chunks(3);

        await Embedder().EmbedAsync(chunks);

        Assert.All(chunks, c => Assert.True(c.HasEmbedding));
        // The fake puts the input length in element 0, so each chunk got its own vector.
        Assert.Equal([1f, 2f, 3f], chunks.Select(c => c.Embedding![0]));
    }

    [Fact]
    public async Task SendsChunksInBatches()
    {
        await Embedder(batchSize: 4).EmbedAsync(Chunks(10));

        Assert.Equal([4, 4, 2], _generator.Calls.Select(c => c.Inputs.Count));
    }

    [Fact]
    public async Task RequestsTheDimensionsTheDatabaseExpects()
    {
        await Embedder().EmbedAsync(Chunks(1));

        Assert.Equal(DocumentChunk.EmbeddingDimensions, _generator.Calls.Single().Options?.Dimensions);
    }

    [Fact]
    public async Task ReportsTokensAndEstimatedCost()
    {
        // 10 chunks x 10 tokens = 100 tokens at $0.02 per million.
        var usage = await Embedder(batchSize: 3).EmbedAsync(Chunks(10));

        Assert.Equal(10, usage.ChunkCount);
        Assert.Equal(100, usage.InputTokens);
        Assert.Equal(0.000002m, usage.EstimatedCost);
    }

    [Fact]
    public async Task ProviderWithoutUsage_ReportsUnknownTokens()
    {
        _generator.ReportUsage = false;

        var usage = await Embedder().EmbedAsync(Chunks(2));

        Assert.Null(usage.InputTokens);
        Assert.Null(usage.EstimatedCost);
    }

    [Fact]
    public async Task WrongDimensionsFromProvider_Throws()
    {
        _generator.Dimensions = 1536; // e.g. a model that ignores the requested size

        await Assert.ThrowsAsync<ArgumentException>(() => Embedder().EmbedAsync(Chunks(1)));
    }
}
