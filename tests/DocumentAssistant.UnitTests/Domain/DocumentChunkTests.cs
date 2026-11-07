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
        var vector = new float[DocumentChunk.EmbeddingDimensions];
        vector[0] = 0.5f;

        chunk.SetEmbedding(vector);
        vector[0] = 99f;

        Assert.True(chunk.HasEmbedding);
        Assert.Equal(0.5f, chunk.Embedding![0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(1536)] // the old OpenAI default size
    public void SetEmbedding_WithWrongDimensions_Throws(int length)
    {
        var chunk = ProcessingDocument().AddChunk(0, "text");

        var ex = Assert.Throws<ArgumentException>(() => chunk.SetEmbedding(new float[length]));
        Assert.Contains($"must have {DocumentChunk.EmbeddingDimensions} dimensions", ex.Message);
    }

    [Fact]
    public void SetEmbedding_WithNull_Throws()
    {
        var chunk = ProcessingDocument().AddChunk(0, "text");

        Assert.Throws<ArgumentNullException>(() => chunk.SetEmbedding(null!));
    }
}
