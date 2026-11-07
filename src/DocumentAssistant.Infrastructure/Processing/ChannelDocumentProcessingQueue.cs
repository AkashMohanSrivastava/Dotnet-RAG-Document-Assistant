using System.Threading.Channels;
using DocumentAssistant.Application.Abstractions;

namespace DocumentAssistant.Infrastructure.Processing;

/// <summary>
/// In-memory queue between the API and <see cref="DocumentProcessingWorker"/>.
/// Not durable: anything queued is lost on restart, which is why the worker re-queues
/// Uploaded documents at startup. A multi-instance deployment would swap this for a real
/// message queue behind the same interface.
/// </summary>
public class ChannelDocumentProcessingQueue : IDocumentProcessingQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(documentId, cancellationToken);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
