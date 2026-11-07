namespace DocumentAssistant.Application.Text;

public class ChunkingOptions
{
    public const string SectionName = "Chunking";

    /// <summary>
    /// Maximum characters per chunk. 1500 characters is roughly 350–400 tokens of English text:
    /// small enough to stay focused on one topic, big enough to carry context.
    /// </summary>
    public int MaxChunkSize { get; set; } = 1500;

    /// <summary>
    /// Up to this many characters from the end of one chunk are repeated at the start of the next,
    /// so a sentence near a boundary is still retrievable with its context.
    /// </summary>
    public int ChunkOverlap { get; set; } = 200;
}
