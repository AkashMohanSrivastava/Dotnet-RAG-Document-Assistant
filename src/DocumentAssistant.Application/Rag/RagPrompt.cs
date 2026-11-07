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
        - If the sources do not contain the answer, reply with exactly: "{NotFoundAnswer}"
        - Be concise: a few sentences, or a short list if the question asks for several items.
        """;

    public static IReadOnlyList<ChatMessage> Build(string question, IReadOnlyList<SearchResult> sources)
    {
        var user = new StringBuilder("Sources:\n\n");
        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            var page = source.PageNumber is { } p ? $", page {p}" : string.Empty;
            user.Append($"[{i + 1}] ({source.FileName}{page})\n{source.Content}\n\n");
        }
        user.Append($"Question: {question}");

        return [new ChatMessage(ChatRole.System, SystemPrompt), new ChatMessage(ChatRole.User, user.ToString())];
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

    [GeneratedRegex(@"\[(\d+)\]")]
    private static partial Regex Citation();
}
