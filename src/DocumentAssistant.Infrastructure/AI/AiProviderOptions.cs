namespace DocumentAssistant.Infrastructure.AI;

/// <summary>
/// Which AI provider and model to use. Bound separately from the "Embeddings" and "Chat" config
/// sections, so each can use a different provider.
/// </summary>
public class AiProviderOptions
{
    /// <summary>"OpenAI" or "Ollama".</summary>
    public string Provider { get; set; } = "OpenAI";

    /// <summary>e.g. "text-embedding-3-small" / "gpt-4o-mini" (OpenAI) or "nomic-embed-text" / "llama3.1:8b" (Ollama).</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>OpenAI only. Keep it in user-secrets, never in appsettings.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Ollama only.</summary>
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
}
