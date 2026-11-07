using DocumentAssistant.Application.Common;
using DocumentAssistant.Application.Embeddings;
using DocumentAssistant.Application.Rag;
using DocumentAssistant.Application.Retrieval;
using DocumentAssistant.UnitTests.Fakes;
using Microsoft.Extensions.Options;

namespace DocumentAssistant.UnitTests.Rag;

public class RagServiceTests
{
    private readonly FakeChatClient _chat = new();

    private static ChunkMatch Match(string content, int page) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "manual.pdf", page - 1, page, content, 0.8);

    private RagService Service(params ChunkMatch[] matches) => Service(5, matches);

    private RagService Service(int sourceCount, params ChunkMatch[] matches) =>
        new(new RetrievalService(new StaticChunkSearch(matches), new FakeEmbeddingGenerator(), Options.Create(new EmbeddingOptions())),
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
}
