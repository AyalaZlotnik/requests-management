using Microsoft.EntityFrameworkCore;
using Requests.Api.Common.Caching;
using Requests.Api.Common.Errors;
using Requests.Api.Data;
using Requests.Api.Domain;
using Requests.Api.Features.Requests.Dtos;

namespace Requests.Api.Features.Requests;

public interface IRequestCommandService
{
    Task<RequestDetailsDto> UpdateStatusAsync(int id, UpdateStatusRequest command, CancellationToken ct);
    Task<BulkUpdateStatusResponse> BulkUpdateStatusAsync(BulkUpdateStatusRequest command, CancellationToken ct);
}

/// <summary>
/// Write side. Concurrency is enforced by the database: the client's RowVersion is set as the
/// original value, so EF issues "UPDATE ... WHERE Id = @id AND RowVersion = @clientVersion".
/// If another user updated the row in the meantime, 0 rows are affected and the update is rejected –
/// no lost updates, even when two requests race between read and write.
/// The status change and its audit row are saved in the same transaction (one SaveChanges).
/// </summary>
public partial class RequestCommandService(
    RequestsDbContext db,
    SummaryCache summaryCache,
    TimeProvider timeProvider,
    ILogger<RequestCommandService> logger) : IRequestCommandService
{
    public async Task<RequestDetailsDto> UpdateStatusAsync(int id, UpdateStatusRequest command, CancellationToken ct)
    {
        var request = await db.Requests.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("Request", id);

        var clientVersion = Convert.FromBase64String(command.RowVersion);

        // Fast path: the client already holds a stale version – no point validating the transition.
        if (!request.RowVersion.AsSpan().SequenceEqual(clientVersion))
        {
            throw new ConcurrencyConflictException(id);
        }

        db.Entry(request).Property(r => r.RowVersion).OriginalValue = clientVersion;
        var previousStatus = request.Status;
        var history = request.ChangeStatus(command.Status!.Value, command.ChangedBy.Trim(), Now());
        db.StatusHistory.Add(history);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone committed between our read and our write.
            throw new ConcurrencyConflictException(id);
        }

        summaryCache.Invalidate();
        LogStatusChanged(logger, id, previousStatus, request.Status);

        return request.ToDetails();
    }

    /// <summary>
    /// Partial success: every item is evaluated independently and gets its own outcome.
    /// All items that can be updated are committed together in one transaction; items that are
    /// missing, stale (409) or not allowed by the workflow are reported back and not changed.
    /// </summary>
    public async Task<BulkUpdateStatusResponse> BulkUpdateStatusAsync(BulkUpdateStatusRequest command, CancellationToken ct)
    {
        var newStatus = command.Status!.Value;
        var changedBy = command.ChangedBy.Trim();
        var now = Now();

        var ids = command.Items.Select(i => i.Id).ToList();
        var requests = await db.Requests.Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);

        var results = new Dictionary<int, BulkItemResult>();
        var pending = new Dictionary<ServiceRequest, RequestStatusHistory>();

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

            db.Entry(request).Property(r => r.RowVersion).OriginalValue = clientVersion;
            var history = request.ChangeStatus(newStatus, changedBy, now);
            db.StatusHistory.Add(history);
            pending.Add(request, history);
        }

        await SaveIsolatingConflictsAsync(pending, results, ct);

        foreach (var request in pending.Keys)
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
        LogBulkCompleted(logger,
            newStatus, succeeded, ordered.Count - succeeded, ordered.Count);

        return new BulkUpdateStatusResponse(ordered.Count, succeeded, ordered.Count - succeeded, ordered);
    }

    /// <summary>
    /// Saves all pending changes in one transaction. If a row was changed by someone else after we read it,
    /// SQL Server rolls back the whole batch; we then drop only the conflicting rows and retry with the rest.
    /// Every retry removes at least one item, so the loop is bounded by the number of items.
    /// </summary>
    private async Task SaveIsolatingConflictsAsync(
        Dictionary<ServiceRequest, RequestStatusHistory> pending,
        Dictionary<int, BulkItemResult> results,
        CancellationToken ct)
    {
        while (pending.Count > 0)
        {
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                var conflicted = ex.Entries.Select(e => e.Entity).OfType<ServiceRequest>().ToList();
                if (conflicted.Count == 0)
                {
                    throw;
                }

                foreach (var request in conflicted)
                {
                    db.Entry(pending[request]).State = EntityState.Detached;
                    db.Entry(request).State = EntityState.Detached;
                    pending.Remove(request);
                    results[request.Id] = Conflict(request.Id);
                }
            }
        }
    }

    private static BulkItemResult Conflict(int id) =>
        new(id, BulkItemOutcome.Conflict, "The request was modified by another user.", null);

    // Truncated to the column precision (datetime2(3)), so the value returned to the client
    // is exactly the one stored in Requests.UpdatedAt and in the audit row.
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
