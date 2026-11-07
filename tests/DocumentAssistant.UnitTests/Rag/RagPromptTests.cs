using DocumentAssistant.Application.Rag;
using DocumentAssistant.Application.Retrieval;
using Microsoft.Extensions.AI;

namespace DocumentAssistant.UnitTests.Rag;

public class RagPromptTests
{
    private static SearchResult Source(string content, int? page, string file = "manual.pdf") =>
        new(Guid.NewGuid(), Guid.NewGuid(), file, 0, page, 0.5, 1, 1, content);

    [Fact]
    public void Build_SystemPromptHasGroundingRules_UserMessageHasNumberedSourcesThenQuestion()
    {
        var messages = RagPrompt.Build("How do I reset it?",
            [Source("Hold the button for 5 seconds.", 3), Source("No page info here.", null, "notes.pdf")]);

        Assert.Equal([ChatRole.System, ChatRole.User], messages.Select(m => m.Role));
        Assert.Contains("ONLY the numbered sources", messages[0].Text);
        Assert.Contains(RagPrompt.NotFoundAnswer, messages[0].Text);
        Assert.Equal(
            "Sources:\n\n" +
            "[1] (manual.pdf, page 3)\nHold the button for 5 seconds.\n\n" +
            "[2] (notes.pdf)\nNo page info here.\n\n" +
            "Question: How do I reset it?",
            messages[1].Text);
    }

    [Theory]
    [InlineData("Hold the button [1].", new[] { 1 })]
    [InlineData("Yes [2][3], and also [2].", new[] { 2, 3 })]
    [InlineData("No citations at all.", new int[0])]
    [InlineData("Invented [9] and zero [0] are ignored [1].", new[] { 1 })]
    public void ParseCitations_ReturnsValidSourceNumbers(string answer, int[] expected)
    {
        Assert.Equal(expected.ToHashSet(), RagPrompt.ParseCitations(answer, sourceCount: 3));
    }

    [Theory]
    [InlineData(RagPrompt.NotFoundAnswer, true)]
    [InlineData("Sorry, I could not find the answer in the documents.", true)]
    [InlineData("The warranty lasts two years [1].", false)]
    public void IsNotFound_DetectsTheNotFoundReply(string answer, bool expected)
    {
        Assert.Equal(expected, RagPrompt.IsNotFound(answer));
    }
}
