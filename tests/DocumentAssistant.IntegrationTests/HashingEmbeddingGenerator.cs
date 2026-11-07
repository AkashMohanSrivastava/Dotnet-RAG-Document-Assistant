using System.Text.RegularExpressions;
using DocumentAssistant.Domain.Entities;
using Microsoft.Extensions.AI;

namespace DocumentAssistant.IntegrationTests;

/// <summary>
/// Offline stand-in for a real embedding model: each word is hashed into one of the vector's
/// dimensions ("bag of words"), then the vector is normalized. Texts sharing words get similar
/// vectors, which is enough to test similarity search without network calls or API costs.
/// </summary>
public sealed partial class HashingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var inputs = values.ToList();
        var embeddings = new GeneratedEmbeddings<Embedding<float>>(inputs.Select(text => new Embedding<float>(Embed(text))))
        {
            Usage = new UsageDetails { InputTokenCount = inputs.Sum(t => Word().Count(t)) }
        };
        return Task.FromResult(embeddings);
    }

    public static float[] Embed(string text)
    {
        var vector = new float[DocumentChunk.EmbeddingDimensions];
        foreach (Match word in Word().Matches(text.ToLowerInvariant()))
            vector[Fnv1a(word.Value) % (uint)vector.Length] += 1f;

        var length = MathF.Sqrt(vector.Sum(v => v * v));
        if (length > 0)
            for (var i = 0; i < vector.Length; i++)
                vector[i] /= length;
        return vector;
    }

    // Stable across runs, unlike string.GetHashCode.
    private static uint Fnv1a(string value)
    {
        var hash = 2166136261;
        foreach (var c in value)
            hash = (hash ^ c) * 16777619;
        return hash;
    }

    [GeneratedRegex(@"\w+")]
    private static partial Regex Word();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
