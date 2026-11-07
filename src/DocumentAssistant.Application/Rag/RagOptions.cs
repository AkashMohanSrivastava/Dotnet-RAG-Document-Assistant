using DocumentAssistant.Application.Retrieval;

namespace DocumentAssistant.Application.Rag;

public class RagOptions
{
    public const string SectionName = "Rag";

    /// <summary>How many chunks to give the model as sources.</summary>
    public int SourceCount { get; set; } = 5;

    /// <summary>Retrieval method for finding sources. Hybrid was the most reliable in the Phase 8 comparison.</summary>
    public SearchMode SearchMode { get; set; } = SearchMode.Hybrid;
}
