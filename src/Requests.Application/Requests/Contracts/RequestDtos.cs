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

/// <summary>
/// Aggregations for the current filter.
/// ByStatus ignores only the status filter and ByPriority only the priority filter (facet counts),
/// so they show how many requests are in the other categories; everything else honours all filters.
/// </summary>
/// <param name="Total">Same number as the list total for the same filters.</param>
/// <param name="ByStatus">Count per status, ignoring the status filter.</param>
/// <param name="ByPriority">Count per priority, ignoring the priority filter.</param>
/// <param name="OpenOlderThan7Days">Not completed and created more than 7 days ago.</param>
/// <param name="LastUpdatedAt">Most recent UpdatedAt among the matching requests.</param>
/// <param name="TopAssignees">Handlers with the most open matching requests (top 5).</param>
/// <param name="GeneratedAt">When the numbers were computed (the unfiltered view may come from the cache).</param>
public record RequestsSummaryDto(
    int Total,
    IReadOnlyList<CountByKey<RequestStatus>> ByStatus,
    IReadOnlyList<CountByKey<RequestPriority>> ByPriority,
    int OpenOlderThan7Days,
    DateTime? LastUpdatedAt,
    IReadOnlyList<CountByKey<string>> TopAssignees,
    DateTime GeneratedAt);
