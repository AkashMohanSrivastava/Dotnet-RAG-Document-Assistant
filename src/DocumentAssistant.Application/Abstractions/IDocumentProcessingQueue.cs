namespace DocumentAssistant.Application.Abstractions;

/// <summary>
/// Hands documents to the background processing worker, so uploads return immediately.
/// </summary>
public interface IDocumentProcessingQueue
{
    ValueTask EnqueueAsync(Guid documentId, CancellationToken cancellationToken = default);
}
