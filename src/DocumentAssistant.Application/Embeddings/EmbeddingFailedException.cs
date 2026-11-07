namespace DocumentAssistant.Application.Embeddings;

/// <summary>The embedding provider call failed. The message is stored as the document's failure reason.</summary>
public class EmbeddingFailedException(string message, Exception? innerException = null)
    : Exception(message, innerException);
