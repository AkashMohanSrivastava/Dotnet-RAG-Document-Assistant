using DocumentAssistant.Domain.Entities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;
using OpenAI;

namespace DocumentAssistant.Infrastructure.AI;

/// <summary>
/// Registers the Microsoft.Extensions.AI abstractions (<see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>
/// and <see cref="IChatClient"/>) for the configured providers. Everything else in the app depends only on
/// those interfaces, so switching between OpenAI and a local Ollama model is a config change.
/// </summary>
public static class AIServiceCollectionExtensions
{
    public static IServiceCollection AddEmbeddingProvider(this IServiceCollection services, AiProviderOptions options)
    {
        services
            .AddEmbeddingGenerator(_ => CreateEmbeddingGenerator(options))
            .UseLogging(); // Microsoft.Extensions.AI middleware: logs calls at Debug/Trace level

        return services;
    }

    public static IServiceCollection AddChatProvider(this IServiceCollection services, AiProviderOptions options)
    {
        services
            .AddChatClient(_ => CreateChatClient(options))
            .UseLogging();

        return services;
    }

    private static IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(AiProviderOptions options) =>
        options.Provider.ToLowerInvariant() switch
        {
            "openai" when string.IsNullOrWhiteSpace(options.ApiKey) => new NotConfiguredClient(MissingKeyMessage("Embeddings")),

            "openai" => new OpenAIClient(options.ApiKey)
                .GetEmbeddingClient(options.Model)
                .AsIEmbeddingGenerator(DocumentChunk.EmbeddingDimensions),

            // OllamaApiClient implements both IEmbeddingGenerator and IChatClient.
            "ollama" => new OllamaApiClient(new Uri(options.OllamaEndpoint), options.Model),

            _ => throw UnknownProvider(options)
        };

    private static IChatClient CreateChatClient(AiProviderOptions options) =>
        options.Provider.ToLowerInvariant() switch
        {
            "openai" when string.IsNullOrWhiteSpace(options.ApiKey) => new NotConfiguredClient(MissingKeyMessage("Chat")),
            "openai" => new OpenAIClient(options.ApiKey).GetChatClient(options.Model).AsIChatClient(),
            "ollama" => new OllamaApiClient(new Uri(options.OllamaEndpoint), options.Model),
            _ => throw UnknownProvider(options)
        };

    private static string MissingKeyMessage(string section) =>
        $"No OpenAI API key is configured. Set {section}:ApiKey with dotnet user-secrets, or switch {section}:Provider to Ollama.";

    private static InvalidOperationException UnknownProvider(AiProviderOptions options) =>
        new($"Unknown AI provider '{options.Provider}'. Use 'OpenAI' or 'Ollama'.");

    /// <summary>
    /// Stands in when a provider isn't configured, so the app still starts and the failing operation
    /// reports a clear reason instead of the whole host crashing.
    /// </summary>
    private sealed class NotConfiguredClient(string message) : IEmbeddingGenerator<string, Embedding<float>>, IChatClient
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(message);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(message);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(message);

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
