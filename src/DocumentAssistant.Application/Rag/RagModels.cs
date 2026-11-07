namespace DocumentAssistant.Application.Rag;

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
/// documents don't contain the answer.
/// </summary>
public record AskResponse(
    string Question,
    string Answer,
    bool Answered,
    IReadOnlyList<AnswerSource> Sources,
    string? Model,
    long? InputTokens,
    long? OutputTokens,
    long DurationMs);
