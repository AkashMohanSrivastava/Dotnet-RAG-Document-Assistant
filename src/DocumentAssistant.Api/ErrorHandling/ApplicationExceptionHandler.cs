using DocumentAssistant.Application.Common;
using DocumentAssistant.Application.Text;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DocumentAssistant.Api.ErrorHandling;

/// <summary>
/// Turns expected application errors into ProblemDetails responses:
/// <see cref="ValidationFailedException"/> → 400, <see cref="ConflictException"/> → 409,
/// <see cref="TextExtractionException"/> → 422.
/// </summary>
public class ApplicationExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            ValidationFailedException => (StatusCodes.Status400BadRequest, "Invalid request"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            TextExtractionException => (StatusCodes.Status422UnprocessableEntity, "Text extraction failed"),
            _ => (0, null)
        };
        if (title is null)
            return false;

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = exception.Message
            }
        });
    }
}
