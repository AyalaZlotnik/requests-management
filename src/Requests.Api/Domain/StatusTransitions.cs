namespace Requests.Api.Domain;

/// <summary>
/// The single source of truth for which status changes are allowed.
/// New → InProgress | Waiting
/// InProgress → Waiting | Completed
/// Waiting → InProgress | Completed
/// Completed → InProgress (reopen)
/// </summary>
public static class StatusTransitions
{
    private static readonly Dictionary<RequestStatus, RequestStatus[]> Allowed =
        new Dictionary<RequestStatus, RequestStatus[]>
        {
            [RequestStatus.New] = [RequestStatus.InProgress, RequestStatus.Waiting],
            [RequestStatus.InProgress] = [RequestStatus.Waiting, RequestStatus.Completed],
            [RequestStatus.Waiting] = [RequestStatus.InProgress, RequestStatus.Completed],
            [RequestStatus.Completed] = [RequestStatus.InProgress]
        };

    public static IReadOnlyList<RequestStatus> AllowedFrom(RequestStatus current) =>
        Allowed.TryGetValue(current, out var next) ? next : [];

    public static bool IsAllowed(RequestStatus from, RequestStatus to) => AllowedFrom(from).Contains(to);
}
