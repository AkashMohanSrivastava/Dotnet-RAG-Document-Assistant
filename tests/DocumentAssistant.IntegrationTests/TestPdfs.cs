using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace DocumentAssistant.IntegrationTests;

/// <summary>Builds real PDFs in memory so tests don't depend on binary fixture files.</summary>
public static class TestPdfs
{
    /// <summary>One page per array; each string is one line of text. An empty array is a blank page.</summary>
    public static byte[] Create(params string[][] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        foreach (var lines in pages)
        {
            var page = builder.AddPage(PageSize.A4);
            var y = 750.0;
            foreach (var line in lines)
            {
                page.AddText(line, 12, new PdfPoint(50, y), font);
                y -= 20;
            }
        }

        return builder.Build();
    }
}
