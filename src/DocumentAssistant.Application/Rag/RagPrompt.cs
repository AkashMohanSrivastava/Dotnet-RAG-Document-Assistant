using System.Text;
using System.Text.RegularExpressions;
using DocumentAssistant.Application.Retrieval;
using Microsoft.Extensions.AI;

namespace DocumentAssistant.Application.Rag;

/// <summary>
/// Builds the grounded prompt and reads citations back out of the answer.
/// Kept separate from the service so the exact prompt is easy to read, test and tweak.
/// </summary>
public static partial class RagPrompt
{
    /// <summary>The model is told to reply with exactly this when the sources don't answer the question.</summary>
    public const string NotFoundAnswer = "I couldn't find the answer in the uploaded documents.";

    public const string SystemPrompt = $"""
        You are a document assistant. Answer the user's question using ONLY the numbered sources provided.

        Rules:
        - Cite the sources that support each statement with their number in square brackets, e.g. [1] or [2][3].
        - Do not use any knowledge that is not in the sources.
        - Source numbers refer only to the sources in the latest message.
        - If the sources do not contain the answer, reply with exactly: "{NotFoundAnswer}"
        - Be concise: a few sentences, or a short list if the question asks for several items.
        """;

    public static IReadOnlyList<ChatMessage> Build(
        string question, IReadOnlyList<SearchResult> sources, IReadOnlyList<ChatTurn>? history = null)
    {
        history ??= [];
        var user = new StringBuilder("Sources:\n\n");
        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            var page = source.PageNumber is { } p ? $", page {p}" : string.Empty;
            user.Append($"[{i + 1}] ({source.FileName}{page})\n{source.Content}\n\n");
        }
        user.Append($"Question: {question}");

        // Earlier turns go between the system prompt and the new question, so the model can follow
        // the conversation. Their [n] citations are removed: those numbers referred to older sources.
        List<ChatMessage> messages = [new(ChatRole.System, SystemPrompt)];
        messages.AddRange(history.Select(turn => new ChatMessage(
            turn.IsUser ? ChatRole.User : ChatRole.Assistant,
            turn.IsUser ? turn.Content : StripCitations(turn.Content))));
        messages.Add(new ChatMessage(ChatRole.User, user.ToString()));
        return messages;
    }

    public const string CondenseSystemPrompt = """
        Rewrite the user's latest question as a standalone question that can be understood without the
        conversation, replacing words like "it", "that" or "they" with what they refer to.
        If it is already standalone, return it unchanged. Reply with the question only.
        """;

    /// <summary>
    /// Prompt for turning a follow-up ("what about after 30 days?") into a question that works for
    /// search on its own. Retrieval only sees this one string, not the conversation.
    /// </summary>
    public static IReadOnlyList<ChatMessage> BuildCondense(string question, IReadOnlyList<ChatTurn> history)
    {
        var conversation = new StringBuilder("Conversation:\n");
        foreach (var turn in history)
            conversation.Append($"{(turn.IsUser ? "User" : "Assistant")}: {StripCitations(turn.Content)}\n");
        conversation.Append($"\nLatest question: {question}");

        return [new ChatMessage(ChatRole.System, CondenseSystemPrompt), new ChatMessage(ChatRole.User, conversation.ToString())];
    }

    /// <summary>Source numbers cited in the answer, ignoring numbers that don't match a source.</summary>
    public static ISet<int> ParseCitations(string answer, int sourceCount) =>
        Citation().Matches(answer)
            .Select(m => int.Parse(m.Groups[1].Value))
            .Where(n => n >= 1 && n <= sourceCount)
            .ToHashSet();

    /// <summary>True if the model used the "not found" reply (small models sometimes paraphrase around it).</summary>
    public static bool IsNotFound(string answer) =>
        answer.Contains("couldn't find the answer", StringComparison.OrdinalIgnoreCase) ||
        answer.Contains("could not find the answer", StringComparison.OrdinalIgnoreCase);

    // Removes "[2]" together with the space before it, so "30 days [2]." becomes "30 days.".
    private static string StripCitations(string text) => CitationWithLeadingSpace().Replace(text, string.Empty).Trim();

    [GeneratedRegex(@"\[(\d+)\]")]
    private static partial Regex Citation();

    [GeneratedRegex(@"\s*\[\d+\]")]
    private static partial Regex CitationWithLeadingSpace();
}
