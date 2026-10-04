using Requests.Application.Requests.Abstractions;
using Requests.Application.Requests.Contracts;
using Requests.Application.Requests.Entities;

namespace Requests.Application.Requests.Services;

public interface IRequestSummaryService
{
    Task<RequestsSummaryDto> GetSummaryAsync(CancellationToken ct);
}

/// <summary>
/// Dashboard aggregations. They scan the whole table, are identical for every user and are read on
/// every page load – so they are cached (see <see cref="ISummaryCache"/>).
/// </summary>
public class RequestSummaryService(IRequestRepository repository, ISummaryCache cache, TimeProvider timeProvider)
    : IRequestSummaryService
{
    private const int TopAssignees = 5;

    public Task<RequestsSummaryDto> GetSummaryAsync(CancellationToken ct) => cache.GetOrCreateAsync(ComputeAsync, ct);

    private async Task<RequestsSummaryDto> ComputeAsync(CancellationToken ct)
    {
        var groups = await repository.CountByStatusAndPriorityAsync(ct);

        var byStatus = Enum.GetValues<RequestStatus>()
            .Select(s => new CountByKey<RequestStatus>(s, groups.Where(g => g.Status == s).Sum(g => g.Count)))
            .ToList();

        var openGroups = groups.Where(g => g.Status != RequestStatus.Completed).ToList();
        var openByPriority = Enum.GetValues<RequestPriority>()
            .Select(p => new CountByKey<RequestPriority>(p, openGroups.Where(g => g.Priority == p).Sum(g => g.Count)))
            .ToList();

        var topAssignees = await repository.GetTopAssigneesByOpenRequestsAsync(TopAssignees, ct);

        return new RequestsSummaryDto(
            TotalCount: groups.Sum(g => g.Count),
            OpenCount: openGroups.Sum(g => g.Count),
            ByStatus: byStatus,
            OpenByPriority: openByPriority,
            TopAssigneesByOpenRequests: topAssignees,
            GeneratedAt: timeProvider.GetUtcNow().UtcDateTime);
    }
}
