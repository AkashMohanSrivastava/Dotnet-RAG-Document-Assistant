using DocumentAssistant.Domain.Entities;
using DocumentAssistant.Domain.Enums;

namespace DocumentAssistant.UnitTests.Domain;

public class DocumentTests
{
    private static Document NewDocument() => Document.Create("manual.pdf", "application/pdf", 1024);

    private static Document ProcessingDocument()
    {
        var document = NewDocument();
        document.StartProcessing();
        return document;
    }

    // --- Create ---

    [Fact]
    public void Create_WithValidInput_SetsPropertiesAndUploadedStatus()
    {
        var document = Document.Create("  manual.pdf ", "application/pdf", 2048);

        Assert.NotEqual(Guid.Empty, document.Id);
        Assert.Equal("manual.pdf", document.FileName);
        Assert.Equal("application/pdf", document.ContentType);
        Assert.Equal(2048, document.FileSizeBytes);
        Assert.Equal(DocumentStatus.Uploaded, document.Status);
        Assert.Null(document.FailureReason);
        Assert.Null(document.ProcessedAt);
        Assert.Empty(document.Chunks);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithMissingFileName_Throws(string? fileName)
    {
        var ex = Assert.Throws<ArgumentException>(() => Document.Create(fileName!, "application/pdf", 1));
        Assert.Equal("fileName", ex.ParamName);
    }

    [Fact]
    public void Create_WithTooLongFileName_Throws()
    {
        var fileName = new string('a', Document.MaxFileNameLength + 1);

        var ex = Assert.Throws<ArgumentException>(() => Document.Create(fileName, "application/pdf", 1));
        Assert.Equal("fileName", ex.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithMissingContentType_Throws(string? contentType)
    {
        var ex = Assert.Throws<ArgumentException>(() => Document.Create("a.pdf", contentType!, 1));
        Assert.Equal("contentType", ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithNonPositiveSize_Throws(long size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Document.Create("a.pdf", "application/pdf", size));
    }

    // --- Status transitions ---

    [Fact]
    public void StartProcessing_FromUploaded_MovesToProcessing()
    {
        var document = NewDocument();

        document.StartProcessing();

        Assert.Equal(DocumentStatus.Processing, document.Status);
    }

    [Fact]
    public void StartProcessing_WhenAlreadyProcessing_Throws()
    {
        var document = ProcessingDocument();

        Assert.Throws<InvalidOperationException>(document.StartProcessing);
    }

    [Fact]
    public void StartProcessing_WhenReady_Throws()
    {
        var document = ProcessingDocument();
        document.AddChunk(0, "text");
        document.MarkReady();

        Assert.Throws<InvalidOperationException>(document.StartProcessing);
    }

    [Fact]
    public void StartProcessing_FromFailed_RetriesAndClearsPreviousAttempt()
    {
        var document = ProcessingDocument();
        document.AddChunk(0, "partial");
        document.MarkFailed("Extraction error");

        document.StartProcessing();

        Assert.Equal(DocumentStatus.Processing, document.Status);
        Assert.Null(document.FailureReason);
        Assert.Empty(document.Chunks);
    }

    [Fact]
    public void MarkReady_FromProcessingWithChunks_MovesToReady()
    {
        var document = ProcessingDocument();
        document.AddChunk(0, "text");

        document.MarkReady();

        Assert.Equal(DocumentStatus.Ready, document.Status);
        Assert.NotNull(document.ProcessedAt);
    }

    [Fact]
    public void MarkReady_WithoutChunks_Throws()
    {
        var document = ProcessingDocument();

        Assert.Throws<InvalidOperationException>(document.MarkReady);
    }

    [Fact]
    public void MarkReady_FromUploaded_Throws()
    {
        var document = NewDocument();

        Assert.Throws<InvalidOperationException>(document.MarkReady);
    }

    [Fact]
    public void MarkFailed_FromProcessing_MovesToFailedWithReason()
    {
        var document = ProcessingDocument();

        document.MarkFailed("  Corrupt PDF  ");

        Assert.Equal(DocumentStatus.Failed, document.Status);
        Assert.Equal("Corrupt PDF", document.FailureReason);
        Assert.NotNull(document.ProcessedAt);
    }

    [Fact]
    public void MarkFailed_TruncatesVeryLongReason()
    {
        var document = ProcessingDocument();

        document.MarkFailed(new string('x', Document.MaxFailureReasonLength + 50));

        Assert.Equal(Document.MaxFailureReasonLength, document.FailureReason!.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void MarkFailed_WithoutReason_Throws(string? reason)
    {
        var document = ProcessingDocument();

        Assert.Throws<ArgumentException>(() => document.MarkFailed(reason!));
        Assert.Equal(DocumentStatus.Processing, document.Status);
    }

    [Fact]
    public void MarkFailed_FromUploaded_Throws()
    {
        var document = NewDocument();

        Assert.Throws<InvalidOperationException>(() => document.MarkFailed("error"));
    }

    // --- Chunks ---

    [Fact]
    public void AddChunk_WhileProcessing_AddsChunkLinkedToDocument()
    {
        var document = ProcessingDocument();

        var chunk = document.AddChunk(0, "Hello world", pageNumber: 3);

        Assert.Single(document.Chunks);
        Assert.Equal(document.Id, chunk.DocumentId);
        Assert.Equal(0, chunk.ChunkIndex);
        Assert.Equal("Hello world", chunk.Content);
        Assert.Equal(3, chunk.PageNumber);
    }

    [Fact]
    public void AddChunk_WhenNotProcessing_Throws()
    {
        var document = NewDocument();

        Assert.Throws<InvalidOperationException>(() => document.AddChunk(0, "text"));
    }

    [Fact]
    public void AddChunk_WithDuplicateIndex_Throws()
    {
        var document = ProcessingDocument();
        document.AddChunk(0, "first");

        Assert.Throws<InvalidOperationException>(() => document.AddChunk(0, "second"));
    }
}
