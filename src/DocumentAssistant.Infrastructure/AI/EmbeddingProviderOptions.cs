namespace DocumentAssistant.Infrastructure.AI;

/// <summary>Which embedding provider to use. Bound from the "Embeddings" config section.</summary>
public class EmbeddingProviderOptions
{
    /// <summary>"OpenAI" or "Ollama".</summary>
    public string Provider { get; set; } = "OpenAI";

    /// <summary>e.g. "text-embedding-3-small" (OpenAI) or "nomic-embed-text" (Ollama).</summary>
    public string Model { get; set; } = "text-embedding-3-small";

    /// <summary>OpenAI only. Keep it in user-secrets, never in appsettings.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Ollama only.</summary>
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
}
