namespace DocumentAssistant.Application.Text;

/// <summary>Text of one page. Page numbers start at 1; blank pages have empty text.</summary>
public record ExtractedPage(int PageNumber, string Text);

public record ExtractedText(IReadOnlyList<ExtractedPage> Pages)
{
    public int TotalCharacters => Pages.Sum(p => p.Text.Length);
}
