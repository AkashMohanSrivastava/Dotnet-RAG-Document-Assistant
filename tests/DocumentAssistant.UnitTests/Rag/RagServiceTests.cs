using DocumentAssistant.Application.Common;
using DocumentAssistant.Application.Embeddings;
using DocumentAssistant.Application.Rag;
using DocumentAssistant.Application.Retrieval;
using DocumentAssistant.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace DocumentAssistant.UnitTests.Rag;

public class RagServiceTests
{
    private readonly FakeChatClient _chat = new();
    private readonly FakeEmbeddingGenerator _embeddings = new(); // records the search query (vector mode)

    private static ChunkMatch Match(string content, int page) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "manual.pdf", page - 1, page, content, 0.8);

    private static readonly ChatTurn[] History =
    [
        new("user", "Can NVIDIA terminate the agreement?"),
        new("assistant", "Yes, if you breach it and don't fix it within 30 days [2].")
    ];

    private RagService Service(params ChunkMatch[] matches) => Service(5, matches);

    private RagService Service(int sourceCount, params ChunkMatch[] matches) =>
        new(new RetrievalService(new StaticChunkSearch(matches), _embeddings, Options.Create(new EmbeddingOptions())),
            _chat,
            Options.Create(new RagOptions { SourceCount = sourceCount, SearchMode = SearchMode.Vector }));

    [Fact]
    public async Task Ask_SendsRetrievedChunksAsSources_AndReturnsAnswerWithCitations()
    {
        _chat.Reply = "Hold the reset button for five seconds [2].";
        var service = Service(Match("The device has a power LED.", 1), Match("Hold reset for 5 seconds to restart.", 4));

        var response = await service.AskAsync("  How do I restart it? ");

        Assert.Equal("How do I restart it?", response.Question);
        Assert.Equal("Hold the reset button for five seconds [2].", response.Answer);
        Assert.True(response.Answered);
        Assert.Equal([(1, false), (2, true)], response.Sources.Select(s => (s.Number, s.Cited)));
        Assert.Equal(4, response.Sources[1].PageNumber);

        var (messages, options) = Assert.Single(_chat.Calls);
        Assert.Contains("[2] (manual.pdf, page 4)\nHold reset for 5 seconds to restart.", messages[1].Text);
        Assert.EndsWith("Question: How do I restart it?", messages[1].Text);
        Assert.Equal(0f, options?.Temperature);
    }

    [Fact]
    public async Task Ask_ReportsModelAndTokenUsage()
    {
        var response = await Service(Match("Some text.", 1)).AskAsync("Question?");

        Assert.Equal("fake-model", response.Model);
        Assert.Equal(500, response.InputTokens);
        Assert.Equal(20, response.OutputTokens);
        Assert.True(response.DurationMs >= 0);
    }

    [Fact]
    public async Task Ask_ModelSaysNotFound_IsMarkedUnanswered()
    {
        _chat.Reply = RagPrompt.NotFoundAnswer;

        var response = await Service(Match("Unrelated text.", 1)).AskAsync("What is the meaning of life?");

        Assert.False(response.Answered);
        Assert.All(response.Sources, s => Assert.False(s.Cited));
    }

    [Fact]
    public async Task Ask_NoSourcesFound_DoesNotCallModel()
    {
        var response = await Service().AskAsync("Anything?");

        Assert.Empty(_chat.Calls);
        Assert.False(response.Answered);
        Assert.Equal(RagPrompt.NotFoundAnswer, response.Answer);
    }

    [Fact]
    public async Task Ask_UsesConfiguredNumberOfSources()
    {
        var matches = Enumerable.Range(1, 10).Select(i => Match($"Chunk {i}.", i)).ToArray();

        var response = await Service(3, matches).AskAsync("Question?");

        Assert.Equal(3, response.Sources.Count);
    }

    [Fact]
    public async Task Ask_EmptyQuestion_IsRejected()
    {
        await Assert.ThrowsAsync<ValidationFailedException>(() => Service().AskAsync(" "));
    }

    // --- Follow-up questions ---

    [Fact]
    public async Task Ask_WithoutHistory_SearchesForTheQuestionItself_WithOneModelCall()
    {
        var response = await Service(Match("Text.", 1)).AskAsync("What is the governing law?");

        Assert.Equal("What is the governing law?", response.SearchQuery);
        Assert.Equal(["What is the governing law?"], _embeddings.Calls.Single().Inputs);
        Assert.Single(_chat.Calls);
    }

    [Fact]
    public async Task Ask_WithHistory_RewritesFollowUp_AndSearchesForTheRewrite()
    {
        _chat.QueuedReplies.Enqueue("\"How long do I have to fix a breach before NVIDIA terminates the agreement?\"");
        _chat.Reply = "30 days [1].";

        var response = await Service(Match("Breach must be cured within 30 days.", 4))
            .AskAsync("How long do I have to fix it?", History);

        // First call: the rewrite, which sees the conversation (minus citation numbers).
        var condense = _chat.Calls[0].Messages;
        Assert.Equal(RagPrompt.CondenseSystemPrompt, condense[0].Text);
        Assert.Contains("Assistant: Yes, if you breach it and don't fix it within 30 days.", condense[1].Text);
        Assert.EndsWith("Latest question: How long do I have to fix it?", condense[1].Text);

        // Retrieval used the rewrite (quotes trimmed), not the vague follow-up.
        const string rewrite = "How long do I have to fix a breach before NVIDIA terminates the agreement?";
        Assert.Equal(rewrite, response.SearchQuery);
        Assert.Equal([rewrite], _embeddings.Calls.Single().Inputs);

        // Second call: the answer, with history between the system prompt and the new question.
        var answer = _chat.Calls[1].Messages;
        Assert.Equal(
            [ChatRole.System, ChatRole.User, ChatRole.Assistant, ChatRole.User],
            answer.Select(m => m.Role));
        Assert.Equal("Yes, if you breach it and don't fix it within 30 days.", answer[2].Text); // [2] stripped
        Assert.EndsWith("Question: How long do I have to fix it?", answer[3].Text);
        Assert.Equal("30 days [1].", response.Answer);
    }

    [Fact]
    public async Task Ask_LongHistory_OnlyRecentTurnsAreSent()
    {
        var longHistory = Enumerable.Range(1, 20)
            .Select(i => new ChatTurn(i % 2 == 1 ? "user" : "assistant", $"Turn {i}"))
            .ToList();
        _chat.QueuedReplies.Enqueue("Standalone question?");

        await Service(Match("Text.", 1)).AskAsync("Follow-up?", longHistory);

        var answerMessages = _chat.Calls[1].Messages;
        Assert.Equal(1 + RagService.MaxHistoryTurns + 1, answerMessages.Count); // system + history + question
        Assert.Equal("Turn 15", answerMessages[1].Text);
    }

    // --- Streaming ---

    [Fact]
    public async Task Stream_YieldsSourcesThenTokensThenDone()
    {
        _chat.Reply = "Hold reset for five seconds [2].";
        var service = Service(Match("Power LED.", 1), Match("Hold reset for 5 seconds.", 4));

        var events = await service.StreamAsync("How do I restart it?").ToListAsync();

        var sources = Assert.IsType<SourcesEvent>(events[0]);
        Assert.Equal(2, sources.Sources.Count);
        Assert.All(sources.Sources, s => Assert.False(s.Cited)); // not known until the answer is complete

        var tokens = events.Skip(1).Take(events.Count - 2).Select(e => Assert.IsType<TokenEvent>(e).Text);
        Assert.Equal("Hold reset for five seconds [2].", string.Concat(tokens));
        Assert.True(events.Count > 3); // really streamed in pieces

        var done = Assert.IsType<DoneEvent>(events[^1]);
        Assert.True(done.Answered);
        Assert.Equal([2], done.CitedSources);
        Assert.Equal("fake-model", done.Model);
        Assert.Equal(500, done.InputTokens);
    }

    [Fact]
    public async Task Stream_NoSources_StreamsNotFoundWithoutCallingModel()
    {
        var events = await Service().StreamAsync("Anything?").ToListAsync();

        Assert.Empty(_chat.Calls);
        Assert.Equal(RagPrompt.NotFoundAnswer, Assert.IsType<TokenEvent>(events[1]).Text);
        Assert.False(Assert.IsType<DoneEvent>(events[2]).Answered);
    }

    [Fact]
    public void Stream_EmptyQuestion_ThrowsImmediately_BeforeEnumeration()
    {
        Assert.Throws<ValidationFailedException>(() => Service().StreamAsync(""));
    }
}
