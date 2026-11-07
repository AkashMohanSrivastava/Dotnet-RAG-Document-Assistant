using System.Text;
using System.Text.RegularExpressions;

namespace DocumentAssistant.Application.Text;

/// <summary>
/// Cleans up raw extracted text so chunking and embeddings see consistent input,
/// while keeping line and paragraph breaks (the chunker uses them as split points).
/// </summary>
public static partial class TextNormalizer
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Unicode compatibility form: ligatures like "ﬁ" become "fi", non-breaking spaces become spaces.
        text = text.Normalize(NormalizationForm.FormKC);

        // Treat every kind of line break (including form feeds) as a plain newline.
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\f', '\n').Replace('\v', '\n');

        // PostgreSQL text columns reject NUL; other control characters are extraction noise.
        text = ControlCharacters().Replace(text, string.Empty);

        text = HorizontalWhitespace().Replace(text, " ");
        text = SpacesAroundNewlines().Replace(text, "\n");
        text = ExtraBlankLines().Replace(text, "\n\n");

        return text.Trim();
    }

    // Everything in the Unicode "control" category except tab and newline.
    [GeneratedRegex(@"[\p{Cc}-[\t\n]]")]
    private static partial Regex ControlCharacters();

    [GeneratedRegex(@"[ \t  -   　]+")]
    private static partial Regex HorizontalWhitespace();

    [GeneratedRegex(@" *\n *")]
    private static partial Regex SpacesAroundNewlines();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExtraBlankLines();
}
