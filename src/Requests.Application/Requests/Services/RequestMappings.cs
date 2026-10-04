using Requests.Application.Requests.Contracts;
using Requests.Application.Requests.Entities;

namespace Requests.Application.Requests.Services;

public static class RequestMappings
{
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
