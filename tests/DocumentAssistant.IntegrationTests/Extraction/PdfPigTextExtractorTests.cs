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
