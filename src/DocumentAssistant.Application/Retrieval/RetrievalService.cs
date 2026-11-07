using DocumentAssistant.Application.Abstractions;
using DocumentAssistant.Application.Common;
using DocumentAssistant.Application.Embeddings;
using DocumentAssistant.Domain.Entities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace DocumentAssistant.Application.Retrieval;

/// <summary>
/// Finds the chunks most relevant to a question: the "R" in RAG.
/// </summary>
public class RetrievalService(
    IChunkSearch search,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IOptions<EmbeddingOptions> embeddingOptions)
{
    public const int DefaultLimit = 5;
    public const int MaxLimit = 20;

    // Hybrid search fetches a deeper candidate list from each method before fusing,
    // so a chunk ranked 8th by one method and 2nd by the other still gets considered.
    private const int HybridCandidates = 20;

    /// <exception cref="ValidationFailedException">The query is empty.</exception>
    public async Task<SearchResponse> SearchAsync(
        string query, SearchMode mode = SearchMode.Hybrid, int limit = DefaultLimit, Guid? documentId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ValidationFailedException("A search query is required.");

        query = query.Trim();
        limit = Math.Clamp(limit, 1, MaxLimit);

        var results = mode switch
        {
            SearchMode.Vector => ToResults(
                await search.VectorSearchAsync(await EmbedQueryAsync(query, cancellationToken), limit, documentId, cancellationToken),
                vector: true),

            SearchMode.Keyword => ToResults(
                await search.KeywordSearchAsync(query, limit, documentId, cancellationToken),
                vector: false),

            _ => ReciprocalRankFusion.Fuse(
                await search.VectorSearchAsync(await EmbedQueryAsync(query, cancellationToken),
                    Math.Max(limit, HybridCandidates), documentId, cancellationToken),
                await search.KeywordSearchAsync(query, Math.Max(limit, HybridCandidates), documentId, cancellationToken),
                limit)
        };

        return new SearchResponse(query, mode, results);
    }

    private async Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken)
    {
        // Same model and dimensions as the chunks, plus the model's query prefix if it uses one.
        var embeddings = await embeddingGenerator.GenerateAsync(
            [embeddingOptions.Value.QueryPrefix + query],
            new EmbeddingGenerationOptions { Dimensions = DocumentChunk.EmbeddingDimensions },
            cancellationToken);
        return embeddings[0].Vector.ToArray();
    }

    private static IReadOnlyList<SearchResult> ToResults(IReadOnlyList<ChunkMatch> matches, bool vector) =>
        matches.Select((m, i) => new SearchResult(
                m.ChunkId, m.DocumentId, m.FileName, m.ChunkIndex, m.PageNumber, m.Score,
                vector ? i + 1 : null, vector ? null : i + 1, m.Content))
            .ToList();
}
