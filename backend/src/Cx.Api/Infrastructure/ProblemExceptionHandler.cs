using Cx.Core.Security;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Cx.Api.Infrastructure;

/// <summary>Maps domain exceptions to RFC 9457 ProblemDetails. Anything unexpected is logged and reported without internals.</summary>
public sealed class ProblemExceptionHandler(IProblemDetailsService problems, ILogger<ProblemExceptionHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title, detail) = exception switch
        {
            ForbiddenException e => (StatusCodes.Status403Forbidden, "Forbidden", e.Message),
            ArgumentException e => (StatusCodes.Status400BadRequest, "Invalid request", e.Message),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Invalid request", "The request could not be read."),
            OperationCanceledException => (StatusCodes.Status499ClientClosedRequest, "Cancelled", "The request was cancelled."),
            _ => (StatusCodes.Status500InternalServerError, "Server error", "An unexpected error occurred."),
        };

        if (status == StatusCodes.Status500InternalServerError) log.LogError(exception, "Unhandled exception for {Path}", context.Request.Path);
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
        });
    }
}
