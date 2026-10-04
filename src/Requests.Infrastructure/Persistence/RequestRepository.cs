using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Requests.Application.Common;
using Requests.Application.Requests.Abstractions;
using Requests.Application.Requests.Contracts;
using Requests.Application.Requests.Entities;

namespace Requests.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation. Every read composes one IQueryable that SQL Server executes –
/// only the requested page (plus a COUNT) ever leaves the database.
/// </summary>
public class RequestRepository(RequestsDbContext db) : IRequestRepository
{
    /// <summary>Only the needed columns are read; the base64 conversion runs on the materialized row.</summary>
    private static readonly Expression<Func<ServiceRequest, RequestListItemDto>> ToListItem = r =>
        new RequestListItemDto(
            r.Id,
            r.Title,
            r.OrganizationName,
            r.Status,
            r.Priority,
            r.AssignedTo,
            r.CreatedAt,
            r.UpdatedAt,
            Convert.ToBase64String(r.RowVersion));

    public async Task<PagedResult<RequestListItemDto>> SearchAsync(RequestSearchQuery query, CancellationToken ct)
    {
        var filtered = ApplyFilters(db.Requests.AsNoTracking(), query);

        var totalCount = await filtered.CountAsync(ct);

        // long: page × pageSize must never overflow, whatever limits validation applies.
        var skip = (long)(query.Page - 1) * query.PageSize;
        if (skip >= totalCount)
        {
            // Past the last page – no need to ask the database for rows that cannot exist.
            return new PagedResult<RequestListItemDto>([], query.Page, query.PageSize, totalCount);
        }

        var items = await ApplySort(filtered, query.SortBy, query.SortDirection)
            .Skip((int)skip)
            .Take(query.PageSize)
            .Select(ToListItem)
            .ToListAsync(ct);

        return new PagedResult<RequestListItemDto>(items, query.Page, query.PageSize, totalCount);
    }

    public Task<ServiceRequest?> FindAsync(int id, CancellationToken ct) =>
        db.Requests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<bool> ExistsAsync(int id, CancellationToken ct) =>
        db.Requests.AnyAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<StatusHistoryDto>> GetHistoryAsync(int requestId, CancellationToken ct) =>
        await db.StatusHistory
            .AsNoTracking()
            .Where(h => h.RequestId == requestId)
            .OrderByDescending(h => h.ChangedAt)
            .ThenByDescending(h => h.Id)
            .Select(h => new StatusHistoryDto(h.Id, h.PreviousStatus, h.NewStatus, h.ChangedAt, h.ChangedBy))
            .ToListAsync(ct);

    public Task<StatusHistoryDto?> GetLastChangeAsync(int requestId, CancellationToken ct) =>
        db.StatusHistory
            .AsNoTracking()
            .Where(h => h.RequestId == requestId)
            .OrderByDescending(h => h.ChangedAt)
            .ThenByDescending(h => h.Id)
            .Select(h => new StatusHistoryDto(h.Id, h.PreviousStatus, h.NewStatus, h.ChangedAt, h.ChangedBy))
            .FirstOrDefaultAsync(ct);

    // A single GROUP BY returns at most 12 rows; every number in the summary is a sum over them.
    public async Task<IReadOnlyList<SummaryBucket>> GetSummaryBucketsAsync(RequestFilter filter, DateTime openOlderThan, CancellationToken ct) =>
        await ApplySharedFilters(db.Requests.AsNoTracking(), filter)
            .GroupBy(r => new { r.Status, r.Priority })
            .Select(g => new SummaryBucket(
                g.Key.Status,
                g.Key.Priority,
                g.Count(),
                g.Count(r => r.CreatedAt < openOlderThan),
                g.Max(r => (DateTime?)r.UpdatedAt)))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CountByKey<string>>> GetTopAssigneesAsync(RequestFilter filter, int count, CancellationToken ct) =>
        await ApplyFilters(db.Requests.AsNoTracking(), filter)
            .Where(r => r.Status != RequestStatus.Completed && r.AssignedTo != null)
            .GroupBy(r => r.AssignedTo!)
            .Select(g => new { Assignee = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Assignee)
            .Take(count)
            .Select(x => new CountByKey<string>(x.Assignee, x.Count))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ServiceRequest>> GetForUpdateAsync(IReadOnlyCollection<int> ids, CancellationToken ct) =>
        await db.Requests.Where(r => ids.Contains(r.Id)).ToListAsync(ct);

    // EF adds "WHERE RowVersion = @original" to the UPDATE, so the check happens atomically in the database.
    public void SetExpectedVersion(ServiceRequest request, byte[] rowVersion) =>
        db.Entry(request).Property(r => r.RowVersion).OriginalValue = rowVersion;

    public void AddHistory(RequestStatusHistory entry) => db.StatusHistory.Add(entry);

    public void Discard(ServiceRequest request, RequestStatusHistory history)
    {
        db.Entry(history).State = EntityState.Detached;
        db.Entry(request).State = EntityState.Detached;
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var ids = ex.Entries.Select(e => e.Entity).OfType<ServiceRequest>().Select(r => r.Id).ToList();
            throw new ConcurrencyConflictException(ids);
        }
    }

