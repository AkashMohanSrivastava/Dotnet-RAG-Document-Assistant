using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace DocumentAssistant.IntegrationTests;

/// <summary>
/// Offline stand-in for an LLM: "answers" by quoting the first line of source [1] from the RAG prompt
/// and citing it, so tests can check the whole pipeline without a real model.
/// </summary>
public sealed partial class QuotingChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var prompt = messages.Last().Text;
        var firstSource = FirstSourceLine().Match(prompt);
        var answer = firstSource.Success ? $"{firstSource.Groups[1].Value} [1]" : "I couldn't find the answer in the uploaded documents.";

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)) { ModelId = "quoting-test-model" });
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    // "[1] (file.pdf, page 2)\n<first line of content>"
    [GeneratedRegex(@"\[1\] \([^)]*\)\n([^\n]+)")]
    private static partial Regex FirstSourceLine();
}
