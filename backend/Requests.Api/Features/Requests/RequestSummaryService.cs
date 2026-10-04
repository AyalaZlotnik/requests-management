using Microsoft.EntityFrameworkCore;
using Requests.Api.Common.Caching;
using Requests.Api.Data;
using Requests.Api.Domain;
using Requests.Api.Features.Requests.Dtos;

namespace Requests.Api.Features.Requests;

public interface IRequestSummaryService
{
    Task<RequestsSummaryDto> GetSummaryAsync(CancellationToken ct);
}

/// <summary>
/// Dashboard aggregations. Each one scans the whole table, it is the same for every user and it is
/// read on every page load – so it is cached (see <see cref="SummaryCache"/>).
/// </summary>
public class RequestSummaryService(RequestsDbContext db, SummaryCache cache, TimeProvider timeProvider) : IRequestSummaryService
{
    private const int TopAssignees = 5;

    public Task<RequestsSummaryDto> GetSummaryAsync(CancellationToken ct) => cache.GetOrCreateAsync(ComputeAsync, ct);

    private async Task<RequestsSummaryDto> ComputeAsync(CancellationToken ct)
    {
        // One GROUP BY (Status, Priority) – at most 12 rows – covered by IX_Requests_Status_CreatedAt (INCLUDE Priority).
        var groups = await db.Requests
            .GroupBy(r => new { r.Status, r.Priority })
            .Select(g => new { g.Key.Status, g.Key.Priority, Count = g.Count() })
            .ToListAsync(ct);

        var byStatus = Enum.GetValues<RequestStatus>()
            .Select(s => new CountByKey<RequestStatus>(s, groups.Where(g => g.Status == s).Sum(g => g.Count)))
            .ToList();

        var openGroups = groups.Where(g => g.Status != RequestStatus.Completed).ToList();
        var openByPriority = Enum.GetValues<RequestPriority>()
            .Select(p => new CountByKey<RequestPriority>(p, openGroups.Where(g => g.Priority == p).Sum(g => g.Count)))
            .ToList();

        // Covered by IX_Requests_AssignedTo_Status.
        var topAssignees = await db.Requests
            .Where(r => r.Status != RequestStatus.Completed && r.AssignedTo != null)
            .GroupBy(r => r.AssignedTo!)
            .Select(g => new { Assignee = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Assignee)
            .Take(TopAssignees)
            .ToListAsync(ct);

        return new RequestsSummaryDto(
            TotalCount: groups.Sum(g => g.Count),
            OpenCount: openGroups.Sum(g => g.Count),
            ByStatus: byStatus,
            OpenByPriority: openByPriority,
            TopAssigneesByOpenRequests: topAssignees.Select(x => new CountByKey<string>(x.Assignee, x.Count)).ToList(),
            GeneratedAt: timeProvider.GetUtcNow().UtcDateTime);
    }
}
