using System.Linq.Expressions;
using Requests.Api.Domain;
using Requests.Api.Features.Requests.Dtos;

namespace Requests.Api.Features.Requests;

public static class RequestMappings
{
    /// <summary>
    /// Projection used inside EF queries, so only the needed columns are read
    /// (the base64 conversion of RowVersion runs on the materialized row).
    /// </summary>
    public static readonly Expression<Func<ServiceRequest, RequestListItemDto>> ToListItem = r =>
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

    public static RequestDetailsDto ToDetails(this ServiceRequest r) =>
        new(
            r.Id,
            r.Title,
            r.OrganizationName,
            r.Status,
            r.Priority,
            r.AssignedTo,
            r.CreatedAt,
            r.UpdatedAt,
            Convert.ToBase64String(r.RowVersion),
            StatusTransitions.AllowedFrom(r.Status));
}
