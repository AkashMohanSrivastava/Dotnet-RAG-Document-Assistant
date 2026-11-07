using DocumentAssistant.Application.Text;

namespace DocumentAssistant.UnitTests.Text;

public class TextNormalizerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t \r\n ")]
    public void EmptyOrWhitespace_BecomesEmpty(string? input)
    {
        Assert.Equal(string.Empty, TextNormalizer.Normalize(input));
    }

    [Fact]
    public void CollapsesRepeatedSpacesAndTabs()
    {
        Assert.Equal("a b c", TextNormalizer.Normalize("a   b\t\tc"));
    }

    [Fact]
    public void NormalizesLineEndings_AndTrimsLines()
    {
        Assert.Equal("line one\nline two\nline three", TextNormalizer.Normalize("line one  \r\n  line two\rline three"));
    }

    [Fact]
    public void KeepsParagraphBreaks_ButCollapsesExtraBlankLines()
    {
        Assert.Equal("para one\n\npara two", TextNormalizer.Normalize("para one\n\n\n\n \n\npara two"));
    }

    [Fact]
    public void FormFeed_IsTreatedAsLineBreak()
    {
        Assert.Equal("page one\npage two", TextNormalizer.Normalize("page one\fpage two"));
    }

    [Fact]
    public void RemovesNulAndOtherControlCharacters()
    {
        Assert.Equal("abc", TextNormalizer.Normalize("a\0b\u0007c"));
    }

    [Fact]
    public void ExpandsLigatures_AndNonBreakingSpaces()
    {
        // "ﬁ" (U+FB01) is common in PDFs; NBSP (U+00A0) and thin space (U+2009) should be plain spaces.
        Assert.Equal("find the file now", TextNormalizer.Normalize("ﬁnd the file now"));
    }
}
