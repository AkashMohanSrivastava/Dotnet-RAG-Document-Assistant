using DocumentAssistant.Domain.Entities;
using DocumentAssistant.Infrastructure.Retrieval;
using DocumentAssistant.IntegrationTests.Persistence;

namespace DocumentAssistant.IntegrationTests.Retrieval;

[Collection(PostgresCollection.Name)]
public class PgChunkSearchTests(PostgresFixture fixture)
{
    private static readonly string[] ManualChunks =
    [
        "Clean the air filter every three months with warm water.",
        "Error code E42 means the water tank is empty.",
        "The warranty covers manufacturing defects for two years."
    ];

    /// <summary>Saves a document with the given chunks, embedded with the offline hashing model.</summary>
    private async Task<Document> SaveDocumentAsync(string[] chunks, bool ready = true)
    {
        var document = Document.Create("manual.pdf", "application/pdf", 100);
        document.StartProcessing();
        for (var i = 0; i < chunks.Length; i++)
            document.AddChunk(i, chunks[i], pageNumber: i + 1).SetEmbedding(HashingEmbeddingGenerator.Embed(chunks[i]));
        if (ready)
            document.MarkReady();

        await using var context = fixture.CreateContext();
        context.Documents.Add(document);
        await context.SaveChangesAsync();
        return document;
    }

    private PgChunkSearch Search() => new(fixture.CreateContext());

    [Fact]
    public async Task VectorSearch_RanksMostSimilarChunkFirst_WithSimilarityScores()
    {
        var document = await SaveDocumentAsync(ManualChunks);

        var results = await Search().VectorSearchAsync(
            HashingEmbeddingGenerator.Embed("how often should I clean the filter"), 3, document.Id);

        Assert.Equal(3, results.Count);
        Assert.Equal(0, results[0].ChunkIndex);
        Assert.Equal(1, results[0].PageNumber);
        Assert.Equal("manual.pdf", results[0].FileName);
        Assert.InRange(results[0].Score, 0.0, 1.0);
        Assert.True(results[0].Score > results[1].Score);
    }

    [Fact]
    public async Task KeywordSearch_FindsExactTerms_WithStemming()
    {
        var document = await SaveDocumentAsync(ManualChunks);

        // "E42" is an exact code; "covered" should match "covers" via stemming.
        var code = await Search().KeywordSearchAsync("What does E42 mean?", 3, document.Id);
        var warranty = await Search().KeywordSearchAsync("what is covered", 3, document.Id);

        Assert.Equal(1, code[0].ChunkIndex);
        Assert.Equal(2, Assert.Single(warranty).ChunkIndex);
    }

    [Fact]
    public async Task KeywordSearch_MatchesAnyWord_RankingChunksWithMoreMatchesHigher()
    {
        var document = await SaveDocumentAsync(ManualChunks);

        // "water" appears in two chunks, "filter" in one; the chunk with both ranks first.
        var results = await Search().KeywordSearchAsync("water filter", 3, document.Id);

        Assert.Equal([0, 1], results.Select(r => r.ChunkIndex));
    }

    [Fact]
    public async Task KeywordSearch_OnlyStopWords_ReturnsNothing()
    {
        var document = await SaveDocumentAsync(ManualChunks);

        Assert.Empty(await Search().KeywordSearchAsync("what is the", 3, document.Id));
    }

    [Fact]
    public async Task DocumentsThatAreNotReady_AreExcluded()
    {
        var processing = await SaveDocumentAsync(["Zyxwv quokka unique text."], ready: false);

        Assert.Empty(await Search().KeywordSearchAsync("zyxwv quokka", 5, processing.Id));
        Assert.Empty(await Search().VectorSearchAsync(HashingEmbeddingGenerator.Embed("zyxwv quokka"), 5, processing.Id));
    }

    [Fact]
    public async Task WithoutDocumentFilter_SearchesAllReadyDocuments()
    {
        var first = await SaveDocumentAsync(["Plinth frobnicator alpha instructions."]);
        var second = await SaveDocumentAsync(["Plinth frobnicator beta instructions."]);

        var results = await Search().KeywordSearchAsync("plinth frobnicator", 10, documentId: null);

        Assert.Contains(results, r => r.DocumentId == first.Id);
        Assert.Contains(results, r => r.DocumentId == second.Id);
    }
}
