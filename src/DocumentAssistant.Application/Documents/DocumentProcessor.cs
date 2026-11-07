using DocumentAssistant.Application.Abstractions;
using DocumentAssistant.Application.Text;
using DocumentAssistant.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DocumentAssistant.Application.Documents;

/// <summary>
/// The ingestion pipeline for one document: extract text → chunk → store chunks.
/// Embedding generation will slot in before the document is marked ready (Phase 7).
/// </summary>
public class DocumentProcessor(
    IDocumentRepository repository,
    IFileStorage fileStorage,
    IPdfTextExtractor textExtractor,
    TextChunker chunker,
    IDocumentProcessingQueue queue,
    ILogger<DocumentProcessor> logger)
{
    public const string UnexpectedErrorMessage = "An unexpected error occurred while processing the document.";
    public const string InterruptedMessage = "Processing was interrupted because the server stopped. Retry to process it again.";

    /// <summary>
    /// Processes a document that is waiting (Uploaded) or being retried (Failed). Documents in any
    /// other state are skipped, so a document queued twice is only processed once.
    /// Failures are recorded on the document rather than thrown.
    /// </summary>
    public async Task ProcessAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await repository.GetByIdWithChunksAsync(documentId, cancellationToken);
        if (document is null)
        {
            logger.LogWarning("Document {DocumentId} was queued for processing but no longer exists", documentId);
            return;
        }
        if (document.Status is not (DocumentStatus.Uploaded or DocumentStatus.Failed))
        {
            logger.LogInformation("Skipping document {DocumentId}: status is {Status}", documentId, document.Status);
            return;
        }

        // Save the Processing status first so clients can see the document is being worked on.
        document.StartProcessing();
        await repository.SaveChangesAsync(cancellationToken);

        try
        {
            ExtractedText text;
            await using (var pdf = await fileStorage.OpenReadAsync(DocumentService.StorageKeyFor(document.Id), cancellationToken))
            {
                text = await textExtractor.ExtractAsync(pdf, cancellationToken);
            }

            foreach (var chunk in chunker.Chunk(text))
                document.AddChunk(chunk.Index, chunk.Content, chunk.PageNumber);

            document.MarkReady();
            logger.LogInformation(
                "Processed document {DocumentId}: {PageCount} pages, {ChunkCount} chunks",
                documentId, text.Pages.Count, document.Chunks.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down. The document stays Processing; RecoverInterruptedAsync handles it on restart.
            throw;
        }
        catch (TextExtractionException ex)
        {
            logger.LogWarning("Text extraction failed for document {DocumentId}: {Reason}", documentId, ex.Message);
            document.MarkFailed(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error processing document {DocumentId}", documentId);
            document.MarkFailed(UnexpectedErrorMessage);
        }

        // Record the outcome even if shutdown starts right now.
        await repository.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// Run at startup: documents left in Processing by a crash or restart are marked Failed
    /// (so they can be retried), and documents still waiting in Uploaded are queued again.
    /// </summary>
    public async Task RecoverInterruptedAsync(CancellationToken cancellationToken = default)
    {
        var interrupted = await repository.GetByStatusAsync(DocumentStatus.Processing, cancellationToken);
        foreach (var document in interrupted)
            document.MarkFailed(InterruptedMessage);
        if (interrupted.Count > 0)
        {
            await repository.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Marked {Count} interrupted document(s) as failed", interrupted.Count);
        }

        var waiting = await repository.GetByStatusAsync(DocumentStatus.Uploaded, cancellationToken);
        foreach (var document in waiting)
            await queue.EnqueueAsync(document.Id, cancellationToken);
        if (waiting.Count > 0)
            logger.LogInformation("Re-queued {Count} uploaded document(s) for processing", waiting.Count);
    }
}
