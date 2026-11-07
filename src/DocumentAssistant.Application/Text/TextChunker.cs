using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace DocumentAssistant.Application.Text;

/// <summary>A piece of text ready to be stored as a <c>DocumentChunk</c>.</summary>
public record TextChunk(int Index, int PageNumber, string Content);

/// <summary>
/// Splits extracted text into overlapping chunks for embedding.
/// <para>
/// Recursive strategy: try to split on the most meaningful boundary first (paragraphs), and only
/// fall back to smaller ones (lines, sentences, words, then a hard cut) for pieces that are still
/// too big. Small pieces are packed together up to <see cref="ChunkingOptions.MaxChunkSize"/>.
/// </para>
/// <para>Chunks never span pages, so every chunk can be cited with a single page number.</para>
/// </summary>
public partial class TextChunker(IOptions<ChunkingOptions> options)
{
    private sealed record Separator(Regex Pattern, string Joiner);

    // Most to least meaningful. The joiner re-inserts the separator when parts are packed back together.
    private static readonly Separator[] Separators =
    [
        new(ParagraphBreak(), "\n\n"),
        new(LineBreak(), "\n"),
        new(SentenceEnd(), " "),
        new(Whitespace(), " ")
    ];

    public IReadOnlyList<TextChunk> Chunk(ExtractedText text)
    {
        var settings = options.Value;
        if (settings.MaxChunkSize < 1)
            throw new InvalidOperationException("Chunking:MaxChunkSize must be positive.");
        if (settings.ChunkOverlap < 0 || settings.ChunkOverlap >= settings.MaxChunkSize)
            throw new InvalidOperationException("Chunking:ChunkOverlap must be at least 0 and less than MaxChunkSize.");

        var chunks = new List<TextChunk>();
        foreach (var page in text.Pages)
        {
            foreach (var content in Split(page.Text, level: 0, settings))
                chunks.Add(new TextChunk(chunks.Count, page.PageNumber, content));
        }
        return chunks;
    }

    private static IEnumerable<string> Split(string text, int level, ChunkingOptions settings)
    {
        text = text.Trim();
        if (text.Length == 0)
            yield break;
        if (text.Length <= settings.MaxChunkSize)
        {
            yield return text;
            yield break;
        }
        if (level == Separators.Length)
        {
            foreach (var piece in HardSplit(text, settings))
                yield return piece;
            yield break;
        }

        var separator = Separators[level];
        var joinLength = separator.Joiner.Length;

        // Parts waiting to be emitted as one chunk, and their joined length.
        var current = new List<string>();
        var currentLength = 0;

        foreach (var part in separator.Pattern.Split(text).Select(p => p.Trim()).Where(p => p.Length > 0))
        {
            if (part.Length > settings.MaxChunkSize)
            {
                // Too big on its own: emit what we have, then split this part on a finer boundary.
                if (current.Count > 0)
                {
                    yield return string.Join(separator.Joiner, current);
                    current.Clear();
                    currentLength = 0;
                }
                foreach (var piece in Split(part, level + 1, settings))
                    yield return piece;
                continue;
            }

            if (current.Count > 0 && currentLength + joinLength + part.Length > settings.MaxChunkSize)
            {
                yield return string.Join(separator.Joiner, current);

                // Keep the tail of the emitted chunk as overlap for the next one, as long as it
                // stays within the overlap budget and leaves room for the incoming part.
                while (current.Count > 0 &&
                       (currentLength > settings.ChunkOverlap ||
                        currentLength + joinLength + part.Length > settings.MaxChunkSize))
                {
                    currentLength -= current[0].Length + (current.Count > 1 ? joinLength : 0);
                    current.RemoveAt(0);
                }
            }

            currentLength += part.Length + (current.Count > 0 ? joinLength : 0);
            current.Add(part);
        }

        if (current.Count > 0)
            yield return string.Join(separator.Joiner, current);
    }

    /// <summary>Last resort for a single "word" longer than a chunk (e.g. a long URL or garbage).</summary>
    private static IEnumerable<string> HardSplit(string text, ChunkingOptions settings)
    {
        var step = settings.MaxChunkSize - settings.ChunkOverlap;
        for (var start = 0; start < text.Length; start += step)
        {
            yield return text.Substring(start, Math.Min(settings.MaxChunkSize, text.Length - start));
            if (start + settings.MaxChunkSize >= text.Length)
                yield break;
        }
    }

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex ParagraphBreak();

    [GeneratedRegex(@"\n")]
    private static partial Regex LineBreak();

    // Split after ., ! or ? followed by whitespace, keeping the punctuation with its sentence.
    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
