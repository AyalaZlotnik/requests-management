using Microsoft.Extensions.DependencyInjection;
using Requests.Application.Common;
using Requests.Application.Requests.Abstractions;
using Requests.Application.Requests.Contracts;
using Requests.Application.Requests.Entities;
using Requests.Infrastructure.Persistence;

namespace Requests.Tests.Support;

/// <summary>
/// Makes the "read → someone else commits → save" race deterministic. When armed, the next save of any
/// request waits at the gate until the test releases it, so the test can commit other changes in between.
/// The save then goes to SQL Server unchanged – the database's rowversion check decides, not the test.
/// </summary>
public sealed class SaveGate
{
    private TaskCompletionSource? _reached;
    private TaskCompletionSource? _release;

    /// <summary>Completes when the armed save has read its data and is waiting to save.</summary>
    public Task Reached => _reached?.Task ?? throw new InvalidOperationException("Arm the gate first.");

    public void Arm()
    {
        _reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Release() => _release?.TrySetResult();

    /// <summary>One-shot: only the first save after Arm() waits.</summary>
    internal async Task PassAsync()
    {
        var reached = Interlocked.Exchange(ref _reached, null);
        if (reached is null) return;

        reached.SetResult();
        await _release!.Task;
    }
}

/// <summary>Repository that waits at the <see cref="SaveGate"/> before saving; everything else is the real one.</summary>
internal sealed class GatedRequestRepository(RequestRepository inner, SaveGate gate) : IRequestRepository
{
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await gate.PassAsync();
        await inner.SaveChangesAsync(ct);
    }

    public Task<PagedResult<RequestListItemDto>> SearchAsync(RequestSearchQuery query, CancellationToken ct) => inner.SearchAsync(query, ct);
    public Task<ServiceRequest?> FindAsync(int id, CancellationToken ct) => inner.FindAsync(id, ct);
    public Task<bool> ExistsAsync(int id, CancellationToken ct) => inner.ExistsAsync(id, ct);
    public Task<IReadOnlyList<StatusHistoryDto>> GetHistoryAsync(int requestId, CancellationToken ct) => inner.GetHistoryAsync(requestId, ct);
    public Task<StatusHistoryDto?> GetLastChangeAsync(int requestId, CancellationToken ct) => inner.GetLastChangeAsync(requestId, ct);
    public Task<IReadOnlyList<SummaryBucket>> GetSummaryBucketsAsync(RequestFilter filter, DateTime openOlderThan, CancellationToken ct) => inner.GetSummaryBucketsAsync(filter, openOlderThan, ct);
    public Task<IReadOnlyList<CountByKey<string>>> GetTopAssigneesAsync(RequestFilter filter, int count, CancellationToken ct) => inner.GetTopAssigneesAsync(filter, count, ct);
    public Task<IReadOnlyList<ServiceRequest>> GetForUpdateAsync(IReadOnlyCollection<int> ids, CancellationToken ct) => inner.GetForUpdateAsync(ids, ct);
    public void SetExpectedVersion(ServiceRequest request, byte[] rowVersion) => inner.SetExpectedVersion(request, rowVersion);
    public void AddHistory(RequestStatusHistory entry) => inner.AddHistory(entry);
    public void Discard(ServiceRequest request, RequestStatusHistory history) => inner.Discard(request, history);
}

/// <summary>API factory whose repository saves pass through a <see cref="SaveGate"/>.</summary>
public sealed class GatedApiFactory : RequestsApiFactory
{
    public SaveGate Gate => Services.GetRequiredService<SaveGate>();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddSingleton<SaveGate>();
        services.AddScoped<RequestRepository>();
        services.AddScoped<IRequestRepository>(sp => new GatedRequestRepository(sp.GetRequiredService<RequestRepository>(), sp.GetRequiredService<SaveGate>()));
    }
}
