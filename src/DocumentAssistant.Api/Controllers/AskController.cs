using DocumentAssistant.Application.Rag;
using Microsoft.AspNetCore.Mvc;

namespace DocumentAssistant.Api.Controllers;

public record AskRequest(string Question, Guid? DocumentId = null);

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
        ragService.AskAsync(request.Question, request.DocumentId, cancellationToken);
}
