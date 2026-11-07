using DocumentAssistant.Application.Retrieval;
using Microsoft.AspNetCore.Mvc;

namespace DocumentAssistant.Api.Controllers;

[ApiController]
[Route("api/search")]
public class SearchController(RetrievalService retrievalService) : ControllerBase
{
    /// <summary>
    /// Retrieves the chunks most relevant to a query. Try the same query with mode=vector, keyword
    /// and hybrid to compare how each method ranks the results.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<SearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<SearchResponse> Search(
        [FromQuery] string q,
        [FromQuery] SearchMode mode = SearchMode.Hybrid,
        [FromQuery] int limit = RetrievalService.DefaultLimit,
        [FromQuery] Guid? documentId = null,
        CancellationToken cancellationToken = default) =>
        retrievalService.SearchAsync(q, mode, limit, documentId, cancellationToken);
}
