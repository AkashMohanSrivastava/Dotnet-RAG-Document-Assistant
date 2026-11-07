using DocumentAssistant.Application.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DocumentAssistant.Infrastructure.Processing;

/// <summary>
/// Background service that processes queued documents one at a time.
/// Each document gets its own DI scope, so it gets a fresh DbContext.
/// </summary>
public class DocumentProcessingWorker(
    ChannelDocumentProcessingQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<DocumentProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DocumentProcessor>().RecoverInterruptedAsync(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            // E.g. the database isn't reachable yet. New uploads still work; leftovers wait for the next start.
            logger.LogError(ex, "Startup recovery of interrupted documents failed");
        }

        await foreach (var documentId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<DocumentProcessor>().ProcessAsync(documentId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The processor records failures on the document; this only catches things like a
                // lost database connection. Keep the worker alive for the next document.
                logger.LogError(ex, "Failed to process document {DocumentId}", documentId);
            }
        }
    }
}
