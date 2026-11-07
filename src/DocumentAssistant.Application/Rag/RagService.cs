using System.Diagnostics;
using DocumentAssistant.Application.Common;
using DocumentAssistant.Application.Retrieval;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace DocumentAssistant.Application.Rag;

/// <summary>
/// Retrieval-Augmented Generation: find relevant chunks, then have the LLM answer from them only,
/// citing which chunks it used.
/// </summary>
public class RagService(RetrievalService retrieval, IChatClient chatClient, IOptions<RagOptions> options)
{
    // Temperature 0: the most likely wording every time. We want faithful answers, not creative ones.
    private static readonly ChatOptions ChatOptions = new() { Temperature = 0 };

    /// <exception cref="ValidationFailedException">The question is empty.</exception>
    public async Task<AskResponse> AskAsync(string question, Guid? documentId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ValidationFailedException("A question is required.");
        question = question.Trim();

        var stopwatch = Stopwatch.StartNew();
        var search = await retrieval.SearchAsync(
            question, options.Value.SearchMode, options.Value.SourceCount, documentId, cancellationToken);

        // Nothing to ground an answer in (no Ready documents yet): don't ask the model to guess.
        if (search.Results.Count == 0)
            return new AskResponse(question, RagPrompt.NotFoundAnswer, false, [], null, null, null, stopwatch.ElapsedMilliseconds);

        var response = await chatClient.GetResponseAsync(RagPrompt.Build(question, search.Results), ChatOptions, cancellationToken);
        var answer = response.Text.Trim();
        var cited = RagPrompt.ParseCitations(answer, search.Results.Count);

        var sources = search.Results
            .Select((r, i) => new AnswerSource(i + 1, r.DocumentId, r.FileName, r.PageNumber, r.ChunkIndex, cited.Contains(i + 1), r.Content))
            .ToList();

        return new AskResponse(
            question,
            answer,
            Answered: !RagPrompt.IsNotFound(answer),
            sources,
            response.ModelId,
            response.Usage?.InputTokenCount,
            response.Usage?.OutputTokenCount,
            stopwatch.ElapsedMilliseconds);
    }
}
