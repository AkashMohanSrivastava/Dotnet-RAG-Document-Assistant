using System.Diagnostics;
using System.Runtime.CompilerServices;
using DocumentAssistant.Application.Common;
using DocumentAssistant.Application.Retrieval;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace DocumentAssistant.Application.Rag;

/// <summary>
/// Retrieval-Augmented Generation: find relevant chunks, then have the LLM answer from them only,
/// citing which chunks it used. Supports follow-up questions and streaming.
/// </summary>
public class RagService(RetrievalService retrieval, IChatClient chatClient, IOptions<RagOptions> options)
{
    /// <summary>Only the most recent turns are sent, to keep the prompt (and latency) bounded.</summary>
    public const int MaxHistoryTurns = 6;

    // Temperature 0: the most likely wording every time. We want faithful answers, not creative ones.
    private static readonly ChatOptions ChatOptions = new() { Temperature = 0 };

    private sealed record PreparedQuestion(
        string Question, string SearchQuery, IReadOnlyList<SearchResult> Sources, IReadOnlyList<ChatMessage> Messages);

    /// <exception cref="ValidationFailedException">The question is empty.</exception>
    public async Task<AskResponse> AskAsync(
        string question, IReadOnlyList<ChatTurn>? history = null, Guid? documentId = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var prepared = await PrepareAsync(question, history, documentId, cancellationToken);

        // Nothing to ground an answer in: don't ask the model to guess.
        if (prepared.Sources.Count == 0)
            return new AskResponse(prepared.Question, prepared.SearchQuery, RagPrompt.NotFoundAnswer, false, [],
                null, null, null, stopwatch.ElapsedMilliseconds);

        var response = await chatClient.GetResponseAsync(prepared.Messages, ChatOptions, cancellationToken);
        var answer = response.Text.Trim();
        var cited = RagPrompt.ParseCitations(answer, prepared.Sources.Count);

        return new AskResponse(
            prepared.Question,
            prepared.SearchQuery,
            answer,
            Answered: !RagPrompt.IsNotFound(answer),
            ToAnswerSources(prepared.Sources, cited),
            response.ModelId,
            response.Usage?.InputTokenCount,
            response.Usage?.OutputTokenCount,
            stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// Same as <see cref="AskAsync"/>, but yields the answer as it is generated: sources first, then
    /// text tokens, then a summary with the cited sources.
    /// </summary>
    /// <exception cref="ValidationFailedException">
    /// The question is empty. Thrown immediately, not on enumeration, so the API can still return 400
    /// before the event stream starts.
    /// </exception>
    public IAsyncEnumerable<RagStreamEvent> StreamAsync(
        string question, IReadOnlyList<ChatTurn>? history = null, Guid? documentId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ValidationFailedException("A question is required.");

        return StreamCoreAsync(question, history, documentId, cancellationToken);
    }

    private async IAsyncEnumerable<RagStreamEvent> StreamCoreAsync(
        string question, IReadOnlyList<ChatTurn>? history, Guid? documentId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var prepared = await PrepareAsync(question, history, documentId, cancellationToken);

        yield return new SourcesEvent(prepared.SearchQuery, ToAnswerSources(prepared.Sources, cited: new HashSet<int>()));

        if (prepared.Sources.Count == 0)
        {
            yield return new TokenEvent(RagPrompt.NotFoundAnswer);
            yield return new DoneEvent(false, [], null, null, null, stopwatch.ElapsedMilliseconds);
            yield break;
        }

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in chatClient.GetStreamingResponseAsync(prepared.Messages, ChatOptions, cancellationToken))
        {
            updates.Add(update);
            if (!string.IsNullOrEmpty(update.Text))
                yield return new TokenEvent(update.Text);
        }

        // Rebuild the full response from the streamed pieces to get the complete text and usage.
        var response = updates.ToChatResponse();
        var answer = response.Text.Trim();

        yield return new DoneEvent(
            Answered: !RagPrompt.IsNotFound(answer),
            RagPrompt.ParseCitations(answer, prepared.Sources.Count).Order().ToList(),
            response.ModelId,
            response.Usage?.InputTokenCount,
            response.Usage?.OutputTokenCount,
            stopwatch.ElapsedMilliseconds);
    }

    private async Task<PreparedQuestion> PrepareAsync(
        string question, IReadOnlyList<ChatTurn>? history, Guid? documentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ValidationFailedException("A question is required.");
        question = question.Trim();

        var recentHistory = (history ?? [])
            .Where(turn => !string.IsNullOrWhiteSpace(turn.Content))
            .TakeLast(MaxHistoryTurns)
            .ToList();

        // A follow-up like "what about after that?" is useless as a search query on its own,
        // so the model first rewrites it into a standalone question.
        var searchQuery = recentHistory.Count == 0
            ? question
            : await CondenseAsync(question, recentHistory, cancellationToken);

        var search = await retrieval.SearchAsync(
            searchQuery, options.Value.SearchMode, options.Value.SourceCount, documentId, cancellationToken);

        return new PreparedQuestion(question, searchQuery, search.Results, RagPrompt.Build(question, search.Results, recentHistory));
    }

    private async Task<string> CondenseAsync(string question, IReadOnlyList<ChatTurn> history, CancellationToken cancellationToken)
    {
        var response = await chatClient.GetResponseAsync(RagPrompt.BuildCondense(question, history), ChatOptions, cancellationToken);
        var rewritten = response.Text.Trim().Trim('"');
        return rewritten.Length > 0 ? rewritten : question;
    }

    private static List<AnswerSource> ToAnswerSources(IReadOnlyList<SearchResult> sources, ISet<int> cited) =>
        sources
            .Select((r, i) => new AnswerSource(i + 1, r.DocumentId, r.FileName, r.PageNumber, r.ChunkIndex, cited.Contains(i + 1), r.Content))
            .ToList();
}
