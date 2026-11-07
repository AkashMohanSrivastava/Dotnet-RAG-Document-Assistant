using DocumentAssistant.Application.Text;
using Microsoft.Extensions.Options;

namespace DocumentAssistant.UnitTests.Text;

public class TextChunkerTests
{
    private static TextChunker Chunker(int maxSize, int overlap) =>
        new(Options.Create(new ChunkingOptions { MaxChunkSize = maxSize, ChunkOverlap = overlap }));

    private static ExtractedText Pages(params string[] pages) =>
        new(pages.Select((text, i) => new ExtractedPage(i + 1, text)).ToList());

    private static string Words(int count, string prefix = "w") =>
        string.Join(' ', Enumerable.Range(0, count).Select(i => $"{prefix}{i}"));

    [Fact]
    public void ShortPage_BecomesSingleChunk()
    {
        var chunks = Chunker(100, 10).Chunk(Pages("Hello world."));

        var chunk = Assert.Single(chunks);
        Assert.Equal(new TextChunk(0, 1, "Hello world."), chunk);
    }

    [Fact]
    public void ChunksNeverSpanPages_AndIndexesContinueAcrossPages()
    {
        var chunks = Chunker(100, 10).Chunk(Pages("Page one text.", "", "Page three text."));

        Assert.Equal(
            [new TextChunk(0, 1, "Page one text."), new TextChunk(1, 3, "Page three text.")],
            chunks);
    }

    [Fact]
    public void SmallParagraphs_ArePackedTogether_KeepingParagraphBreaks()
    {
        var chunks = Chunker(100, 0).Chunk(Pages("First para.\n\nSecond para.\n\nThird para."));

        Assert.Equal("First para.\n\nSecond para.\n\nThird para.", Assert.Single(chunks).Content);
    }

    [Fact]
    public void SplitsOnParagraphsBeforeSentences()
    {
        var paragraph1 = "Alpha one. Alpha two. Alpha three.";  // 34 chars
        var paragraph2 = "Beta one. Beta two. Beta three.";     // 31 chars

        var chunks = Chunker(50, 0).Chunk(Pages($"{paragraph1}\n\n{paragraph2}"));

        Assert.Equal([paragraph1, paragraph2], chunks.Select(c => c.Content));
    }

    [Fact]
    public void LongParagraph_FallsBackToSentenceBoundaries()
    {
        var text = "Sentence number one is here. Sentence number two is here. Sentence number three is here.";

        var chunks = Chunker(60, 0).Chunk(Pages(text));

        Assert.All(chunks, c => Assert.EndsWith(".", c.Content));
        Assert.Equal(text, string.Join(" ", chunks.Select(c => c.Content)));
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(100, 30)]
    [InlineData(250, 50)]
    [InlineData(40, 39)]
    public void NoChunkExceedsMaxSize(int maxSize, int overlap)
    {
        var text = string.Join("\n\n", Enumerable.Range(0, 20).Select(i => $"Paragraph {i}. {Words(i * 7)}."));

        var chunks = Chunker(maxSize, overlap).Chunk(Pages(text));

        Assert.All(chunks, c => Assert.InRange(c.Content.Length, 1, maxSize));
    }

    [Fact]
    public void NoWordIsLost_AndOrderIsPreserved()
    {
        var text = Words(300);

        var chunks = Chunker(120, 30).Chunk(Pages(text));

        // Walk through the chunks; every input word must appear, in order (overlap may repeat some).
        var words = text.Split(' ');
        var next = 0;
        foreach (var chunkWord in chunks.SelectMany(c => c.Content.Split(' ')))
        {
            if (next < words.Length && chunkWord == words[next])
                next++;
        }
        Assert.Equal(words.Length, next);
    }

    [Fact]
    public void ConsecutiveChunks_Overlap()
    {
        var chunks = Chunker(100, 30).Chunk(Pages(Words(100)));

        Assert.True(chunks.Count > 2);
        for (var i = 1; i < chunks.Count; i++)
        {
            var firstWordOfNext = chunks[i].Content.Split(' ')[0];
            Assert.Contains(firstWordOfNext, chunks[i - 1].Content.Split(' '));
        }
    }

    [Fact]
    public void ZeroOverlap_DoesNotRepeatWords()
    {
        var chunks = Chunker(100, 0).Chunk(Pages(Words(100)));

        var allWords = chunks.SelectMany(c => c.Content.Split(' ')).ToList();
        Assert.Equal(100, allWords.Count);
        Assert.Equal(allWords.Count, allWords.Distinct().Count());
    }

    [Fact]
    public void SingleHugeWord_IsHardSplit()
    {
        var url = "https://example.com/" + new string('x', 230); // 250 chars, no spaces

        var chunks = Chunker(100, 20).Chunk(Pages(url));

        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, c => Assert.True(c.Content.Length <= 100));
        Assert.StartsWith(chunks[1].Content[..20], chunks[0].Content[^20..]);
        Assert.EndsWith(chunks[^1].Content, url);
    }

    [Fact]
    public void EmptyText_ProducesNoChunks()
    {
        Assert.Empty(Chunker(100, 10).Chunk(Pages("", "   ")));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(100, -1)]
    public void InvalidOptions_Throw(int maxSize, int overlap)
    {
        Assert.Throws<InvalidOperationException>(() => Chunker(maxSize, overlap).Chunk(Pages("text")));
    }

    [Fact]
    public void DefaultOptions_ProduceReasonableChunksForRealisticText()
    {
        var page = string.Join("\n\n", Enumerable.Range(0, 30).Select(i =>
            $"Section {i}. The device must be installed by a qualified technician. " +
            "Ensure the power supply is disconnected before opening the case. " +
            "Refer to the safety guide for details."));

        var chunks = new TextChunker(Options.Create(new ChunkingOptions())).Chunk(Pages(page));

        Assert.InRange(chunks.Count, 3, 10);
        Assert.All(chunks, c => Assert.True(c.Content.Length <= 1500));
    }
}
