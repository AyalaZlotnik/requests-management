using Microsoft.AspNetCore.Mvc;
using Requests.Api.Http;
using Requests.Application.Common;
using Requests.Application.Requests.Contracts;
using Requests.Application.Requests.Services;

namespace Requests.Api.Controllers;

[ApiController]
[Route("api/requests")]
[Produces("application/json")]
public class RequestsController(
    IRequestQueryService queries,
    IRequestCommandService commands,
    IRequestSummaryService summary) : ControllerBase
{
    /// <summary>Search requests with server-side filtering, sorting and paging.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<RequestListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<RequestListItemDto>> Search([FromQuery] RequestSearchQuery query, CancellationToken ct) =>
        queries.SearchAsync(query, ct);

    /// <summary>Aggregations for the dashboard (cached).</summary>
    [HttpGet("summary")]
    [ProducesResponseType<RequestsSummaryDto>(StatusCodes.Status200OK)]
    public Task<RequestsSummaryDto> GetSummary(CancellationToken ct) => summary.GetSummaryAsync(ct);

    /// <summary>One request. The ETag response header is the version to send back in If-Match when updating.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<RequestDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<RequestDetailsDto> GetById(int id, CancellationToken ct)
    {
        var request = await queries.GetByIdAsync(id, ct);
        Response.Headers.ETag = EntityTags.FromRowVersion(request.RowVersion);
        return request;
    }

    /// <summary>Status change history of a request, newest first.</summary>
    [HttpGet("{id:int}/history")]
    [ProducesResponseType<IReadOnlyList<StatusHistoryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<StatusHistoryDto>> GetHistory(int id, CancellationToken ct) => queries.GetHistoryAsync(id, ct);

    /// <summary>
    /// Change the status of one request. Conditional update: If-Match must carry the ETag the client read.
    /// 428 if it is missing, 409 (with the current state and the last change) if someone else updated first.
    /// </summary>
    [HttpPatch("{id:int}/status")]
    [ProducesResponseType<RequestDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<RequestDetailsDto>> UpdateStatus(
        int id,
        UpdateStatusRequest command,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken ct)
    {
        switch (EntityTags.TryReadIfMatch(ifMatch, out var expectedVersion, out var detail))
        {
            case EntityTags.IfMatchError.Missing:
                return Problem(detail, statusCode: StatusCodes.Status428PreconditionRequired, title: "If-Match header is required");
            case EntityTags.IfMatchError.Invalid:
                return Problem(detail, statusCode: StatusCodes.Status400BadRequest, title: "Invalid If-Match header");
        }

        var updated = await commands.UpdateStatusAsync(id, command, expectedVersion, ct);
        Response.Headers.ETag = EntityTags.FromRowVersion(updated.RowVersion);
        return updated;
    }

    /// <summary>
    /// Change the status of up to 100 requests. Partial success: the response contains an outcome per item
    /// (Updated / NotFound / Conflict / InvalidTransition). Returns 200 whenever the batch itself was valid.
    /// </summary>
    [HttpPost("bulk/status")]
    [ProducesResponseType<BulkUpdateStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<BulkUpdateStatusResponse> BulkUpdateStatus(BulkUpdateStatusRequest command, CancellationToken ct) =>
        commands.BulkUpdateStatusAsync(command, ct);
}
