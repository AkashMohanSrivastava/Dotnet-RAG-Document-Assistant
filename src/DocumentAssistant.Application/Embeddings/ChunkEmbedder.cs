using DocumentAssistant.Domain.Entities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace DocumentAssistant.Application.Embeddings;

/// <summary>Result of embedding a set of chunks. Token count is null if the provider doesn't report usage.</summary>
public record EmbeddingUsage(int ChunkCount, long? InputTokens, decimal? EstimatedCost);

/// <summary>
/// Generates embeddings for chunks through <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>,
/// the Microsoft.Extensions.AI abstraction, so this code doesn't know or care which provider
/// (OpenAI, Ollama, ...) is behind it.
/// </summary>
public class ChunkEmbedder(
    IEmbeddingGenerator<string, Embedding<float>> generator,
    IOptions<EmbeddingOptions> options)
{
    private static readonly EmbeddingGenerationOptions GenerationOptions = new()
    {
        Dimensions = DocumentChunk.EmbeddingDimensions
    };

    public async Task<EmbeddingUsage> EmbedAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        long? totalTokens = null;

        foreach (var batch in chunks.Chunk(Math.Max(1, options.Value.BatchSize)))
        {
            var embeddings = await generator.GenerateAsync(batch.Select(c => c.Content), GenerationOptions, cancellationToken);
            if (embeddings.Count != batch.Length)
                throw new InvalidOperationException($"Expected {batch.Length} embeddings but received {embeddings.Count}.");

            for (var i = 0; i < batch.Length; i++)
                batch[i].SetEmbedding(embeddings[i].Vector.ToArray()); // throws if the dimensions don't match

            if (embeddings.Usage?.InputTokenCount is { } tokens)
                totalTokens = (totalTokens ?? 0) + tokens;
        }

        var cost = totalTokens * options.Value.CostPerMillionTokens / 1_000_000m;
        return new EmbeddingUsage(chunks.Count, totalTokens, cost);
    }
}
