namespace Requests.Application.Common;

public sealed class NotFoundException(string resource, object id)
    : Exception($"{resource} with id '{id}' was not found.");

/// <summary>
/// The version the client read is no longer the stored one – someone else updated first.
/// Carries the ids of the conflicting requests (one for a single update, possibly several when saving a batch).
/// </summary>
public sealed class ConcurrencyConflictException(IReadOnlyCollection<int> requestIds)
    : Exception($"Request {string.Join(", ", requestIds)} was modified by another user. Reload it and try again.")
{
    public ConcurrencyConflictException(int requestId) : this([requestId]) { }

    public IReadOnlyCollection<int> RequestIds { get; } = requestIds;
}
