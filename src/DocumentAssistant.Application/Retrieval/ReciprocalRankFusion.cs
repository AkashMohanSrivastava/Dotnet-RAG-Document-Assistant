namespace DocumentAssistant.Application.Retrieval;

/// <summary>
/// Reciprocal Rank Fusion: merges ranked lists using only positions, not scores.
/// Each item scores Σ 1 / (k + rank) over the lists it appears in.
/// <para>
/// Why ranks? Cosine similarity (0..1) and ts_rank (unbounded) aren't on the same scale, so adding
/// scores would be meaningless. Ranks are comparable, and an item near the top of both lists wins.
/// k = 60 is the value from the original paper; it stops the very first place from dominating.
/// </para>
/// </summary>
public static class ReciprocalRankFusion
{
    public const int DefaultK = 60;

    public static IReadOnlyList<SearchResult> Fuse(
        IReadOnlyList<ChunkMatch> vectorMatches, IReadOnlyList<ChunkMatch> keywordMatches, int limit, int k = DefaultK)
    {
        var fused = new Dictionary<Guid, (ChunkMatch Match, double Score, int? VectorRank, int? KeywordRank)>();

        for (var i = 0; i < vectorMatches.Count; i++)
        {
            var match = vectorMatches[i];
            fused[match.ChunkId] = (match, 1.0 / (k + i + 1), i + 1, null);
        }

        for (var i = 0; i < keywordMatches.Count; i++)
        {
            var match = keywordMatches[i];
            var contribution = 1.0 / (k + i + 1);
            fused[match.ChunkId] = fused.TryGetValue(match.ChunkId, out var existing)
                ? existing with { Score = existing.Score + contribution, KeywordRank = i + 1 }
                : (match, contribution, null, i + 1);
        }

        return fused.Values
            .OrderByDescending(f => f.Score)
            .ThenBy(f => Math.Min(f.VectorRank ?? int.MaxValue, f.KeywordRank ?? int.MaxValue))
            .Take(limit)
            .Select(f => new SearchResult(
                f.Match.ChunkId, f.Match.DocumentId, f.Match.FileName, f.Match.ChunkIndex, f.Match.PageNumber,
                f.Score, f.VectorRank, f.KeywordRank, f.Match.Content))
            .ToList();
    }
}
