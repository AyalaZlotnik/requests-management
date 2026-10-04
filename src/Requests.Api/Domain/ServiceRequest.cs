namespace Requests.Api.Domain;

/// <summary>
/// A request (פנייה) received from an organization.
/// Named ServiceRequest to avoid confusion with HttpRequest / ControllerBase.Request.
/// </summary>
public class ServiceRequest
{
    // Used by EF Core.
    private ServiceRequest() { }

    public ServiceRequest(
        string title,
        string organizationName,
        RequestPriority priority,
        string? assignedTo,
        DateTime createdAt,
        RequestStatus status = RequestStatus.New)
    {
        Title = title;
        OrganizationName = organizationName;
        Priority = priority;
        AssignedTo = assignedTo;
        Status = status;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public int Id { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string OrganizationName { get; private set; } = string.Empty;
    public RequestStatus Status { get; private set; }
    public RequestPriority Priority { get; private set; }
    public string? AssignedTo { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    /// <summary>SQL Server rowversion – changed by the database on every update.</summary>
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>
    /// Changes the status and returns the audit record that must be saved in the same transaction.
    /// </summary>
    public RequestStatusHistory ChangeStatus(RequestStatus newStatus, string changedBy, DateTime now)
    {
        if (!StatusTransitions.IsAllowed(Status, newStatus))
        {
            throw new InvalidStatusTransitionException(Id, Status, newStatus);
        }

        var history = new RequestStatusHistory(Id, Status, newStatus, changedBy, now);
        Status = newStatus;
        UpdatedAt = now;
        return history;
    }
}
