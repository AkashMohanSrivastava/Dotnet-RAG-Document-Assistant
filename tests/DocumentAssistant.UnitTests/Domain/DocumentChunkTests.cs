using DocumentAssistant.Domain.Entities;

namespace DocumentAssistant.UnitTests.Domain;

public class DocumentChunkTests
{
    private static Document ProcessingDocument()
    {
        var document = Document.Create("manual.pdf", "application/pdf", 1024);
        document.StartProcessing();
        return document;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithMissingContent_Throws(string? content)
    {
        var document = ProcessingDocument();

        Assert.Throws<ArgumentException>(() => document.AddChunk(0, content!));
    }

    [Fact]
    public void Create_WithNegativeIndex_Throws()
    {
        var document = ProcessingDocument();

        Assert.Throws<ArgumentOutOfRangeException>(() => document.AddChunk(-1, "text"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Create_WithInvalidPageNumber_Throws(int pageNumber)
    {
        var document = ProcessingDocument();

        Assert.Throws<ArgumentOutOfRangeException>(() => document.AddChunk(0, "text", pageNumber));
    }

    [Fact]
    public void NewChunk_HasNoEmbedding()
    {
        var chunk = ProcessingDocument().AddChunk(0, "text");

        Assert.False(chunk.HasEmbedding);
        Assert.Null(chunk.Embedding);
    }

    [Fact]
    public void SetEmbedding_StoresDefensiveCopy()
    {
        var chunk = ProcessingDocument().AddChunk(0, "text");
        var vector = new[] { 0.1f, 0.2f, 0.3f };

        chunk.SetEmbedding(vector);
        vector[0] = 99f;

        Assert.True(chunk.HasEmbedding);
        Assert.Equal([0.1f, 0.2f, 0.3f], chunk.Embedding!);
    }

    [Fact]
    public void SetEmbedding_WithEmptyVector_Throws()
    {
        var chunk = ProcessingDocument().AddChunk(0, "text");

        Assert.Throws<ArgumentException>(() => chunk.SetEmbedding([]));
    }

    [Fact]
    public void SetEmbedding_WithNull_Throws()
    {
        var chunk = ProcessingDocument().AddChunk(0, "text");

        Assert.Throws<ArgumentNullException>(() => chunk.SetEmbedding(null!));
    }
}
