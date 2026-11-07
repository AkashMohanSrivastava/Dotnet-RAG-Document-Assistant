namespace DocumentAssistant.Application.Embeddings;

/// <summary>Settings from the "Embeddings" config section that the pipeline uses.</summary>
public class EmbeddingOptions
{
    public const string SectionName = "Embeddings";

    /// <summary>Chunks sent per request. OpenAI accepts up to 2048 inputs, but smaller batches fail cheaper.</summary>
    public int BatchSize { get; set; } = 64;

    /// <summary>
    /// Price per million input tokens, only used to log an estimated cost per document.
    /// text-embedding-3-small is $0.02; local models are free.
    /// </summary>
    public decimal CostPerMillionTokens { get; set; } = 0.02m;
}
