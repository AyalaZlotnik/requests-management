using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Requests.Api.Domain;

namespace Requests.Api.Common.Errors;

/// <summary>
/// Maps exceptions to RFC 7807 Problem Details. Unexpected errors are logged
/// and returned without internal details (no stack traces / SQL to the client).
/// </summary>
public sealed class GlobalExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away – nothing to return, and this is not an error.
            logger.LogInformation("Request {Path} was cancelled by the client", httpContext.Request.Path);
            httpContext.Response.StatusCode = 499;
            return true;
        }

        var problem = exception switch
        {
            NotFoundException => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Resource not found",
                Detail = exception.Message
            },
            ConcurrencyConflictException => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Concurrency conflict",
                Detail = exception.Message
            },
            InvalidStatusTransitionException transition => new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Invalid status transition",
                Detail = exception.Message,
                Extensions =
                {
                    ["currentStatus"] = transition.From.ToString(),
                    ["allowedStatuses"] = transition.AllowedStatuses.Select(s => s.ToString()).ToArray()
                }
            },
            _ => null
        };

        if (problem is null)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred"
            };
        }
        else
        {
            logger.LogWarning("{Title}: {Detail}", problem.Title, problem.Detail);
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
