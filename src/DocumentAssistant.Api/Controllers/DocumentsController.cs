using DocumentAssistant.Application.Common;
using DocumentAssistant.Application.Documents;
using Microsoft.AspNetCore.Mvc;

namespace DocumentAssistant.Api.Controllers;

[ApiController]
[Route("api/documents")]
public class DocumentsController(DocumentService documentService) : ControllerBase
{
    /// <summary>Uploads a PDF. Processing (extraction, chunking, embeddings) happens later.</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DocumentDto>> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var document = await documentService.UploadAsync(file.FileName, file.Length, stream, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = document.Id }, document);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var document = await documentService.GetByIdAsync(id, cancellationToken);
        return document is null ? NotFound() : document;
    }

    /// <summary>Preview of the text extracted from each page, useful for checking a PDF before processing.</summary>
    [HttpGet("{id:guid}/text")]
    [ProducesResponseType<DocumentTextDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DocumentTextDto>> GetText(Guid id, CancellationToken cancellationToken)
    {
        var text = await documentService.ExtractTextAsync(id, cancellationToken);
        return text is null ? NotFound() : text;
    }

    /// <summary>The stored chunks, in order. Empty until processing has finished.</summary>
    [HttpGet("{id:guid}/chunks")]
    [ProducesResponseType<IReadOnlyList<ChunkDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ChunkDto>>> GetChunks(Guid id, CancellationToken cancellationToken)
    {
        var chunks = await documentService.GetChunksAsync(id, cancellationToken);
        return chunks is null ? NotFound() : Ok(chunks);
    }

    /// <summary>Queues a failed (or still waiting) document for processing again.</summary>
    /// <summary>Deletes the document with its chunks and stored file.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        await documentService.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/process")]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DocumentDto>> Process(Guid id, CancellationToken cancellationToken)
    {
        var document = await documentService.RequestProcessingAsync(id, cancellationToken);
        return document is null ? NotFound() : AcceptedAtAction(nameof(GetById), new { id }, document);
    }

    [HttpGet]
    [ProducesResponseType<PagedResult<DocumentDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<DocumentDto>> List(int page = 1, int pageSize = 20, CancellationToken cancellationToken = default) =>
        documentService.ListAsync(page, pageSize, cancellationToken);
}
