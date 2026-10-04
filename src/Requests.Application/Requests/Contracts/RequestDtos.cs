using Requests.Application.Requests.Entities;

namespace Requests.Application.Requests.Contracts;

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

public record CountByKey<TKey>(TKey Key, int Count);

public record RequestsSummaryDto(
    int TotalCount,
    int OpenCount,
    IReadOnlyList<CountByKey<RequestStatus>> ByStatus,
    IReadOnlyList<CountByKey<RequestPriority>> OpenByPriority,
    IReadOnlyList<CountByKey<string>> TopAssigneesByOpenRequests,
    DateTime GeneratedAt);
