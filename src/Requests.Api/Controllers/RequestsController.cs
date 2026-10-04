using Microsoft.AspNetCore.Mvc;
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

    [HttpGet("{id:int}")]
    [ProducesResponseType<RequestDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<RequestDetailsDto> GetById(int id, CancellationToken ct) => queries.GetByIdAsync(id, ct);

    /// <summary>Status change history of a request, newest first.</summary>
    [HttpGet("{id:int}/history")]
    [ProducesResponseType<IReadOnlyList<StatusHistoryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<StatusHistoryDto>> GetHistory(int id, CancellationToken ct) => queries.GetHistoryAsync(id, ct);

    /// <summary>Change the status of one request. Requires the RowVersion the client last read.</summary>
    [HttpPatch("{id:int}/status")]
    [ProducesResponseType<RequestDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<RequestDetailsDto> UpdateStatus(int id, UpdateStatusRequest command, CancellationToken ct) =>
        commands.UpdateStatusAsync(id, command, ct);

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
