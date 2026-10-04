using Requests.Api.Domain;

namespace Requests.Api.Features.Requests.Dtos;

/// <summary>A row in the requests list. RowVersion is a base64 concurrency token that must be sent back on update.</summary>
public record RequestListItemDto(
    int Id,
    string Title,
    string OrganizationName,
    RequestStatus Status,
    RequestPriority Priority,
    string? AssignedTo,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string RowVersion);

public record RequestDetailsDto(
    int Id,
    string Title,
    string OrganizationName,
    RequestStatus Status,
    RequestPriority Priority,
    string? AssignedTo,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string RowVersion,
    IReadOnlyList<RequestStatus> AllowedNextStatuses);

public record StatusHistoryDto(
    long Id,
    RequestStatus PreviousStatus,
    RequestStatus NewStatus,
    DateTime ChangedAt,
    string ChangedBy);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public record CountByKey<TKey>(TKey Key, int Count);

public record RequestsSummaryDto(
    int TotalCount,
    int OpenCount,
    IReadOnlyList<CountByKey<RequestStatus>> ByStatus,
    IReadOnlyList<CountByKey<RequestPriority>> OpenByPriority,
    IReadOnlyList<CountByKey<string>> TopAssigneesByOpenRequests,
    DateTime GeneratedAt);
