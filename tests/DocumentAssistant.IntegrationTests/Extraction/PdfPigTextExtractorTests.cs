using System.Text;
using DocumentAssistant.Application.Text;
using DocumentAssistant.Infrastructure.Extraction;

namespace DocumentAssistant.IntegrationTests.Extraction;

public class PdfPigTextExtractorTests
{
    private readonly PdfPigTextExtractor _extractor = new();

    private Task<ExtractedText> Extract(byte[] pdf) => _extractor.ExtractAsync(new MemoryStream(pdf));

    [Fact]
    public async Task ExtractsTextPerPage_WithPageNumbers()
    {
        var pdf = TestPdfs.Create(
            ["Installation Guide", "Step one: unpack the device."],
            ["Troubleshooting", "Restart the device if it freezes."]);

        var result = await Extract(pdf);

        Assert.Equal(2, result.Pages.Count);
        Assert.Equal([1, 2], result.Pages.Select(p => p.PageNumber));
        Assert.Contains("Installation Guide", result.Pages[0].Text);
        Assert.Contains("Step one: unpack the device.", result.Pages[0].Text);
        Assert.Contains("Restart the device if it freezes.", result.Pages[1].Text);
        Assert.DoesNotContain("Troubleshooting", result.Pages[0].Text);
    }

    [Fact]
    public async Task KeepsLinesSeparate()
    {
        var result = await Extract(TestPdfs.Create(["First line", "Second line"]));

        Assert.Equal("First line\nSecond line", result.Pages[0].Text);
    }

    [Fact]
    public async Task BlankPage_IsKeptWithEmptyText_SoPageNumbersStayAccurate()
    {
        var result = await Extract(TestPdfs.Create(["Cover"], [], ["Chapter 1"]));

        Assert.Equal(3, result.Pages.Count);
        Assert.Equal(string.Empty, result.Pages[1].Text);
        Assert.Equal(3, result.Pages[2].PageNumber);
    }

    [Fact]
    public async Task PdfWithNoText_ThrowsWithOcrHint()
    {
        var ex = await Assert.ThrowsAsync<TextExtractionException>(() => Extract(TestPdfs.Create([], [])));

        Assert.Contains("no extractable text", ex.Message);
    }

    private const string ParagraphOfText = "This page contains a full sentence of real, extractable text.";

    [Fact]
    public async Task MostlyImagePdf_IsRejected_WithPageCounts()
    {
        // Like a similarity-report export: text on the cover and the last page, page images with
        // stray markers ("1") everywhere else.
        var pages = new List<string[]> { new[] { ParagraphOfText } };
        pages.AddRange(Enumerable.Range(0, 8).Select(_ => new[] { "1" }));
        pages.Add([ParagraphOfText]);

        var ex = await Assert.ThrowsAsync<TextExtractionException>(() => Extract(TestPdfs.Create(pages.ToArray())));

        Assert.StartsWith("Only 2 of 10 pages contain extractable text.", ex.Message);
    }

    [Fact]
    public async Task SomeBlankPages_AreFine_WhenMostPagesHaveText()
    {
        // 6 text pages, 4 blank or near-blank (section dividers, page numbers): still a text PDF.
        var pages = Enumerable.Range(0, 6).Select(_ => new[] { ParagraphOfText })
            .Concat(new string[][] { [], ["12"], [], ["iv"] })
            .ToArray();

        var result = await Extract(TestPdfs.Create(pages));

        Assert.Equal(10, result.Pages.Count);
    }

    [Fact]
    public async Task ShortDocuments_SkipTheImageCheck()
    {
        // 1 of 3 pages has text: too few pages to judge, so it's accepted.
        var result = await Extract(TestPdfs.Create([ParagraphOfText], [], ["7"]));

        Assert.Equal(3, result.Pages.Count);
    }

    [Fact]
    public async Task CorruptPdf_ThrowsTextExtractionException()
    {
        var corrupt = Encoding.ASCII.GetBytes("%PDF-1.7\nthis is not really a pdf");

        var ex = await Assert.ThrowsAsync<TextExtractionException>(() => Extract(corrupt));

        Assert.Contains("could not be read", ex.Message);
    }

    [Fact]
    public async Task Cancellation_IsNotWrappedAsExtractionFailure()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _extractor.ExtractAsync(new MemoryStream(TestPdfs.Create(["Text"])), cts.Token));
    }
}
