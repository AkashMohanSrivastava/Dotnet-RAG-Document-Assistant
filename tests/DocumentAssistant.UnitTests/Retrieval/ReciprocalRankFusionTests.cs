using DocumentAssistant.Application.Retrieval;

namespace DocumentAssistant.UnitTests.Retrieval;

public class ReciprocalRankFusionTests
{
    private static readonly Dictionary<string, Guid> Ids = [];

    private static ChunkMatch Match(string name, double score = 0) =>
        new(Ids.TryGetValue(name, out var id) ? id : Ids[name] = Guid.NewGuid(), Guid.Empty, "doc.pdf", 0, 1, name, score);

    private static List<ChunkMatch> List(params string[] names) => names.Select(n => Match(n)).ToList();

    [Fact]
    public void ChunkFoundByBothMethods_BeatsChunkRankedFirstByOnlyOne()
    {
        // "b" is 2nd in both lists; "a" and "c" are each 1st in only one list.
        var results = ReciprocalRankFusion.Fuse(List("a", "b"), List("c", "b"), limit: 10);

        Assert.Equal("b", results[0].Content);
    }

    [Fact]
    public void Score_IsSumOfReciprocalRanks()
    {
        var results = ReciprocalRankFusion.Fuse(List("a", "b"), List("b"), limit: 10, k: 60);

        var b = results.Single(r => r.Content == "b");
        Assert.Equal(1.0 / 62 + 1.0 / 61, b.Score, precision: 10);
        Assert.Equal(1.0 / 61, results.Single(r => r.Content == "a").Score, precision: 10);
    }

    [Fact]
    public void RecordsRankFromEachMethod()
    {
        var results = ReciprocalRankFusion.Fuse(List("a", "b"), List("b", "c"), limit: 10);

        var byName = results.ToDictionary(r => r.Content);
        Assert.Equal((2, 1), (byName["b"].VectorRank, byName["b"].KeywordRank));
        Assert.Equal((1, (int?)null), (byName["a"].VectorRank, byName["a"].KeywordRank));
        Assert.Equal(((int?)null, 2), (byName["c"].VectorRank, byName["c"].KeywordRank));
    }

    [Fact]
    public void IgnoresOriginalScores_OnlyRanksMatter()
    {
        // Keyword scores are huge compared to cosine similarities; fusion must not care.
        var vector = new List<ChunkMatch> { Match("a", 0.9), Match("b", 0.8) };
        var keyword = new List<ChunkMatch> { Match("b", 1000), Match("a", 999) };

        var results = ReciprocalRankFusion.Fuse(vector, keyword, limit: 10);

        // Both appear at ranks 1 and 2, so they tie; the tie-break is the best single rank.
        Assert.Equal(results[0].Score, results[1].Score, precision: 10);
    }

    [Fact]
    public void RespectsLimit_AndHandlesEmptyLists()
    {
        Assert.Equal(2, ReciprocalRankFusion.Fuse(List("a", "b", "c"), [], limit: 2).Count);
        Assert.Empty(ReciprocalRankFusion.Fuse([], [], limit: 5));
    }
}
