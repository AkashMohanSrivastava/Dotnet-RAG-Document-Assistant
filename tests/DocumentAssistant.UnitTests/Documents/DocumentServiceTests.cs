using System.Text;
using DocumentAssistant.Application.Common;
using DocumentAssistant.Application.Documents;
using DocumentAssistant.Application.Text;
using DocumentAssistant.Domain.Entities;
using DocumentAssistant.Domain.Enums;
using DocumentAssistant.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocumentAssistant.UnitTests.Documents;

public class DocumentServiceTests
{
    private const long MaxSize = 1024;

    private readonly FakeDocumentRepository _repository = new();
    private readonly FakeFileStorage _storage = new();
    private readonly FakePdfTextExtractor _extractor = new();
    private readonly FakeProcessingQueue _queue = new();
    private readonly DocumentService _service;

    public DocumentServiceTests()
    {
        _service = new DocumentService(
            _repository,
            _storage,
            _extractor,
            _queue,
            Options.Create(new DocumentUploadOptions { MaxFileSizeBytes = MaxSize }),
            NullLogger<DocumentService>.Instance);
    }

    private static MemoryStream Pdf() => new(Encoding.ASCII.GetBytes("%PDF-1.7\n%fake pdf body"));

    private Task<DocumentDto> Upload(string fileName, Stream content, long? length = null) =>
        _service.UploadAsync(fileName, length ?? content.Length, content);

    [Fact]
    public async Task Upload_ValidPdf_StoresFileAndSavesUploadedDocument()
    {
        using var content = Pdf();

        var result = await Upload("manual.pdf", content);

        Assert.Equal("manual.pdf", result.FileName);
        Assert.Equal(DocumentService.PdfContentType, result.ContentType);
        Assert.Equal(content.Length, result.FileSizeBytes);
        Assert.Equal(nameof(DocumentStatus.Uploaded), result.Status);

        var saved = Assert.Single(_repository.Documents);
        Assert.Equal(result.Id, saved.Id);
        Assert.Equal(1, _repository.SaveCount);

        // The whole file is stored, including the header bytes we peeked at.
        Assert.Equal(content.ToArray(), _storage.Files[DocumentService.StorageKeyFor(result.Id)]);

        Assert.Equal([result.Id], _queue.Enqueued);
    }

    [Fact]
    public async Task Upload_Rejected_IsNotQueued()
    {
        await Assert.ThrowsAsync<ValidationFailedException>(() => Upload("notes.txt", Pdf()));

        Assert.Empty(_queue.Enqueued);
    }

    [Fact]
    public async Task Upload_DatabaseSaveFails_IsNotQueued()
    {
        _repository.FailOnSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Upload("manual.pdf", Pdf()));

