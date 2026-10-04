namespace Requests.Application.Requests.Entities;

/// <summary>
/// Maximum lengths of text fields. Used by input validation and by the database schema,
/// so both always agree.
/// </summary>
public static class RequestFieldLimits
{
    public const int Title = 200;
    public const int OrganizationName = 200;
    public const int AssignedTo = 100;
    public const int ChangedBy = 100;
}
