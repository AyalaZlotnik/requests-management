using Requests.Application.Requests.Abstractions;
using Requests.Application.Requests.Contracts;
using Requests.Application.Requests.Entities;

namespace Requests.Application.Requests.Services;

public interface IRequestSummaryService
{
    Task<RequestsSummaryDto> GetSummaryAsync(RequestFilter filter, CancellationToken ct);
}

/// <summary>
/// Summary for the current filter, computed from one GROUP BY (Status, Priority) plus a top-handlers query.
/// The unfiltered view – the screen everyone lands on, and the most expensive one (whole table) – is cached;
/// filtered views are narrower, use the indexes, and are computed on every call.
/// </summary>
public class RequestSummaryService(IRequestRepository repository, ISummaryCache cache, TimeProvider timeProvider)
    : IRequestSummaryService
{
    private const int TopAssignees = 5;
    private static readonly TimeSpan OpenAgeThreshold = TimeSpan.FromDays(7);

    public Task<RequestsSummaryDto> GetSummaryAsync(RequestFilter filter, CancellationToken ct) =>
        filter.HasNoFilters()
            ? cache.GetOrCreateAsync(token => ComputeAsync(filter, token), ct)
            : ComputeAsync(filter, ct);

    private async Task<RequestsSummaryDto> ComputeAsync(RequestFilter filter, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var buckets = await repository.GetSummaryBucketsAsync(filter, now - OpenAgeThreshold, ct);

        var selectedStatuses = filter.Status is { Length: > 0 } s ? s.ToHashSet() : [.. Enum.GetValues<RequestStatus>()];
        var selectedPriorities = filter.Priority is { Length: > 0 } p ? p.ToHashSet() : [.. Enum.GetValues<RequestPriority>()];

        // Facet counts: each breakdown ignores its own filter but honours the other one.
        var byStatus = Enum.GetValues<RequestStatus>()
            .Select(status => new CountByKey<RequestStatus>(status, buckets
                .Where(b => b.Status == status && selectedPriorities.Contains(b.Priority))
                .Sum(b => b.Count)))
            .ToList();

        var byPriority = Enum.GetValues<RequestPriority>()
            .Select(priority => new CountByKey<RequestPriority>(priority, buckets
                .Where(b => b.Priority == priority && selectedStatuses.Contains(b.Status))
                .Sum(b => b.Count)))
            .ToList();

        var matching = buckets
            .Where(b => selectedStatuses.Contains(b.Status) && selectedPriorities.Contains(b.Priority))
            .ToList();

        var topAssignees = await repository.GetTopAssigneesAsync(filter, TopAssignees, ct);

        return new RequestsSummaryDto(
            Total: matching.Sum(b => b.Count),
            ByStatus: byStatus,
            ByPriority: byPriority,
            OpenOlderThan7Days: matching.Where(b => b.Status != RequestStatus.Completed).Sum(b => b.CreatedBeforeCutoff),
            LastUpdatedAt: matching.Max(b => b.LastUpdatedAt),
            TopAssignees: topAssignees,
            GeneratedAt: now);
    }
}