        Assert.Empty(_queue.Enqueued);
    }

    [Fact]
    public async Task RequestProcessing_FailedDocument_IsQueued()
    {
        var document = await SavedDocumentWithStatus(DocumentStatus.Failed);

        var result = await _service.RequestProcessingAsync(document.Id);

        Assert.NotNull(result);
        Assert.Equal([document.Id], _queue.Enqueued);
    }

    [Theory]
    [InlineData(DocumentStatus.Processing)]
    [InlineData(DocumentStatus.Ready)]
    public async Task RequestProcessing_ProcessingOrReady_ThrowsConflict(DocumentStatus status)
    {
        var document = await SavedDocumentWithStatus(status);

        await Assert.ThrowsAsync<ConflictException>(() => _service.RequestProcessingAsync(document.Id));
        Assert.Empty(_queue.Enqueued);
    }

    [Fact]
    public async Task RequestProcessing_UnknownDocument_ReturnsNull()
    {
        Assert.Null(await _service.RequestProcessingAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetChunks_ReturnsChunksInOrder_OrNullForUnknownDocument()
    {
        var document = await SavedDocumentWithStatus(DocumentStatus.Ready);

        var chunks = await _service.GetChunksAsync(document.Id);

        Assert.NotNull(chunks);
        Assert.Equal([0, 1], chunks.Select(c => c.ChunkIndex));
        Assert.Null(await _service.GetChunksAsync(Guid.NewGuid()));
    }

    /// <summary>Builds a document in the given status (Ready has two chunks) and saves it.</summary>
    private async Task<Document> SavedDocumentWithStatus(DocumentStatus status)
    {
        var document = Document.Create("doc.pdf", DocumentService.PdfContentType, 10);
        if (status != DocumentStatus.Uploaded)
            document.StartProcessing();
        if (status == DocumentStatus.Ready)
        {
            document.AddChunk(1, "second");
            document.AddChunk(0, "first");
            document.MarkReady();
        }
        if (status == DocumentStatus.Failed)
            document.MarkFailed("broken");

        _repository.Add(document);
        await _repository.SaveChangesAsync();
        _queue.Enqueued.Clear();
        return document;
    }

    [Fact]
    public async Task Upload_FileNameWithClientPath_KeepsOnlyFileName()
    {
        var result = await Upload(@"C:\Users\someone\Desktop\manual.pdf", Pdf());

        Assert.Equal("manual.pdf", result.FileName);
    }

    [Fact]
    public async Task Upload_UppercaseExtension_IsAccepted()
    {
        var result = await Upload("MANUAL.PDF", Pdf());

        Assert.Equal("MANUAL.PDF", result.FileName);
    }

    [Fact]
    public async Task Upload_NonSeekableStream_StillStoresWholeFile()
    {
        var bytes = Pdf().ToArray();

        var result = await Upload("manual.pdf", new NonSeekableStream(bytes), bytes.Length);

        Assert.Equal(bytes, _storage.Files[DocumentService.StorageKeyFor(result.Id)]);
    }

    [Fact]
    public async Task Upload_EmptyFile_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => Upload("empty.pdf", new MemoryStream()));

        Assert.Contains("empty", ex.Message);
        AssertNothingSaved();
    }

    [Fact]
    public async Task Upload_TooLarge_IsRejected()
    {
        await Assert.ThrowsAsync<ValidationFailedException>(() => Upload("big.pdf", Pdf(), length: MaxSize + 1));

        AssertNothingSaved();
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("manual.pdf.exe")]
    [InlineData("noextension")]
    public async Task Upload_WrongExtension_IsRejected(string fileName)
    {
        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => Upload(fileName, Pdf()));

        Assert.Equal("Only PDF files are supported.", ex.Message);
        AssertNothingSaved();
    }

    [Theory]
    [InlineData("This is plain text renamed to .pdf")]
    [InlineData("%PD")] // shorter than the signature
    public async Task Upload_PdfExtensionButNotPdfContent_IsRejected(string text)
    {
        var ex = await Assert.ThrowsAsync<ValidationFailedException>(
            () => Upload("fake.pdf", new MemoryStream(Encoding.ASCII.GetBytes(text))));

        Assert.Equal("The file is not a valid PDF.", ex.Message);
        AssertNothingSaved();
    }

    [Fact]
    public async Task Upload_FileNameTooLong_IsRejectedAsValidationError()
    {
        var fileName = new string('a', Document.MaxFileNameLength) + ".pdf";

        await Assert.ThrowsAsync<ValidationFailedException>(() => Upload(fileName, Pdf()));
        AssertNothingSaved();
    }

    [Fact]
    public async Task Upload_DatabaseSaveFails_DeletesStoredFile()
    {
        _repository.FailOnSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Upload("manual.pdf", Pdf()));

        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task GetById_UnknownId_ReturnsNull()
    {
        Assert.Null(await _service.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task List_ClampsPagingAndReturnsTotal()
    {
        for (var i = 0; i < 3; i++)
            await Upload($"doc{i}.pdf", Pdf());

        var result = await _service.ListAsync(page: 0, pageSize: 1000);

        Assert.Equal(1, result.Page);
        Assert.Equal(DocumentService.MaxPageSize, result.PageSize);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public async Task ExtractText_ReadsStoredFileAndReturnsPages()
    {
        var uploaded = await Upload("manual.pdf", Pdf());
        _extractor.Result = new ExtractedText([new ExtractedPage(1, "Hello"), new ExtractedPage(2, "World!")]);

        var text = await _service.ExtractTextAsync(uploaded.Id);

        Assert.NotNull(text);
        Assert.Equal(uploaded.Id, text.DocumentId);
        Assert.Equal("manual.pdf", text.FileName);
        Assert.Equal(2, text.PageCount);
        Assert.Equal(11, text.TotalCharacters);
        Assert.Equal(_storage.Files[DocumentService.StorageKeyFor(uploaded.Id)], _extractor.LastInput);
    }

    [Fact]
    public async Task ExtractText_UnknownDocument_ReturnsNull()
    {
        Assert.Null(await _service.ExtractTextAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ExtractText_ExtractionFails_PropagatesAndLeavesDocumentUnchanged()
    {
        var uploaded = await Upload("manual.pdf", Pdf());
        _extractor.Failure = new TextExtractionException("No text.");

        await Assert.ThrowsAsync<TextExtractionException>(() => _service.ExtractTextAsync(uploaded.Id));

        Assert.Equal(DocumentStatus.Uploaded, _repository.Documents.Single().Status);
    }

    private void AssertNothingSaved()
    {
        Assert.Empty(_repository.Documents);
        Assert.Empty(_storage.Files);
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
