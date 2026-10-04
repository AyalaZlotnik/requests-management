using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Requests.Application.Common;
using Requests.Application.Requests.Entities;

namespace Requests.Api.ErrorHandling;

/// <summary>
/// Maps exceptions to RFC 7807 Problem Details. Unexpected errors are logged
/// and returned without internal details (no stack traces / SQL to the client).
/// </summary>
public sealed partial class GlobalExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away – nothing to return, and this is not an error.
            LogCancelled(logger, httpContext.Request.Path);
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
            ConcurrencyConflictException conflict => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Concurrency conflict",
                Detail = exception.Message,
                // 409 rather than 412: 412 carries no body, while the client needs the current state
                // (and who changed it) to show the user what happened and let them decide.
                Extensions =
                {
                    ["currentState"] = conflict.CurrentState,
                    ["lastChange"] = conflict.LastChange
                }
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
            LogUnhandled(logger, exception, httpContext.Request.Method, httpContext.Request.Path);
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred"
            };
        }
        else
        {
            LogHandled(logger, problem.Status!.Value, problem.Title, problem.Detail);
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Request {Path} was cancelled by the client")]
    private static partial void LogCancelled(ILogger logger, PathString path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, PathString path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{StatusCode} {Title}: {Detail}")]
    private static partial void LogHandled(ILogger logger, int statusCode, string? title, string? detail);
}
