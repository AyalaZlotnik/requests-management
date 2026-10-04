using Requests.Application.Common;
using Requests.Application.Requests.Contracts;
using Requests.Application.Requests.Entities;

namespace Requests.Application.Requests.Abstractions;

/// <summary>
/// Data access for requests, implemented in Infrastructure.
/// Reads return DTOs projected inside the database (filtering, sorting and paging never happen in memory).
/// Writes follow a unit-of-work pattern: load → change entities → <see cref="SaveChangesAsync"/>.
/// </summary>
public interface IRequestRepository
{
    Task<PagedResult<RequestListItemDto>> SearchAsync(RequestSearchQuery query, CancellationToken ct);

    /// <summary>Read-only load (not tracked for changes).</summary>
    Task<ServiceRequest?> FindAsync(int id, CancellationToken ct);

    Task<bool> ExistsAsync(int id, CancellationToken ct);

    Task<IReadOnlyList<StatusHistoryDto>> GetHistoryAsync(int requestId, CancellationToken ct);

    /// <summary>Number of requests per (Status, Priority) – at most 12 rows.</summary>
    Task<IReadOnlyList<StatusPriorityCount>> CountByStatusAndPriorityAsync(CancellationToken ct);

    Task<IReadOnlyList<CountByKey<string>>> GetTopAssigneesByOpenRequestsAsync(int count, CancellationToken ct);

    /// <summary>Loads requests for modification. Missing ids are simply not returned.</summary>
    Task<IReadOnlyList<ServiceRequest>> GetForUpdateAsync(IReadOnlyCollection<int> ids, CancellationToken ct);

    /// <summary>
    /// Declares the version the client read. Saving succeeds only if the stored version still equals it.
    /// </summary>
    void SetExpectedVersion(ServiceRequest request, byte[] rowVersion);

    void AddHistory(RequestStatusHistory entry);

    /// <summary>Drops pending changes of a request (and its audit entry) so the rest can be saved without it.</summary>
    void Discard(ServiceRequest request, RequestStatusHistory history);

    /// <summary>
    /// Saves all pending changes in one transaction.
    /// Throws <see cref="ConcurrencyConflictException"/> with the conflicting ids if a request was changed by someone else;
    /// in that case nothing is saved.
    /// </summary>
    Task SaveChangesAsync(CancellationToken ct);
}

public record StatusPriorityCount(RequestStatus Status, RequestPriority Priority, int Count);
