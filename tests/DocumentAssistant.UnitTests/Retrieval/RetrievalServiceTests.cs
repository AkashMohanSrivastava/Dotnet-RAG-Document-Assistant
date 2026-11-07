using DocumentAssistant.Application.Abstractions;
using DocumentAssistant.Application.Common;
using DocumentAssistant.Application.Embeddings;
using DocumentAssistant.Application.Retrieval;
using DocumentAssistant.Domain.Entities;
using DocumentAssistant.UnitTests.Fakes;
using Microsoft.Extensions.Options;

namespace DocumentAssistant.UnitTests.Retrieval;

public class RetrievalServiceTests
{
    private readonly FakeChunkSearch _search = new();
    private readonly FakeEmbeddingGenerator _embeddings = new();
    private readonly RetrievalService _service;

    public RetrievalServiceTests()
    {
        _service = new RetrievalService(_search, _embeddings,
            Options.Create(new EmbeddingOptions { QueryPrefix = "search_query: " }));
    }

    [Fact]
    public async Task VectorMode_EmbedsQueryWithPrefix_AndOnlyRunsVectorSearch()
    {
        var response = await _service.SearchAsync("  clean the filter ", SearchMode.Vector, limit: 3);

        var call = Assert.Single(_embeddings.Calls);
        Assert.Equal(["search_query: clean the filter"], call.Inputs);
        Assert.Equal(DocumentChunk.EmbeddingDimensions, call.Options?.Dimensions);
        Assert.Equal([("vector", 3)], _search.Calls);
        Assert.Equal("clean the filter", response.Query);
        Assert.Equal([1, 2], response.Results.Select(r => r.VectorRank));
        Assert.All(response.Results, r => Assert.Null(r.KeywordRank));
    }

    [Fact]
    public async Task KeywordMode_DoesNotCallEmbeddingModel()
    {
        var response = await _service.SearchAsync("filter", SearchMode.Keyword);

        Assert.Empty(_embeddings.Calls);
        Assert.Equal([("keyword", RetrievalService.DefaultLimit)], _search.Calls);
        Assert.All(response.Results, r => Assert.Null(r.VectorRank));
    }

    [Fact]
    public async Task HybridMode_FetchesDeeperCandidateLists_AndFuses()
    {
        var response = await _service.SearchAsync("filter", SearchMode.Hybrid, limit: 2);

        Assert.Equal([("vector", 20), ("keyword", 20)], _search.Calls);
        Assert.Equal(2, response.Results.Count);
        // The fake returns the same "shared" chunk from both searches, so it wins the fusion.
        Assert.Equal("shared", response.Results[0].Content);
        Assert.NotNull(response.Results[0].VectorRank);
        Assert.NotNull(response.Results[0].KeywordRank);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(500, RetrievalService.MaxLimit)]
    public async Task Limit_IsClamped(int requested, int expected)
    {
        await _service.SearchAsync("filter", SearchMode.Keyword, requested);

        Assert.Equal(expected, _search.Calls.Single().Limit);
    }

    [Fact]
    public async Task DocumentFilter_IsPassedThrough()
    {
        var documentId = Guid.NewGuid();

        await _service.SearchAsync("filter", SearchMode.Hybrid, documentId: documentId);

        Assert.All(_search.DocumentIds, id => Assert.Equal(documentId, id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyQuery_IsRejected(string query)
    {
        await Assert.ThrowsAsync<ValidationFailedException>(() => _service.SearchAsync(query));
    }

    private sealed class FakeChunkSearch : IChunkSearch
    {
        private static readonly ChunkMatch Shared = Make("shared");

        public List<(string Method, int Limit)> Calls { get; } = [];
        public List<Guid?> DocumentIds { get; } = [];

        private static ChunkMatch Make(string content) =>
            new(Guid.NewGuid(), Guid.NewGuid(), "doc.pdf", 0, 1, content, 0.5);

        public Task<IReadOnlyList<ChunkMatch>> VectorSearchAsync(
            float[] queryEmbedding, int limit, Guid? documentId, CancellationToken cancellationToken = default)
        {
            Calls.Add(("vector", limit));
            DocumentIds.Add(documentId);
            return Task.FromResult<IReadOnlyList<ChunkMatch>>([Make("vector only"), Shared]);
        }

        public Task<IReadOnlyList<ChunkMatch>> KeywordSearchAsync(
            string query, int limit, Guid? documentId, CancellationToken cancellationToken = default)
        {
            Calls.Add(("keyword", limit));
            DocumentIds.Add(documentId);
            return Task.FromResult<IReadOnlyList<ChunkMatch>>([Make("keyword only"), Shared]);
        }
    }
}
