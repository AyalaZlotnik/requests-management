using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Requests.Application.Common;
using Requests.Application.Requests.Abstractions;
using Requests.Application.Requests.Contracts;
using Requests.Application.Requests.Entities;
using Requests.Application.Requests.Services;
using Requests.Tests.Support;

namespace Requests.Tests.Application;

/// <summary>
/// Service-level tests with a fake repository – possible because the service depends only on
/// IRequestRepository. They cover what is timing-dependent through HTTP: a conflict raised by the
/// database while saving (the row changed between our read and our write).
/// </summary>
public class BulkUpdateServiceTests
{
    private static readonly byte[] Version = [0, 0, 0, 0, 0, 0, 0, 1];

    [Fact]
    public async Task Conflict_raised_while_saving_drops_only_that_item_and_saves_the_rest()
    {
        var repository = new FakeRepository(
            Request(1, RequestStatus.New), Request(2, RequestStatus.New), Request(3, RequestStatus.Waiting));
        repository.ConflictOnNextSave(2);
        var service = CreateService(repository);

        var response = await service.BulkUpdateStatusAsync(Command(RequestStatus.InProgress, 1, 2, 3), CancellationToken.None);

        response.Results.Select(r => (r.Id, r.Outcome)).Should().Equal(
            (1, BulkItemOutcome.Updated), (2, BulkItemOutcome.Conflict), (3, BulkItemOutcome.Updated));
        repository.SaveAttempts.Should().Be(2);
        repository.Discarded.Should().Equal(2);
        repository.SavedHistory.Select(h => h.RequestId).Should().BeEquivalentTo([1, 3]);
    }

    [Fact]
    public async Task When_every_item_conflicts_nothing_is_saved_and_the_loop_ends()
    {
        var repository = new FakeRepository(Request(1, RequestStatus.New), Request(2, RequestStatus.New));
        repository.ConflictOnNextSave(1, 2);
        var service = CreateService(repository);

        var response = await service.BulkUpdateStatusAsync(Command(RequestStatus.InProgress, 1, 2), CancellationToken.None);

        response.Succeeded.Should().Be(0);
        response.Results.Should().OnlyContain(r => r.Outcome == BulkItemOutcome.Conflict);
        repository.SavedHistory.Should().BeEmpty();
    }

    private static RequestCommandService CreateService(FakeRepository repository) =>
        new(repository, new NoCache(), TimeProvider.System, NullLogger<RequestCommandService>.Instance);

    private static ServiceRequest Request(int id, RequestStatus status) =>
        RequestsApiFactory.NewRequest(status: status).WithId(id).WithRowVersion(Version);

    private static BulkUpdateStatusRequest Command(RequestStatus status, params int[] ids) => new()
    {
        Status = status,
        ChangedBy = "דנה לוי",
        Items = ids.Select(id => new BulkStatusItem { Id = id, RowVersion = Convert.ToBase64String(Version) }).ToList()
    };

    private sealed class NoCache : ISummaryCache
    {
        public Task<RequestsSummaryDto> GetOrCreateAsync(Func<CancellationToken, Task<RequestsSummaryDto>> factory, CancellationToken ct) => factory(ct);
        public void Invalidate() { }
    }

    /// <summary>In-memory repository that can simulate the database rejecting a batch because of a conflict.</summary>
    private sealed class FakeRepository(params ServiceRequest[] requests) : IRequestRepository
    {
        private readonly Dictionary<int, ServiceRequest> _requests = requests.ToDictionary(r => r.Id);
        private readonly List<RequestStatusHistory> _pendingHistory = [];
        private int[] _conflictOnNextSave = [];

        public int SaveAttempts { get; private set; }
        public List<int> Discarded { get; } = [];
        public List<RequestStatusHistory> SavedHistory { get; } = [];

        public void ConflictOnNextSave(params int[] ids) => _conflictOnNextSave = ids;

        public Task<IReadOnlyList<ServiceRequest>> GetForUpdateAsync(IReadOnlyCollection<int> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ServiceRequest>>(ids.Where(_requests.ContainsKey).Select(id => _requests[id]).ToList());

        public void SetExpectedVersion(ServiceRequest request, byte[] rowVersion) { }

        public void AddHistory(RequestStatusHistory entry) => _pendingHistory.Add(entry);

        public void Discard(ServiceRequest request, RequestStatusHistory history)
        {
            Discarded.Add(request.Id);
            _pendingHistory.Remove(history);
        }

        public Task SaveChangesAsync(CancellationToken ct)
        {
            SaveAttempts++;
            // Like the database: a conflict rolls back the whole batch and reports the conflicting rows.
            var conflicting = _conflictOnNextSave.Where(id => _pendingHistory.Any(h => h.RequestId == id)).ToList();
            if (conflicting.Count > 0)
            {
                throw new ConcurrencyConflictException(conflicting);
            }

            SavedHistory.AddRange(_pendingHistory);
            _pendingHistory.Clear();
            return Task.CompletedTask;
        }

        public Task<PagedResult<RequestListItemDto>> SearchAsync(RequestSearchQuery query, CancellationToken ct) => throw new NotSupportedException();
        public Task<ServiceRequest?> FindAsync(int id, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(int id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<StatusHistoryDto>> GetHistoryAsync(int requestId, CancellationToken ct) => throw new NotSupportedException();
        public Task<StatusHistoryDto?> GetLastChangeAsync(int requestId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<SummaryBucket>> GetSummaryBucketsAsync(RequestFilter filter, DateTime openOlderThan, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<CountByKey<string>>> GetTopAssigneesAsync(RequestFilter filter, int count, CancellationToken ct) => throw new NotSupportedException();
    }
}
