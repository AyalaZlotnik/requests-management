using Microsoft.Extensions.Logging;
using Requests.Application.Common;
using Requests.Application.Requests.Abstractions;
using Requests.Application.Requests.Contracts;
using Requests.Application.Requests.Entities;

namespace Requests.Application.Requests.Services;

public interface IRequestCommandService
{
    Task<RequestDetailsDto> UpdateStatusAsync(int id, UpdateStatusRequest command, CancellationToken ct);
    Task<BulkUpdateStatusResponse> BulkUpdateStatusAsync(BulkUpdateStatusRequest command, CancellationToken ct);
}

/// <summary>
/// Write side. The client's version is declared as the expected version, so the repository only saves
/// if the stored version is still the same (enforced by the database inside the UPDATE statement).
/// If another user committed in between, nothing is saved and the change is reported as a conflict –
/// no lost updates, even when two requests race between read and write.
/// The status change and its audit row are saved in the same transaction.
/// </summary>
public partial class RequestCommandService(
    IRequestRepository repository,
    ISummaryCache summaryCache,
    TimeProvider timeProvider,
    ILogger<RequestCommandService> logger) : IRequestCommandService
{
    public async Task<RequestDetailsDto> UpdateStatusAsync(int id, UpdateStatusRequest command, CancellationToken ct)
    {
        var request = (await repository.GetForUpdateAsync([id], ct)).SingleOrDefault()
            ?? throw new NotFoundException("Request", id);

        var clientVersion = Convert.FromBase64String(command.RowVersion);

        // Fast path: the client already holds a stale version – no point validating the transition.
        if (!request.RowVersion.AsSpan().SequenceEqual(clientVersion))
        {
            throw new ConcurrencyConflictException(id);
        }

        repository.SetExpectedVersion(request, clientVersion);
        var previousStatus = request.Status;
        repository.AddHistory(request.ChangeStatus(command.Status!.Value, command.ChangedBy.Trim(), Now()));

        // Throws ConcurrencyConflictException if someone committed between our read and our write.
        await repository.SaveChangesAsync(ct);

        summaryCache.Invalidate();
        LogStatusChanged(logger, id, previousStatus, request.Status);

        return request.ToDetails();
    }

    /// <summary>
    /// Partial success: every item is evaluated independently and gets its own outcome.
    /// All items that can be updated are committed together in one transaction; items that are
    /// missing, stale (conflict) or not allowed by the workflow are reported back and not changed.
    /// </summary>
    public async Task<BulkUpdateStatusResponse> BulkUpdateStatusAsync(BulkUpdateStatusRequest command, CancellationToken ct)
    {
        var newStatus = command.Status!.Value;
        var changedBy = command.ChangedBy.Trim();
        var now = Now();

        var ids = command.Items.Select(i => i.Id).ToList();
        var requests = (await repository.GetForUpdateAsync(ids, ct)).ToDictionary(r => r.Id);

        var results = new Dictionary<int, BulkItemResult>();
        var pending = new Dictionary<int, (ServiceRequest Request, RequestStatusHistory History)>();

        foreach (var item in command.Items)
        {
            if (!requests.TryGetValue(item.Id, out var request))
            {
                results[item.Id] = new BulkItemResult(item.Id, BulkItemOutcome.NotFound, "Request not found.", null);
                continue;
            }

            var clientVersion = Convert.FromBase64String(item.RowVersion);
            if (!request.RowVersion.AsSpan().SequenceEqual(clientVersion))
            {
                results[item.Id] = Conflict(item.Id);
                continue;
            }

            if (!StatusTransitions.IsAllowed(request.Status, newStatus))
            {
                results[item.Id] = new BulkItemResult(item.Id, BulkItemOutcome.InvalidTransition,
                    $"Cannot change status from {request.Status} to {newStatus}.", null);
                continue;
            }

            repository.SetExpectedVersion(request, clientVersion);
            var history = request.ChangeStatus(newStatus, changedBy, now);
            repository.AddHistory(history);
            pending.Add(request.Id, (request, history));
        }

        await SaveIsolatingConflictsAsync(pending, results, ct);

        foreach (var (request, _) in pending.Values)
        {
            results[request.Id] = new BulkItemResult(request.Id, BulkItemOutcome.Updated, null, Convert.ToBase64String(request.RowVersion));
        }

        if (pending.Count > 0)
        {
            summaryCache.Invalidate();
        }

        // Keep the response in the same order as the request.
        var ordered = command.Items.Select(i => results[i.Id]).ToList();
        var succeeded = pending.Count;
        LogBulkCompleted(logger, newStatus, succeeded, ordered.Count - succeeded, ordered.Count);

        return new BulkUpdateStatusResponse(ordered.Count, succeeded, ordered.Count - succeeded, ordered);
    }

    /// <summary>
    /// Saves all pending changes in one transaction. If a row was changed by someone else after we read it,
    /// the database rolls back the whole batch; we then drop only the conflicting rows and retry with the rest.
    /// Every retry removes at least one item, so the loop is bounded by the number of items.
    /// </summary>
    private async Task SaveIsolatingConflictsAsync(
        Dictionary<int, (ServiceRequest Request, RequestStatusHistory History)> pending,
        Dictionary<int, BulkItemResult> results,
        CancellationToken ct)
    {
        while (pending.Count > 0)
        {
            try
            {
                await repository.SaveChangesAsync(ct);
                return;
            }
            catch (ConcurrencyConflictException ex)
            {
                var conflicted = ex.RequestIds.Where(pending.ContainsKey).ToList();
                if (conflicted.Count == 0)
                {
                    throw;
                }

                foreach (var id in conflicted)
                {
                    var (request, history) = pending[id];
                    repository.Discard(request, history);
                    pending.Remove(id);
                    results[id] = Conflict(id);
                }
            }
        }
    }

    private static BulkItemResult Conflict(int id) =>
        new(id, BulkItemOutcome.Conflict, "The request was modified by another user.", null);

    // Truncated to millisecond precision (the precision of the date columns), so the value returned
    // to the client is exactly the one stored in UpdatedAt and in the audit row.
    private DateTime Now()
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Request {RequestId} status changed {PreviousStatus} -> {NewStatus}")]
    private static partial void LogStatusChanged(ILogger logger, int requestId, RequestStatus previousStatus, RequestStatus newStatus);

    [LoggerMessage(Level = LogLevel.Information, Message = "Bulk status update to {Status}: {Succeeded} succeeded, {Failed} failed out of {Requested}")]
    private static partial void LogBulkCompleted(ILogger logger, RequestStatus status, int succeeded, int failed, int requested);
}
