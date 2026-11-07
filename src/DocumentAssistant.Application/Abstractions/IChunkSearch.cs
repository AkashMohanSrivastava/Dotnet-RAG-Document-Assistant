using DocumentAssistant.Application.Retrieval;

namespace DocumentAssistant.Application.Abstractions;

/// <summary>
/// Raw search over chunks of Ready documents, best match first. Optionally limited to one document.
/// </summary>
public interface IChunkSearch
{
    /// <summary>Nearest neighbours by cosine similarity (score 0..1, higher is closer).</summary>
    Task<IReadOnlyList<ChunkMatch>> VectorSearchAsync(
        float[] queryEmbedding, int limit, Guid? documentId, CancellationToken cancellationToken = default);

    /// <summary>Full-text match on words (score is PostgreSQL's ts_rank_cd).</summary>
    Task<IReadOnlyList<ChunkMatch>> KeywordSearchAsync(
        string query, int limit, Guid? documentId, CancellationToken cancellationToken = default);
}
