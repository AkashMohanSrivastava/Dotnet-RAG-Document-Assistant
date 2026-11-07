using DocumentAssistant.Application.Documents;
using DocumentAssistant.Application.Embeddings;
using DocumentAssistant.Application.Text;
using DocumentAssistant.Domain.Entities;
using DocumentAssistant.Domain.Enums;
using DocumentAssistant.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocumentAssistant.UnitTests.Documents;

public class DocumentProcessorTests
{
    private readonly FakeDocumentRepository _repository = new();
    private readonly FakeFileStorage _storage = new();
    private readonly FakePdfTextExtractor _extractor = new();
    private readonly FakeProcessingQueue _queue = new();
    private readonly FakeEmbeddingGenerator _embeddings = new();
    private readonly DocumentProcessor _processor;

    public DocumentProcessorTests()
    {
        var chunker = new TextChunker(Options.Create(new ChunkingOptions { MaxChunkSize = 50, ChunkOverlap = 0 }));
        _processor = new DocumentProcessor(
            _repository, _storage, _extractor, chunker,
            new ChunkEmbedder(_embeddings, Options.Create(new EmbeddingOptions())),
            _queue, NullLogger<DocumentProcessor>.Instance);
    }

    private async Task<Document> SavedUploadedDocument()
    {
        var document = Document.Create("manual.pdf", DocumentService.PdfContentType, 10);
        _storage.Files[DocumentService.StorageKeyFor(document.Id)] = [1, 2, 3];
        _repository.Add(document);
        await _repository.SaveChangesAsync();
        _repository.SavedStatuses.Clear();
        return document;
    }

    [Fact]
    public async Task Process_Success_StoresChunksWithPagesAndMarksReady()
    {
        var document = await SavedUploadedDocument();
        _extractor.Result = new ExtractedText([
            new ExtractedPage(1, "Short intro."),
            new ExtractedPage(2, "First paragraph on page two.\n\nSecond paragraph on page two.")
        ]);

        await _processor.ProcessAsync(document.Id);

        Assert.Equal(DocumentStatus.Ready, document.Status);
        Assert.Equal(
            [(0, 1, "Short intro."), (1, 2, "First paragraph on page two."), (2, 2, "Second paragraph on page two.")],
            document.Chunks.Select(c => (c.ChunkIndex, c.PageNumber ?? 0, c.Content)));
        Assert.Equal([1, 2, 3], _extractor.LastInput); // read the stored file
        Assert.All(document.Chunks, c => Assert.True(c.HasEmbedding));

        // Processing is saved first, then the final state.
        Assert.Equal([DocumentStatus.Processing, DocumentStatus.Ready], _repository.SavedStatuses);
    }

    [Fact]
    public async Task Process_EmbeddingFails_MarksFailedWithProviderReason()
    {
        var document = await SavedUploadedDocument();
        _embeddings.Failure = new HttpRequestException("401 Unauthorized");

        await _processor.ProcessAsync(document.Id);

        Assert.Equal(DocumentStatus.Failed, document.Status);
        Assert.Equal("Generating embeddings failed: 401 Unauthorized", document.FailureReason);
        Assert.Empty(document.Chunks);
    }

    [Fact]
    public async Task Process_ExtractionFails_MarksFailedWithReason()
    {
        var document = await SavedUploadedDocument();
        _extractor.Failure = new TextExtractionException("The PDF is password-protected.");

        await _processor.ProcessAsync(document.Id);

        Assert.Equal(DocumentStatus.Failed, document.Status);
        Assert.Equal("The PDF is password-protected.", document.FailureReason);
        Assert.Empty(document.Chunks);
        Assert.Equal([DocumentStatus.Processing, DocumentStatus.Failed], _repository.SavedStatuses);
    }

    [Fact]
    public async Task Process_UnexpectedError_MarksFailedWithGenericMessage()
    {
        var document = await SavedUploadedDocument();
        _storage.Files.Clear(); // stored file missing

        await _processor.ProcessAsync(document.Id);

        Assert.Equal(DocumentStatus.Failed, document.Status);
        Assert.Equal(DocumentProcessor.UnexpectedErrorMessage, document.FailureReason);
    }

    [Fact]
    public async Task Process_FailedDocument_IsRetried()
    {
        var document = await SavedUploadedDocument();
        _extractor.Failure = new TextExtractionException("Temporary problem.");
        await _processor.ProcessAsync(document.Id);

        _extractor.Failure = null;
        await _processor.ProcessAsync(document.Id);

        Assert.Equal(DocumentStatus.Ready, document.Status);
        Assert.Null(document.FailureReason);
        Assert.NotEmpty(document.Chunks);
    }

    [Fact]
    public async Task Process_ReadyDocument_IsSkipped()
    {
        var document = await SavedUploadedDocument();
        await _processor.ProcessAsync(document.Id);
        var saves = _repository.SaveCount;

        await _processor.ProcessAsync(document.Id);

        Assert.Equal(DocumentStatus.Ready, document.Status);
        Assert.Equal(saves, _repository.SaveCount);
    }

    [Fact]
    public async Task Process_UnknownDocument_DoesNothing()
    {
        await _processor.ProcessAsync(Guid.NewGuid());

        Assert.Equal(0, _repository.SaveCount);
    }

    [Fact]
    public async Task Process_Cancelled_LeavesDocumentProcessing()
    {
        var document = await SavedUploadedDocument();
        using var cts = new CancellationTokenSource();
        _extractor.Failure = new OperationCanceledException(cts.Token);
        await cts.CancelAsync();

        // The repository fake ignores the token, so cancellation surfaces from the extractor.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _processor.ProcessAsync(document.Id, cts.Token));

        Assert.Equal(DocumentStatus.Processing, document.Status);
    }

    [Fact]
    public async Task RecoverInterrupted_FailsStuckDocuments_AndRequeuesWaitingOnes()
    {
        var stuck = await SavedUploadedDocument();
        stuck.StartProcessing();
        var waiting = await SavedUploadedDocument();

        await _processor.RecoverInterruptedAsync();

        Assert.Equal(DocumentStatus.Failed, stuck.Status);
        Assert.Equal(DocumentProcessor.InterruptedMessage, stuck.FailureReason);
        Assert.Equal([waiting.Id], _queue.Enqueued);
    }
}
