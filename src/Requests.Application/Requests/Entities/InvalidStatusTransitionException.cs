namespace Requests.Application.Requests.Entities;

public sealed class InvalidStatusTransitionException(int requestId, RequestStatus from, RequestStatus to)
    : Exception($"Request {requestId}: status change from {from} to {to} is not allowed.")
{
    public int RequestId { get; } = requestId;
    public RequestStatus From { get; } = from;
    public RequestStatus To { get; } = to;
    public IReadOnlyList<RequestStatus> AllowedStatuses => StatusTransitions.AllowedFrom(From);
}
