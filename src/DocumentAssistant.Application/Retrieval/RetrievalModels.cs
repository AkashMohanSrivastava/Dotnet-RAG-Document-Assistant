namespace DocumentAssistant.Application.Retrieval;

public enum SearchMode
{
    /// <summary>By meaning: embeddings + cosine similarity. Finds paraphrases, misses exact codes and names.</summary>
    Vector,

    /// <summary>By words: PostgreSQL full-text search. Finds exact terms, misses synonyms.</summary>
    Keyword,

    /// <summary>Both, merged with Reciprocal Rank Fusion.</summary>
    Hybrid
}

/// <summary>A chunk returned by one search method, with that method's score.</summary>
public record ChunkMatch(
    Guid ChunkId,
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    int? PageNumber,
    string Content,
    double Score);

/// <summary>
/// A retrieved chunk. <see cref="VectorRank"/> and <see cref="KeywordRank"/> (1 = best) show where each
/// method placed it, which makes hybrid results easy to explain.
/// </summary>
public record SearchResult(
    Guid ChunkId,
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    int? PageNumber,
    double Score,
    int? VectorRank,
    int? KeywordRank,
    string Content);

public record SearchResponse(string Query, SearchMode Mode, IReadOnlyList<SearchResult> Results);
