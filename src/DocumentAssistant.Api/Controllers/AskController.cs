using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using DocumentAssistant.Application.Rag;
using Microsoft.AspNetCore.Mvc;

namespace DocumentAssistant.Api.Controllers;

/// <param name="History">Earlier turns, oldest first, as { role: "user" | "assistant", content }.</param>
public record AskRequest(string Question, IReadOnlyList<ChatTurn>? History = null, Guid? DocumentId = null);

[ApiController]
[Route("api/ask")]
public class AskController(RagService ragService) : ControllerBase
{
    /// <summary>
    /// Answers a question from the uploaded documents, citing sources as [n]. The response lists every
    /// source the model was given, which ones it cited, and the model's token usage and latency.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<AskResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<AskResponse> Ask(AskRequest request, CancellationToken cancellationToken) =>
        ragService.AskAsync(request.Question, request.History, request.DocumentId, cancellationToken);

    /// <summary>
    /// Streams the answer as Server-Sent Events: "sources" first, then "token" events as the model
    /// writes, then "done" with the cited source numbers and usage.
    /// </summary>
    [HttpPost("stream")]
    [Produces("text/event-stream")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public IResult Stream(AskRequest request, CancellationToken cancellationToken)
    {
        // Validates the question now, so a bad request still gets a normal 400.
        var events = ragService.StreamAsync(request.Question, request.History, request.DocumentId, cancellationToken);

        return TypedResults.ServerSentEvents(ToSseItems(events, cancellationToken));
    }

    // Typed as object so System.Text.Json serializes each event's actual type, not the empty base record.
    private static async IAsyncEnumerable<SseItem<object>> ToSseItems(
        IAsyncEnumerable<RagStreamEvent> events, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var e in events.WithCancellation(cancellationToken))
        {
            var eventType = e switch
            {
                SourcesEvent => "sources",
                TokenEvent => "token",
                _ => "done"
            };
            yield return new SseItem<object>(e, eventType);
        }
    }
}
