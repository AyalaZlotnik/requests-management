using Microsoft.AspNetCore.Mvc;

namespace Requests.Api.ErrorHandling;

/// <summary>
/// Log lines for requests rejected before reaching the services: invalid input (automatic 400) and
/// If-Match problems (428/400). Only field names and reasons are logged – never the submitted values.
/// </summary>
public static partial class RequestRejectionLog
{
    public const string Category = "Requests.Api.Validation";

    /// <summary>Wraps the default "invalid model state" response so every validation 400 is logged.</summary>
    public static void LogValidationFailures(this ApiBehaviorOptions options)
    {
        var createResponse = options.InvalidModelStateResponseFactory;
        options.InvalidModelStateResponseFactory = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(Category);
            var fields = context.ModelState.Where(entry => entry.Value?.Errors.Count > 0).Select(entry => entry.Key);
            ValidationFailed(logger, context.HttpContext.Request.Method, context.HttpContext.Request.Path, string.Join(", ", fields));
            return createResponse(context);
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "400 Validation failed for {Method} {Path}; fields: {Fields}")]
    private static partial void ValidationFailed(ILogger logger, string method, PathString path, string fields);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{StatusCode} If-Match rejected for {Path}: {Reason}")]
    public static partial void IfMatchRejected(ILogger logger, int statusCode, PathString path, string reason);
}
