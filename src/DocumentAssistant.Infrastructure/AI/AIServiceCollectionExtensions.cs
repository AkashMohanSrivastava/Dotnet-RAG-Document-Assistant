using DocumentAssistant.Domain.Entities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;
using OpenAI;

namespace DocumentAssistant.Infrastructure.AI;

public static class AIServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> for the configured provider.
    /// Everything else in the app depends only on that interface, so switching between OpenAI and a
    /// local Ollama model is a config change.
    /// </summary>
    public static IServiceCollection AddEmbeddingProvider(this IServiceCollection services, EmbeddingProviderOptions options)
    {
        services
            .AddEmbeddingGenerator(_ => CreateGenerator(options))
            .UseLogging(); // Microsoft.Extensions.AI middleware: logs calls at Debug/Trace level

        return services;
    }

    private static IEmbeddingGenerator<string, Embedding<float>> CreateGenerator(EmbeddingProviderOptions options) =>
        options.Provider.ToLowerInvariant() switch
        {
            "openai" when string.IsNullOrWhiteSpace(options.ApiKey) => new NotConfiguredEmbeddingGenerator(
                "No OpenAI API key is configured. Set Embeddings:ApiKey with dotnet user-secrets, then retry the document."),

            "openai" => new OpenAIClient(options.ApiKey)
                .GetEmbeddingClient(options.Model)
                .AsIEmbeddingGenerator(DocumentChunk.EmbeddingDimensions),

            // OllamaApiClient implements IEmbeddingGenerator directly.
            "ollama" => new OllamaApiClient(new Uri(options.OllamaEndpoint), options.Model),

            _ => throw new InvalidOperationException(
                $"Unknown embedding provider '{options.Provider}'. Use 'OpenAI' or 'Ollama'.")
        };

    /// <summary>
    /// Stands in when the provider isn't configured, so the app still starts and the document fails
    /// with a clear reason instead of the whole host crashing.
    /// </summary>
    private sealed class NotConfiguredEmbeddingGenerator(string message) : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(message);

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
