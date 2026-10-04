namespace Requests.Api.Common.Errors;

public sealed class NotFoundException(string resource, object id)
    : Exception($"{resource} with id '{id}' was not found.");

/// <summary>Thrown when the client's RowVersion does not match the stored one (someone else updated first).</summary>
public sealed class ConcurrencyConflictException(int requestId)
    : Exception($"Request {requestId} was modified by another user. Reload it and try again.")
{
    public int RequestId { get; } = requestId;
}
