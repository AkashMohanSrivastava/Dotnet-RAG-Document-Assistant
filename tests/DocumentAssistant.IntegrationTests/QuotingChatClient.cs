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
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Answer(messages))) { ModelId = "quoting-test-model" });

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var words = Answer(messages).Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, i == 0 ? words[i] : " " + words[i]) { ModelId = "quoting-test-model" };
        }
    }

    private static string Answer(IEnumerable<ChatMessage> messages)
    {
        var firstSource = FirstSourceLine().Match(messages.Last().Text);
        return firstSource.Success ? $"{firstSource.Groups[1].Value} [1]" : "I couldn't find the answer in the uploaded documents.";
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    // "[1] (file.pdf, page 2)\n<first line of content>"
    [GeneratedRegex(@"\[1\] \([^)]*\)\n([^\n]+)")]
    private static partial Regex FirstSourceLine();
}