    private static IQueryable<ServiceRequest> ApplyFilters(IQueryable<ServiceRequest> source, RequestFilter filter)
    {
        source = ApplySharedFilters(source, filter);

        if (filter.Status is { Length: > 0 } statuses)
            source = source.Where(AnyOf(r => r.Status, statuses));

        if (filter.Priority is { Length: > 0 } priorities)
            source = source.Where(AnyOf(r => r.Priority, priorities));

        return source;
    }

    /// <summary>Every filter except Status and Priority (the summary facets apply those themselves).</summary>
    private static IQueryable<ServiceRequest> ApplySharedFilters(IQueryable<ServiceRequest> source, RequestFilter query)
    {
        if (!string.IsNullOrWhiteSpace(query.OrganizationName))
        {
            var organization = query.OrganizationName.Trim();
            source = source.Where(r => r.OrganizationName.StartsWith(organization));
        }

        if (!string.IsNullOrWhiteSpace(query.AssignedTo))
        {
            var assignedTo = query.AssignedTo.Trim();
            // Contains: the user can type a first or last name.
            source = source.Where(r => r.AssignedTo!.Contains(assignedTo));
        }

        if (query.CreatedFrom is { } from)
            source = source.Where(r => r.CreatedAt >= from);

        if (query.CreatedTo is { } to)
            source = source.Where(r => r.CreatedAt <= to);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Translated to LIKE '%term%' with wildcard characters escaped by EF Core.
            var term = query.Search.Trim();
            source = source.Where(r => r.Title.Contains(term) || r.OrganizationName.Contains(term));
        }

        return source;
    }

    private static IQueryable<ServiceRequest> ApplySort(IQueryable<ServiceRequest> source, RequestSortField sortBy, SortDirection direction)
    {
        var desc = direction == SortDirection.Desc;
        var ordered = sortBy switch
        {
            RequestSortField.UpdatedAt => desc ? source.OrderByDescending(r => r.UpdatedAt) : source.OrderBy(r => r.UpdatedAt),
            RequestSortField.Priority => desc ? source.OrderByDescending(r => r.Priority) : source.OrderBy(r => r.Priority),
            RequestSortField.Status => desc ? source.OrderByDescending(r => r.Status) : source.OrderBy(r => r.Status),
            RequestSortField.Title => desc ? source.OrderByDescending(r => r.Title) : source.OrderBy(r => r.Title),
            RequestSortField.OrganizationName => desc ? source.OrderByDescending(r => r.OrganizationName) : source.OrderBy(r => r.OrganizationName),
            _ => desc ? source.OrderByDescending(r => r.CreatedAt) : source.OrderBy(r => r.CreatedAt)
        };

        // Id as a tie-breaker gives a stable order, so rows don't jump between pages.
        return desc ? ordered.ThenByDescending(r => r.Id) : ordered.ThenBy(r => r.Id);
    }

    /// <summary>
    /// Builds "field = @p0 OR field = @p1 …" for a short list of values.
    /// EF Core 8 would translate values.Contains(field) to IN (SELECT … FROM OPENJSON(@json)), whose row count
    /// SQL Server cannot estimate – it then scans instead of seeking (measured: 649 vs 202 reads on the status
    /// COUNT, see docs/PERFORMANCE.md). Each value stays a parameter, so the plan is still reused.
    /// </summary>
    private static Expression<Func<ServiceRequest, bool>> AnyOf<T>(Expression<Func<ServiceRequest, T>> field, IEnumerable<T> values)
    {
        var body = values
            .Distinct()
            .Select(value => (Expression)Expression.Equal(field.Body, AsParameter(value)))
            .Aggregate(Expression.OrElse);
        return Expression.Lambda<Func<ServiceRequest, bool>>(body, field.Parameters);
    }

    // A value read from an object property is sent by EF as a SQL parameter, not inlined as a constant.
    private static MemberExpression AsParameter<T>(T value) =>
        Expression.Property(Expression.Constant(new ParameterValue<T>(value)), nameof(ParameterValue<T>.Value));

    private sealed class ParameterValue<T>(T value)
    {
        public T Value { get; } = value;
    }
}
