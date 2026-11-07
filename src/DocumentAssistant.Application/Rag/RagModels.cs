namespace DocumentAssistant.Application.Rag;

/// <summary>
/// One earlier message in the conversation. The client sends the history with each question, so the
/// server stays stateless (no chat sessions to store).
/// </summary>
public record ChatTurn(string Role, string Content)
{
    public bool IsUser => string.Equals(Role, "user", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A chunk given to the model as source [Number]. <see cref="Cited"/> is true if the answer refers to it.</summary>
public record AnswerSource(
    int Number,
    Guid DocumentId,
    string FileName,
    int? PageNumber,
    int ChunkIndex,
    bool Cited,
    string Content);

/// <summary>
/// The model's answer with its sources. <see cref="Answered"/> is false when the model said the
/// documents don't contain the answer. <see cref="SearchQuery"/> is what retrieval actually searched
/// for: the question itself, or its standalone rewrite when there is history.
/// </summary>
public record AskResponse(
    string Question,
    string SearchQuery,
    string Answer,
    bool Answered,
    IReadOnlyList<AnswerSource> Sources,
    string? Model,
    long? InputTokens,
    long? OutputTokens,
    long DurationMs);

/// <summary>Events of a streamed answer, in order: one <see cref="SourcesEvent"/>, many <see cref="TokenEvent"/>s, one <see cref="DoneEvent"/>.</summary>
public abstract record RagStreamEvent;

/// <summary>Sent first, so a UI can show the sources while the answer is still being written.</summary>
public sealed record SourcesEvent(string SearchQuery, IReadOnlyList<AnswerSource> Sources) : RagStreamEvent;

public sealed record TokenEvent(string Text) : RagStreamEvent;

/// <summary>Sent last, once the full answer is known. <see cref="CitedSources"/> are source numbers.</summary>
public sealed record DoneEvent(
    bool Answered,
    IReadOnlyList<int> CitedSources,
    string? Model,
    long? InputTokens,
    long? OutputTokens,
    long DurationMs) : RagStreamEvent;
