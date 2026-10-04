using Microsoft.EntityFrameworkCore;
using Requests.Api.Common.Errors;
using Requests.Api.Data;
using Requests.Api.Domain;
using Requests.Api.Features.Requests.Dtos;

namespace Requests.Api.Features.Requests;

public interface IRequestQueryService
{
    Task<PagedResult<RequestListItemDto>> SearchAsync(RequestSearchQuery query, CancellationToken ct);
    Task<RequestDetailsDto> GetByIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<StatusHistoryDto>> GetHistoryAsync(int id, CancellationToken ct);
}

/// <summary>
/// Read side. Filtering, sorting and paging are composed into a single IQueryable and executed by
/// SQL Server – only one page of rows (plus a COUNT) ever leaves the database.
/// </summary>
public class RequestQueryService(RequestsDbContext db) : IRequestQueryService
{
    public async Task<PagedResult<RequestListItemDto>> SearchAsync(RequestSearchQuery query, CancellationToken ct)
    {
        var filtered = ApplyFilters(db.Requests.AsNoTracking(), query);

        var totalCount = await filtered.CountAsync(ct);

        var items = await ApplySort(filtered, query.SortBy, query.SortDirection)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(RequestMappings.ToListItem)
            .ToListAsync(ct);

        return new PagedResult<RequestListItemDto>(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<RequestDetailsDto> GetByIdAsync(int id, CancellationToken ct)
    {
        var request = await db.Requests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("Request", id);
        return request.ToDetails();
    }

    public async Task<IReadOnlyList<StatusHistoryDto>> GetHistoryAsync(int id, CancellationToken ct)
    {
        if (!await db.Requests.AnyAsync(r => r.Id == id, ct))
        {
            throw new NotFoundException("Request", id);
        }

        return await db.StatusHistory
            .AsNoTracking()
            .Where(h => h.RequestId == id)
            .OrderByDescending(h => h.ChangedAt)
            .ThenByDescending(h => h.Id)
            .Select(h => new StatusHistoryDto(h.Id, h.PreviousStatus, h.NewStatus, h.ChangedAt, h.ChangedBy))
            .ToListAsync(ct);
    }

    private static IQueryable<ServiceRequest> ApplyFilters(IQueryable<ServiceRequest> source, RequestSearchQuery query)
    {
        if (query.Status is { Length: > 0 } statuses)
            source = source.Where(r => statuses.Contains(r.Status));

        if (query.Priority is { Length: > 0 } priorities)
            source = source.Where(r => priorities.Contains(r.Priority));

        if (!string.IsNullOrWhiteSpace(query.OrganizationName))
        {
            var organization = query.OrganizationName.Trim();
            source = source.Where(r => r.OrganizationName.StartsWith(organization));
        }

        if (!string.IsNullOrWhiteSpace(query.AssignedTo))
        {
            var assignedTo = query.AssignedTo.Trim();
            source = source.Where(r => r.AssignedTo == assignedTo);
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
}
