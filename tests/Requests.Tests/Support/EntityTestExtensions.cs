using Requests.Application.Requests.Entities;

namespace Requests.Tests.Support;

/// <summary>
/// Id and RowVersion are set by the database, so the entity has no public setters for them.
/// Service tests that run without a database set them here instead.
/// </summary>
public static class EntityTestExtensions
{
    public static ServiceRequest WithId(this ServiceRequest request, int id)
    {
        typeof(ServiceRequest).GetProperty(nameof(ServiceRequest.Id))!.SetValue(request, id);
        return request;
    }

    public static ServiceRequest WithRowVersion(this ServiceRequest request, byte[] rowVersion)
    {
        typeof(ServiceRequest).GetProperty(nameof(ServiceRequest.RowVersion))!.SetValue(request, rowVersion);
        return request;
    }
}
